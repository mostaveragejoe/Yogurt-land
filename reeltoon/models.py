"""Pydantic models for the pipeline's structured data.

ReelAnalysis and CartoonScript are produced by Claude via structured outputs,
so their schemas double as the contract for those calls.
"""

from __future__ import annotations

from typing import List, Optional

from pydantic import BaseModel, Field


class Beat(BaseModel):
    """One narrative beat of the source reel."""

    start_s: float = Field(description="Approximate start time in seconds")
    end_s: float = Field(description="Approximate end time in seconds")
    description: str = Field(description="What happens in this beat")
    on_screen_text: Optional[str] = Field(default=None, description="Any text overlay shown during the beat")


class ReelAnalysis(BaseModel):
    """Structural breakdown of a source reel: the *format*, not the content."""

    summary: str = Field(description="One-paragraph summary of the reel")
    format_type: str = Field(description="Named format, e.g. 'POV skit', 'before/after', 'listicle', 'talking head with b-roll'")
    hook: str = Field(description="What grabs attention in the first 1-2 seconds and why it works")
    beats: List[Beat] = Field(description="The reel's narrative beats in order")
    audio_style: str = Field(description="Voiceover / dialogue / trending-sound / music-only, plus tone")
    pacing: str = Field(description="Cut frequency and rhythm, e.g. 'fast cuts every 1-2s'")
    why_it_works: str = Field(description="The psychological/comedic mechanics driving engagement")
    remixable_premise: str = Field(description="The underlying premise or joke structure, abstracted away from the specific people/products shown, suitable for an original reinterpretation")


class Shot(BaseModel):
    """One shot of the cartoon adaptation."""

    duration_s: float = Field(description="Shot length in seconds, typically 1.5-4")
    visual_prompt: str = Field(description="Detailed text-to-image/video prompt for this shot, no style words (style is appended separately), 9:16 composition")
    overlay_text: Optional[str] = Field(default=None, description="Short caption burned onto this shot, if any (max ~8 words)")
    voiceover: Optional[str] = Field(default=None, description="Voiceover line spoken during this shot, if any")


class CartoonScript(BaseModel):
    """An original cartoon short inspired by the source reel's format."""

    title: str = Field(description="Internal working title")
    style_key: str = Field(description="One of the offered style preset keys")
    logline: str = Field(description="One sentence describing the cartoon short")
    shots: List[Shot] = Field(description="Ordered shot list, total duration 15-40 seconds")
    caption: str = Field(description="Post caption text, engaging, 1-3 sentences")
    hashtags: List[str] = Field(description="5-10 hashtags without the # prefix")
    music_mood: str = Field(description="Mood for background music, e.g. 'upbeat ukulele', 'tense synth'")
