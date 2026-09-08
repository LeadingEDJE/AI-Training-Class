// Turns hand-authored pixel data into offscreen canvases, once, at startup.
//
// Pixel data is an array of equal-length strings; each character is looked up in
// a per-sprite map to get a palette key. '.' and ' ' are always transparent, so
// the art stays readable in source.

import { color } from '../art/palette.js';

const TRANSPARENT = new Set(['.', ' ']);

/** Create a bare offscreen canvas with pixel-art-safe context settings. */
export function makeCanvas(w, h) {
  const canvas = document.createElement('canvas');
  canvas.width = w;
  canvas.height = h;
  const ctx = canvas.getContext('2d');
  ctx.imageSmoothingEnabled = false;
  return canvas;
}

/**
 * Bake one frame of pixel data into a canvas.
 * @param {string[]} rows Pixel rows, all the same length.
 * @param {Record<string,string>} map Character -> palette key.
 */
export function bake(rows, map) {
  if (!rows.length) throw new Error('bake() got empty pixel data');
  const h = rows.length;
  const w = rows[0].length;

  for (const row of rows) {
    if (row.length !== w) {
      throw new Error(`Ragged pixel data: expected width ${w}, got ${row.length} in "${row}"`);
    }
  }

  const canvas = makeCanvas(w, h);
  const ctx = canvas.getContext('2d');

  for (let y = 0; y < h; y++) {
    const row = rows[y];
    for (let x = 0; x < w; x++) {
      const ch = row[x];
      if (TRANSPARENT.has(ch)) continue;
      const key = map[ch];
      if (!key) throw new Error(`Pixel char '${ch}' is not in the sprite's palette map`);
      ctx.fillStyle = color(key);
      ctx.fillRect(x, y, 1, 1);
    }
  }

  return canvas;
}

/**
 * Bake a sprite definition of the form { palette, frames: [rows, rows, ...] }
 * into an array of canvases, one per frame.
 */
export function bakeFrames(def) {
  return def.frames.map((rows) => bake(rows, def.palette));
}

/**
 * Produce a solid-color silhouette of an already-baked canvas, preserving its
 * alpha. Used for the damage flash, so we don't have to author hurt frames.
 */
export function silhouette(source, paletteKey) {
  const out = makeCanvas(source.width, source.height);
  const ctx = out.getContext('2d');
  ctx.drawImage(source, 0, 0);
  ctx.globalCompositeOperation = 'source-in';
  ctx.fillStyle = color(paletteKey);
  ctx.fillRect(0, 0, out.width, out.height);
  return out;
}

/**
 * Mirror a baked canvas horizontally. Lets us author one side-facing sprite and
 * get the other for free.
 */
export function flipH(source) {
  const out = makeCanvas(source.width, source.height);
  const ctx = out.getContext('2d');
  ctx.translate(source.width, 0);
  ctx.scale(-1, 1);
  ctx.drawImage(source, 0, 0);
  return out;
}
