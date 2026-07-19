# Yogurt-land — ReelToon 🎬

Turn trending reels into **original cartoon shorts** — and post them semi-automatically.

Give it the URL of a popular reel. It:

1. **Ingests** the reel (yt-dlp) and samples frames + transcript — *analysis only, the source footage is never reused*.
2. **Analyzes** the format with Claude: hook, beat structure, pacing, why it works.
3. **Reimagines** it as an original cartoon script (characters, jokes, shot list, caption, hashtags) in one of six cartoon styles.
4. **Renders** each shot with an AI image/video model (Replicate), generates a voiceover (edge-tts), and assembles a 1080×1920 reel with burned-in captions (ffmpeg).
5. **Queues** the result for review, then **posts** to YouTube Shorts, Instagram Reels, and/or TikTok on approval — or fully automatically in autopilot mode.

## Quick start

```bash
pip install -r requirements.txt
sudo apt install ffmpeg          # or brew install ffmpeg

cp .env.example .env             # fill in ANTHROPIC_API_KEY + REPLICATE_API_TOKEN

# Full pipeline for one reel
python -m reeltoon create "https://www.instagram.com/reel/XXXX/"

# Cheap dry-run: analysis + script only, no render spend
python -m reeltoon create "https://..." --skip-render
python -m reeltoon show <job-id>       # read the script
python -m reeltoon render <job-id>     # render when happy

# Review & post
python -m reeltoon list
python -m reeltoon approve <job-id>
python -m reeltoon post <job-id> --to youtube,instagram
```

## Cartoon styles

`rubber_hose` (1930s b&w), `saturday_morning` (80s TV), `anime`, `flat_modern` (vector), `claymation`, `pixel` — pick with `--style`, or leave on `auto` and Claude picks the best fit per reel.

## Automation

**Batch / cron:** put reel URLs in a file (one per line) and run:

```bash
python -m reeltoon autopilot --urls-file trending.txt
```

Already-processed URLs are skipped, so it's safe to run on a schedule (cron, systemd timer). Drafts land in the review queue; add `--auto-approve --post-to youtube` only once you trust the output enough to publish unreviewed.

**GitHub Actions:** `.github/workflows/autopilot.yml` runs the same thing in CI on a schedule (disabled by default — see comments in the file for setup).

## Platform setup

| Platform | What you need |
|---|---|
| YouTube Shorts | Google Cloud project with YouTube Data API v3, OAuth desktop client JSON at `secrets/youtube_client_secret.json`. First `post` opens a browser once; the token is cached after that. |
| Instagram Reels | Business/Creator account linked to a Facebook app; `IG_USER_ID` + long-lived `IG_ACCESS_TOKEN` with `instagram_content_publish`. Uses resumable upload — no public hosting needed. |
| TikTok | Developer app with the Content Posting API + `video.publish` scope → `TIKTOK_ACCESS_TOKEN`. Note: unaudited TikTok apps can only post as private (SELF_ONLY). |

## How rendering works

- **Storyboard mode (default):** one image per shot (`flux-schnell` by default, ~$0.003/image), animated with a Ken Burns zoom. Fast and nearly free.
- **Video mode:** set `REELTOON_VIDEO_MODEL` to any Replicate text-to-video model to render true animated shots instead. Better results, higher cost.
- Voiceover is free via `edge-tts`; set `REELTOON_TTS_VOICE` to taste.

## Content policy note

This tool deliberately imitates *formats and trends*, not content. The reference reel is downloaded solely as analysis input; Claude is instructed to abstract the premise away from specific people, brands, and characters and to write original characters, dialogue, and visuals. Don't point it at content whose format itself is protected expression, don't reuse trending *audio* you don't have rights to, and review outputs before posting — you're responsible for what your accounts publish.

## Layout

```
reeltoon/
  ingest.py      yt-dlp download, frame sampling, optional whisper transcript
  analyze.py     Claude vision → ReelAnalysis (format breakdown)
  reimagine.py   Claude → CartoonScript (original shots, caption, hashtags)
  styles.py      cartoon style presets
  render.py      Replicate text-to-image / text-to-video per shot
  voice.py       edge-tts voiceover
  assemble.py    ffmpeg: Ken Burns, captions, audio, 1080×1920 concat
  store.py       job state machine (created → … → rendered → approved → posted)
  publish/       youtube.py, instagram.py, tiktok.py
  cli.py         create / list / show / approve / post / autopilot
```
