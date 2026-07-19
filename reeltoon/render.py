"""Render each shot of a CartoonScript to media via the Replicate API.

Two modes, chosen by config:
- Storyboard mode (default): text-to-image per shot; assemble.py animates the
  stills with a Ken Burns effect. Cheap and fast.
- Video mode (REELTOON_VIDEO_MODEL set): text-to-video per shot.
"""

from __future__ import annotations

import time
from pathlib import Path

import requests

from .config import settings
from .models import CartoonScript
from .store import Job
from .styles import style_prompt

_API = "https://api.replicate.com/v1"


def render_shots(job: Job, script: CartoonScript) -> list[Path]:
    if not settings.replicate_api_token:
        raise RuntimeError("REPLICATE_API_TOKEN is not set — cannot render shots")

    shots_dir = job.path("shots")
    shots_dir.mkdir(exist_ok=True)
    style = style_prompt(script.style_key)

    paths = []
    for i, shot in enumerate(script.shots):
        prompt = f"{shot.visual_prompt}, {style}, vertical 9:16 composition"
        if settings.video_model:
            out = shots_dir / f"shot_{i:02d}.mp4"
            _run_model(settings.video_model, {"prompt": prompt, "aspect_ratio": "9:16"}, out)
        else:
            out = shots_dir / f"shot_{i:02d}.png"
            _run_model(
                settings.image_model,
                {"prompt": prompt, "aspect_ratio": "9:16", "output_format": "png"},
                out,
            )
        paths.append(out)
    return paths


def _run_model(model: str, inputs: dict, out: Path) -> None:
    """Create a prediction on a named Replicate model and download its output."""
    headers = {
        "Authorization": f"Bearer {settings.replicate_api_token}",
        "Content-Type": "application/json",
        "Prefer": "wait=60",
    }
    resp = requests.post(
        f"{_API}/models/{model}/predictions",
        headers=headers,
        json={"input": inputs},
        timeout=120,
    )
    resp.raise_for_status()
    prediction = resp.json()

    # Poll if the synchronous wait didn't finish it
    while prediction["status"] not in ("succeeded", "failed", "canceled"):
        time.sleep(3)
        poll = requests.get(f"{_API}/predictions/{prediction['id']}", headers=headers, timeout=30)
        poll.raise_for_status()
        prediction = poll.json()

    if prediction["status"] != "succeeded":
        raise RuntimeError(f"Replicate prediction failed for {model}: {prediction.get('error')}")

    output = prediction["output"]
    url = output[0] if isinstance(output, list) else output
    media = requests.get(url, timeout=300)
    media.raise_for_status()
    out.write_bytes(media.content)
