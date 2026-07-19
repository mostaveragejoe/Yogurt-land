"""Platform publishers. Each module exposes publish(job, script) -> dict."""

from __future__ import annotations

from ..models import CartoonScript
from ..store import Job

PLATFORMS = ("youtube", "instagram", "tiktok")


def publish(platform: str, job: Job, script: CartoonScript) -> dict:
    if platform == "youtube":
        from . import youtube as mod
    elif platform == "instagram":
        from . import instagram as mod
    elif platform == "tiktok":
        from . import tiktok as mod
    else:
        raise ValueError(f"Unknown platform '{platform}'. Options: {', '.join(PLATFORMS)}")
    return mod.publish(job, script)
