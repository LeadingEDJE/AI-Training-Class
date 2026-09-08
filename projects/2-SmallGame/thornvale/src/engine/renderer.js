// Everything draws into a 256x240 backbuffer at native resolution. Once per
// frame that backbuffer is blitted to the visible canvas at the largest INTEGER
// scale that fits the window. Integer-only scaling is what keeps pixels crisp —
// a fractional scale would blur every edge.

import { SCREEN_W, SCREEN_H } from '../constants.js';
import { makeCanvas } from './pixels.js';
import { color } from '../art/palette.js';

export class Renderer {
  constructor(displayCanvas) {
    this.display = displayCanvas;
    this.displayCtx = displayCanvas.getContext('2d');
    this.displayCtx.imageSmoothingEnabled = false;

    this.buffer = makeCanvas(SCREEN_W, SCREEN_H);
    this.ctx = this.buffer.getContext('2d');
    this.scale = 1;

    this.resize();
    window.addEventListener('resize', () => this.resize());
  }

  /** Pick the largest integer scale that fits the viewport, minimum 1. */
  resize() {
    const margin = 16;
    const availW = Math.max(1, window.innerWidth - margin);
    const availH = Math.max(1, window.innerHeight - margin);
    const scale = Math.max(1, Math.floor(Math.min(availW / SCREEN_W, availH / SCREEN_H)));

    this.scale = scale;
    this.display.width = SCREEN_W * scale;
    this.display.height = SCREEN_H * scale;
    this.display.style.width = `${SCREEN_W * scale}px`;
    this.display.style.height = `${SCREEN_H * scale}px`;
    // Resizing a canvas resets its context state.
    this.displayCtx.imageSmoothingEnabled = false;
  }

  clear(paletteKey = 'black') {
    this.ctx.fillStyle = color(paletteKey);
    this.ctx.fillRect(0, 0, SCREEN_W, SCREEN_H);
  }

  /** Draw a baked sprite canvas at integer coordinates. */
  sprite(canvas, x, y) {
    this.ctx.drawImage(canvas, Math.round(x), Math.round(y));
  }

  rect(x, y, w, h, paletteKey) {
    this.ctx.fillStyle = color(paletteKey);
    this.ctx.fillRect(Math.round(x), Math.round(y), Math.round(w), Math.round(h));
  }

  /** Outline for the debug overlay. Uses a raw CSS color, not a palette key. */
  outline(x, y, w, h, cssColor) {
    this.ctx.strokeStyle = cssColor;
    this.ctx.lineWidth = 1;
    this.ctx.strokeRect(Math.round(x) + 0.5, Math.round(y) + 0.5, Math.round(w) - 1, Math.round(h) - 1);
  }

  /** Push the backbuffer to the screen. Called once per animation frame. */
  present() {
    this.displayCtx.drawImage(
      this.buffer,
      0,
      0,
      SCREEN_W,
      SCREEN_H,
      0,
      0,
      SCREEN_W * this.scale,
      SCREEN_H * this.scale,
    );
  }
}
