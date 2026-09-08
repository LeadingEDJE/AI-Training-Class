// Screen management: what room you're in, what's in it, and the sliding
// transition between rooms.
//
// Edge exits fall out of collision naturally. Walking outside the field is never
// "clear", so the player ends up flush against the field edge; if they're still
// pressing outward from there and the screen has a neighbor that way, we slide.

import { TILE, COLS, ROWS, FIELD_W, FIELD_H, FIELD_Y, TRANSITION_STEPS } from '../constants.js';
import { TILES, tileArt } from '../art/tiles.js';
import { screens, caveLinks, exitLinks } from './screens.js';
import { spawnEnemies } from './enemies.js';
import { State } from './state.js';
import { Dir } from '../engine/input.js';

export function getScreen(id) {
  const screen = screens[id];
  if (!screen) throw new Error(`Unknown screen: ${id}`);
  return screen;
}

export function tileAt(screen, tx, ty) {
  if (tx < 0 || ty < 0 || tx >= COLS || ty >= ROWS) return null;
  return screen.tiles[ty][tx];
}

export function isWalkable(screen, tx, ty) {
  const ch = tileAt(screen, tx, ty);
  if (ch === null) return false;
  const tile = TILES[ch];
  return tile ? tile.walkable : false;
}

export function triggerAt(screen, tx, ty) {
  const ch = tileAt(screen, tx, ty);
  if (ch === null) return null;
  return TILES[ch]?.trigger ?? null;
}

/** Tile the center of a box sits in. */
export function centerTile(box) {
  return {
    tx: Math.floor((box.x + box.w / 2) / TILE),
    ty: Math.floor((box.y + box.h / 2) / TILE),
  };
}

/**
 * Enter a screen. `placement` positions the player; omit it to leave the player
 * where they are (used by the transition, which has already moved them).
 */
export function loadScreen(game, screenId, placement) {
  const screen = getScreen(screenId);
  game.screen = screen;
  game.enemies = spawnEnemies(screen);
  game.projectiles.length = 0;
  game.pickups.length = 0;
  game.effects.length = 0;
  game.npcs = (screen.npcs ?? []).map((npc) => ({
    ...npc,
    x: npc.tx * TILE,
    y: npc.ty * TILE,
    w: TILE,
    h: TILE,
  }));

  if (placement) {
    game.player.x = placement.x;
    game.player.y = placement.y;
    if (placement.dir) game.player.dir = placement.dir;
  }
  game.triggerLock = true; // don't re-fire the trigger we may have landed on
}

// --- Edge transitions -------------------------------------------------------

const OPPOSITE = { up: 'down', down: 'up', left: 'right', right: 'left' };
const DIR_TO_EDGE = { up: 'north', down: 'south', left: 'west', right: 'east' };

/**
 * If the player is flush against a field edge and pressing outward, and the
 * screen has a neighbor that way, begin a slide.
 */
export function checkEdgeExit(game, dir) {
  const p = game.player;
  const edge = DIR_TO_EDGE[dir];
  const nextId = game.screen.neighbors?.[edge];
  if (!nextId) return false;

  const atEdge =
    (dir === Dir.LEFT && p.x <= 0) ||
    (dir === Dir.RIGHT && p.x + p.w >= FIELD_W) ||
    (dir === Dir.UP && p.y <= 0) ||
    (dir === Dir.DOWN && p.y + p.h >= FIELD_H);
  if (!atEdge) return false;

  startTransition(game, dir, nextId);
  return true;
}

function startTransition(game, dir, toId) {
  const p = game.player;
  const to = getScreen(toId);

  // Land on the far side of the new screen, keeping the other axis unchanged.
  const target = { x: p.x, y: p.y };
  if (dir === Dir.LEFT) target.x = FIELD_W - p.w;
  if (dir === Dir.RIGHT) target.x = 0;
  if (dir === Dir.UP) target.y = FIELD_H - p.h;
  if (dir === Dir.DOWN) target.y = 0;

  game.transition = {
    dir,
    from: game.screen,
    to,
    toId,
    progress: 0,
    fromPos: { x: p.x, y: p.y },
    toPos: target,
  };
  game.state = State.TRANSITION;
}

export function updateTransition(game) {
  const t = game.transition;
  t.progress++;
  if (t.progress < TRANSITION_STEPS) return;

  // Slide finished: commit to the new screen.
  loadScreen(game, t.toId, { x: t.toPos.x, y: t.toPos.y });
  game.transition = null;
  game.state = State.PLAYING;
}

/** Pixel offsets for the outgoing and incoming screens mid-slide. */
export function transitionOffsets(t) {
  const ratio = t.progress / TRANSITION_STEPS;
  const dx = { left: FIELD_W, right: -FIELD_W, up: 0, down: 0 }[t.dir] * ratio;
  const dy = { up: FIELD_H, down: -FIELD_H, left: 0, right: 0 }[t.dir] * ratio;
  const spanX = { left: -FIELD_W, right: FIELD_W, up: 0, down: 0 }[t.dir];
  const spanY = { up: -FIELD_H, down: FIELD_H, left: 0, right: 0 }[t.dir];
  return {
    out: { x: dx, y: dy },
    in: { x: spanX + dx, y: spanY + dy },
    ratio,
  };
}

// --- Cave entry and exit ----------------------------------------------------

/**
 * Handle standing on a trigger tile. Returns true if a room change happened.
 * `triggerLock` stops the tile you arrive on from immediately firing again.
 */
export function checkTriggers(game) {
  const { tx, ty } = centerTile(game.player);
  const trigger = triggerAt(game.screen, tx, ty);

  if (!trigger) {
    game.triggerLock = false;
    return false;
  }
  if (game.triggerLock) return false;

  if (trigger === 'enter') {
    const link = caveLinks[game.screen.id];
    if (!link) return false;
    loadScreen(game, link.to, {
      x: link.entryTile.tx * TILE + (TILE - game.player.w) / 2,
      y: link.entryTile.ty * TILE,
      dir: Dir.UP,
    });
    return true;
  }

  if (trigger === 'exit') {
    const link = exitLinks[game.screen.id];
    if (!link) return false;
    loadScreen(game, link.screen, {
      x: link.tx * TILE + (TILE - game.player.w) / 2,
      y: link.ty * TILE,
      dir: Dir.DOWN,
    });
    return true;
  }

  return false;
}

// --- Drawing ----------------------------------------------------------------

/** Draw one screen's tiles at an offset within the playfield. */
export function drawScreen(renderer, screen, offsetX = 0, offsetY = 0) {
  for (let ty = 0; ty < ROWS; ty++) {
    const row = screen.tiles[ty];
    for (let tx = 0; tx < COLS; tx++) {
      const canvas = tileArt[row[tx]];
      if (canvas) renderer.sprite(canvas, tx * TILE + offsetX, FIELD_Y + ty * TILE + offsetY);
    }
  }
}

export { OPPOSITE };
