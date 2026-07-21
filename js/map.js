/* Canvas world map: rendering, pan/zoom, click hit-testing. */

const MapView = (() => {
  const WORLD_W = 1800, WORLD_H = 900;

  let canvas, ctx, dpr = 1;
  const cam = { x: WORLD_W * 0.28, y: WORLD_H * 0.32, zoom: 1 }; // start over N. America
  let minZoom = 0.4, maxZoom = 10;

  // input tracking
  let dragging = false, moved = false;
  let lastX = 0, lastY = 0, downX = 0, downY = 0;

  let onSelectCity = () => {};
  let onSelectPlane = () => {};
  let onSelectNothing = () => {};

  function project(lon, lat) {
    return {
      x: (lon + 180) / 360 * WORLD_W,
      y: (90 - lat) / 180 * WORLD_H,
    };
  }

  function toScreen(wx, wy) {
    return {
      x: (wx - cam.x) * cam.zoom + canvas.width / dpr / 2,
      y: (wy - cam.y) * cam.zoom + canvas.height / dpr / 2,
    };
  }

  function toWorld(sx, sy) {
    return {
      x: (sx - canvas.width / dpr / 2) / cam.zoom + cam.x,
      y: (sy - canvas.height / dpr / 2) / cam.zoom + cam.y,
    };
  }

  function cityScreen(city) {
    const w = project(city.lon, city.lat);
    return toScreen(w.x, w.y);
  }

  function clampCam() {
    const halfW = canvas.width / dpr / 2 / cam.zoom;
    const halfH = canvas.height / dpr / 2 / cam.zoom;
    const pad = 80;
    cam.x = Math.min(WORLD_W + pad - halfW * 0.2, Math.max(-pad + halfW * 0.2, cam.x));
    cam.y = Math.min(WORLD_H + pad - halfH * 0.2, Math.max(-pad + halfH * 0.2, cam.y));
  }

  function resize() {
    dpr = window.devicePixelRatio || 1;
    canvas.width = Math.round(window.innerWidth * dpr);
    canvas.height = Math.round(window.innerHeight * dpr);
    canvas.style.width = window.innerWidth + 'px';
    canvas.style.height = window.innerHeight + 'px';
    const fit = Math.min(window.innerWidth / WORLD_W, window.innerHeight / WORLD_H);
    minZoom = fit * 0.9;
    maxZoom = fit * 14;
    if (cam.zoom < minZoom) cam.zoom = fit * 2.2;
  }

  // ---------- hit testing ----------

  function hitTest(sx, sy) {
    const st = Game.state;
    // planes in flight first (they are on top)
    for (const plane of st.planes) {
      if (!plane.flight) continue;
      const pos = flightScreenPos(plane);
      if (Math.hypot(pos.x - sx, pos.y - sy) < 14) return { plane };
    }
    let best = null, bestD = 16;
    for (const city of CITIES) {
      const p = cityScreen(city);
      const d = Math.hypot(p.x - sx, p.y - sy);
      if (d < bestD) { bestD = d; best = city; }
    }
    if (best) return { city: best };
    return null;
  }

  function flightProgress(plane) {
    const f = plane.flight;
    const t = (Date.now() - f.departAt) / (f.arriveAt - f.departAt);
    return Math.min(1, Math.max(0, t));
  }

  function flightScreenPos(plane) {
    const f = plane.flight;
    const a = cityScreen(Game.cityByCode[f.from]);
    const b = cityScreen(Game.cityByCode[f.to]);
    const t = flightProgress(plane);
    return { x: a.x + (b.x - a.x) * t, y: a.y + (b.y - a.y) * t, ax: a.x, ay: a.y, bx: b.x, by: b.y };
  }

  // ---------- drawing ----------

  function drawContinents() {
    ctx.fillStyle = '#16283b';
    ctx.strokeStyle = '#27415c';
    ctx.lineWidth = 1;
    for (const ring of CONTINENTS) {
      ctx.beginPath();
      ring.forEach(([lon, lat], i) => {
        const w = project(lon, lat);
        const s = toScreen(w.x, w.y);
        if (i === 0) ctx.moveTo(s.x, s.y); else ctx.lineTo(s.x, s.y);
      });
      ctx.closePath();
      ctx.fill();
      ctx.stroke();
    }
  }

  function drawGrid() {
    ctx.strokeStyle = 'rgba(90,130,180,0.07)';
    ctx.lineWidth = 1;
    for (let lon = -180; lon <= 180; lon += 30) {
      const a = toScreen(project(lon, 90).x, project(lon, 90).y);
      const b = toScreen(project(lon, -90).x, project(lon, -90).y);
      ctx.beginPath(); ctx.moveTo(a.x, a.y); ctx.lineTo(b.x, b.y); ctx.stroke();
    }
    for (let lat = -90; lat <= 90; lat += 30) {
      const a = toScreen(project(-180, lat).x, project(-180, lat).y);
      const b = toScreen(project(180, lat).x, project(180, lat).y);
      ctx.beginPath(); ctx.moveTo(a.x, a.y); ctx.lineTo(b.x, b.y); ctx.stroke();
    }
  }

  function drawCities(selection) {
    const st = Game.state;
    const showLabels = cam.zoom > minZoom * 1.6;
    ctx.textAlign = 'center';
    for (const city of CITIES) {
      const owned = !!st.airports[city.code];
      const p = cityScreen(city);
      if (p.x < -30 || p.y < -30 || p.x > canvas.width / dpr + 30 || p.y > canvas.height / dpr + 30) continue;

      const selected = selection && selection.type === 'city' && selection.code === city.code;
      if (selected) {
        ctx.beginPath();
        ctx.arc(p.x, p.y, 11, 0, Math.PI * 2);
        ctx.strokeStyle = '#ffd75e';
        ctx.lineWidth = 2;
        ctx.stroke();
      }

      if (owned) {
        ctx.beginPath();
        ctx.arc(p.x, p.y, 5.5, 0, Math.PI * 2);
        ctx.fillStyle = '#ffd75e';
        ctx.fill();
        ctx.strokeStyle = '#0b1322';
        ctx.lineWidth = 1.5;
        ctx.stroke();
        const jobs = st.airports[city.code].jobs.length;
        if (jobs > 0 && showLabels) {
          ctx.fillStyle = '#7be08a';
          ctx.font = 'bold 10px sans-serif';
          ctx.fillText(String(jobs), p.x + 10, p.y - 6);
        }
      } else {
        ctx.beginPath();
        ctx.arc(p.x, p.y, 4, 0, Math.PI * 2);
        ctx.strokeStyle = '#5a6f92';
        ctx.lineWidth = 1.5;
        ctx.stroke();
      }

      if (showLabels) {
        ctx.fillStyle = owned ? '#dce7f5' : '#5a6f92';
        ctx.font = owned ? 'bold 11px sans-serif' : '10px sans-serif';
        ctx.fillText(city.code, p.x, p.y + 17);
      }
    }
  }

  function drawPlaneSprite(x, y, angle, color) {
    ctx.save();
    ctx.translate(x, y);
    ctx.rotate(angle);
    ctx.fillStyle = color;
    ctx.beginPath();
    ctx.moveTo(9, 0);      // nose
    ctx.lineTo(1, 3);
    ctx.lineTo(-1, 8);     // wing
    ctx.lineTo(-3, 8);
    ctx.lineTo(-3, 2);
    ctx.lineTo(-7, 4);     // tail
    ctx.lineTo(-8, 1);
    ctx.lineTo(-8, -1);
    ctx.lineTo(-7, -4);
    ctx.lineTo(-3, -2);
    ctx.lineTo(-3, -8);
    ctx.lineTo(-1, -8);
    ctx.lineTo(1, -3);
    ctx.closePath();
    ctx.fill();
    ctx.restore();
  }

  function drawFlights(selection) {
    const st = Game.state;
    for (const plane of st.planes) {
      if (!plane.flight) continue;
      const pos = flightScreenPos(plane);
      const selected = selection && selection.type === 'plane' && selection.id === plane.id;

      ctx.setLineDash([6, 6]);
      ctx.strokeStyle = selected ? 'rgba(255,215,94,0.8)' : 'rgba(140,180,230,0.45)';
      ctx.lineWidth = selected ? 2 : 1.2;
      ctx.beginPath();
      ctx.moveTo(pos.ax, pos.ay);
      ctx.lineTo(pos.bx, pos.by);
      ctx.stroke();
      ctx.setLineDash([]);

      const angle = Math.atan2(pos.by - pos.ay, pos.bx - pos.ax);
      drawPlaneSprite(pos.x, pos.y, angle, selected ? '#ffd75e' : '#9fc6f5');
    }
    // parked planes: small markers beside their airport
    const parkedCount = {};
    for (const plane of st.planes) {
      if (!plane.at) continue;
      parkedCount[plane.at] = (parkedCount[plane.at] || 0) + 1;
    }
    if (cam.zoom > minZoom * 1.6) {
      ctx.font = '10px sans-serif';
      ctx.textAlign = 'center';
      for (const [code, n] of Object.entries(parkedCount)) {
        const p = cityScreen(Game.cityByCode[code]);
        ctx.fillStyle = '#9fc6f5';
        ctx.fillText('✈'.repeat(Math.min(n, 3)) + (n > 3 ? '+' : ''), p.x - 14, p.y - 6);
      }
    }
  }

  function draw(selection) {
    const w = canvas.width / dpr, h = canvas.height / dpr;
    ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    const grad = ctx.createLinearGradient(0, 0, 0, h);
    grad.addColorStop(0, '#0a1526');
    grad.addColorStop(1, '#081020');
    ctx.fillStyle = grad;
    ctx.fillRect(0, 0, w, h);

    drawGrid();
    drawContinents();
    drawFlights(selection);
    drawCities(selection);
  }

  // ---------- input ----------

  function pointerDown(x, y) {
    dragging = true; moved = false;
    lastX = downX = x; lastY = downY = y;
    canvas.classList.add('dragging');
  }
  function pointerMove(x, y) {
    if (!dragging) return;
    if (Math.hypot(x - downX, y - downY) > 5) moved = true;
    cam.x -= (x - lastX) / cam.zoom;
    cam.y -= (y - lastY) / cam.zoom;
    clampCam();
    lastX = x; lastY = y;
  }
  function pointerUp(x, y) {
    canvas.classList.remove('dragging');
    if (!dragging) return;
    dragging = false;
    if (moved) return;
    const hit = hitTest(x, y);
    if (!hit) onSelectNothing();
    else if (hit.city) onSelectCity(hit.city.code);
    else if (hit.plane) onSelectPlane(hit.plane.id);
  }

  function init(canvasEl, handlers) {
    canvas = canvasEl;
    ctx = canvas.getContext('2d');
    onSelectCity = handlers.onSelectCity;
    onSelectPlane = handlers.onSelectPlane;
    onSelectNothing = handlers.onSelectNothing;

    resize();
    window.addEventListener('resize', resize);

    canvas.addEventListener('mousedown', e => pointerDown(e.clientX, e.clientY));
    window.addEventListener('mousemove', e => pointerMove(e.clientX, e.clientY));
    window.addEventListener('mouseup', e => pointerUp(e.clientX, e.clientY));

    canvas.addEventListener('wheel', e => {
      e.preventDefault();
      const before = toWorld(e.clientX, e.clientY);
      const factor = Math.exp(-e.deltaY * 0.0015);
      cam.zoom = Math.min(maxZoom, Math.max(minZoom, cam.zoom * factor));
      const after = toWorld(e.clientX, e.clientY);
      cam.x += before.x - after.x;
      cam.y += before.y - after.y;
      clampCam();
    }, { passive: false });

    canvas.addEventListener('touchstart', e => {
      if (e.touches.length === 1) {
        pointerDown(e.touches[0].clientX, e.touches[0].clientY);
      }
    }, { passive: true });
    canvas.addEventListener('touchmove', e => {
      if (e.touches.length === 1) {
        pointerMove(e.touches[0].clientX, e.touches[0].clientY);
        e.preventDefault();
      }
    }, { passive: false });
    canvas.addEventListener('touchend', e => {
      pointerUp(lastX, lastY);
    });
  }

  function focusCity(code) {
    const city = Game.cityByCode[code];
    if (!city) return;
    const w = project(city.lon, city.lat);
    cam.x = w.x; cam.y = w.y;
    if (cam.zoom < minZoom * 2) cam.zoom = minZoom * 2.6;
    clampCam();
  }

  return { init, draw, focusCity };
})();
