// Wren: movement, the walk cycle, the sword swing, and taking a hit.
//
// The collision box is deliberately smaller than the 16x16 sprite and sits over
// the lower body. That's what lets you slip through a one-tile gap and makes the
// sprite look like it's standing in the world rather than on top of it.

import {
  TILE,
  PLAYER_SPEED,
  SWING_STEPS,
  SWING_ACTIVE_FROM,
  SWING_ACTIVE_TO,
  INVULN_STEPS,
  KNOCKBACK_STEPS,
  KNOCKBACK_DIST,
} from '../constants.js';
import { Dir } from '../engine/input.js';
import { moveAndCollide } from '../engine/collision.js';
import { isWalkable, checkEdgeExit, checkTriggers } from './world.js';
import { art } from '../art/sprites.js';
import { State } from './state.js';

const BOX_W = 12;
const BOX_H = 12;
const DRAW_OFF_X = -2;
const DRAW_OFF_Y = -4;

// Distance walked per animation frame flip. Tying the walk cycle to distance
// rather than time keeps the feet locked to the motion at any speed.
const STRIDE = 8;

export function createPlayer() {
  return {
    x: 0,
    y: 0,
    w: BOX_W,
    h: BOX_H,
    dir: Dir.DOWN,
    // Animation
    anim: 0,
    walked: 0,
    // Sword
    swinging: 0, // steps remaining in the swing
    // Damage
    invuln: 0,
    knockback: 0,
    knockDir: Dir.DOWN,
  };
}

export function placePlayerAtTile(player, tx, ty) {
  player.x = tx * TILE + (TILE - player.w) / 2;
  player.y = ty * TILE + (TILE - player.h) / 2;
}

const DELTA = {
  up: { x: 0, y: -1 },
  down: { x: 0, y: 1 },
  left: { x: -1, y: 0 },
  right: { x: 1, y: 0 },
};

export function updatePlayer(game) {
  const p = game.player;

  if (p.invuln > 0) p.invuln--;

  // Knockback overrides player control entirely — you don't get to steer out of
  // a hit, which is what makes damage feel like it landed.
  if (p.knockback > 0) {
    p.knockback--;
    const d = DELTA[p.knockDir];
    const speed = KNOCKBACK_DIST / KNOCKBACK_STEPS;
    moveAndCollide(p, d.x * speed, d.y * speed, game.screen, isWalkable);
    return;
  }

  if (p.swinging > 0) {
    p.swinging--;
    return; // rooted in place for the whole swing
  }

  // Start a swing.
  if (game.input.consumeAction()) {
    if (tryTalk(game)) return;
    if (game.run.hasSword) {
      p.swinging = SWING_STEPS;
      return;
    }
  }

  const dir = game.input.direction;
  if (!dir) {
    p.walked = 0;
    p.anim = 0;
    return;
  }

  p.dir = dir;
  const d = DELTA[dir];
  const dx = d.x * PLAYER_SPEED;
  const dy = d.y * PLAYER_SPEED;

  const before = { x: p.x, y: p.y };
  moveAndCollide(p, dx, dy, game.screen, isWalkable);
  const moved = Math.abs(p.x - before.x) + Math.abs(p.y - before.y);

  if (moved > 0) {
    p.walked += moved;
    if (p.walked >= STRIDE) {
      p.walked -= STRIDE;
      p.anim = (p.anim + 1) % 2;
    }
  } else if (checkEdgeExit(game, dir)) {
    return; // walked off the edge; the transition owns things now
  }

  checkTriggers(game);
}

/** Talk to an adjacent NPC. Returns true if a conversation started. */
function tryTalk(game) {
  const p = game.player;
  const d = DELTA[p.dir];
  // Probe a little way in front of the player.
  const probe = {
    x: p.x + d.x * TILE,
    y: p.y + d.y * TILE,
    w: p.w,
    h: p.h,
  };
  for (const npc of game.npcs) {
    if (
      probe.x < npc.x + npc.w &&
      probe.x + probe.w > npc.x &&
      probe.y < npc.y + npc.h &&
      probe.y + probe.h > npc.y
    ) {
      game.startDialogue(npc);
      return true;
    }
  }
  return false;
}

/** The sword's damage box, or null when the blade isn't live. */
export function swordHitbox(player) {
  if (player.swinging === 0) return null;
  const elapsed = SWING_STEPS - player.swinging;
  if (elapsed < SWING_ACTIVE_FROM || elapsed > SWING_ACTIVE_TO) return null;

  const reach = 14;
  const thick = 8;
  switch (player.dir) {
    case Dir.UP:
      return { x: player.x + (player.w - thick) / 2, y: player.y - reach, w: thick, h: reach };
    case Dir.DOWN:
      return { x: player.x + (player.w - thick) / 2, y: player.y + player.h, w: thick, h: reach };
    case Dir.LEFT:
      return { x: player.x - reach, y: player.y + (player.h - thick) / 2, w: reach, h: thick };
    default:
      return { x: player.x + player.w, y: player.y + (player.h - thick) / 2, w: reach, h: thick };
  }
}

export function hurtPlayer(game, amount, fromDir) {
  const p = game.player;
  if (p.invuln > 0) return;

  game.run.health = Math.max(0, game.run.health - amount);
  p.invuln = INVULN_STEPS;
  p.knockback = KNOCKBACK_STEPS;
  p.knockDir = fromDir;

  if (game.run.health === 0) {
    p.knockback = 0;
    game.state = State.DYING;
    game.deathTimer = 90;
  }
}

export function drawPlayer(renderer, game, offsetX = 0, offsetY = 0) {
  const p = game.player;
  // Blink white every few steps while invulnerable.
  const flashing = p.invuln > 0 && Math.floor(p.invuln / 4) % 2 === 1;
  const set = flashing ? art.wrenFlash : art.wren;
  const frames = set[p.dir];
  const frame = frames[p.swinging > 0 ? 0 : p.anim];

  const drawX = p.x + DRAW_OFF_X + offsetX;
  const drawY = p.y + DRAW_OFF_Y + offsetY;
  renderer.sprite(frame, drawX, drawY);

  // The blade, drawn only while it's actually out.
  if (p.swinging > 0) {
    const elapsed = SWING_STEPS - p.swinging;
    if (elapsed >= SWING_ACTIVE_FROM - 1 && elapsed <= SWING_ACTIVE_TO + 1) {
      drawSword(renderer, p, drawX, drawY);
    }
  }
}

function drawSword(renderer, p, drawX, drawY) {
  const blade = art.sword[p.dir];
  switch (p.dir) {
    case Dir.UP:
      renderer.sprite(blade, drawX + 5, drawY - 12);
      break;
    case Dir.DOWN:
      renderer.sprite(blade, drawX + 5, drawY + 12);
      break;
    case Dir.LEFT:
      renderer.sprite(blade, drawX - 14, drawY + 8);
      break;
    default:
      renderer.sprite(blade, drawX + 14, drawY + 8);
      break;
  }
}
