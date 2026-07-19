"""Turn a ReelAnalysis into an original cartoon short: script, shot list,
caption, hashtags — via structured generation."""

from __future__ import annotations

from .config import settings
from .llm import generate_structured
from .models import CartoonScript, ReelAnalysis
from .store import Job
from .styles import STYLES

_SYSTEM = """You are a comedy writer and storyboard artist for a cartoon
channel that reimagines trending reel formats as original animated shorts.

Rules:
- Write ORIGINAL characters, jokes, and dialogue. Never reproduce the source
  reel's specific people, brands, copyrighted characters, footage, or audio.
  You inherit only the format: the hook style, beat structure, and pacing.
- Target 15-40 seconds total across all shots.
- The first shot must land the hook within ~1.5 seconds.
- visual_prompt fields must be self-contained scene descriptions (characters,
  action, camera, composition for a 9:16 vertical frame) WITHOUT style
  adjectives — the style preset is appended automatically, and characters must
  be described consistently across shots so they look the same in every image.
- Keep overlay_text punchy (max ~8 words) and voiceover lines natural to read
  aloud at a normal pace within the shot's duration."""


def write_script(job: Job, style: str | None = None) -> CartoonScript:
    analysis = ReelAnalysis.model_validate_json(job.path("analysis.json").read_text())

    style = style or settings.style
    if style == "auto":
        style_instruction = (
            "Pick the style preset that best fits the material from this list:\n"
            + "\n".join(f"- {k}: {v}" for k, v in STYLES.items())
        )
    else:
        style_instruction = f"Use the style preset key '{style}'."

    prompt = (
        "Here is the format analysis of a trending reel:\n\n"
        f"{analysis.model_dump_json(indent=2)}\n\n"
        f"{style_instruction}\n\n"
        "Write an original cartoon short that participates in this format. "
        "Make it genuinely funny or satisfying, not a generic imitation."
    )

    script: CartoonScript = generate_structured(_SYSTEM, [prompt], CartoonScript)
    if script.style_key not in STYLES:
        script.style_key = next(iter(STYLES))
    job.path("script.json").write_text(script.model_dump_json(indent=2))
    return script
