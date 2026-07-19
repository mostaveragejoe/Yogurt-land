"""Job persistence: each job is a directory under data/jobs/<job_id>/.

Files inside a job dir:
    meta.json       status + timestamps + post results
    source.json     yt-dlp metadata of the reference reel
    reference.mp4   downloaded reference (analysis only, never republished)
    frames/         sampled frames for vision analysis
    transcript.txt  optional audio transcript
    analysis.json   ReelAnalysis
    script.json     CartoonScript
    shots/          rendered shot images/clips
    voiceover.mp3   generated narration
    final.mp4       assembled reel
"""

from __future__ import annotations

import json
import re
import time
import uuid
from dataclasses import asdict, dataclass, field
from pathlib import Path
from typing import Optional

from .config import settings

# pending_render -> rendered (in review) -> approved -> posted
STATUSES = ["created", "analyzed", "scripted", "rendered", "approved", "posted", "failed"]


@dataclass
class Job:
    id: str
    source_url: str
    status: str = "created"
    created_at: float = field(default_factory=time.time)
    updated_at: float = field(default_factory=time.time)
    error: Optional[str] = None
    posts: dict = field(default_factory=dict)  # platform -> result info

    @property
    def dir(self) -> Path:
        return settings.jobs_dir() / self.id

    def path(self, name: str) -> Path:
        return self.dir / name

    def save(self) -> None:
        self.updated_at = time.time()
        self.dir.mkdir(parents=True, exist_ok=True)
        self.path("meta.json").write_text(json.dumps(asdict(self), indent=2))

    def set_status(self, status: str, error: str | None = None) -> None:
        self.status = status
        self.error = error
        self.save()


def new_job(source_url: str) -> Job:
    slug = re.sub(r"[^a-z0-9]+", "-", source_url.lower()).strip("-")[-24:]
    job = Job(id=f"{time.strftime('%Y%m%d')}-{slug}-{uuid.uuid4().hex[:6]}", source_url=source_url)
    job.save()
    return job


def load_job(job_id: str) -> Job:
    meta = settings.jobs_dir() / job_id / "meta.json"
    if not meta.exists():
        raise FileNotFoundError(f"No such job: {job_id}")
    return Job(**json.loads(meta.read_text()))


def list_jobs() -> list[Job]:
    jobs = []
    for meta in sorted(settings.jobs_dir().glob("*/meta.json")):
        jobs.append(Job(**json.loads(meta.read_text())))
    return sorted(jobs, key=lambda j: j.created_at, reverse=True)
