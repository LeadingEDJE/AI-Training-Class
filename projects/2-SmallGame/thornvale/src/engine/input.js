// Keyboard input, normalized into intents so the game never sees key codes.
//
// Movement accepts WASD, arrows, and the numpad. The original game has no
// diagonal movement, so we track the order directions were pressed and let the
// most recent win — which also means we never have to normalize diagonal speed.

export const Dir = {
  UP: 'up',
  DOWN: 'down',
  LEFT: 'left',
  RIGHT: 'right',
};

const DIR_KEYS = {
  // WASD
  KeyW: Dir.UP,
  KeyA: Dir.LEFT,
  KeyS: Dir.DOWN,
  KeyD: Dir.RIGHT,
  // Arrows
  ArrowUp: Dir.UP,
  ArrowLeft: Dir.LEFT,
  ArrowDown: Dir.DOWN,
  ArrowRight: Dir.RIGHT,
  // Numpad
  Numpad8: Dir.UP,
  Numpad4: Dir.LEFT,
  Numpad2: Dir.DOWN,
  Numpad6: Dir.RIGHT,
};

const ACTION_KEYS = new Set(['Space', 'Enter', 'NumpadEnter', 'ControlLeft', 'ControlRight']);

// Keys we swallow so the page never scrolls or scrubs while you're playing.
// Ctrl is deliberately absent: swallowing it would break Ctrl+R and friends.
const SWALLOW = new Set([
  ...Object.keys(DIR_KEYS),
  'Space',
  'Enter',
  'NumpadEnter',
  'F1',
]);

export class Input {
  constructor() {
    this.held = new Set();
    this.dirStack = []; // oldest first, newest last
    this.pendingAction = false;
    this.pendingDebug = false;
    this.pendingRestart = false;

    window.addEventListener('keydown', (e) => this.#onDown(e));
    window.addEventListener('keyup', (e) => this.#onUp(e));
    // Releasing focus mid-hold would otherwise leave a key stuck down.
    window.addEventListener('blur', () => this.#releaseAll());
  }

  #onDown(e) {
    if (SWALLOW.has(e.code)) e.preventDefault();

    if (e.repeat) return; // we do our own edge detection

    const dir = DIR_KEYS[e.code];
    if (dir) {
      this.held.add(e.code);
      // Re-pressing a direction moves it to the top of the stack.
      const existing = this.dirStack.indexOf(dir);
      if (existing !== -1) this.dirStack.splice(existing, 1);
      this.dirStack.push(dir);
      return;
    }

    if (ACTION_KEYS.has(e.code)) {
      this.pendingAction = true;
      return;
    }

    if (e.code === 'F1') this.pendingDebug = true;
    if (e.code === 'KeyR' && !e.ctrlKey && !e.metaKey) this.pendingRestart = true;
  }

  #onUp(e) {
    const dir = DIR_KEYS[e.code];
    if (!dir) return;
    this.held.delete(e.code);

    // Only drop the direction from the stack if no other key still holds it
    // (e.g. W and ArrowUp both mean UP).
    const stillHeld = Object.entries(DIR_KEYS).some(
      ([code, d]) => d === dir && this.held.has(code),
    );
    if (!stillHeld) {
      const at = this.dirStack.indexOf(dir);
      if (at !== -1) this.dirStack.splice(at, 1);
    }
  }

  #releaseAll() {
    this.held.clear();
    this.dirStack.length = 0;
  }

  /** The single direction currently being requested, or null. */
  get direction() {
    return this.dirStack.length ? this.dirStack[this.dirStack.length - 1] : null;
  }

  /** True once per press. Consumes the edge. */
  consumeAction() {
    const was = this.pendingAction;
    this.pendingAction = false;
    return was;
  }

  consumeDebugToggle() {
    const was = this.pendingDebug;
    this.pendingDebug = false;
    return was;
  }

  consumeRestart() {
    const was = this.pendingRestart;
    this.pendingRestart = false;
    return was;
  }
}
