// The status panel, laid out to match the NES original: a minimap box on the
// left, the item counters beside it, the B and A slots, and the -LIFE- hearts
// over on the right.

import { SCREEN_W, HUD_H } from '../constants.js';
import { drawText } from '../art/font.js';
import { art } from '../art/sprites.js';
import { screens } from './screens.js';

// Minimap box, sized like the original's 8x4 room grid but showing our little
// 2x2 corner of the world.
const MAP_X = 16;
const MAP_Y = 12;
const MAP_W = 64;
const MAP_H = 32;
const MAP_COLS = 2;
const MAP_ROWS = 2;

const COUNTER_X = 96;
const SLOT_B_X = 152;
const SLOT_A_X = 184;
const SLOT_Y = 16;
const SLOT_SIZE = 24;

const LIFE_X = 216;

export function drawHud(renderer, game) {
  renderer.rect(0, 0, SCREEN_W, HUD_H, 'black');

  drawMinimap(renderer, game);
  drawCounters(renderer, game);
  drawItemSlots(renderer, game);
  drawLife(renderer, game);
}

function drawMinimap(renderer, game) {
  renderer.rect(MAP_X, MAP_Y, MAP_W, MAP_H, 'grayDark');

  const cellW = MAP_W / MAP_COLS;
  const cellH = MAP_H / MAP_ROWS;

  // Rooms you've been in show as lighter cells; the one you're in gets a blip.
  for (const screen of Object.values(screens)) {
    if (!screen.outdoors || !screen.mapPos) continue;
    if (!game.visited.has(screen.id)) continue;
    renderer.rect(
      MAP_X + screen.mapPos.x * cellW,
      MAP_Y + screen.mapPos.y * cellH,
      cellW,
      cellH,
      'gray',
    );
  }

  const here = game.screen.outdoors ? game.screen : screens[game.lastOutdoorScreen];
  if (here?.mapPos) {
    renderer.rect(
      MAP_X + here.mapPos.x * cellW + cellW / 2 - 2,
      MAP_Y + here.mapPos.y * cellH + cellH / 2 - 2,
      4,
      4,
      'leafLight',
    );
  }
}

function drawCounters(renderer, game) {
  const { run } = game;

  renderer.sprite(art.rupee, COUNTER_X, 10);
  drawText(renderer, `X${String(run.rupees).padStart(2, '0')}`, COUNTER_X + 10, 11, 'white');

  drawText(renderer, `KEY X${run.keys}`, COUNTER_X, 26, 'white');
  drawText(renderer, `BOM X${run.bombs}`, COUNTER_X, 38, 'white');
}

function drawItemSlots(renderer, game) {
  drawText(renderer, 'B', SLOT_B_X + 8, 4, 'white');
  drawText(renderer, 'A', SLOT_A_X + 8, 4, 'white');

  renderer.outline(SLOT_B_X, SLOT_Y, SLOT_SIZE, SLOT_SIZE, '#5878f8');
  renderer.outline(SLOT_A_X, SLOT_Y, SLOT_SIZE, SLOT_SIZE, '#5878f8');

  // The sword lives in the A slot once the Hermit hands it over.
  if (game.run.hasSword) {
    renderer.sprite(art.sword.up, SLOT_A_X + 9, SLOT_Y + 5);
  }
}

function drawLife(renderer, game) {
  drawText(renderer, '-LIFE-', LIFE_X, 10, 'hudRed');

  const { health, maxHealth } = game.run;
  const totalHearts = Math.ceil(maxHealth / 2);
  for (let i = 0; i < totalHearts; i++) {
    const remaining = health - i * 2;
    const frame = remaining >= 2 ? art.heart.full : remaining === 1 ? art.heart.half : art.heart.empty;
    renderer.sprite(frame, LIFE_X + i * 10, 26);
  }
}
