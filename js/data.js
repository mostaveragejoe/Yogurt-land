/* Static game data: cities, plane models, stylized continent outlines. */

// pop is metro population in millions; it drives airport price and job volume.
const CITIES = [
  { code: 'BOS', name: 'Boston',        lat: 42.4,  lon: -71.0,  pop: 4.9 },
  { code: 'JFK', name: 'New York',      lat: 40.7,  lon: -74.0,  pop: 19.0 },
  { code: 'IAD', name: 'Washington',    lat: 38.9,  lon: -77.0,  pop: 6.3 },
  { code: 'YYZ', name: 'Toronto',       lat: 43.7,  lon: -79.4,  pop: 6.2 },
  { code: 'ORD', name: 'Chicago',       lat: 41.9,  lon: -87.6,  pop: 8.9 },
  { code: 'MIA', name: 'Miami',         lat: 25.8,  lon: -80.2,  pop: 6.1 },
  { code: 'DFW', name: 'Dallas',        lat: 32.8,  lon: -96.8,  pop: 7.6 },
  { code: 'DEN', name: 'Denver',        lat: 39.7,  lon: -105.0, pop: 2.9 },
  { code: 'LAX', name: 'Los Angeles',   lat: 34.0,  lon: -118.2, pop: 12.5 },
  { code: 'SFO', name: 'San Francisco', lat: 37.8,  lon: -122.4, pop: 4.7 },
  { code: 'SEA', name: 'Seattle',       lat: 47.6,  lon: -122.3, pop: 4.0 },
  { code: 'ANC', name: 'Anchorage',     lat: 61.2,  lon: -149.9, pop: 0.4 },
  { code: 'HNL', name: 'Honolulu',      lat: 21.3,  lon: -157.9, pop: 1.0 },
  { code: 'MEX', name: 'Mexico City',   lat: 19.4,  lon: -99.1,  pop: 21.8 },
  { code: 'BOG', name: 'Bogotá',        lat: 4.7,   lon: -74.1,  pop: 11.3 },
  { code: 'LIM', name: 'Lima',          lat: -12.0, lon: -77.0,  pop: 11.2 },
  { code: 'GRU', name: 'São Paulo',     lat: -23.5, lon: -46.6,  pop: 22.4 },
  { code: 'GIG', name: 'Rio de Janeiro',lat: -22.9, lon: -43.2,  pop: 13.6 },
  { code: 'EZE', name: 'Buenos Aires',  lat: -34.6, lon: -58.4,  pop: 15.4 },
  { code: 'KEF', name: 'Reykjavík',     lat: 64.1,  lon: -21.9,  pop: 0.2 },
  { code: 'LHR', name: 'London',        lat: 51.5,  lon: -0.1,   pop: 9.5 },
  { code: 'CDG', name: 'Paris',         lat: 48.9,  lon: 2.4,    pop: 11.0 },
  { code: 'MAD', name: 'Madrid',        lat: 40.4,  lon: -3.7,   pop: 6.7 },
  { code: 'FCO', name: 'Rome',          lat: 41.9,  lon: 12.5,   pop: 4.3 },
  { code: 'BER', name: 'Berlin',        lat: 52.5,  lon: 13.4,   pop: 3.6 },
  { code: 'SVO', name: 'Moscow',        lat: 55.8,  lon: 37.6,   pop: 12.6 },
  { code: 'IST', name: 'Istanbul',      lat: 41.0,  lon: 28.9,   pop: 15.6 },
  { code: 'CAI', name: 'Cairo',         lat: 30.0,  lon: 31.2,   pop: 21.3 },
  { code: 'LOS', name: 'Lagos',         lat: 6.5,   lon: 3.4,    pop: 15.4 },
  { code: 'NBO', name: 'Nairobi',       lat: -1.3,  lon: 36.8,   pop: 5.1 },
  { code: 'JNB', name: 'Johannesburg',  lat: -26.2, lon: 28.0,   pop: 6.1 },
  { code: 'DXB', name: 'Dubai',         lat: 25.3,  lon: 55.3,   pop: 3.5 },
  { code: 'BOM', name: 'Mumbai',        lat: 19.1,  lon: 72.9,   pop: 20.7 },
  { code: 'DEL', name: 'Delhi',         lat: 28.6,  lon: 77.2,   pop: 32.9 },
  { code: 'BKK', name: 'Bangkok',       lat: 13.8,  lon: 100.5,  pop: 10.9 },
  { code: 'SIN', name: 'Singapore',     lat: 1.4,   lon: 103.8,  pop: 6.0 },
  { code: 'HKG', name: 'Hong Kong',     lat: 22.3,  lon: 114.2,  pop: 7.5 },
  { code: 'PVG', name: 'Shanghai',      lat: 31.2,  lon: 121.5,  pop: 29.2 },
  { code: 'PEK', name: 'Beijing',       lat: 39.9,  lon: 116.4,  pop: 21.5 },
  { code: 'ICN', name: 'Seoul',         lat: 37.6,  lon: 127.0,  pop: 9.9 },
  { code: 'HND', name: 'Tokyo',         lat: 35.7,  lon: 139.7,  pop: 37.3 },
  { code: 'SYD', name: 'Sydney',        lat: -33.9, lon: 151.2,  pop: 5.3 },
  { code: 'AKL', name: 'Auckland',      lat: -36.8, lon: 174.8,  pop: 1.7 },
];

