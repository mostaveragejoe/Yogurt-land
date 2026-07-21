# Yogurt-land — Yogurt Air ✈

A tiny, high-level clone of *Pocket Planes* that runs entirely in the browser —
no build step, no dependencies, no server required.

## Play

Open `index.html` in any modern browser, or serve the folder:

```sh
python3 -m http.server 8000
# then visit http://localhost:8000
```

## How it works

You run a small airline on a stylized world map:

- **Airports** — you start with Boston, New York, and Washington. Click gray
  dots on the map to buy new airports; bigger cities cost more but generate
  more jobs.
- **Jobs** — each airport's job board fills over time with passengers 👤 and
  cargo 📦 headed to other airports you own. Pay scales with distance.
- **Planes** — each model has passenger seats, cargo slots, speed, and range.
  Select a parked plane, load jobs, and fly to any owned airport in range.
  Flights happen in real time on the map.
- **Layovers** — jobs that aren't going to your destination stay onboard, or
  can be unloaded at an intermediate airport for a 25% pay cut.
- **Coins & bux** — deliveries pay coins; rare jobs and level-ups pay premium
  bux (Ƀ), which can rush flights to finish instantly or be exchanged for
  coins in the Store.
- **Levels** — deliveries earn XP; leveling up unlocks bigger, faster planes.

Progress autosaves to `localStorage`. Use **Reset** in the top bar to start
over.

## Controls

- Drag to pan, scroll wheel to zoom (single-finger drag on touch).
- Click a city dot or a flying plane to inspect it.

## Code layout

| File | Purpose |
| --- | --- |
| `js/data.js` | Cities, plane models, stylized continent outlines |
| `js/game.js` | Simulation: economy, jobs, flights, XP, save/load |
| `js/map.js` | Canvas rendering, pan/zoom, hit-testing |
| `js/ui.js` | HUD, side panel, modals, toasts |
| `js/main.js` | Boot and main loop |

## Simplifications (it's a *high-level* clone)

- Flight paths are drawn as straight lines on an equirectangular map (no
  great-circle wrapping across the dateline), though distances and pay use
  real haversine distance.
- No plane parts/crafting, events, or multiplayer from the original game.
- The world map outlines are deliberately rough — it's a game, not a globe.
