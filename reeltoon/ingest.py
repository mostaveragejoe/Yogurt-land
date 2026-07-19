"""Download a reference reel and extract material for analysis.

The reference video is used ONLY as analysis input (frames + transcript go to
Claude). It is never re-uploaded or reused in the output video.
"""

from __future__ import annotations

import json
import subprocess
from pathlib import Path

from .store import Job

FRAME_COUNT = 8


def download(job: Job) -> dict:
    """Fetch the reel + metadata with yt-dlp. Returns the metadata dict."""
    import yt_dlp

    out = job.path("reference.%(ext)s")
    opts = {
        "outtmpl": str(out),
        "format": "mp4/bestvideo*+bestaudio/best",
        "merge_output_format": "mp4",
        "quiet": True,
        "noplaylist": True,
    }
    with yt_dlp.YoutubeDL(opts) as ydl:
        info = ydl.extract_info(job.source_url, download=True)

    meta = {
        "id": info.get("id"),
        "title": info.get("title"),
        "description": info.get("description"),
        "uploader": info.get("uploader"),
        "duration": info.get("duration"),
        "view_count": info.get("view_count"),
        "like_count": info.get("like_count"),
        "webpage_url": info.get("webpage_url"),
    }
    job.path("source.json").write_text(json.dumps(meta, indent=2))
    return meta


def extract_frames(job: Job) -> list[Path]:
    """Sample FRAME_COUNT frames evenly across the video with ffmpeg."""
    video = job.path("reference.mp4")
    if not video.exists():
        raise FileNotFoundError(f"{video} missing — run download first")

    duration = _probe_duration(video)
    frames_dir = job.path("frames")
    frames_dir.mkdir(exist_ok=True)

    paths = []
    for i in range(FRAME_COUNT):
        t = duration * (i + 0.5) / FRAME_COUNT
        out = frames_dir / f"frame_{i:02d}.jpg"
        subprocess.run(
            ["ffmpeg", "-y", "-loglevel", "error", "-ss", f"{t:.2f}", "-i", str(video),
             "-frames:v", "1", "-vf", "scale=-2:720", "-q:v", "4", str(out)],
            check=True,
        )
        paths.append(out)
    return paths


def transcribe(job: Job) -> str | None:
    """Transcribe the reference audio if faster-whisper is installed; else None."""
    try:
        from faster_whisper import WhisperModel
    except ImportError:
        return None

    model = WhisperModel("base", compute_type="int8")
    segments, _ = model.transcribe(str(job.path("reference.mp4")))
    text = " ".join(seg.text.strip() for seg in segments)
    job.path("transcript.txt").write_text(text)
    return text


def _probe_duration(video: Path) -> float:
    out = subprocess.run(
        ["ffprobe", "-v", "error", "-show_entries", "format=duration",
         "-of", "default=noprint_wrappers=1:nokey=1", str(video)],
        check=True, capture_output=True, text=True,
    )
    return float(out.stdout.strip())
