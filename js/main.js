/* Boot + main loop. */

(function () {
  if (!Game.load()) {
    Game.newGame();
    setTimeout(() => UI.showHelp(), 400);
  }

  UI.init();
  MapView.init(document.getElementById('map'), {
    onSelectCity: code => UI.selectCity(code),
    onSelectPlane: id => UI.selectPlane(id),
    onSelectNothing: () => UI.clearSelection(),
  });
  MapView.focusCity('JFK');

  let lastSave = Date.now();

  function frame() {
    const now = Date.now();
    Game.tick(now);
    UI.slowTick(now);
    UI.tick();
    MapView.draw(UI.getSelection());
    if (now - lastSave > 5000) {
      lastSave = now;
      Game.save();
    }
    requestAnimationFrame(frame);
  }

  window.addEventListener('beforeunload', () => Game.save());
  requestAnimationFrame(frame);
})();
