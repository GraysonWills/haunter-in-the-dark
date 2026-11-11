mergeInto(LibraryManager.library, {
  Haunter_SmokeRequested: function () {
    return new URLSearchParams(window.location.search).get("smoke") === "1" ? 1 : 0;
  },
  Haunter_DiagnosticsRequested: function () {
    var query = new URLSearchParams(window.location.search);
    return query.get("debug") === "1" || query.get("smoke") === "1" ? 1 : 0;
  },
  Haunter_HasPointerLock: function () {
    return document.pointerLockElement === Module.canvas ? 1 : 0;
  },
  Haunter_RequestPointerLock: function () {
    var canvas = Module.canvas;
    if (!canvas || !canvas.requestPointerLock || document.pointerLockElement === canvas) return;
    // One request per Begin/Resume gesture; browser rejection returns the game to pause.
    // Do not queue a retry or wait for an unrelated future key/click.
    try {
      var pending = canvas.requestPointerLock();
      if (pending && typeof pending.catch === "function") pending.catch(function () {});
    } catch (error) {
      // Older browsers can throw synchronously instead of rejecting a Promise.
    }
  },
  Haunter_PublishState: function (json) {
    window.haunterState = JSON.parse(UTF8ToString(json));
    window.render_game_to_text = function () { return JSON.stringify(window.haunterState); };
  },
  Haunter_RecordScreenshot: function (name, base64) {
    window.haunterSmokeScreenshots = window.haunterSmokeScreenshots || {};
    window.haunterSmokeScreenshots[UTF8ToString(name)] = "data:image/png;base64," + UTF8ToString(base64);
  },
  Haunter_RecordSmokeReport: function (json) {
    window.haunterSmokeReport = JSON.parse(UTF8ToString(json));
  }
});
