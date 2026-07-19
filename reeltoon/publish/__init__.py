"""Optional platform publishers.

The default (free, zero-config) posting flow is manual-with-one-tap: download
the finished video on your phone and share it to any app. YouTube auto-upload
is kept here as an optional extra for people willing to do a one-time free
Google OAuth setup on a computer.
"""

from __future__ import annotations

from ..models import CartoonScript
from ..store import Job

PLATFORMS = ("youtube",)


def publish(platform: str, job: Job, script: CartoonScript) -> dict:
    if platform == "youtube":
        from . import youtube as mod
    else:
        raise ValueError(f"Unknown platform '{platform}'. Options: {', '.join(PLATFORMS)}")
    return mod.publish(job, script)
