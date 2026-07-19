"""Analyze a reference reel: frames + metadata + transcript in,
a structured ReelAnalysis out."""

from __future__ import annotations

import json

from .llm import generate_structured, image_part
from .models import ReelAnalysis
from .store import Job

_SYSTEM = """You are a short-form video strategist. You break down reels into
their structural format so an animation team can create an ORIGINAL cartoon
that participates in the same trend/format.

Focus on the transferable mechanics: hook construction, beat structure,
pacing, and why it holds attention. Abstract the premise away from the
specific people, brands, products, or copyrighted characters shown — the
adaptation must not copy the source's footage, audio, or protected
expression, only its general format."""


def analyze(job: Job) -> ReelAnalysis:
    contents = [
        image_part(frame.read_bytes())
        for frame in sorted(job.path("frames").glob("frame_*.jpg"))
    ]

    meta = json.loads(job.path("source.json").read_text())
    transcript = job.path("transcript.txt")
    parts = [
        "Frames above are sampled evenly across the reel, in order.",
        f"Metadata: {json.dumps(meta, ensure_ascii=False)}",
    ]
    if transcript.exists():
        parts.append(f"Audio transcript: {transcript.read_text()}")
    parts.append("Analyze this reel's format.")
    contents.append("\n\n".join(parts))

    analysis: ReelAnalysis = generate_structured(_SYSTEM, contents, ReelAnalysis)
    job.path("analysis.json").write_text(analysis.model_dump_json(indent=2))
    return analysis
