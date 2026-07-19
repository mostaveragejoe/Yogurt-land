"""ReelToon command-line interface.

Typical flow:
    python -m reeltoon create <reel-url>       # analyze -> script -> render
    python -m reeltoon list                    # see the review queue
    python -m reeltoon show <job-id>           # inspect script + video path

Batch mode:
    python -m reeltoon autopilot --urls-file trending.txt

From an iPhone, don't use this CLI at all — open a GitHub issue with the
"Make a cartoon" template and let the make-cartoon workflow run it (README).
"""

from __future__ import annotations

import argparse
import sys
import traceback

from .models import CartoonScript
from .store import Job, list_jobs, load_job, new_job
from .styles import STYLES


def cmd_create(args) -> None:
    job = new_job(args.url)
    print(f"[{job.id}] created")
    try:
        _run_pipeline(job, style=args.style, skip_render=args.skip_render)
    except Exception as e:
        job.set_status("failed", error=f"{type(e).__name__}: {e}")
        traceback.print_exc()
        sys.exit(1)


def _run_pipeline(job: Job, style: str | None, skip_render: bool = False) -> None:
    from . import analyze, assemble, ingest, reimagine, render, voice

    print(f"[{job.id}] downloading reference...")
    ingest.download(job)
    ingest.extract_frames(job)
    if ingest.transcribe(job):
        print(f"[{job.id}] transcribed audio")

    print(f"[{job.id}] analyzing format with Claude...")
    analysis = analyze.analyze(job)
    job.set_status("analyzed")
    print(f"[{job.id}] format: {analysis.format_type} — {analysis.hook[:80]}")

    print(f"[{job.id}] writing original cartoon script...")
    script = reimagine.write_script(job, style=style)
    job.set_status("scripted")
    print(f"[{job.id}] script: '{script.title}' ({script.style_key}, {len(script.shots)} shots)")

    if skip_render:
        print(f"[{job.id}] --skip-render: stopping after script. Run 'render {job.id}' later.")
        return

    print(f"[{job.id}] rendering {len(script.shots)} shots...")
    render.render_shots(job, script)
    voice.synthesize_voiceover(job, script)
    final = assemble.assemble(job, script)
    job.set_status("rendered")
    print(f"[{job.id}] ready for review: {final}")


def cmd_render(args) -> None:
    from . import assemble, render, voice

    job = load_job(args.job_id)
    script = _load_script(job)
    render.render_shots(job, script)
    voice.synthesize_voiceover(job, script)
    final = assemble.assemble(job, script)
    job.set_status("rendered")
    print(f"[{job.id}] ready for review: {final}")


def cmd_list(_args) -> None:
    jobs = list_jobs()
    if not jobs:
        print("No jobs yet. Start with: python -m reeltoon create <reel-url>")
        return
    for job in jobs:
        posts = ",".join(job.posts) if job.posts else "-"
        print(f"{job.id:<45} {job.status:<10} posted:{posts}  {job.source_url}")


def cmd_show(args) -> None:
    job = load_job(args.job_id)
    print(f"id:      {job.id}\nstatus:  {job.status}\nsource:  {job.source_url}")
    if job.error:
        print(f"error:   {job.error}")
    script_path = job.path("script.json")
    if script_path.exists():
        script = CartoonScript.model_validate_json(script_path.read_text())
        print(f"\ntitle:   {script.title}\nstyle:   {script.style_key}\nlogline: {script.logline}")
        print(f"caption: {script.caption}\ntags:    {' '.join('#' + t for t in script.hashtags)}\n")
        for i, s in enumerate(script.shots):
            print(f"  shot {i}: [{s.duration_s:.1f}s] {s.visual_prompt[:90]}")
            if s.overlay_text:
                print(f"          text: {s.overlay_text}")
            if s.voiceover:
                print(f"          vo:   {s.voiceover}")
    final = job.path("final.mp4")
    if final.exists():
        print(f"\nvideo:   {final}")


def cmd_approve(args) -> None:
    job = load_job(args.job_id)
    if job.status != "rendered":
        print(f"[{job.id}] is '{job.status}', expected 'rendered'", file=sys.stderr)
        sys.exit(1)
    job.set_status("approved")
    print(f"[{job.id}] approved")


def cmd_post(args) -> None:
    job = load_job(args.job_id)
    if job.status not in ("approved", "posted"):
        print(f"[{job.id}] must be approved first (status: {job.status})", file=sys.stderr)
        sys.exit(1)
    _post(job, args.to.split(","))


def _post(job: Job, platforms: list[str]) -> None:
    from . import publish

    script = _load_script(job)
    for platform in platforms:
        platform = platform.strip()
        if platform in job.posts:
            print(f"[{job.id}] already posted to {platform}, skipping")
            continue
        print(f"[{job.id}] posting to {platform}...")
        try:
            result = publish.publish(platform, job, script)
        except Exception as e:
            print(f"[{job.id}] {platform} FAILED: {e}", file=sys.stderr)
            continue
        job.posts[platform] = result
        job.set_status("posted")
        print(f"[{job.id}] {platform} OK: {result}")


def cmd_autopilot(args) -> None:
    urls = [
        line.strip()
        for line in open(args.urls_file)
        if line.strip() and not line.startswith("#")
    ]
    seen = {j.source_url for j in list_jobs()}
    for url in urls:
        if url in seen:
            print(f"skip (already processed): {url}")
            continue
        job = new_job(url)
        try:
            _run_pipeline(job, style=args.style)
        except Exception as e:
            job.set_status("failed", error=f"{type(e).__name__}: {e}")
            traceback.print_exc()
            continue
        if args.auto_approve and args.post_to:
            job.set_status("approved")
            _post(job, args.post_to.split(","))
    print("autopilot run complete")


def _load_script(job: Job) -> CartoonScript:
    return CartoonScript.model_validate_json(job.path("script.json").read_text())


def main() -> None:
    parser = argparse.ArgumentParser(prog="reeltoon", description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = parser.add_subparsers(dest="command", required=True)

    p = sub.add_parser("create", help="run the full pipeline for one reel URL")
    p.add_argument("url")
    p.add_argument("--style", choices=list(STYLES) + ["auto"], default=None)
    p.add_argument("--skip-render", action="store_true", help="stop after scripting (review before spending on renders)")
    p.set_defaults(func=cmd_create)

    p = sub.add_parser("render", help="(re)render a scripted job")
    p.add_argument("job_id")
    p.set_defaults(func=cmd_render)

    p = sub.add_parser("list", help="list jobs / review queue")
    p.set_defaults(func=cmd_list)

    p = sub.add_parser("show", help="show a job's script and outputs")
    p.add_argument("job_id")
    p.set_defaults(func=cmd_show)

    p = sub.add_parser("approve", help="approve a rendered job for posting")
    p.add_argument("job_id")
    p.set_defaults(func=cmd_approve)

    p = sub.add_parser("post", help="post an approved job")
    p.add_argument("job_id")
    p.add_argument("--to", default="youtube", help="currently supported: youtube")
    p.set_defaults(func=cmd_post)

    p = sub.add_parser("autopilot", help="batch-process a file of reel URLs")
    p.add_argument("--urls-file", required=True)
    p.add_argument("--style", choices=list(STYLES) + ["auto"], default=None)
    p.add_argument("--post-to", default=None, help="comma-separated platforms")
    p.add_argument("--auto-approve", action="store_true",
                   help="post WITHOUT human review (use only once you trust the output)")
    p.set_defaults(func=cmd_autopilot)

    args = parser.parse_args()
    args.func(args)


if __name__ == "__main__":
    main()
