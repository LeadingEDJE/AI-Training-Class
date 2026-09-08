// Tile art, hand-authored as 16 rows of 16 characters.
//
// Two different character alphabets are in play and it's worth keeping them
// straight: the strings BELOW are pixel data (each char is one pixel, mapped by
// that tile's own palette map). The single-character keys in TILES are the
// characters used in screen layouts over in game/screens.js.
//
// Pixel data must never use '.' or ' ' — those mean "transparent" to the baker,
// and ground tiles are opaque.

import { bake } from '../engine/pixels.js';

const GREEN = { s: 'sand', d: 'leafDark', l: 'leaf', L: 'leafLight', t: 'rockDark' };
const BLUE = { w: 'water', W: 'waterLight', v: 'waterDark' };
const BROWN = { L: 'rockLight', r: 'rock', d: 'rockDark' };
const SANDY = { s: 'sand', S: 'sandDark' };
const CAVE = { s: 'sand', k: 'black' };
const STAIR = { d: 'rockDark', r: 'rock', L: 'rockLight' };
const WALL = { r: 'rock', d: 'rockDark', L: 'rockLight', k: 'black' };
const FLOOR = { k: 'black', g: 'grayDark' };

// A full canopy with a short trunk. Fills the tile so tree masses read solid.
const TREE = [
  'sssssddddddsssss',
  'sssdLLLLLLLLdsss',
  'ssdLLLLLLLLLLdss',
  'sdLLLLllllllllds',
  'sdLLllllllllllds',
  'dLllllllllllllld',
  'dlllllllllllllld',
  'dlllllllllllllld',
  'sdllllllllllllds',
  'ssdlllllllllldss',
  'ssssddddddddssss',
  'ssssssttttssssss',
  'ssssssttttssssss',
  'ssssssttttssssss',
  'ssssssttttssssss',
  'sssssttttttsssss',
];

// Dithered foliage block — the alternating pixels are deliberate NES-style
// dithering rather than a flat fill.
const BUSH = (() => {
  const rows = ['dddddddddddddddd'];
  for (let i = 0; i < 14; i++) {
    const interior = Array.from({ length: 14 }, (_, x) => ((x + i) % 2 === 0 ? 'L' : 'l')).join('');
    rows.push(`d${interior}d`);
  }
  rows.push('dddddddddddddddd');
  return rows;
})();

const WATER = [
  'wwwwwwwwwwwwwwww',
  'wwwwwwwwwwwwwwww',
  'wwWWWWwwwwWWWWww',
  'wwwwwwwwwwwwwwww',
  'wwwwwwwwwwwwwwww',
  'WWwwwwWWWWwwwwWW',
  'wwwwwwwwwwwwwwww',
  'wwwwwvwwwwvwwwww',
  'wwwwwwwwwwwwwwww',
  'wwwwwwwwwwwwwwww',
  'wwWWWWwwwwWWWWww',
  'wwwwwwwwwwwwwwww',
  'wwwwwwwwwwwwwwww',
  'WWwwwwWWWWwwwwWW',
  'wwwwwwwwwwwwwwww',
  'wwwwwvwwwwvwwwww',
];

const ROCK = [
  'LLLLLLLLLLLLLLLL',
  'Lrrrrrrrrrrrrrrd',
  'Lrrrrrrrrrrrrrrd',
  'Lrrrrdddrrrrrrrd',
  'Lrrrrrrrrrrrrrrd',
  'Lrrrrrrrrrddrrrd',
  'Lrrrrrrrrrrrrrrd',
  'Lrrdddrrrrrrrrrd',
  'Lrrrrrrrrrrrrrrd',
  'Lrrrrrrrrrrddrrd',
  'Lrrrrrrrrrrrrrrd',
  'Lrrdrrrrrrrrrrrd',
  'Lrrrrrrrrrrrrrrd',
  'Lrrrrrrrrrrrrrrd',
  'Lrrrrrrrrrrrrrrd',
  'dddddddddddddddd',
];

const SAND = [
  'ssssssssssssssss',
  'sssssssssssSssss',
  'ssssssssssssssss',
  'ssSsssssssssssss',
  'ssssssssssssssss',
  'ssssssssssssssss',
  'sssssssssssssSss',
  'ssssssssssssssss',
  'ssssssssssssssss',
  'ssssSsssssssssss',
  'ssssssssssssssss',
  'ssssssssssssssss',
  'ssssssssssssssss',
  'sssssssssSssssss',
  'ssssssssssssssss',
  'ssssssssssssssss',
];

