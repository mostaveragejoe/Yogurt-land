/* DOM: HUD, side panel, modals, toasts. */

const UI = (() => {
  // selection: {type:'city', code} | {type:'plane', id} | null
  let selection = null;
  let panelDirty = false;

  const $ = id => document.getElementById(id);
  const esc = s => String(s).replace(/[&<>"]/g, ch => (
    { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[ch]
  ));

  function fmt(n) { return n.toLocaleString('en-US'); }

  function jobIcon(job) { return job.type === 'p' ? '👤' : '📦'; }

  function payHtml(job) {
    return job.isBux
      ? `<span class="pay bux">${job.pay}Ƀ</span>`
      : `<span class="pay">${fmt(job.pay)}¢</span>`;
  }

  function etaText(ms) {
    const s = Math.max(0, Math.ceil(ms / 1000));
    if (s >= 60) return `${Math.floor(s / 60)}m ${s % 60}s`;
    return `${s}s`;
  }

  // ---------- toasts ----------

  function toast(msg, kind) {
    const el = document.createElement('div');
    el.className = 'toast' + (kind ? ' ' + kind : '');
    el.textContent = msg;
    $('toasts').appendChild(el);
    setTimeout(() => el.remove(), 5000);
  }

  function drainToasts() {
    while (Game.toasts.length) {
      const t = Game.toasts.shift();
      toast(t.msg, t.kind);
    }
  }

  // ---------- HUD ----------

  function refreshHud() {
    const st = Game.state;
    $('hud-coins').textContent = fmt(st.coins);
    $('hud-bux').textContent = fmt(st.bux);
    $('hud-level').textContent = st.level;
    const pct = Math.min(100, 100 * st.xp / Game.xpToNext(st.level));
    $('xpbar-fill').style.width = pct.toFixed(1) + '%';
  }

  // ---------- selection ----------

  function select(sel) {
    selection = sel;
    panelDirty = true;
  }

  function selectCity(code) { select({ type: 'city', code }); }
  function selectPlane(id) { select({ type: 'plane', id }); }
  function clearSelection() {
    selection = null;
    $('panel').classList.add('hidden');
  }

  function getSelection() { return selection; }

  // ---------- panel rendering ----------

  function slotBar(plane) {
    const model = Game.planeModel(plane);
    let html = '<span class="slotbar">';
    const pUsed = Game.cargoCount(plane, 'p');
    const cUsed = Game.cargoCount(plane, 'c');
    for (let i = 0; i < model.p; i++) html += `<span class="slot${i < pUsed ? ' filled-p' : ''}"></span>`;
    for (let i = 0; i < model.c; i++) html += `<span class="slot${i < cUsed ? ' filled-c' : ''}"></span>`;
    return html + '</span>';
  }

  function renderCityPanel(code) {
    const st = Game.state;
    const city = Game.cityByCode[code];
    const owned = !!st.airports[code];
    let html = `<h2>${esc(city.name)} <span class="dim">${city.code}</span></h2>`;
    html += `<div class="sub">Population ${city.pop}M · ${owned ? '<span class="tag owned">OWNED</span>' : '<span class="tag">CLOSED</span>'}</div>`;

    if (!owned) {
      const cost = Game.airportCost(city);
      html += `<p style="margin:10px 0">Open this airport to send and receive jobs here.</p>`;
      html += `<button class="primary" data-act="buy-airport" data-code="${code}" ${st.coins < cost ? 'disabled' : ''}>Open airport — ${fmt(cost)}¢</button>`;
      $('panel-body').innerHTML = html;
      return;
    }

    const parked = Game.planesAt(code);
    html += `<h3>Planes here (${parked.length})</h3>`;
    if (!parked.length) html += `<div class="dim">No planes at this airport.</div>`;
    for (const plane of parked) {
      html += `<div class="row clickable" data-act="sel-plane" data-id="${plane.id}">
        <span class="grow">✈ ${esc(plane.name)}</span>${slotBar(plane)}</div>`;
    }

    const jobs = st.airports[code].jobs;
    html += `<h3>Job board (${jobs.length}/${Game.maxJobs(city)})</h3>`;
    if (!jobs.length) html += `<div class="dim">No jobs right now — more arrive over time.</div>`;
    for (const job of jobs) {
      html += `<div class="row">
        <span>${jobIcon(job)}</span>
        <span class="grow">→ ${job.dest} <span class="dim">${fmt(job.dist)} km${job.layover ? ' · layover' : ''}</span></span>
        ${payHtml(job)}</div>`;
    }
    html += `<div class="sub" style="margin-top:8px">Select a plane above to load jobs onto it.</div>`;
    $('panel-body').innerHTML = html;
  }

  function renderPlanePanel(id) {
    const st = Game.state;
    const plane = st.planes.find(p => p.id === id);
    if (!plane) { clearSelection(); return; }
    const model = Game.planeModel(plane);
    let html = `<h2>✈ ${esc(plane.name)}</h2>`;
    html += `<div class="sub">${model.p}👤 ${model.c}📦 · ${model.speed} km/h · range ${fmt(model.range)} km</div>`;

    if (plane.flight) {
      const f = plane.flight;
      html += `<div class="sub"><span class="tag flying">IN FLIGHT</span> ${f.from} → ${f.to} (${fmt(f.km)} km)</div>`;
      html += `<div class="row"><span class="grow">Arrives in</span><span data-eta="${f.arriveAt}">${etaText(f.arriveAt - Date.now())}</span></div>`;
      html += `<button data-act="rush" data-id="${plane.id}">⚡ Rush — ${Game.rushCost(plane)}Ƀ</button>`;
    }

    html += `<h3>Onboard (${plane.cargo.length}/${model.p + model.c})</h3>`;
    if (!plane.cargo.length) html += `<div class="dim">Empty.</div>`;
    for (const job of plane.cargo) {
      html += `<div class="row">
        <span>${jobIcon(job)}</span>
        <span class="grow">→ ${job.dest}</span>
        ${payHtml(job)}
        ${plane.at ? `<button class="small" data-act="unload" data-id="${plane.id}" data-job="${job.id}">Unload</button>` : ''}
      </div>`;
    }

    if (plane.at) {
      const jobs = st.airports[plane.at].jobs;
      html += `<h3>Jobs at ${plane.at}</h3>`;
      if (!jobs.length) html += `<div class="dim">No jobs waiting.</div>`;
      for (const job of jobs) {
        const cap = job.type === 'p' ? model.p : model.c;
        const full = Game.cargoCount(plane, job.type) >= cap;
        html += `<div class="row">
          <span>${jobIcon(job)}</span>
          <span class="grow">→ ${job.dest} <span class="dim">${fmt(job.dist)} km</span></span>
          ${payHtml(job)}
          <button class="small" data-act="load" data-id="${plane.id}" data-job="${job.id}" ${full ? 'disabled' : ''}>Load</button>
        </div>`;
      }

      const from = Game.cityByCode[plane.at];
      const dests = Game.ownedCodes()
        .filter(c => c !== plane.at)
        .map(c => ({ code: c, km: Math.round(Game.distKm(from, Game.cityByCode[c])) }))
        .sort((a, b) => a.km - b.km);
      html += `<h3>Fly to</h3>`;
      for (const d of dests) {
        const inRange = d.km <= model.range;
        const secs = Game.flightSeconds(model, d.km);
        const carrying = plane.cargo.filter(j => j.dest === d.code).length;
        html += `<div class="row">
          <span class="grow">${d.code} <span class="dim">${fmt(d.km)} km · ${etaText(secs * 1000)}${carrying ? ` · ${carrying} onboard` : ''}</span></span>
          <button class="small ${carrying ? 'primary' : ''}" data-act="depart" data-id="${plane.id}" data-code="${d.code}" ${inRange ? '' : 'disabled'}>${inRange ? 'Fly' : 'Too far'}</button>
        </div>`;
      }

      html += `<h3>Manage</h3>`;
      const refund = Math.round(model.cost * 0.4);
      html += `<button class="danger small" data-act="sell" data-id="${plane.id}" ${plane.cargo.length ? 'disabled' : ''}>Sell for ${fmt(refund)}¢</button>`;
    }

    $('panel-body').innerHTML = html;
  }

  function renderPanel() {
    if (!selection) return;
    $('panel').classList.remove('hidden');
    if (selection.type === 'city') renderCityPanel(selection.code);
    else renderPlanePanel(selection.id);
    panelDirty = false;
  }

  // ---------- modals ----------

  function openModal(html) {
    $('modal-body').innerHTML = html;
    $('modal-backdrop').classList.remove('hidden');
  }
  function closeModal() {
    $('modal-backdrop').classList.add('hidden');
  }

  function showStore() {
    const st = Game.state;
    let html = `<h2>Store</h2>`;
    html += `<h3>Planes <span class="dim">(delivered to your first airport)</span></h3>`;
    for (const m of PLANE_MODELS) {
      const locked = st.level < m.level;
      html += `<div class="store-plane${locked ? ' locked' : ''}">
        <div class="info">
          <div class="name">✈ ${esc(m.name)} ${locked ? `<span class="tag">Lv ${m.level}</span>` : ''}</div>
          <div class="stats">${m.p}👤 ${m.c}📦 · ${m.speed} km/h · range ${fmt(m.range)} km</div>
        </div>
        <button data-act="buy-plane" data-model="${m.id}" ${locked || st.coins < m.cost ? 'disabled' : ''}>${fmt(m.cost)}¢</button>
      </div>`;
    }
    html += `<h3>Exchange</h3>
      <div class="store-plane"><div class="info"><div class="name">1Ƀ → ${fmt(Game.BUX_TO_COINS)}¢</div>
      <div class="stats">Trade premium bux for coins.</div></div>
      <button data-act="exchange" ${st.bux < 1 ? 'disabled' : ''}>Exchange</button></div>`;
    html += `<h3>Airports</h3><p class="dim">Click any gray dot on the map to open a new airport.</p>`;
    openModal(html);
  }

  function showFleet() {
    const st = Game.state;
    let html = `<h2>Fleet (${st.planes.length})</h2>`;
    for (const plane of st.planes) {
      const status = plane.flight
        ? `<span class="tag flying">${plane.flight.from} → ${plane.flight.to}</span>`
        : `<span class="tag owned">at ${plane.at}</span>`;
      html += `<div class="row clickable" data-act="focus-plane" data-id="${plane.id}">
        <span class="grow">✈ ${esc(plane.name)}</span>${status}</div>`;
    }
    openModal(html);
  }

  function showHelp() {
    openModal(`<h2>How to play</h2>
      <p><b>Yogurt Air</b> is a tiny airline tycoon inspired by Pocket Planes.</p>
      <ul>
        <li>Click a <b>yellow dot</b> (your airport) to see its job board and parked planes.</li>
        <li>Select a plane, <b>load</b> passenger 👤 and cargo 📦 jobs, then <b>fly</b> to a destination in range.</li>
        <li>Jobs pay <b>coins</b> when delivered to their destination. Jobs going elsewhere stay onboard — or unload them (layover, −25% pay).</li>
        <li>Click a <b>gray dot</b> to open new airports; buy bigger planes in the <b>Store</b>.</li>
        <li>Earn <b>XP</b> for deliveries to level up and unlock better planes. Rare jobs and level-ups pay <b>bux</b> Ƀ — use them to rush flights.</li>
        <li>Drag to pan, scroll to zoom. Progress saves automatically.</li>
      </ul>`);
  }

  function confirmReset() {
    openModal(`<h2>Start over?</h2>
      <p>This erases your airline and starts a new game.</p>
      <p style="margin-top:12px">
        <button class="danger" data-act="do-reset">Yes, reset everything</button>
        <button data-act="close-modal">Cancel</button>
      </p>`);
  }

  // ---------- actions (event delegation) ----------

  function handleAction(el) {
    const act = el.dataset.act;
    const id = el.dataset.id ? Number(el.dataset.id) : null;
    switch (act) {
      case 'buy-airport':
        if (Game.buyAirport(el.dataset.code)) panelDirty = true;
        break;
      case 'sel-plane':
        selectPlane(id);
        break;
      case 'load':
        Game.loadJob(id, Number(el.dataset.job));
        panelDirty = true;
        break;
      case 'unload':
        Game.unloadJob(id, Number(el.dataset.job));
        panelDirty = true;
        break;
      case 'depart':
        if (Game.depart(id, el.dataset.code)) panelDirty = true;
        break;
      case 'rush':
        if (Game.rush(id)) panelDirty = true;
        break;
      case 'sell':
        if (Game.sellPlane(id)) clearSelection();
        break;
      case 'buy-plane':
        if (Game.buyPlane(el.dataset.model)) showStore();
        break;
      case 'exchange':
        if (Game.exchangeBux()) showStore();
        break;
      case 'focus-plane': {
        const plane = Game.state.planes.find(p => p.id === id);
        if (plane) {
          closeModal();
          selectPlane(id);
          MapView.focusCity(plane.at || plane.flight.to);
        }
        break;
      }
      case 'do-reset':
        Game.reset();
        clearSelection();
        closeModal();
        break;
      case 'close-modal':
        closeModal();
        break;
    }
    Game.save();
  }

  // ---------- periodic refresh ----------

  function tick() {
    drainToasts();
    refreshHud();
    if (panelDirty) renderPanel();
    // live-update ETAs without rebuilding the DOM
    document.querySelectorAll('[data-eta]').forEach(el => {
      el.textContent = etaText(Number(el.dataset.eta) - Date.now());
    });
  }

  let lastSlowTick = 0;
  function slowTick(now) {
    // once a second: re-render open panel so job boards / arrivals stay fresh
    if (now - lastSlowTick >= 1000) {
      lastSlowTick = now;
      // don't rebuild under the cursor — it would swallow clicks
      if (selection && !document.querySelector('#panel:hover')) panelDirty = true;
    }
  }

  function init() {
    $('panel-close').addEventListener('click', clearSelection);
    $('modal-close').addEventListener('click', closeModal);
    $('modal-backdrop').addEventListener('click', e => {
      if (e.target === $('modal-backdrop')) closeModal();
    });
    $('btn-store').addEventListener('click', showStore);
    $('btn-fleet').addEventListener('click', showFleet);
    $('btn-help').addEventListener('click', showHelp);
    $('btn-reset').addEventListener('click', confirmReset);

    document.body.addEventListener('click', e => {
      const el = e.target.closest('[data-act]');
      if (el && !el.disabled) handleAction(el);
    });
  }

  return { init, tick, slowTick, selectCity, selectPlane, clearSelection, getSelection, showHelp, toast };
})();
