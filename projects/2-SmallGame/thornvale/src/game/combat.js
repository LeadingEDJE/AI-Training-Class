// Where things touch each other: sword vs enemy, enemy vs player, rock vs
// player, player vs rupee.
//
// Kept in one place so the interaction order is explicit and easy to reason
// about — the sword resolves before contact damage, so killing something the
// same step it touches you doesn't also cost you a heart.

import { overlaps } from '../engine/collision.js';
import { swordHitbox, hurtPlayer } from './player.js';
import { killEnemy } from './enemies.js';
import { Dir } from '../engine/input.js';

/** Which way should a hit push the player, given what hit them. */
function pushDirection(player, source) {
  const dx = player.x + player.w / 2 - (source.x + source.w / 2);
  const dy = player.y + player.h / 2 - (source.y + source.h / 2);
  if (Math.abs(dx) > Math.abs(dy)) return dx > 0 ? Dir.RIGHT : Dir.LEFT;
  return dy > 0 ? Dir.DOWN : Dir.UP;
}

export function resolveCombat(game) {
  const p = game.player;

  // 1. The sword.
  const blade = swordHitbox(p);
  if (blade) {
    for (const e of game.enemies) {
      if (e.dead) continue;
      if (overlaps(blade, e)) killEnemy(game, e);
    }
  }

  // 2. Contact damage from surviving enemies.
  for (const e of game.enemies) {
    if (e.dead) continue;
    if (overlaps(p, e)) {
      hurtPlayer(game, 1, pushDirection(p, e));
      break;
    }
  }

  // 3. Rocks.
  for (let i = game.projectiles.length - 1; i >= 0; i--) {
    const shot = game.projectiles[i];
    if (overlaps(p, shot)) {
      hurtPlayer(game, 1, pushDirection(p, shot));
      game.projectiles.splice(i, 1);
      break;
    }
  }

  // 4. Pickups.
  for (let i = game.pickups.length - 1; i >= 0; i--) {
    const item = game.pickups[i];
    if (overlaps(p, item)) {
      if (item.kind === 'rupee') game.run.rupees = Math.min(255, game.run.rupees + 1);
      game.pickups.splice(i, 1);
    }
  }
}
