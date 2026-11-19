// Browser input adapter. Unity remains authoritative over the session and tutorial actions.
(() => {
  "use strict";
  const canvas = document.getElementById("unity-canvas");
  const frame = document.getElementById("player-frame");
  const layer = document.getElementById("touch-controls");
  const toggle = document.getElementById("touch-toggle");
  const stick = document.getElementById("touch-stick");
  const thumb = document.getElementById("touch-thumb");
  const fullscreen = document.getElementById("fullscreen-button");
  const exitExpanded = document.getElementById("exit-expanded");
  const menu = document.getElementById("touch-menu");
  const tutorial = document.getElementById("touch-tutorial");
  const tutorialHeading = document.getElementById("touch-tutorial-heading");
  const tutorialCopy = document.getElementById("touch-tutorial-copy");
  const menuButtons = {
    begin: document.getElementById("touch-begin"), skip: document.getElementById("touch-skip"),
    resume: document.getElementById("touch-resume"), restart: document.getElementById("touch-restart"),
    distortion: document.getElementById("touch-distortion")
  };
  const controls = {
    cane: { element: document.getElementById("touch-cane"), action: 16, pulse: 1 },
    pickup: { element: document.getElementById("touch-pickup"), action: 32, pulse: 2 },
    drop: { element: document.getElementById("touch-drop"), action: 128, pulse: 4 },
    throw: { element: document.getElementById("touch-throw"), action: 64 },
    crouch: { element: document.getElementById("touch-crouch"), action: 4 },
    sprint: { element: document.getElementById("touch-sprint"), action: 8 },
    pause: { element: document.getElementById("touch-pause"), action: 0, pulse: 128 }
  };
  const query = new URLSearchParams(window.location.search);
  const pointerLockAvailable = typeof canvas.requestPointerLock === "function";
  let enabled = query.get("touch") === "1" || (query.get("touch") !== "0"
    && (!pointerLockAvailable || (navigator.maxTouchPoints > 0
      && window.matchMedia("(any-pointer: coarse)").matches)));
  let session = 0, actions = 0, held = false, nearby = 0;
  let moveX = 0, moveY = 0, lookX = 0, lookY = 0, pulses = 0;
  let throwing = false, sprinting = false, crouching = false;
  let stickPointer = null, lookPointer = null;
  let lastLookX = 0, lastLookY = 0, lastCharge = -1;
  let expanded = false, savedScrollY = 0;
  let nativeFullscreen = false;
  let uiSession = -1, uiActions = -1, uiHeld = null, uiNearby = -1, uiEnabled = null;
  let menuFlags = 1, uiMenuFlags = -1;
  const pointers = new Map();

  function allowed(mask) { return enabled && session === 1 && (mask === 0 || (actions & mask) !== 0); }
  function usable(name) {
    const item = controls[name];
    if (name === "pickup" && nearby === 2) return allowed(256);
    return allowed(item.action) && (name !== "pickup" || (nearby === 1 && !held))
      && ((name !== "throw" && name !== "drop") || held);
  }
  function paintStick() {
    const width = stick.getBoundingClientRect().width;
    const travel = Math.min(36, width * .29);
    thumb.style.transform = `translate(${moveX * travel}px, ${-moveY * travel}px)`;
  }
  function paintCrouch() {
    controls.crouch.element.setAttribute("aria-pressed", String(crouching));
    controls.crouch.element.textContent = crouching ? "Crouch: ON" : "Crouch";
  }
  function capture(event, element, kind) {
    event.preventDefault();
    pointers.set(event.pointerId, { element, kind });
    try { element.setPointerCapture(event.pointerId); } catch (_) { /* A canceled touch can vanish before capture. */ }
  }
  function finish(pointerId, canceled) {
    const pointer = pointers.get(pointerId);
    if (!pointer) return;
    pointers.delete(pointerId);
    if (pointer.kind === "move") {
      stickPointer = null; moveX = moveY = 0; paintStick();
    } else if (pointer.kind === "look") {
      lookPointer = null;
    } else if (pointer.kind === "throw") {
      throwing = false;
      pulses |= canceled ? 256 : 16;
    } else if (pointer.kind === "sprint") {
      sprinting = false;
    }
    pointer.element.classList.remove("touch-active");
    try {
      if (pointer.element.hasPointerCapture(pointerId)) pointer.element.releasePointerCapture(pointerId);
    } catch (_) { /* The browser may already have released capture. */ }
  }
  function reset() {
    const wasThrowing = throwing || (pulses & 256) !== 0;
    for (const id of Array.from(pointers.keys())) finish(id, true);
    moveX = moveY = lookX = lookY = 0;
    throwing = sprinting = crouching = false;
    pulses = wasThrowing ? 256 : 0;
    stickPointer = lookPointer = null;
    paintStick(); paintCrouch();
  }
  function interrupt() {
    const shouldPause = enabled && session === 1;
    reset();
    if (shouldPause) pulses |= 128;
  }
  function updateStick(event) {
    const rect = stick.getBoundingClientRect();
    const radius = Math.max(1, Math.min(rect.width, rect.height) * .34);
    let x = (event.clientX - rect.left - rect.width / 2) / radius;
    let y = -(event.clientY - rect.top - rect.height / 2) / radius;
    const length = Math.hypot(x, y);
    if (length < .12) x = y = 0;
    else {
      const amount = Math.min(1, (length - .12) / .88);
      x = x / length * amount; y = y / length * amount;
    }
    moveX = x; moveY = y; paintStick();
  }
  stick.addEventListener("pointerdown", event => {
    if (!allowed(2) || stickPointer !== null || event.button > 0) return;
    stickPointer = event.pointerId;
    capture(event, stick, "move"); updateStick(event);
  });
  stick.addEventListener("pointermove", event => {
    if (event.pointerId !== stickPointer) return;
    event.preventDefault(); updateStick(event);
  });
  canvas.addEventListener("pointerdown", event => {
    if (!allowed(1) || lookPointer !== null || event.button > 0) return;
    lookPointer = event.pointerId;
    lastLookX = event.clientX; lastLookY = event.clientY;
    capture(event, canvas, "look");
  });
  canvas.addEventListener("pointermove", event => {
    if (event.pointerId !== lookPointer) return;
    event.preventDefault();
    lookX += event.clientX - lastLookX; lookY += event.clientY - lastLookY;
    lastLookX = event.clientX; lastLookY = event.clientY;
  });
  for (const [name, item] of Object.entries(controls)) {
    item.element.addEventListener("pointerdown", event => {
      if (!usable(name) || event.button > 0 || Array.from(pointers.values()).some(p => p.kind === name)) return;
      capture(event, item.element, name);
      item.element.classList.add("touch-active");
      if (name === "throw") throwing = true;
      else if (name === "sprint") sprinting = true;
      else if (name === "crouch") { crouching = !crouching; paintCrouch(); }
      else pulses |= item.pulse;
    });
    // Pointer input is authoritative, including when the browser synthesizes a click.
    item.element.addEventListener("click", event => event.preventDefault());
  }
  for (const element of [canvas, stick, ...Object.values(controls).map(c => c.element)]) {
    element.addEventListener("pointerup", event => {
      if (!pointers.has(event.pointerId)) return;
      event.preventDefault(); finish(event.pointerId, false);
    });
    element.addEventListener("pointercancel", event => finish(event.pointerId, true));
    element.addEventListener("lostpointercapture", event => finish(event.pointerId, true));
    element.addEventListener("contextmenu", event => { if (enabled) event.preventDefault(); });
  }
  // Prevent an iOS edge gesture or pinch inside the game from becoming page navigation/zoom.
  // The surrounding page retains normal scrolling and zooming.
  frame.addEventListener("touchmove", event => {
    if (enabled && session === 1) event.preventDefault();
  }, { passive: false });
  window.addEventListener("blur", interrupt);
  document.addEventListener("visibilitychange", () => { if (document.hidden) interrupt(); });
  window.addEventListener("orientationchange", interrupt);
  for (const [action, element] of Object.entries(menuButtons)) {
    if (!element) continue;
    element.addEventListener("click", event => {
      event.preventDefault(); event.stopPropagation();
      if (!enabled || session === 1 || !window.unityInstance) return;
      reset();
      window.unityInstance.SendMessage("Haunter browser bridge", "TouchMenuAction", action);
    });
  }

  function refreshMode() {
    toggle.checked = enabled;
    frame.classList.toggle("touch-mode", enabled);
    document.getElementById("mobile-note").hidden = !enabled;
    document.getElementById("touch-instructions").hidden = !enabled;
    document.getElementById("keyboard-instructions").hidden = enabled;
    if (!expanded) fullscreen.textContent = enabled ? "Expand view ↗" : "Fullscreen ↗";
    canvas.setAttribute("aria-label", enabled
      ? "Haunter in the Dark game. Tap Begin to start. Move with the left stick and drag the scene to look."
      : "Haunter in the Dark game. Press Enter to begin. Use WASD and mouse to move.");
    uiEnabled = null;
    update(session, actions, held, nearby, 0);
    if (window.haunterLoadingState === "ready") ready();
    else if (!window.haunterLoadingState || window.haunterLoadingState === "idle") {
      document.getElementById("status").textContent = enabled
        ? "Touch controls · Landscape recommended · Headphones recommended"
        : "Keyboard + mouse · Headphones recommended · No installation needed";
    }
  }
  toggle.addEventListener("change", () => {
    if (session === 1) { toggle.checked = enabled; return; }
    reset(); enabled = toggle.checked; refreshMode();
  });
  function ready() {
    uiEnabled = null;
    update(session, actions, held, nearby, 0);
    document.getElementById("status").textContent = enabled
      ? "Tap Begin. Use the left stick to move and drag the scene to look. Landscape gives you more room."
      : "Press Enter or click Begin to play. Esc pauses and releases the mouse.";
  }
  function update(nextSession, nextActions, nextHeld, nextNearby, charge, flags = menuFlags) {
    if (nextSession !== session) reset();
    session = nextSession; actions = nextActions; held = !!nextHeld; nearby = nextNearby;
    menuFlags = flags;
    if (uiSession !== session || uiActions !== actions || uiHeld !== held || uiNearby !== nearby || uiEnabled !== enabled || uiMenuFlags !== menuFlags) {
      uiSession = session; uiActions = actions; uiHeld = held; uiNearby = nearby; uiEnabled = enabled; uiMenuFlags = menuFlags;
      layer.hidden = !enabled || session !== 1;
      menu.hidden = !enabled || session === 1 || window.haunterLoadingState !== "ready";
      tutorial.hidden = !enabled || session !== 1 || !tutorialHeading.textContent;
      menuButtons.begin.hidden = session !== 0;
      menuButtons.skip.hidden = session !== 0 && (session !== 2 || !(menuFlags & 1));
      menuButtons.skip.textContent = session === 0 ? "Skip tutorial" : "Skip tutorial & resume";
      menuButtons.resume.hidden = session !== 2;
      menuButtons.restart.hidden = session === 0 || session === 1;
      if (menuButtons.distortion) {
        menuButtons.distortion.hidden = session !== 2;
        menuButtons.distortion.setAttribute("aria-pressed", String(!!(menuFlags & 2)));
        menuButtons.distortion.textContent = (menuFlags & 2) ? "Reduced distortion on" : "Reduce distortion";
      }
      toggle.disabled = session === 1;
      stick.classList.toggle("touch-unavailable", !allowed(2));
      stick.setAttribute("aria-disabled", String(!allowed(2)));
      for (const [id, pointer] of pointers) {
        if ((pointer.kind === "move" && !allowed(2)) || (pointer.kind === "look" && !allowed(1))
          || (controls[pointer.kind] && !usable(pointer.kind))) finish(id, true);
      }
      if (!allowed(4) && crouching) { crouching = false; paintCrouch(); }
      for (const [name, item] of Object.entries(controls)) {
        const disabled = !usable(name);
        if (item.element.disabled !== disabled) item.element.disabled = disabled;
      }
      controls.pickup.element.textContent = nearby === 2 ? "Exit" : "Pick up";
    }
    const percent = Math.round(Math.max(0, Math.min(1, charge)) * 100);
    if (lastCharge !== percent) {
      controls.throw.element.textContent = percent > 0 ? `Throw ${percent}%` : "Hold to throw";
      lastCharge = percent;
    }
  }
  function updateTutorial(heading, copy, frozen) {
    tutorialHeading.textContent = heading;
    tutorialCopy.textContent = copy;
    tutorial.setAttribute("data-frozen", String(frozen));
    tutorial.hidden = !enabled || session !== 1 || !heading;
  }
  function pointerLockFailed() {
    if (enabled || window.haunterLoadingState !== "ready") return;
    document.getElementById("status").textContent =
      "Mouse capture is unavailable. Turn on Touch controls to use drag-to-look and WASD.";
  }
  document.addEventListener("pointerlockerror", pointerLockFailed);

  function prepareForPlay() {
    // Fill small screens without requiring native fullscreen, which iPhone may not expose.
    if (enabled && window.matchMedia("(max-width: 520px), (orientation: landscape) and (max-height: 460px)").matches)
      setExpanded(true);
  }
  function setExpanded(value) {
    if (expanded === value) return;
    interrupt();
    if (value) savedScrollY = window.scrollY;
    expanded = value;
    updateViewportHeight();
    frame.classList.toggle("game-expanded", value);
    document.body.classList.toggle("game-expanded-page", value);
    exitExpanded.hidden = !value;
    const exitLabel = nativeFullscreen ? "Exit fullscreen" : "Exit expanded view";
    exitExpanded.textContent = exitLabel;
    fullscreen.textContent = value ? exitLabel : (enabled ? "Expand view ↗" : "Fullscreen ↗");
    if (!value) window.scrollTo(0, savedScrollY);
  }
  function updateViewportHeight() {
    if (expanded && !nativeFullscreen && window.visualViewport)
      frame.style.setProperty("--haunter-viewport-height", `${window.visualViewport.height}px`);
    else if (window.visualViewport)
      frame.style.removeProperty("--haunter-viewport-height");
  }
  if (window.visualViewport) window.visualViewport.addEventListener("resize", updateViewportHeight);
  async function enterFullscreen() {
    if (expanded || document.fullscreenElement === frame) { await leaveFullscreen(); return; }
    // Fullscreen the frame so the HTML controls remain above the Unity canvas.
    if (frame.requestFullscreen) {
      try {
        await frame.requestFullscreen();
        nativeFullscreen = document.fullscreenElement === frame;
        setExpanded(true);
        return;
      } catch (_) { /* Fall back to the viewport on browsers that deny fullscreen. */ }
    }
    setExpanded(true);
  }
  async function leaveFullscreen() {
    if (document.fullscreenElement && document.exitFullscreen) {
      try { await document.exitFullscreen(); } catch (_) { /* Restore the page even if fullscreen already ended. */ }
    }
    nativeFullscreen = false;
    setExpanded(false);
  }
  exitExpanded.addEventListener("click", leaveFullscreen);
  document.addEventListener("fullscreenchange", () => {
    if (!document.fullscreenElement && expanded && nativeFullscreen) {
      nativeFullscreen = false;
      setExpanded(false);
    }
  });
  window.addEventListener("keydown", event => {
    if (event.key === "Escape" && expanded && !document.fullscreenElement) leaveFullscreen();
  });
  window.haunterTouch = {
    get enabled() { return enabled; },
    axis(index) {
      if (index === 0) return moveX;
      if (index === 1) return moveY;
      if (index === 2) { const value = lookX; lookX = 0; return value; }
      if (index === 3) { const value = lookY; lookY = 0; return value; }
      return 0;
    },
    buttons() {
      const value = pulses | (throwing ? 8 : 0) | (crouching ? 32 : 0) | (sprinting ? 64 : 0);
      pulses = 0;
      return value;
    },
    reset, update, updateTutorial, ready, enterFullscreen, prepareForPlay, pointerLockFailed
  };
  refreshMode();
})();
