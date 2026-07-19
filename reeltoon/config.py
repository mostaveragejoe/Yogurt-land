"""Environment-driven configuration.

All secrets and knobs come from environment variables (a local .env is loaded
if present). See .env.example for the full list.
"""

from __future__ import annotations

import os
from dataclasses import dataclass, field
from pathlib import Path

from dotenv import load_dotenv

load_dotenv()


def _env(name: str, default: str = "") -> str:
    return os.environ.get(name, default).strip()


@dataclass
class Settings:
    # Where jobs (downloads, frames, scripts, rendered videos) live
    data_dir: Path = field(default_factory=lambda: Path(_env("REELTOON_DATA_DIR", "data")))

    # Claude (analysis + scriptwriting). Key resolves via the SDK's normal chain
    # (ANTHROPIC_API_KEY / ant auth login profile), so no field needed here.
    claude_model: str = field(default_factory=lambda: _env("REELTOON_CLAUDE_MODEL", "claude-opus-4-8"))

    # Rendering
    replicate_api_token: str = field(default_factory=lambda: _env("REPLICATE_API_TOKEN"))
    image_model: str = field(default_factory=lambda: _env("REELTOON_IMAGE_MODEL", "black-forest-labs/flux-schnell"))
    video_model: str = field(default_factory=lambda: _env("REELTOON_VIDEO_MODEL", ""))  # empty = storyboard mode
    tts_voice: str = field(default_factory=lambda: _env("REELTOON_TTS_VOICE", "en-US-GuyNeural"))

    # Default cartoon style preset (see styles.py); "auto" lets Claude pick
    style: str = field(default_factory=lambda: _env("REELTOON_STYLE", "auto"))

    # Output video geometry
    width: int = 1080
    height: int = 1920

    # YouTube (OAuth client secrets + stored token)
    youtube_client_secrets: str = field(default_factory=lambda: _env("YOUTUBE_CLIENT_SECRETS", "secrets/youtube_client_secret.json"))
    youtube_token_file: str = field(default_factory=lambda: _env("YOUTUBE_TOKEN_FILE", "secrets/youtube_token.json"))

    # Instagram Graph API (a Business/Creator account linked to a FB app)
    ig_user_id: str = field(default_factory=lambda: _env("IG_USER_ID"))
    ig_access_token: str = field(default_factory=lambda: _env("IG_ACCESS_TOKEN"))

    # TikTok Content Posting API
    tiktok_access_token: str = field(default_factory=lambda: _env("TIKTOK_ACCESS_TOKEN"))

    def jobs_dir(self) -> Path:
        d = self.data_dir / "jobs"
        d.mkdir(parents=True, exist_ok=True)
        return d


settings = Settings()
