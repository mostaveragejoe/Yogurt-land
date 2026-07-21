/* Core simulation: state, economy, jobs, flights, save/load. */

const Game = (() => {
  const SAVE_KEY = 'yogurtair-save-v1';
  const SECONDS_PER_GAME_HOUR = 4;   // a 10h flight takes 40 real seconds
  const MIN_FLIGHT_SECONDS = 6;
  const JOB_REFILL_MS = 12000;       // one new job per airport per 12s
  const BUX_JOB_CHANCE = 0.05;
  const BUX_TO_COINS = 600;
  const LAYOVER_PAY_FACTOR = 0.75;   // pay cut when a job is unloaded mid-route
  const SELL_FACTOR = 0.4;

  const cityByCode = {};
  CITIES.forEach(c => { cityByCode[c.code] = c; });
  const modelById = {};
  PLANE_MODELS.forEach(m => { modelById[m.id] = m; });

  let state = null;
  const toasts = []; // {msg, kind} queued for the UI

  // ---------- helpers ----------

  function distKm(a, b) {
    const R = 6371, rad = Math.PI / 180;
    const dLat = (b.lat - a.lat) * rad;
    const dLon = (b.lon - a.lon) * rad;
    const s = Math.sin(dLat / 2) ** 2 +
      Math.cos(a.lat * rad) * Math.cos(b.lat * rad) * Math.sin(dLon / 2) ** 2;
    return 2 * R * Math.asin(Math.sqrt(s));
  }

  function flightSeconds(model, km) {
    return Math.max(MIN_FLIGHT_SECONDS, (km / model.speed) * SECONDS_PER_GAME_HOUR);
  }

  function airportCost(city) {
    return Math.round(city.pop * 2500 / 50) * 50;
  }

  function maxJobs(city) {
    return Math.min(10, 3 + Math.round(city.pop / 5));
  }

  function xpToNext(level) {
    return 350 * level;
  }

  function ownedCodes() {
    return Object.keys(state.airports);
  }

  function planesAt(code) {
    return state.planes.filter(p => p.at === code);
  }

  function planeModel(plane) {
    return modelById[plane.model];
  }

  function cargoCount(plane, type) {
    return plane.cargo.filter(j => j.type === type).length;
  }

  function toast(msg, kind) {
    toasts.push({ msg, kind: kind || '' });
  }

  // ---------- jobs ----------

  function makeJob(fromCode) {
    const owned = ownedCodes().filter(c => c !== fromCode);
    if (!owned.length) return null;
    const dest = owned[Math.floor(Math.random() * owned.length)];
    const dist = Math.round(distKm(cityByCode[fromCode], cityByCode[dest]));
    const type = Math.random() < 0.5 ? 'p' : 'c';
    const isBux = Math.random() < BUX_JOB_CHANCE;
    const job = {
      id: state.jobCounter++,
      type, from: fromCode, dest, dist,
      layover: false,
      isBux,
      pay: isBux
        ? Math.max(1, Math.round(dist / 2500) + 1)
        : Math.round((30 + dist * 0.12) * (type === 'p' ? 1.1 : 1.0)),
      xp: 3 + Math.round(dist / 400),
    };
    return job;
  }

  function refillJobs(now) {
    for (const code of ownedCodes()) {
      const ap = state.airports[code];
      const cap = maxJobs(cityByCode[code]);
      if (ap.jobs.length >= cap) { ap.lastJobAt = now; continue; }
      let guard = 0;
      while (ap.jobs.length < cap && now - ap.lastJobAt >= JOB_REFILL_MS && guard++ < 50) {
        ap.lastJobAt += JOB_REFILL_MS;
        const job = makeJob(code);
        if (job) ap.jobs.push(job);
      }
      if (now - ap.lastJobAt > JOB_REFILL_MS) ap.lastJobAt = now - JOB_REFILL_MS;
    }
  }

  function fillAirport(code, count, now) {
    const ap = state.airports[code];
    for (let i = 0; i < count; i++) {
      const job = makeJob(code);
      if (job) ap.jobs.push(job);
    }
    ap.lastJobAt = now;
  }

  // ---------- levels ----------

  function grantXp(amount) {
    state.xp += amount;
    while (state.xp >= xpToNext(state.level)) {
      state.xp -= xpToNext(state.level);
      state.level++;
      const buxReward = 2 + state.level;
      state.bux += buxReward;
      toast(`Level up! You are now level ${state.level} (+${buxReward} Ƀ)`, 'good');
    }
  }

  // ---------- flights ----------

  function arrive(plane, now) {
    const to = plane.flight.to;
    plane.at = to;
    plane.flight = null;
    let coins = 0, bux = 0, xp = 0, delivered = 0;
    plane.cargo = plane.cargo.filter(job => {
      if (job.dest !== to) return true;
      if (job.isBux) bux += job.pay; else coins += job.pay;
      xp += job.xp;
      delivered++;
      return false;
    });
    state.coins += coins;
    state.bux += bux;
    if (xp) grantXp(xp);
    const bits = [];
    if (coins) bits.push(`+${coins}¢`);
    if (bux) bits.push(`+${bux}Ƀ`);
    if (delivered) {
      toast(`${plane.name} delivered ${delivered} job${delivered > 1 ? 's' : ''} at ${to} (${bits.join(', ')})`, 'good');
    } else {
      toast(`${plane.name} landed at ${to}`);
    }
  }

  function processArrivals(now) {
    for (const plane of state.planes) {
      if (plane.flight && now >= plane.flight.arriveAt) arrive(plane, now);
    }
  }

  // ---------- public actions ----------

  function buyAirport(code) {
    const city = cityByCode[code];
    if (!city || state.airports[code]) return false;
    const cost = airportCost(city);
    if (state.coins < cost) { toast('Not enough coins for that airport', 'bad'); return false; }
    state.coins -= cost;
    state.airports[code] = { jobs: [], lastJobAt: Date.now() };
    fillAirport(code, Math.ceil(maxJobs(city) / 2), Date.now());
    toast(`Opened ${city.name} (${code})!`, 'good');
    return true;
  }

  function buyPlane(modelId) {
    const model = modelById[modelId];
    if (!model) return false;
    if (state.level < model.level) { toast(`Reach level ${model.level} to buy the ${model.name}`, 'bad'); return false; }
    if (state.coins < model.cost) { toast('Not enough coins for that plane', 'bad'); return false; }
    state.coins -= model.cost;
    const n = ++state.planeCounter;
    const home = ownedCodes()[0];
    state.planes.push({
      id: n, model: model.id,
      name: `${model.name} ${n}`,
      at: home, flight: null, cargo: [],
    });
    toast(`${model.name} ${n} delivered to ${home}`, 'good');
    return true;
  }

  function sellPlane(planeId) {
    const i = state.planes.findIndex(p => p.id === planeId);
    if (i < 0) return false;
    const plane = state.planes[i];
    if (plane.flight || plane.cargo.length) { toast('Land and empty the plane first', 'bad'); return false; }
    if (state.planes.length <= 1) { toast('You need at least one plane', 'bad'); return false; }
    const refund = Math.round(planeModel(plane).cost * SELL_FACTOR);
    state.planes.splice(i, 1);
    state.coins += refund;
    toast(`Sold ${plane.name} for ${refund}¢`);
    return true;
  }

  function loadJob(planeId, jobId) {
    const plane = state.planes.find(p => p.id === planeId);
    if (!plane || !plane.at) return false;
    const ap = state.airports[plane.at];
    const idx = ap.jobs.findIndex(j => j.id === jobId);
    if (idx < 0) return false;
    const job = ap.jobs[idx];
    const model = planeModel(plane);
    const cap = job.type === 'p' ? model.p : model.c;
    if (cargoCount(plane, job.type) >= cap) return false;
    ap.jobs.splice(idx, 1);
    plane.cargo.push(job);
    return true;
  }

  function unloadJob(planeId, jobId) {
    const plane = state.planes.find(p => p.id === planeId);
    if (!plane || !plane.at) return false;
    const idx = plane.cargo.findIndex(j => j.id === jobId);
    if (idx < 0) return false;
    const job = plane.cargo.splice(idx, 1)[0];
    if (job.dest !== plane.at && !job.layover) {
      job.layover = true;
      job.pay = Math.max(1, Math.round(job.pay * LAYOVER_PAY_FACTOR));
    }
    state.airports[plane.at].jobs.push(job);
    return true;
  }

  function depart(planeId, destCode) {
    const plane = state.planes.find(p => p.id === planeId);
    if (!plane || !plane.at || plane.flight) return false;
    if (!state.airports[destCode] || destCode === plane.at) return false;
    const model = planeModel(plane);
    const km = distKm(cityByCode[plane.at], cityByCode[destCode]);
    if (km > model.range) { toast(`${destCode} is out of range for ${plane.name}`, 'bad'); return false; }
    const now = Date.now();
    plane.flight = {
      from: plane.at, to: destCode,
      departAt: now,
      arriveAt: now + flightSeconds(model, km) * 1000,
      km: Math.round(km),
    };
    plane.at = null;
    return true;
  }

  function rushCost(plane) {
    if (!plane.flight) return 0;
    const remain = Math.max(0, plane.flight.arriveAt - Date.now()) / 1000;
    return Math.max(1, Math.ceil(remain / 20));
  }

  function rush(planeId) {
    const plane = state.planes.find(p => p.id === planeId);
    if (!plane || !plane.flight) return false;
    const cost = rushCost(plane);
    if (state.bux < cost) { toast('Not enough bux to rush', 'bad'); return false; }
    state.bux -= cost;
    plane.flight.arriveAt = Date.now();
    return true;
  }

  function exchangeBux() {
    if (state.bux < 1) { toast('No bux to exchange', 'bad'); return false; }
    state.bux -= 1;
    state.coins += BUX_TO_COINS;
    toast(`Exchanged 1Ƀ for ${BUX_TO_COINS}¢`);
    return true;
  }

  // ---------- lifecycle ----------

  function newGame() {
    const now = Date.now();
    state = {
      coins: 30000, bux: 10, xp: 0, level: 1,
      airports: {}, planes: [],
      jobCounter: 1, planeCounter: 0,
      createdAt: now,
    };
    for (const code of ['BOS', 'JFK', 'IAD']) {
      state.airports[code] = { jobs: [], lastJobAt: now };
    }
    for (const code of ownedCodes()) {
      fillAirport(code, maxJobs(cityByCode[code]), now);
    }
    state.planes.push(
      { id: ++state.planeCounter, model: 'hummingbird', name: 'Hummingbird 1', at: 'BOS', flight: null, cargo: [] },
      { id: ++state.planeCounter, model: 'pelican', name: 'Pelican 2', at: 'JFK', flight: null, cargo: [] },
    );
    save();
  }

  function save() {
    try {
      localStorage.setItem(SAVE_KEY, JSON.stringify(state));
    } catch (e) { /* private mode etc. — play without saving */ }
  }

  function load() {
    try {
      const raw = localStorage.getItem(SAVE_KEY);
      if (!raw) return false;
      const s = JSON.parse(raw);
      if (!s || !s.airports || !s.planes) return false;
      state = s;
      return true;
    } catch (e) {
      return false;
    }
  }

  function reset() {
    try { localStorage.removeItem(SAVE_KEY); } catch (e) {}
    newGame();
  }

  function tick(now) {
    processArrivals(now);
    refillJobs(now);
  }

  return {
    get state() { return state; },
    toasts,
    cityByCode, modelById,
    distKm, flightSeconds, airportCost, maxJobs, xpToNext,
    ownedCodes, planesAt, planeModel, cargoCount, rushCost,
    buyAirport, buyPlane, sellPlane, loadJob, unloadJob, depart, rush, exchangeBux,
    newGame, save, load, reset, tick,
    BUX_TO_COINS,
  };
})();
