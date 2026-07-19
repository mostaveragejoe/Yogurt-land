"""Environment-driven configuration.

The only required setting is GEMINI_API_KEY (free key from
https://aistudio.google.com — the google-genai SDK reads it from the
environment automatically). Everything else has sensible defaults.
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

    # Gemini models (free tier covers both)
    text_model: str = field(default_factory=lambda: _env("REELTOON_TEXT_MODEL", "gemini-2.5-flash"))
    image_model: str = field(default_factory=lambda: _env("REELTOON_IMAGE_MODEL", "gemini-2.5-flash-image"))

    # Voiceover (edge-tts, free)
    tts_voice: str = field(default_factory=lambda: _env("REELTOON_TTS_VOICE", "en-US-GuyNeural"))

    # Default cartoon style preset (see styles.py); "auto" lets the model pick
    style: str = field(default_factory=lambda: _env("REELTOON_STYLE", "auto"))

    # Output video geometry
    width: int = 1080
    height: int = 1920

    def jobs_dir(self) -> Path:
        d = self.data_dir / "jobs"
        d.mkdir(parents=True, exist_ok=True)
        return d


settings = Settings()
