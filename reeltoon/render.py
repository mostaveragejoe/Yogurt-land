"""Render each shot of a CartoonScript to a still image with Gemini's free
image model. The first shot's image is fed back as a reference for later shots
so characters stay visually consistent; assemble.py animates the stills with a
Ken Burns effect."""

from __future__ import annotations

import time
from pathlib import Path

from .llm import generate_image
from .models import CartoonScript
from .store import Job
from .styles import style_prompt


def render_shots(job: Job, script: CartoonScript) -> list[Path]:
    shots_dir = job.path("shots")
    shots_dir.mkdir(exist_ok=True)
    style = style_prompt(script.style_key)

    paths: list[Path] = []
    reference: bytes | None = None
    for i, shot in enumerate(script.shots):
        out = shots_dir / f"shot_{i:02d}.png"
        prompt = (
            f"{shot.visual_prompt}. {style}. "
            "Vertical 9:16 composition, single frame, no text or captions in the image."
        )
        print(f"  rendering shot {i + 1}/{len(script.shots)}")
        generate_image(prompt, out, reference=reference)
        if reference is None:
            reference = out.read_bytes()
        paths.append(out)
        time.sleep(2)  # stay well under free-tier requests-per-minute limits
    return paths
