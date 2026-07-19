"""Upload the final video as a YouTube Short.

Setup (one-time):
1. Google Cloud project with the YouTube Data API v3 enabled.
2. OAuth client (Desktop app) -> download JSON to YOUTUBE_CLIENT_SECRETS path.
3. First run opens a browser for consent; the token is cached at
   YOUTUBE_TOKEN_FILE for subsequent headless runs.
"""

from __future__ import annotations

import os
from pathlib import Path

from ..models import CartoonScript
from ..store import Job

SCOPES = ["https://www.googleapis.com/auth/youtube.upload"]
CLIENT_SECRETS = os.environ.get("YOUTUBE_CLIENT_SECRETS", "secrets/youtube_client_secret.json")
TOKEN_FILE = os.environ.get("YOUTUBE_TOKEN_FILE", "secrets/youtube_token.json")


def _credentials():
    from google.auth.transport.requests import Request
    from google.oauth2.credentials import Credentials
    from google_auth_oauthlib.flow import InstalledAppFlow

    token_file = Path(TOKEN_FILE)
    creds = None
    if token_file.exists():
        creds = Credentials.from_authorized_user_file(str(token_file), SCOPES)
    if creds and creds.expired and creds.refresh_token:
        creds.refresh(Request())
    if not creds or not creds.valid:
        flow = InstalledAppFlow.from_client_secrets_file(CLIENT_SECRETS, SCOPES)
        creds = flow.run_local_server(port=0)
    token_file.parent.mkdir(parents=True, exist_ok=True)
    token_file.write_text(creds.to_json())
    return creds


def publish(job: Job, script: CartoonScript) -> dict:
    from googleapiclient.discovery import build
    from googleapiclient.http import MediaFileUpload

    video = job.path("final.mp4")
    tags = script.hashtags[:10]
    description = f"{script.caption}\n\n" + " ".join(f"#{t}" for t in tags)

    youtube = build("youtube", "v3", credentials=_credentials())
    request = youtube.videos().insert(
        part="snippet,status",
        body={
            "snippet": {
                "title": f"{script.title} #Shorts"[:100],
                "description": description[:5000],
                "tags": tags,
                "categoryId": "24",  # Entertainment
            },
            "status": {"privacyStatus": "public", "selfDeclaredMadeForKids": False},
        },
        media_body=MediaFileUpload(str(video), mimetype="video/mp4", resumable=True),
    )
    response = None
    while response is None:
        _, response = request.next_chunk()
    return {"video_id": response["id"], "url": f"https://youtube.com/shorts/{response['id']}"}
