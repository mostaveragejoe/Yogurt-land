# Yogurt-land — ReelToon 🎬

Turn trending reels into **original cartoon shorts** — from your iPhone, with free tools only.

Paste a reel link into a GitHub issue. A few minutes later the bot replies with a finished 1080×1920 cartoon video and a ready-to-paste caption. Save it to your camera roll and post it anywhere.

Under the hood (all free): yt-dlp downloads the reel for analysis → Gemini (free API tier) breaks down the *format* — hook, beats, pacing → Gemini writes an original cartoon script and renders each shot as an image → edge-tts generates the voiceover → ffmpeg assembles the reel with a Ken Burns effect and burned-in captions → GitHub Actions runs the whole thing and posts the result back to your issue.

## Setup — once, ~2 minutes, $0

1. Get a free Gemini API key at **[aistudio.google.com](https://aistudio.google.com)** (Google account, no credit card).
2. In this repo: **Settings → Secrets and variables → Actions → New repository secret**, name it `GEMINI_API_KEY`, paste the key.

That's it. (Keep the repo public for unlimited free Actions minutes; private repos still get 2,000 free minutes/month, which is plenty.)

## Making a cartoon from your iPhone

1. Open this repo in the GitHub app or Safari.
2. **Issues → New issue → 🎬 Make a cartoon**.
3. Paste the reel URL (YouTube Shorts and TikTok links work best), optionally pick a style, submit.
4. In ~5–10 minutes the bot comments on your issue with the video link and caption.
5. Tap the link → **Share → Save Video** → post it from Instagram / TikTok / YouTube with the copy-pasted caption.

Cartoon styles: `rubber_hose` (1930s b&w), `saturday_morning` (80s TV), `anime`, `flat_modern` (vector), `claymation`, `pixel` — or `auto` to let the model pick per reel.

> **Instagram source links:** Instagram frequently blocks downloads from cloud servers. If a run fails on an IG link, grab the same trend from YouTube Shorts or TikTok instead.

## Why posting isn't 100% automatic

Instagram's and TikTok's posting APIs require business accounts, developer-app review, and (for TikTok) private-only posts until audited — none of which is free or low-friction. The save-and-share flow above is the most automation the platforms allow without that overhead. If you later want hands-off YouTube uploads, `reeltoon/publish/youtube.py` supports it with a one-time free Google OAuth setup on a computer.

## Running locally (optional)

```bash
pip install -r requirements.txt && sudo apt install ffmpeg   # or brew install ffmpeg
cp .env.example .env                                          # add your GEMINI_API_KEY

python -m reeltoon create "https://www.youtube.com/shorts/XXXX"
python -m reeltoon list
python -m reeltoon show <job-id>
```

Useful extras: `create --skip-render` stops after the script (read it with `show`, then `render <job-id>`); `pip install faster-whisper` adds audio transcription for sharper analysis.

## Content policy note

This tool deliberately imitates *formats and trends*, not content. The reference reel is downloaded solely as analysis input and never republished; the model is instructed to abstract the premise away from specific people, brands, and characters and to write original characters, dialogue, and visuals. Don't reuse trending *audio* you don't have rights to, and review outputs before posting — you're responsible for what your accounts publish.

## Layout

```
.github/
  ISSUE_TEMPLATE/make-cartoon.yml   the iPhone-facing request form
  workflows/make-cartoon.yml        runs the pipeline, replies with the video
reeltoon/
  ingest.py      yt-dlp download, frame sampling, optional whisper transcript
  analyze.py     Gemini vision → ReelAnalysis (format breakdown)
  reimagine.py   Gemini → CartoonScript (original shots, caption, hashtags)
  styles.py      cartoon style presets
  render.py      Gemini image generation per shot (consistent characters)
  voice.py       edge-tts voiceover (free)
  assemble.py    ffmpeg: Ken Burns, captions, audio, 1080×1920 concat
  store.py       job state machine
  publish/       optional YouTube auto-upload
  cli.py         create / list / show / render / approve / post
```
