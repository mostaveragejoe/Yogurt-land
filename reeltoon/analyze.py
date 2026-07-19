"""Analyze a reference reel with Claude: frames + metadata + transcript in,
a structured ReelAnalysis out."""

from __future__ import annotations

import base64
import json

import anthropic

from .config import settings
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
    client = anthropic.Anthropic()

    content: list[dict] = []
    for frame in sorted(job.path("frames").glob("frame_*.jpg")):
        content.append({
            "type": "image",
            "source": {
                "type": "base64",
                "media_type": "image/jpeg",
                "data": base64.standard_b64encode(frame.read_bytes()).decode(),
            },
        })

    meta = json.loads(job.path("source.json").read_text())
    transcript = job.path("transcript.txt")
    parts = [
        "Frames above are sampled evenly across the reel, in order.",
        f"Metadata: {json.dumps(meta, ensure_ascii=False)}",
    ]
    if transcript.exists():
        parts.append(f"Audio transcript: {transcript.read_text()}")
    parts.append("Analyze this reel's format.")
    content.append({"type": "text", "text": "\n\n".join(parts)})

    response = client.messages.parse(
        model=settings.claude_model,
        max_tokens=16000,
        thinking={"type": "adaptive"},
        system=_SYSTEM,
        messages=[{"role": "user", "content": content}],
        output_format=ReelAnalysis,
    )
    analysis: ReelAnalysis = response.parsed_output
    job.path("analysis.json").write_text(analysis.model_dump_json(indent=2))
    return analysis
