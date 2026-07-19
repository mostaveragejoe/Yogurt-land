"""Thin wrapper around the free Gemini API (google-genai SDK).

One client, two capabilities: structured generation (analysis + scripts) and
image generation (shot rendering). Retries transparently on free-tier rate
limits.
"""

from __future__ import annotations

import time
from pathlib import Path
from typing import Any, Sequence

from .config import settings

_client = None


def client():
    global _client
    if _client is None:
        from google import genai

        _client = genai.Client()  # reads GEMINI_API_KEY from the environment
    return _client


def _with_retry(fn, attempts: int = 5):
    """Free-tier friendly: back off and retry on rate-limit/overload errors."""
    for attempt in range(attempts):
        try:
            return fn()
        except Exception as e:  # noqa: BLE001 - SDK raises several error types
            msg = str(e)
            retryable = any(t in msg for t in ("429", "RESOURCE_EXHAUSTED", "503", "UNAVAILABLE"))
            if not retryable or attempt == attempts - 1:
                raise
            wait = 15 * (attempt + 1)
            print(f"  rate limited, retrying in {wait}s...")
            time.sleep(wait)


def generate_structured(system: str, contents: Sequence[Any], schema: type) -> Any:
    """Run a generation constrained to a pydantic schema; returns the parsed model."""
    from google.genai import types

    def run():
        response = client().models.generate_content(
            model=settings.text_model,
            contents=list(contents),
            config=types.GenerateContentConfig(
                system_instruction=system,
                response_mime_type="application/json",
                response_schema=schema,
            ),
        )
        if response.parsed is None:
            raise RuntimeError(f"Model returned no parseable output: {response.text!r:.200}")
        return response.parsed

    return _with_retry(run)


def image_part(data: bytes, mime_type: str = "image/jpeg"):
    from google.genai import types

    return types.Part.from_bytes(data=data, mime_type=mime_type)


def generate_image(prompt: str, out: Path, reference: bytes | None = None) -> Path:
    """Text-to-image. If `reference` is given, it is passed as an input image so
    characters stay consistent across shots."""
    from google.genai import types

    contents: list[Any] = []
    if reference is not None:
        contents.append(image_part(reference, "image/png"))
        contents.append(
            "Keep the exact same characters, art style, palette and line quality "
            "as this reference image. New scene: " + prompt
        )
    else:
        contents.append(prompt)

    config = types.GenerateContentConfig(response_modalities=["IMAGE"])
    try:
        config.image_config = types.ImageConfig(aspect_ratio="9:16")
    except Exception:
        pass  # older SDKs: prompt already asks for vertical framing; assemble crops

    def run():
        response = client().models.generate_content(
            model=settings.image_model, contents=contents, config=config
        )
        for cand in response.candidates or []:
            for part in cand.content.parts or []:
                if getattr(part, "inline_data", None) and part.inline_data.data:
                    out.write_bytes(part.inline_data.data)
                    return out
        raise RuntimeError("Image model returned no image data")

    return _with_retry(run)
