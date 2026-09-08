// Spitters: the octopus-like wanderers. They amble in cardinal directions,
// repick when they get bored or hit a wall, and spit a rock when they happen to
// line up with the player.
//
// Deliberately dumb on purpose — the original's enemies are barely smarter, and
// the difficulty comes from crowding and knockback rather than clever pathing.

import { TILE, ENEMY_SPEED, ENEMY_ROCK_SPEED, FIELD_W, FIELD_H } from '../constants.js';
import { moveAndCollide } from '../engine/collision.js';
import { isWalkable } from './world.js';
import { art } from '../art/sprites.js';
import { Dir } from '../engine/input.js';

const BOX = 12;
const DRAW_OFF = -2;
const DIRS = [Dir.UP, Dir.DOWN, Dir.LEFT, Dir.RIGHT];
const DELTA = {
  up: { x: 0, y: -1 },
  down: { x: 0, y: 1 },
  left: { x: -1, y: 0 },
  right: { x: 1, y: 0 },
};

function randomDir() {
  return DIRS[(Math.random() * DIRS.length) | 0];
}

export function spawnEnemies(screen) {
  return (screen.spawns ?? []).map((spec) => ({
    type: spec.type,
    x: spec.tx * TILE + (TILE - BOX) / 2,
    y: spec.ty * TILE + (TILE - BOX) / 2,
    w: BOX,
    h: BOX,
    dir: randomDir(),
    hp: 1,
    timer: 30 + ((Math.random() * 60) | 0),
    anim: 0,
    animClock: 0,
    cooldown: 60 + ((Math.random() * 120) | 0),
    dead: false,
  }));
}

export function updateEnemies(game) {
  for (const e of game.enemies) {
    if (e.dead) continue;

    e.animClock++;
    if (e.animClock >= 12) {
      e.animClock = 0;
      e.anim = (e.anim + 1) % 2;
    }

    e.timer--;
    if (e.timer <= 0) {
      e.dir = randomDir();
      e.timer = 30 + ((Math.random() * 60) | 0);
    }

    const d = DELTA[e.dir];
    const result = moveAndCollide(e, d.x * ENEMY_SPEED, d.y * ENEMY_SPEED, game.screen, isWalkable);
    // Bounce off whatever stopped us instead of grinding against it.
    if (result.blockedX || result.blockedY) {
      e.dir = randomDir();
      e.timer = 30 + ((Math.random() * 60) | 0);
    }

    e.cooldown--;
    if (e.cooldown <= 0) {
      e.cooldown = 90 + ((Math.random() * 150) | 0);
      if (roughlyAligned(e, game.player)) {
        game.projectiles.push(makeRock(e, aimAt(e, game.player)));
      }
    }
  }

  // Drop the dead once their poof has played out.
  game.enemies = game.enemies.filter((e) => !e.dead);
}

function roughlyAligned(e, p) {
  const tolerance = TILE;
  return (
    Math.abs(e.x - p.x) < tolerance ||
    Math.abs(e.y - p.y) < tolerance
  );
}

function aimAt(e, p) {
  if (Math.abs(e.x - p.x) < Math.abs(e.y - p.y)) {
    return p.y < e.y ? Dir.UP : Dir.DOWN;
  }
  return p.x < e.x ? Dir.LEFT : Dir.RIGHT;
}

function makeRock(e, dir) {
  return {
    x: e.x + e.w / 2 - 4,
    y: e.y + e.h / 2 - 4,
    w: 8,
    h: 8,
    dir,
  };
}

export function updateProjectiles(game) {
  for (const shot of game.projectiles) {
    const d = DELTA[shot.dir];
    shot.x += d.x * ENEMY_ROCK_SPEED;
    shot.y += d.y * ENEMY_ROCK_SPEED;
  }

  // Rocks vanish on walls and at the field edge.
  game.projectiles = game.projectiles.filter((shot) => {
    if (shot.x + shot.w < 0 || shot.x > FIELD_W || shot.y + shot.h < 0 || shot.y > FIELD_H) {
      return false;
    }
    const tx = Math.floor((shot.x + shot.w / 2) / TILE);
    const ty = Math.floor((shot.y + shot.h / 2) / TILE);
    return isWalkable(game.screen, tx, ty);
  });
}

export function killEnemy(game, enemy) {
  enemy.dead = true;
  game.effects.push({
    kind: 'poof',
    x: enemy.x - 2,
    y: enemy.y - 2,
    life: 16,
    max: 16,
  });
  // Roughly half the time you get paid for it.
  if (Math.random() < 0.5) {
    game.pickups.push({
      kind: 'rupee',
      x: enemy.x + 3,
      y: enemy.y + 2,
      w: 6,
      h: 8,
      life: 480,
    });
  }
}

export function updateEffects(game) {
  for (const fx of game.effects) fx.life--;
  game.effects = game.effects.filter((fx) => fx.life > 0);

  for (const item of game.pickups) item.life--;
  game.pickups = game.pickups.filter((item) => item.life > 0);
}

export function drawEnemies(renderer, game, ox = 0, oy = 0) {
  for (const e of game.enemies) {
    renderer.sprite(art.spitter[e.anim], e.x + DRAW_OFF + ox, e.y + DRAW_OFF + oy);
  }
}

export function drawProjectiles(renderer, game, ox = 0, oy = 0) {
  for (const shot of game.projectiles) {
    renderer.sprite(art.rock, shot.x + ox, shot.y + oy);
  }
}

export function drawPickups(renderer, game, ox = 0, oy = 0) {
  for (const item of game.pickups) {
    // Blink out over the last second and a bit so it's clear it's expiring.
    if (item.life < 90 && Math.floor(item.life / 6) % 2 === 0) continue;
    renderer.sprite(art.rupee, item.x + ox, item.y + oy);
  }
}

export function drawEffects(renderer, game, ox = 0, oy = 0) {
  for (const fx of game.effects) {
    const frame = fx.life > fx.max / 2 ? 0 : 1;
    renderer.sprite(art.poof[frame], fx.x + ox, fx.y + oy);
  }
}
