"""Generate the voiceover track with edge-tts (free Microsoft neural voices)."""

from __future__ import annotations

import asyncio
from pathlib import Path

from .config import settings
from .models import CartoonScript
from .store import Job


def synthesize_voiceover(job: Job, script: CartoonScript) -> Path | None:
    """One mp3 per shot that has a voiceover line (kept separate so assembly
    can align each line to its shot). Returns the shots' voice dir, or None
    if the script has no voiceover at all."""
    lines = [(i, s.voiceover) for i, s in enumerate(script.shots) if s.voiceover]
    if not lines:
        return None

    voice_dir = job.path("voice")
    voice_dir.mkdir(exist_ok=True)

    async def run() -> None:
        import edge_tts

        for i, text in lines:
            out = voice_dir / f"vo_{i:02d}.mp3"
            await edge_tts.Communicate(text, settings.tts_voice).save(str(out))

    asyncio.run(run())
    return voice_dir