// p = passenger seats, c = cargo slots, speed km/h, range km, cost in coins.
const PLANE_MODELS = [
  { id: 'hummingbird', name: 'Hummingbird', p: 1, c: 0, speed: 420, range: 2000,  cost: 12000,  level: 1 },
  { id: 'pelican',     name: 'Pelican',     p: 0, c: 1, speed: 400, range: 2200,  cost: 12000,  level: 1 },
  { id: 'kestrel',     name: 'Kestrel',     p: 1, c: 1, speed: 460, range: 2600,  cost: 26000,  level: 2 },
  { id: 'swallow',     name: 'Swallow',     p: 2, c: 1, speed: 500, range: 3200,  cost: 48000,  level: 3 },
  { id: 'albatross',   name: 'Albatross',   p: 2, c: 2, speed: 520, range: 4800,  cost: 90000,  level: 4 },
  { id: 'falcon',      name: 'Falcon',      p: 3, c: 2, speed: 620, range: 5600,  cost: 150000, level: 5 },
  { id: 'condor',      name: 'Condor',      p: 4, c: 3, speed: 640, range: 7500,  cost: 260000, level: 6 },
  { id: 'phoenix',     name: 'Phoenix',     p: 5, c: 4, speed: 720, range: 9500,  cost: 420000, level: 8 },
  { id: 'zephyr',      name: 'Zephyr',      p: 6, c: 6, speed: 800, range: 14000, cost: 700000, level: 10 },
];

// Deliberately rough, stylized continent outlines as [lon, lat] rings.
// This is a game map, not cartography.
const CONTINENTS = [
  // North & Central America
  [[-165,70],[-140,70],[-95,72],[-80,68],[-65,60],[-65,47],[-70,44],[-74,40],
   [-81,31],[-80,25],[-84,30],[-90,29],[-97,26],[-95,18],[-92,15],[-84,10],
   [-77,8],[-83,9],[-90,14],[-105,20],[-110,23],[-114,28],[-117,32],[-124,40],
   [-125,48],[-135,57],[-150,60],[-162,55],[-168,63],[-165,70]],
  // South America
  [[-75,11],[-62,10],[-52,4],[-50,0],[-35,-8],[-39,-15],[-41,-23],[-48,-28],
   [-53,-35],[-62,-39],[-66,-47],[-68,-54],[-71,-53],[-74,-46],[-73,-37],
   [-71,-30],[-70,-18],[-77,-12],[-81,-5],[-80,0],[-78,2],[-77,8],[-75,11]],
  // Africa
  [[-6,35],[3,37],[10,37],[20,32],[32,31],[35,27],[38,18],[43,12],[51,11],
   [46,4],[42,-1],[40,-10],[36,-18],[33,-26],[27,-33],[20,-34],[18,-33],
   [14,-22],[12,-15],[12,-6],[9,4],[3,6],[-4,6],[-8,4],[-13,9],[-17,14],
   [-17,21],[-13,28],[-6,35]],
  // Eurasia (one big blob)
  [[-9,36],[-9,43],[-2,46],[-5,48],[0,49],[4,51],[8,54],[8,57],[13,55],
   [18,56],[22,59],[28,60],[30,63],[22,64],[25,70],[40,71],[60,73],[100,76],
   [140,73],[170,68],[179,66],[163,62],[157,60],[157,52],[140,58],[141,53],
   [135,45],[129,42],[127,39],[126,35],[122,37],[121,30],[117,23],[110,21],
   [108,16],[105,10],[104,1],[98,8],[98,14],[94,16],[91,22],[87,20],[80,13],
   [77,8],[74,15],[70,21],[67,24],[61,25],[57,26],[60,22],[54,17],[45,13],
   [43,12],[34,28],[34,31],[36,36],[30,36],[27,37],[26,40],[29,41],[22,38],
   [19,40],[18,40],[16,38],[15,40],[12,44],[5,43],[3,42],[0,40],[-2,37],
   [-6,36],[-9,36]],
  // Australia
  [[142,-11],[145,-15],[146,-19],[149,-21],[153,-25],[152,-32],[150,-36],
   [147,-38],[140,-38],[136,-35],[132,-32],[124,-33],[115,-34],[113,-26],
   [114,-22],[119,-20],[122,-18],[127,-14],[130,-15],[131,-12],[136,-11],
   [137,-15],[141,-15],[142,-11]],
  // Greenland
  [[-43,60],[-22,70],[-18,78],[-35,83],[-60,82],[-68,76],[-54,70],[-53,65],
   [-48,60],[-43,60]],
  // Britain & Ireland (one blob)
  [[-10,52],[-6,55],[-5,58],[-3,58],[0,53],[1,51],[-5,50],[-10,52]],
  // Japan
  [[130,31],[133,34],[137,35],[140,36],[141,40],[142,45],[145,44],[141,42],
   [140,37],[136,36],[132,33],[130,31]],
  // Madagascar
  [[49,-12],[50,-16],[47,-25],[45,-25],[44,-19],[48,-13],[49,-12]],
  // New Zealand
  [[173,-35],[178,-38],[176,-41],[174,-42],[170,-44],[167,-46],[168,-44],
   [172,-41],[173,-38],[173,-35]],
  // Sumatra
  [[95,5],[103,-2],[106,-6],[102,-4],[96,2],[95,5]],
  // Borneo
  [[117,7],[119,1],[116,-3],[110,-1],[109,2],[115,6],[117,7]],
  // New Guinea
  [[131,-1],[137,-2],[141,-3],[147,-7],[143,-9],[135,-5],[131,-2],[131,-1]],
  // Iceland
  [[-22,64],[-16,66],[-14,65],[-18,63],[-22,64]],
  // Cuba
  [[-84,22],[-79,22],[-75,20],[-78,20],[-83,21],[-84,22]],
];
