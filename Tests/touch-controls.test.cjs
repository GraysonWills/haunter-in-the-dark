// Browser adapter behavior tests; no Unity engine or browser device emulation implied.
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const template = path.resolve(__dirname, '../Assets/WebGLTemplates/Haunter');
const code = fs.readFileSync(path.join(template, 'touch-controls.js'), 'utf8');
const html = fs.readFileSync(path.join(template, 'index.html'), 'utf8');

function boot({ query = '?touch=1', touchPoints = 0, coarse = false, pointerLock = true } = {}) {
  class Element {
    constructor() {
      this.listeners = new Map(); this.attributes = {}; this.style = {}; this.captures = new Set();
      this.hidden = false; this.disabled = false; this.checked = false; this.textContent = '';
      const classes = new Set();
      this.classList = {
        add: name => classes.add(name), remove: name => classes.delete(name), contains: name => classes.has(name),
        toggle(name, value) { if (value) classes.add(name); else classes.delete(name); }
      };
    }
    addEventListener(name, fn) { if (!this.listeners.has(name)) this.listeners.set(name, []); this.listeners.get(name).push(fn); }
    emit(name, options = {}) {
      const event = { pointerId: 1, button: 0, clientX: 0, clientY: 0, preventDefault() {}, stopPropagation() {}, ...options };
      for (const fn of this.listeners.get(name) || []) fn(event);
    }
    setAttribute(name, value) { this.attributes[name] = value; }
    getBoundingClientRect() { return { left: 0, top: 0, width: 128, height: 128 }; }
    setPointerCapture(id) { this.captures.add(id); }
    hasPointerCapture(id) { return this.captures.has(id); }
    releasePointerCapture(id) { this.captures.delete(id); this.emit('lostpointercapture', { pointerId: id }); }
  }
  const nodes = new Map(Array.from(html.matchAll(/id="([^"]+)"/g), match => [match[1], new Element()]));
  if (pointerLock) nodes.get('unity-canvas').requestPointerLock = () => {};
  const document = new Element();
  document.getElementById = id => { assert.ok(nodes.has(id), `HTML contains #${id}`); return nodes.get(id); };
  document.body = new Element(); document.hidden = false; document.fullscreenElement = null;
  const window = new Element();
  const messages = [];
  window.location = { search: query }; window.matchMedia = () => ({ matches: coarse });
  window.scrollY = 72; window.scrollTo = (...args) => { window.lastScroll = args; };
  window.haunterLoadingState = 'ready';
  window.unityInstance = { SendMessage: (...args) => messages.push(args) };
  vm.runInNewContext(code, { window, document, navigator: { maxTouchPoints: touchPoints }, URLSearchParams, Map, Array, Object, Math });
  return { api: window.haunterTouch, window, document, messages, get: id => nodes.get(id) };
}

test('touch defaults detect an iPad even with mouse-oriented media queries', () => {
  assert.equal(boot({ query: '', touchPoints: 5, coarse: false, pointerLock: false }).api.enabled, true);
  assert.equal(boot({ query: '', touchPoints: 0, coarse: false }).api.enabled, false);
  assert.equal(boot({ query: '?touch=0', touchPoints: 5, coarse: true }).api.enabled, false);
});

test('desktop browsers without pointer lock start with the playable drag controls', () => {
  const { api, get } = boot({ query: '', pointerLock: false });
  assert.equal(api.enabled, true);
  assert.equal(get('touch-toggle').checked, true);
  assert.equal(get('touch-menu').hidden, false);
});

test('a denied capture explains how to switch to drag and keyboard controls', () => {
  const { document, get } = boot({ query: '' });
  document.emit('pointerlockerror');
  assert.match(get('status').textContent, /Turn on Touch controls.*WASD/);
});

test('touch tutorial shows the current frozen step and hides while paused or skipped', () => {
  const { api, get } = boot();
  api.update(1, 32, 0, 1, 0);
  api.updateTutorial('PICK UP', 'Tap Pick up to continue.', true);
  assert.equal(get('touch-tutorial').hidden, false);
  assert.equal(get('touch-tutorial').attributes['data-frozen'], 'true');
  assert.equal(get('touch-tutorial-copy').textContent, 'Tap Pick up to continue.');
  api.update(2, 32, 0, 1, 0);
  assert.equal(get('touch-tutorial').hidden, true);
  api.update(1, 32, 0, 1, 0);
  assert.equal(get('touch-tutorial').hidden, false);
  api.updateTutorial('', '', false);
  assert.equal(get('touch-tutorial').hidden, true);
});

test('movement and look accept independent simultaneous pointers', () => {
  const { api, get } = boot(); api.update(1, 511, 0, 0, 0);
  get('touch-stick').emit('pointerdown', { pointerId: 11, clientX: 64, clientY: 0 });
  get('unity-canvas').emit('pointerdown', { pointerId: 22, clientX: 10, clientY: 20 });
  get('unity-canvas').emit('pointermove', { pointerId: 22, clientX: 40, clientY: 35 });
  get('touch-cane').emit('pointerdown', { pointerId: 33 });
  assert.equal(api.buttons(), 1);
  assert.equal(api.axis(0), 0); assert.equal(api.axis(1), 1);
  assert.equal(api.axis(2), 30); assert.equal(api.axis(3), 15);
  assert.equal(api.axis(2), 0); assert.equal(api.axis(3), 0);
  get('unity-canvas').emit('pointerup', { pointerId: 22 });
  assert.equal(api.axis(1), 1);
  get('touch-stick').emit('pointerup', { pointerId: 11, clientX: 9000, clientY: -9000 });
  assert.equal(api.axis(1), 0);
});

test('cane press is a one-shot pulse, with no synthetic-click duplicate', () => {
  const { api, get } = boot(); api.update(1, 16, 0, 0, 0);
  get('touch-cane').emit('pointerdown'); assert.equal(api.buttons(), 1); assert.equal(api.buttons(), 0);
  get('touch-cane').emit('pointerup'); get('touch-cane').emit('click'); assert.equal(api.buttons(), 0);
});

test('maze scan is a one-shot control available in the maze between scans', () => {
  const { api, get } = boot();
  const scan = get('touch-scan');
  api.update(1, 511, 1, 0, 0, 0);
  assert.equal(scan.hidden, true);
  scan.emit('pointerdown', { pointerId: 1 });
  assert.equal(api.buttons(), 0);
  api.update(1, 511, 1, 0, 0, 4);
  assert.equal(scan.hidden, false);
  scan.emit('pointerdown', { pointerId: 1 });
  assert.equal(api.buttons(), 512);
  scan.emit('pointerup', { pointerId: 1 });
  scan.emit('click');
  assert.equal(api.buttons(), 0);
  get('touch-throw').emit('pointerdown', { pointerId: 2 });
  api.update(1, 511, 1, 0, 0, 12);
  assert.equal(scan.hidden, true);
  assert.equal(api.buttons(), 256);
  get('touch-cane').emit('pointerdown', { pointerId: 3 });
  get('touch-pause').emit('pointerdown', { pointerId: 4 });
  assert.equal(api.buttons(), 128);
  api.update(1, 511, 1, 0, 0, 4);
  assert.equal(scan.hidden, false);
});

test('pickup and throw remain usable when tutorial permits only those actions', () => {
  const { api, get } = boot(); api.update(1, 32, 0, 1, 0);
  get('touch-pickup').emit('pointerdown'); assert.equal(api.buttons(), 2);
  get('touch-pickup').emit('pointerup'); api.update(1, 64, 1, 0, 0);
  get('touch-throw').emit('pointerdown'); assert.equal(api.buttons(), 8);
  api.update(1, 64, 1, 0, .6); assert.equal(get('touch-throw').textContent, 'Throw 60%');
  get('touch-throw').emit('pointerup'); assert.equal(api.buttons(), 16); assert.equal(api.buttons(), 0);
});

test('a quick throw tap survives until the next Unity poll', () => {
  const { api, get } = boot(); api.update(1, 64, 1, 0, 0);
  get('touch-throw').emit('pointerdown'); get('touch-throw').emit('pointerup'); assert.equal(api.buttons(), 16);
});

test('canceled throw never queues a release', () => {
  const { api, get } = boot(); api.update(1, 64, 1, 0, 0);
  get('touch-throw').emit('pointerdown'); api.buttons();
  get('touch-throw').emit('pointercancel'); assert.equal(api.buttons(), 256); assert.equal(api.buttons(), 0);
});

test('lost pointer capture cancels movement and charge', () => {
  const { api, get } = boot(); api.update(1, 511, 1, 0, 0);
  get('touch-stick').emit('pointerdown', { pointerId: 1, clientX: 64, clientY: 0 });
  get('touch-throw').emit('pointerdown', { pointerId: 2 });
  get('touch-stick').emit('lostpointercapture', { pointerId: 1 });
  get('touch-throw').emit('lostpointercapture', { pointerId: 2 });
  assert.equal(api.axis(1), 0); assert.equal(api.buttons(), 256);
});

test('crouch toggles while sprint requires holding its own finger', () => {
  const { api, get } = boot(); api.update(1, 511, 0, 0, 0);
  get('touch-crouch').emit('pointerdown'); get('touch-crouch').emit('pointerup');
  assert.equal(api.buttons(), 32); assert.equal(api.buttons(), 32);
  get('touch-sprint').emit('pointerdown', { pointerId: 2 }); assert.equal(api.buttons(), 96);
  get('touch-sprint').emit('pointercancel', { pointerId: 2 }); assert.equal(api.buttons(), 32);
  get('touch-crouch').emit('pointerdown'); assert.equal(api.buttons(), 0);
});

test('tutorial action changes release now-forbidden gestures', () => {
  const { api, get } = boot(); api.update(1, 511, 1, 0, 0);
  get('touch-stick').emit('pointerdown', { pointerId: 1, clientX: 64, clientY: 0 });
  get('touch-throw').emit('pointerdown', { pointerId: 2 });
  api.update(1, 16, 1, 0, 0);
  assert.equal(api.axis(1), 0); assert.equal(api.buttons(), 256);
  assert.equal(get('touch-throw').disabled, true);
  get('touch-throw').emit('pointerdown'); assert.equal(api.buttons(), 0);
});

test('pause clears held inputs and shows large resume controls', () => {
  const { api, get } = boot(); api.update(1, 511, 1, 0, 0);
  get('touch-crouch').emit('pointerdown'); get('touch-crouch').emit('pointerup');
  get('touch-pause').emit('pointerdown'); assert.equal(api.buttons(), 160);
  api.update(2, 511, 1, 0, 0);
  assert.equal(api.buttons(), 0); assert.equal(get('touch-controls').hidden, true);
  assert.equal(get('touch-menu').hidden, false); assert.equal(get('touch-resume').hidden, false);
  api.update(1, 511, 1, 0, 0); assert.equal(api.buttons(), 0);
});

test('window backgrounding pauses once and retains canceled throw across both browser events', () => {
  const { api, get, window, document } = boot(); api.update(1, 511, 1, 0, 0);
  get('touch-throw').emit('pointerdown'); window.emit('blur');
  document.hidden = true; document.emit('visibilitychange');
  assert.equal(api.buttons(), 384); assert.equal(api.buttons(), 0);
});

test('orientation change pauses and drops all movement', () => {
  const { api, get, window } = boot(); api.update(1, 511, 0, 0, 0);
  get('touch-stick').emit('pointerdown', { clientX: 64, clientY: 0 }); window.emit('orientationchange');
  assert.equal(api.axis(1), 0); assert.equal(api.buttons(), 128);
});

test('Exit action works with only Exit permission and while carrying an object', () => {
  const { api, get } = boot(); api.update(1, 256, 1, 2, 0);
  assert.equal(get('touch-pickup').textContent, 'Exit'); assert.equal(get('touch-pickup').disabled, false);
  get('touch-pickup').emit('pointerdown'); assert.equal(api.buttons(), 2);
});

test('input mode is locked during play and can change in pause', () => {
  const { api, get } = boot(); api.update(1, 511, 0, 0, 0);
  get('touch-toggle').checked = false; get('touch-toggle').emit('change'); assert.equal(api.enabled, true);
  api.update(2, 511, 0, 0, 0); get('touch-toggle').checked = false; get('touch-toggle').emit('change');
  assert.equal(api.enabled, false); assert.equal(get('touch-menu').hidden, true);
});

test('native menu buttons target the existing Unity bridge and never run while Playing', () => {
  const { api, get, messages } = boot(); get('touch-begin').emit('click');
  assert.equal(JSON.stringify(messages), JSON.stringify([['Haunter browser bridge', 'TouchMenuAction', 'begin']]));
  api.update(1, 511, 0, 0, 0); get('touch-restart').emit('click'); assert.equal(messages.length, 1);
  api.update(2, 511, 0, 0, 0); get('touch-resume').emit('click'); assert.equal(messages[1][2], 'resume');
  for (const state of [3, 4]) {
    api.update(state, 511, 0, 0, 0);
    assert.equal(get('touch-menu').hidden, false);
    assert.equal(get('touch-restart').hidden, state === 4);
    assert.equal(get('touch-title').hidden, false);
    assert.equal(get('touch-resume').hidden, true);
    assert.equal(get('touch-skip').hidden, true);
  }
});

test('saved checkpoint, level choices and retry are shown in the appropriate menus', () => {
  const { api, get, messages } = boot();
  api.update(0, 511, 0, 0, 0, 0, 3, 1);
  assert.equal(get('touch-begin').textContent, 'Continue · Ballroom');
  assert.equal(get('touch-levels').hidden, false);
  get('touch-level-4').emit('click');
  assert.equal(messages[0][2], 'level-4');

  api.update(5, 511, 0, 0, 0, 0, 3, 4);
  assert.equal(get('touch-menu').hidden, true);
  assert.equal(get('touch-controls').hidden, true);
  assert.equal(get('touch-toggle').disabled, true);
  get('touch-level-1').emit('click');
  assert.equal(messages.length, 1);

  api.update(3, 511, 0, 0, 0, 0, 3, 4);
  assert.equal(get('touch-restart').textContent, 'Retry Final maze');
  get('touch-restart').emit('click');
  get('touch-title').emit('click');
  assert.deepEqual(messages.slice(1).map(message => message[2]), ['restart', 'title']);
});

test('unsupported fullscreen uses an escapable full-frame layout, preserving the controls', async () => {
  const { api, get, document, window } = boot();
  await api.enterFullscreen();
  assert.equal(get('player-frame').classList.contains('game-expanded'), true);
  assert.equal(document.body.classList.contains('game-expanded-page'), true);
  assert.equal(get('exit-expanded').hidden, false);
  assert.equal(get('exit-expanded').textContent, 'Exit expanded view');
  document.emit('fullscreenchange');
  assert.equal(get('player-frame').classList.contains('game-expanded'), true);
  get('exit-expanded').emit('click');
  assert.equal(get('player-frame').classList.contains('game-expanded'), false);
  assert.equal(JSON.stringify(window.lastScroll), '[0,72]');
});
