// A 5x7 pixel font, written in compact form: seven rows of bits per glyph,
// slash-separated, so each character stays on one readable line.
//
// Glyphs are baked once in white, then tinted on demand and cached per color —
// which means the HUD's red "-LIFE-" and white counters share the same source.

import { bake, silhouette } from '../engine/pixels.js';

export const GLYPH_W = 5;
export const GLYPH_H = 7;

const GLYPHS = {
  A: '01110/10001/10001/11111/10001/10001/10001',
  B: '11110/10001/10001/11110/10001/10001/11110',
  C: '01110/10001/10000/10000/10000/10001/01110',
  D: '11110/10001/10001/10001/10001/10001/11110',
  E: '11111/10000/10000/11110/10000/10000/11111',
  F: '11111/10000/10000/11110/10000/10000/10000',
  G: '01110/10001/10000/10111/10001/10001/01110',
  H: '10001/10001/10001/11111/10001/10001/10001',
  I: '11111/00100/00100/00100/00100/00100/11111',
  J: '00111/00010/00010/00010/00010/10010/01100',
  K: '10001/10010/10100/11000/10100/10010/10001',
  L: '10000/10000/10000/10000/10000/10000/11111',
  M: '10001/11011/10101/10101/10001/10001/10001',
  N: '10001/11001/10101/10011/10001/10001/10001',
  O: '01110/10001/10001/10001/10001/10001/01110',
  P: '11110/10001/10001/11110/10000/10000/10000',
  Q: '01110/10001/10001/10001/10101/10011/01101',
  R: '11110/10001/10001/11110/10100/10010/10001',
  S: '01111/10000/10000/01110/00001/00001/11110',
  T: '11111/00100/00100/00100/00100/00100/00100',
  U: '10001/10001/10001/10001/10001/10001/01110',
  V: '10001/10001/10001/10001/10001/01010/00100',
  W: '10001/10001/10001/10101/10101/11011/10001',
  X: '10001/10001/01010/00100/01010/10001/10001',
  Y: '10001/10001/01010/00100/00100/00100/00100',
  Z: '11111/00001/00010/00100/01000/10000/11111',
  0: '01110/10001/10011/10101/11001/10001/01110',
  1: '00100/01100/00100/00100/00100/00100/01110',
  2: '01110/10001/00001/00010/00100/01000/11111',
  3: '11111/00010/00100/00010/00001/10001/01110',
  4: '00010/00110/01010/10010/11111/00010/00010',
  5: '11111/10000/11110/00001/00001/10001/01110',
  6: '00110/01000/10000/11110/10001/10001/01110',
  7: '11111/00001/00010/00100/01000/01000/01000',
  8: '01110/10001/10001/01110/10001/10001/01110',
  9: '01110/10001/10001/01111/00001/00010/01100',
  ' ': '00000/00000/00000/00000/00000/00000/00000',
  '-': '00000/00000/00000/11111/00000/00000/00000',
  '.': '00000/00000/00000/00000/00000/01100/01100',
  ',': '00000/00000/00000/00000/01100/01100/00100',
  ':': '00000/01100/01100/00000/01100/01100/00000',
  '!': '00100/00100/00100/00100/00100/00000/00100',
  '?': '01110/10001/00001/00110/00100/00000/00100',
  "'": '00100/00100/00000/00000/00000/00000/00000',
  '+': '00000/00100/00100/11111/00100/00100/00000',
  '=': '00000/00000/11111/00000/11111/00000/00000',
};

const base = {}; // char -> white canvas
const tinted = new Map(); // paletteKey -> { char -> canvas }

export function bakeFont() {
  for (const [ch, spec] of Object.entries(GLYPHS)) {
    const rows = spec.split('/').map((r) => r.replace(/0/g, '.').replace(/1/g, 'w'));
    base[ch] = bake(rows, { w: 'white' });
  }
}

function glyphs(paletteKey) {
  if (paletteKey === 'white') return base;
  let set = tinted.get(paletteKey);
  if (!set) {
    set = {};
    for (const [ch, canvas] of Object.entries(base)) {
      set[ch] = silhouette(canvas, paletteKey);
    }
    tinted.set(paletteKey, set);
  }
  return set;
}

export function textWidth(text, spacing = 1) {
  if (!text.length) return 0;
  return text.length * (GLYPH_W + spacing) - spacing;
}

/**
 * Draw a string of text. Unknown characters are skipped rather than throwing —
 * dialogue copy shouldn't be able to crash the game.
 */
export function drawText(renderer, text, x, y, paletteKey = 'white', spacing = 1) {
  const set = glyphs(paletteKey);
  let cursor = Math.round(x);
  for (const raw of text.toUpperCase()) {
    const canvas = set[raw];
    if (canvas) renderer.sprite(canvas, cursor, y);
    cursor += GLYPH_W + spacing;
  }
}
