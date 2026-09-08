// Tile collision. The important behavior here is SLIDING: X and Y are resolved
// independently, so clipping a tree corner slides you along it instead of
// stopping you dead. Resolving both axes at once is the classic mistake that
// makes a top-down game feel sticky.

import { TILE, COLS, ROWS } from '../constants.js';

export function overlaps(a, b) {
  return a.x < b.x + b.w && a.x + a.w > b.x && a.y < b.y + b.h && a.y + a.h > b.y;
}

/**
 * Is every tile touched by this box walkable?
 * Boxes are in playfield coordinates (0,0 = top-left of the field).
 */
export function boxIsClear(screen, isWalkable, x, y, w, h) {
  // Outside the field is never "clear" — edge exits are handled separately by
  // the world, which needs to know the direction you left in.
  if (x < 0 || y < 0 || x + w > COLS * TILE || y + h > ROWS * TILE) return false;

  const left = Math.floor(x / TILE);
  const right = Math.floor((x + w - 1) / TILE);
  const top = Math.floor(y / TILE);
  const bottom = Math.floor((y + h - 1) / TILE);

  for (let ty = top; ty <= bottom; ty++) {
    for (let tx = left; tx <= right; tx++) {
      if (!isWalkable(screen, tx, ty)) return false;
    }
  }
  return true;
}

/**
 * Move a box by (dx, dy) against the tile grid, one axis at a time.
 * Mutates and returns the entity. `blocked` reports which axes were stopped,
 * which the world uses to detect walking into a screen edge.
 */
export function moveAndCollide(entity, dx, dy, screen, isWalkable) {
  const result = { blockedX: false, blockedY: false };

  if (dx !== 0) {
    const nx = entity.x + dx;
    if (boxIsClear(screen, isWalkable, nx, entity.y, entity.w, entity.h)) {
      entity.x = nx;
    } else {
      // Step toward the wall a pixel at a time so we end up flush against it
      // rather than a fractional gap away.
      const step = Math.sign(dx);
      while (boxIsClear(screen, isWalkable, entity.x + step, entity.y, entity.w, entity.h)) {
        entity.x += step;
      }
      result.blockedX = true;
    }
  }

  if (dy !== 0) {
    const ny = entity.y + dy;
    if (boxIsClear(screen, isWalkable, entity.x, ny, entity.w, entity.h)) {
      entity.y = ny;
    } else {
      const step = Math.sign(dy);
      while (boxIsClear(screen, isWalkable, entity.x, entity.y + step, entity.w, entity.h)) {
        entity.y += step;
      }
      result.blockedY = true;
    }
  }

  return result;
}
