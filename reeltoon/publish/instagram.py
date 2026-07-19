"""Publish the final video as an Instagram Reel via the Graph API.

Requirements:
- Instagram Business or Creator account linked to a Facebook Page/app.
- IG_USER_ID: the IG user's numeric ID.
- IG_ACCESS_TOKEN: a long-lived token with instagram_content_publish scope.

Uses the resumable upload flow so no public hosting of the video is needed.
"""

from __future__ import annotations

import time

import requests

from ..config import settings
from ..models import CartoonScript
from ..store import Job

GRAPH = "https://graph.facebook.com/v21.0"


def publish(job: Job, script: CartoonScript) -> dict:
    if not (settings.ig_user_id and settings.ig_access_token):
        raise RuntimeError("IG_USER_ID / IG_ACCESS_TOKEN not configured")

    video = job.path("final.mp4")
    caption = f"{script.caption}\n\n" + " ".join(f"#{t}" for t in script.hashtags)

    # 1. Create a resumable REELS media container
    resp = requests.post(
        f"{GRAPH}/{settings.ig_user_id}/media",
        params={
            "media_type": "REELS",
            "upload_type": "resumable",
            "caption": caption[:2200],
            "access_token": settings.ig_access_token,
        },
        timeout=60,
    )
    resp.raise_for_status()
    container = resp.json()
    container_id, upload_uri = container["id"], container["uri"]

    # 2. Upload the binary
    data = video.read_bytes()
    up = requests.post(
        upload_uri,
        headers={
            "Authorization": f"OAuth {settings.ig_access_token}",
            "offset": "0",
            "file_size": str(len(data)),
        },
        data=data,
        timeout=600,
    )
    up.raise_for_status()

    # 3. Wait for processing, then publish
    for _ in range(60):
        status = requests.get(
            f"{GRAPH}/{container_id}",
            params={"fields": "status_code", "access_token": settings.ig_access_token},
            timeout=30,
        ).json()
        code = status.get("status_code")
        if code == "FINISHED":
            break
        if code == "ERROR":
            raise RuntimeError(f"Instagram processing failed: {status}")
        time.sleep(5)
    else:
        raise TimeoutError("Instagram container never finished processing")

    pub = requests.post(
        f"{GRAPH}/{settings.ig_user_id}/media_publish",
        params={"creation_id": container_id, "access_token": settings.ig_access_token},
        timeout=60,
    )
    pub.raise_for_status()
    media_id = pub.json()["id"]
    return {"media_id": media_id}
