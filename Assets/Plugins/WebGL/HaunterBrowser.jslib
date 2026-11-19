mergeInto(LibraryManager.library, {
  Haunter_HasWebAudio: function () {
    return (typeof window.AudioContext === "function" || typeof window.webkitAudioContext === "function") ? 1 : 0;
  },
  Haunter_TouchEnabled: function () {
    return window.haunterTouch && window.haunterTouch.enabled ? 1 : 0;
  },
  Haunter_TouchAxis: function (axis) {
    return window.haunterTouch ? window.haunterTouch.axis(axis) : 0;
  },
  Haunter_TouchButtons: function () {
    return window.haunterTouch ? window.haunterTouch.buttons() : 0;
  },
  Haunter_ResetTouchInput: function () {
    if (window.haunterTouch) window.haunterTouch.reset();
  },
  Haunter_UpdateTouchState: function (state, actions, held, nearby, charge, menuFlags, checkpoint, level) {
    if (window.haunterTouch) window.haunterTouch.update(state, actions, held, nearby, charge, menuFlags, checkpoint, level);
  },
  Haunter_UpdateTouchTutorial: function (heading, instruction, frozen) {
    if (window.haunterTouch) window.haunterTouch.updateTutorial(UTF8ToString(heading), UTF8ToString(instruction), frozen !== 0);
  },
  Haunter_SmokeRequested: function () {
    return new URLSearchParams(window.location.search).get("smoke") === "1" ? 1 : 0;
  },
  Haunter_TutorialSmokeRequested: function () {
    return new URLSearchParams(window.location.search).get("tutorialSmoke") === "1" ? 1 : 0;
  },
  Haunter_ExpansionSmokeRequested: function () {
    return new URLSearchParams(window.location.search).get("expansionSmoke") === "1" ? 1 : 0;
  },
  Haunter_PreviewLevel: function () {
    var query = new URLSearchParams(window.location.search);
    var level = Number(query.get("level"));
    return query.get("debug") === "1" && level >= 2 && level <= 4 && level % 1 === 0 ? level : 0;
  },
  Haunter_ObserverRequested: function () {
    var query = new URLSearchParams(window.location.search);
    return query.get("debug") === "1" && query.get("observer") === "1" ? 1 : 0;
  },
  Haunter_CaptureShot: function () {
    var query = new URLSearchParams(window.location.search);
    var shot = Number(query.get("capture"));
    return query.get("debug") === "1" && shot >= 1 && shot <= 8 && shot % 1 === 0 ? shot : 0;
  },
  Haunter_CaptureReady: function () {
    return window.haunterCaptureReady === true ? 1 : 0;
  },
  Haunter_RecordCaptureState: function (json) {
    window.haunterCaptureState = JSON.parse(UTF8ToString(json));
  },
  Haunter_DiagnosticsRequested: function () {
    var query = new URLSearchParams(window.location.search);
    return query.get("debug") === "1" || query.get("smoke") === "1" || query.get("tutorialSmoke") === "1"
      || query.get("expansionSmoke") === "1" ? 1 : 0;
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
      if (pending && typeof pending.catch === "function") pending.catch(function () {
        if (window.haunterTouch) window.haunterTouch.pointerLockFailed();
      });
    } catch (error) {
      // Older browsers can throw synchronously instead of rejecting a Promise.
      if (window.haunterTouch) window.haunterTouch.pointerLockFailed();
    }
  },
  Haunter_PublishState: function (json) {
    window.haunterState = JSON.parse(UTF8ToString(json));
    window.render_game_to_text = function () { return JSON.stringify(window.haunterState); };
    // Opt-in diagnostic UI permits inspecting the same snapshot without browser internals.
    if (new URLSearchParams(window.location.search).get("debug") === "1") {
      var state = document.getElementById("haunter-debug-state");
      if (!state) {
        state = document.createElement("pre");
        state.id = "haunter-debug-state";
        state.style.cssText = "white-space:pre-wrap;overflow-wrap:anywhere;padding:20px;font-size:12px";
        document.body.appendChild(state);
      }
      state.textContent = JSON.stringify(window.haunterState);
    }
  },
  Haunter_RecordScreenshot: function (name, base64) {
    window.haunterSmokeScreenshots = window.haunterSmokeScreenshots || {};
    window.haunterSmokeScreenshots[UTF8ToString(name)] = "data:image/png;base64," + UTF8ToString(base64);
  },
  Haunter_RecordSmokeReport: function (json) {
    window.haunterSmokeReport = JSON.parse(UTF8ToString(json));
    // Opt-in test pages expose their result in the DOM as well as the diagnostic API.
    var report = document.getElementById("haunter-test-report");
    if (!report) {
      report = document.createElement("pre");
      report.id = "haunter-test-report";
      report.style.cssText = "white-space:pre-wrap;color:white;background:black;padding:24px;position:relative;z-index:20";
      document.body.appendChild(report);
    }
    report.textContent = JSON.stringify(window.haunterSmokeReport, null, 2);
  }
});
