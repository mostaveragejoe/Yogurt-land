"""Publish the final video to TikTok via the Content Posting API.

Requirements:
- A TikTok developer app with the Content Posting API product added and the
  video.publish scope granted; TIKTOK_ACCESS_TOKEN from its OAuth flow.
- Note: until your app passes TikTok's audit, posts are forced to
  SELF_ONLY (private) visibility.
"""

from __future__ import annotations

import time

import requests

from ..config import settings
from ..models import CartoonScript
from ..store import Job

API = "https://open.tiktokapis.com/v2"


def publish(job: Job, script: CartoonScript) -> dict:
    if not settings.tiktok_access_token:
        raise RuntimeError("TIKTOK_ACCESS_TOKEN not configured")

    video = job.path("final.mp4")
    data = video.read_bytes()
    title = f"{script.caption} " + " ".join(f"#{t}" for t in script.hashtags)

    headers = {"Authorization": f"Bearer {settings.tiktok_access_token}"}

    # 1. Init a direct post with single-chunk file upload
    init = requests.post(
        f"{API}/post/publish/video/init/",
        headers={**headers, "Content-Type": "application/json; charset=UTF-8"},
        json={
            "post_info": {
                "title": title[:2200],
                "privacy_level": "PUBLIC_TO_EVERYONE",
                "disable_comment": False,
            },
            "source_info": {
                "source": "FILE_UPLOAD",
                "video_size": len(data),
                "chunk_size": len(data),
                "total_chunk_count": 1,
            },
        },
        timeout=60,
    )
    init.raise_for_status()
    body = init.json()
    if body.get("error", {}).get("code") not in (None, "ok"):
        raise RuntimeError(f"TikTok init failed: {body['error']}")
    publish_id = body["data"]["publish_id"]
    upload_url = body["data"]["upload_url"]

    # 2. Upload the whole file as one chunk
    up = requests.put(
        upload_url,
        headers={
            "Content-Type": "video/mp4",
            "Content-Range": f"bytes 0-{len(data) - 1}/{len(data)}",
        },
        data=data,
        timeout=600,
    )
    up.raise_for_status()

    # 3. Poll publish status
    for _ in range(60):
        status = requests.post(
            f"{API}/post/publish/status/fetch/",
            headers={**headers, "Content-Type": "application/json; charset=UTF-8"},
            json={"publish_id": publish_id},
            timeout=30,
        ).json()
        state = status.get("data", {}).get("status")
        if state == "PUBLISH_COMPLETE":
            return {"publish_id": publish_id, "status": state}
        if state in ("FAILED", "PUBLISH_FAILED"):
            raise RuntimeError(f"TikTok publish failed: {status}")
        time.sleep(5)
    return {"publish_id": publish_id, "status": "PROCESSING"}
