"""Assemble rendered shots + voiceover into the final 1080x1920 reel (ffmpeg).

Each shot becomes an intermediate clip (Ken Burns pan/zoom for stills, crop
for video shots, overlay text burned in, voiceover or silence as audio), then
everything is concatenated.
"""

from __future__ import annotations

import subprocess
from pathlib import Path

from .config import settings
from .models import CartoonScript, Shot
from .store import Job

FPS = 30

_FONT_CANDIDATES = [
    "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf",
    "/usr/share/fonts/dejavu/DejaVuSans-Bold.ttf",
    "/System/Library/Fonts/Supplemental/Arial Bold.ttf",
    "C:/Windows/Fonts/arialbd.ttf",
]


def assemble(job: Job, script: CartoonScript) -> Path:
    shots_dir = job.path("shots")
    voice_dir = job.path("voice")
    clips_dir = job.path("clips")
    clips_dir.mkdir(exist_ok=True)

    clip_paths = []
    for i, shot in enumerate(script.shots):
        image = shots_dir / f"shot_{i:02d}.png"
        video = shots_dir / f"shot_{i:02d}.mp4"
        vo = voice_dir / f"vo_{i:02d}.mp3"
        clip = clips_dir / f"clip_{i:02d}.mp4"
        _build_clip(
            source=video if video.exists() else image,
            is_video=video.exists(),
            shot=shot,
            voiceover=vo if vo.exists() else None,
            out=clip,
        )
        clip_paths.append(clip)

    concat_list = clips_dir / "concat.txt"
    concat_list.write_text("".join(f"file '{p.resolve()}'\n" for p in clip_paths))

    final = job.path("final.mp4")
    subprocess.run(
        ["ffmpeg", "-y", "-loglevel", "error", "-f", "concat", "-safe", "0",
         "-i", str(concat_list), "-c", "copy", str(final)],
        check=True,
    )
    return final


def _build_clip(source: Path, is_video: bool, shot: Shot, voiceover: Path | None, out: Path) -> None:
    w, h, dur = settings.width, settings.height, max(shot.duration_s, 0.5)

    if is_video:
        vf = f"scale={w}:{h}:force_original_aspect_ratio=increase,crop={w}:{h},fps={FPS}"
    else:
        # Ken Burns: oversample the still, then slow zoom-in
        frames = int(dur * FPS)
        vf = (
            f"scale={w * 2}:{h * 2}:force_original_aspect_ratio=increase,"
            f"crop={w * 2}:{h * 2},"
            f"zoompan=z='min(zoom+0.0012,1.18)':x='iw/2-(iw/zoom/2)':y='ih/2-(ih/zoom/2)'"
            f":d={frames}:s={w}x{h}:fps={FPS}"
        )

    if shot.overlay_text:
        vf += "," + _drawtext(shot.overlay_text, w)

    cmd = ["ffmpeg", "-y", "-loglevel", "error"]
    if is_video:
        cmd += ["-i", str(source)]
    else:
        cmd += ["-loop", "1", "-i", str(source)]

    if voiceover:
        cmd += ["-i", str(voiceover)]
        audio = ["-af", "apad", "-map", "0:v", "-map", "1:a"]
    else:
        cmd += ["-f", "lavfi", "-i", "anullsrc=channel_layout=stereo:sample_rate=44100"]
        audio = ["-map", "0:v", "-map", "1:a"]

    cmd += ["-vf", vf, *audio, "-t", f"{dur:.2f}",
            "-c:v", "libx264", "-pix_fmt", "yuv420p", "-preset", "medium",
            "-c:a", "aac", "-b:a", "128k", "-ar", "44100", str(out)]
    subprocess.run(cmd, check=True)


def _drawtext(text: str, width: int) -> str:
    escaped = text.replace("\\", "\\\\").replace("'", "\\'").replace(":", "\\:").replace("%", "\\%")
    font = next((f for f in _FONT_CANDIDATES if Path(f).exists()), None)
    font_arg = f"fontfile='{font}':" if font else "font='Sans':"
    return (
        f"drawtext={font_arg}text='{escaped}':fontsize={width // 14}:fontcolor=white:"
        f"borderw=6:bordercolor=black:x=(w-text_w)/2:y=h*0.72"
    )
