// F1 overlay. With no automated tests in this project, this IS the verification
// tool — hitboxes, walkability, and state are all easier to trust when you can
// see them.

import { TILE, COLS, ROWS, FIELD_Y, SCREEN_W, SCREEN_H } from '../constants.js';
import { drawText } from '../art/font.js';
import { isWalkable, centerTile } from './world.js';
import { swordHitbox } from './player.js';

export function drawDebug(renderer, game, stats) {
  const p = game.player;
  const { tx, ty } = centerTile(p);

  // Tint every blocked tile so collision data is visible at a glance.
  const ctx = renderer.ctx;
  ctx.save();
  ctx.globalAlpha = 0.25;
  for (let y = 0; y < ROWS; y++) {
    for (let x = 0; x < COLS; x++) {
      if (!isWalkable(game.screen, x, y)) {
        renderer.rect(x * TILE, FIELD_Y + y * TILE, TILE, TILE, 'hudRed');
      }
    }
  }
  ctx.restore();

  // Hitboxes.
  renderer.outline(p.x, p.y + FIELD_Y, p.w, p.h, '#00ff00');
  const blade = swordHitbox(p);
  if (blade) renderer.outline(blade.x, blade.y + FIELD_Y, blade.w, blade.h, '#ffffff');
  for (const e of game.enemies) renderer.outline(e.x, e.y + FIELD_Y, e.w, e.h, '#ff00ff');
  for (const s of game.projectiles) renderer.outline(s.x, s.y + FIELD_Y, s.w, s.h, '#ffff00');
  for (const item of game.pickups) renderer.outline(item.x, item.y + FIELD_Y, item.w, item.h, '#00ffff');
  for (const npc of game.npcs) renderer.outline(npc.x, npc.y + FIELD_Y, npc.w, npc.h, '#ff8800');

  // Readout, bottom-left, over a strip so it stays legible on sand.
  const lines = [
    `FPS ${stats.fps} STEPS ${stats.steps}`,
    `STATE ${game.state}`,
    `SCREEN ${game.screen.id}`,
    `POS ${Math.round(p.x)},${Math.round(p.y)} TILE ${tx},${ty}`,
    `DIR ${p.dir} SWING ${p.swinging} INV ${p.invuln}`,
    `FOES ${game.enemies.length} SHOTS ${game.projectiles.length}`,
  ];
  const boxH = lines.length * 8 + 4;
  renderer.rect(0, SCREEN_H - boxH, SCREEN_W, boxH, 'black');
  lines.forEach((line, i) => {
    drawText(renderer, line, 2, SCREEN_H - boxH + 2 + i * 8, 'white');
  });
}