const CAVE_MOUTH = [
  'ssssssssssssssss',
  'ssssskkkkkksssss',
  'sssskkkkkkkkssss',
  'ssskkkkkkkkkksss',
  'sskkkkkkkkkkkkss',
  'sskkkkkkkkkkkkss',
  'sskkkkkkkkkkkkss',
  'sskkkkkkkkkkkkss',
  'sskkkkkkkkkkkkss',
  'sskkkkkkkkkkkkss',
  'sskkkkkkkkkkkkss',
  'sskkkkkkkkkkkkss',
  'sskkkkkkkkkkkkss',
  'sskkkkkkkkkkkkss',
  'sskkkkkkkkkkkkss',
  'sskkkkkkkkkkkkss',
];

const STAIRS = [
  'dddddddddddddddd',
  'dLLLLLLLLLLLLLLd',
  'drrrrrrrrrrrrrrd',
  'dddddddddddddddd',
  'dLLLLLLLLLLLLLLd',
  'drrrrrrrrrrrrrrd',
  'dddddddddddddddd',
  'dLLLLLLLLLLLLLLd',
  'drrrrrrrrrrrrrrd',
  'dddddddddddddddd',
  'dLLLLLLLLLLLLLLd',
  'drrrrrrrrrrrrrrd',
  'dddddddddddddddd',
  'dLLLLLLLLLLLLLLd',
  'drrrrrrrrrrrrrrd',
  'dddddddddddddddd',
];

const CAVE_WALL = [
  'LLLLLLLLLLLLLLLL',
  'Lrrrrrrdrrrrrrrd',
  'Lrrrrrrdrrrrrrrd',
  'dddddddddddddddd',
  'LLLLLLLLLLLLLLLL',
  'Lrrdrrrrrrrrdrrd',
  'Lrrdrrrrrrrrdrrd',
  'dddddddddddddddd',
  'LLLLLLLLLLLLLLLL',
  'Lrrrrrrdrrrrrrrd',
  'Lrrrrrrdrrrrrrrd',
  'dddddddddddddddd',
  'LLLLLLLLLLLLLLLL',
  'Lrrdrrrrrrrrdrrd',
  'Lrrdrrrrrrrrdrrd',
  'dddddddddddddddd',
];

const CAVE_FLOOR = [
  'kkkkkkkkkkkkkkkk',
  'kkkkkkkkkkkkkkkk',
  'kkkkkkkkkkkkkkkk',
  'kkkkkkkkkkkkkkkk',
  'kkkkkkkkkkkkkkkk',
  'kkkkkkkkkkkkkkkk',
  'kkkkkkkkkkkkkkkk',
  'kkkkkkkgkkkkkkkk',
  'kkkkkkkkkkkkkkkk',
  'kkkkkkkkkkkkkkkk',
  'kkkkkkkkkkkkkkkk',
  'kkkkkkkkkkkkkkkk',
  'kkkkkkkkkkkkkkkk',
  'kkkkkkkkkkkkkkkk',
  'kkkkkkkkkkkkkkkk',
  'kkkkkkkkkkkkkkkk',
];

/**
 * The tile table. Keys are the characters used in screen layouts.
 * `walkable` drives collision; `trigger` marks tiles the world reacts to.
 */
export const TILES = {
  '.': { name: 'sand', pixels: SAND, map: SANDY, walkable: true },
  T: { name: 'tree', pixels: TREE, map: GREEN, walkable: false },
  B: { name: 'bush', pixels: BUSH, map: GREEN, walkable: false },
  W: { name: 'water', pixels: WATER, map: BLUE, walkable: false },
  R: { name: 'rock', pixels: ROCK, map: BROWN, walkable: false },
  C: { name: 'caveMouth', pixels: CAVE_MOUTH, map: CAVE, walkable: true, trigger: 'enter' },
  S: { name: 'stairs', pixels: STAIRS, map: STAIR, walkable: true, trigger: 'exit' },
  '#': { name: 'caveWall', pixels: CAVE_WALL, map: WALL, walkable: false },
  _: { name: 'caveFloor', pixels: CAVE_FLOOR, map: FLOOR, walkable: true },
};

/** Baked tile canvases, keyed by layout character. Populated by bakeTiles(). */
export const tileArt = {};

export function bakeTiles() {
  for (const [ch, tile] of Object.entries(TILES)) {
    tileArt[ch] = bake(tile.pixels, tile.map);
  }
}
