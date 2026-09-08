# Thornvale — Backlog

Work is sliced so that **every slice runs in a browser on its own**. Something is on
screen from slice 1 and playable from slice 2. Check items off as you go; the slice
heading is done when all of its boxes are.

Naming is deliberately original — the land is **Thornvale**, the hero is **Wren**, the
rock-throwing enemy is a **Spitter**, and the cave dweller is **the Hermit**. This is a
homage to the opening of the NES Legend of Zelda overworld, not a copy of it, and no
Nintendo names or assets appear anywhere in the repo.

---

## Slice 1 — Skeleton and a screen you can look at

- [x] `index.html` with a 256x240 canvas and a control legend
- [x] `styles.css`: dark page, centered canvas, `image-rendering: pixelated`
- [x] `src/constants.js` — tile size, screen/playfield dimensions, tuning numbers in one place
- [x] `src/engine/renderer.js` — 256x240 backbuffer, integer-scale blit, `sprite`/`rect`/`outline`
- [x] `src/engine/loop.js` — fixed 60 Hz update with an accumulator, render per animation frame
- [x] `src/engine/pixels.js` — bake string-row pixel data into offscreen canvases
- [x] `src/art/palette.js` — named NES-ish colors, throws on an unknown key
- [x] `src/art/tiles.js` — sand, tree, bush, water, rock, cave mouth, stairs, cave wall/floor
- [x] `src/game/screens.js` — the first screen as 11 rows of 16 characters
- [x] `src/game/debug.js` — F1 overlay: FPS, state, walkability tint, hitboxes

**Verify:** the clearing renders crisp at an integer scale, resizing rescales cleanly, F1
shows a steady 60 FPS.

## Slice 2 — Wren moves

- [x] `src/engine/input.js` — WASD + arrows + numpad, last-pressed-axis wins
- [x] Action keys: Space, Enter, NumpadEnter, Ctrl (left and right)
- [x] `preventDefault` on handled keys so arrows and space don't scroll the page
- [x] Deliberately **do not** swallow Ctrl, so Ctrl+R and Ctrl+W keep working
- [x] `src/engine/collision.js` — AABB overlap, tile-grid resolution, wall sliding
- [x] `src/game/player.js` — four-directional movement, no diagonals
- [x] Wren sprites: down, up, right (left mirrored from right), two frames each
- [x] Walk cycle advancing on distance travelled, not wall time
- [x] Collision box (12x12) smaller than the sprite so one-tile gaps stay passable

**Verify:** walking into trees and water blocks you; brushing a tree corner slides you
along it; corner gaps are passable; arrows and space don't scroll the page.

## Slice 3 — Three screens and transitions

- [x] `src/game/world.js` — screen registry, tile lookups, walkability, triggers
- [x] Two more overworld screens (Bramble Grove, the Sandflats) with aligned openings
- [x] `neighbors` links per screen
- [x] Edge exits derived from collision: out-of-field is never clear, so you end flush
- [x] Sliding transition over 45 steps, both screens drawn at their offsets
- [x] `TRANSITION` state freezes entities while the slide runs
- [x] Wren lerps between the outgoing and incoming positions during the slide

**Verify:** leave by each edge, arrive on the correct neighbor at the mirrored position,
and walk straight back without a transition loop.

## Slice 4 — The NES HUD

- [x] `src/art/font.js` — 5x7 pixel font, baked white then tinted and cached per color
- [x] `src/game/hud.js` — 256x64 panel above the playfield
- [x] Minimap box with visited-room shading and a green blip for where you are
- [x] Rupee / key / bomb counters
- [x] B and A item slots, sword drawn into A once you have it
- [x] `-LIFE-` label in red with a row of hearts (full / half / empty)
- [x] Minimap falls back to the last outdoor screen while you're in a cave

**Verify:** the layout matches the reference screenshots; poke `game.run.health` in the
console and watch the hearts empty.

## Slice 5 — The cave, the Hermit, and the sword

- [x] Cave interior screen with walls, floor, and exit stairs
- [x] `caveLinks` / `exitLinks` — where a cave mouth leads and where you pop back out
- [x] `triggerLock` so the tile you arrive on doesn't immediately re-fire
- [x] `src/game/dialogue.js` — text box with a typewriter reveal
- [x] Action press skips to the full line, then advances to the next
- [x] Blinking continue nub while a line is waiting
- [x] The Hermit gives the sword once; a second visit has different dialogue

**Verify:** enter the cave mouth, read the dialogue, advance it with each action key,
receive the sword, exit, and land back below the cave mouth with the sword in the HUD.

## Slice 6 — Enemies, combat, death

- [x] `src/game/enemies.js` — Spitter wander AI, repicking direction on a timer or a block
- [x] Rock projectiles when roughly axis-aligned with Wren
- [x] Sword swing: 12 steps, hitbox live only during the middle steps
- [x] `src/game/combat.js` with an explicit resolution order (sword, contact, rocks, pickups)
- [x] Damage: half a heart, 16px of knockback over 8 steps, ~40 steps of invulnerability
- [x] White silhouette flash while invulnerable, derived from the normal frames
- [x] Death poof effect and a 50% rupee drop
- [x] `DYING` -> `GAME_OVER` states and restart on an action key or R

**Verify:** kill a Spitter; take contact damage and a rock hit and see the flash and
knockback; collect a rupee and watch the counter rise; die and restart cleanly.

## Slice 7 — Polish (optional)

- [ ] Title screen with a "press start" prompt
- [ ] Feel tuning pass on speed, knockback, and swing timing
- [ ] Richer bush, rock, and water tile art; animated water
- [ ] More overworld screens, and a second cave
- [ ] Sound effects (Web Audio, generated — still no external assets)
- [ ] Heart container pickup to raise max health
- [ ] Persist rupees and the sword to `localStorage`

---

## Ideas parked for later

- A dungeon with locked doors, giving the key counter something to do
- Bombs that blast a specific wall tile open
- A second enemy type that chases instead of wandering
- Screen-transition scrolling for a large contiguous map rather than room-by-room

## Conventions worth keeping

- No build step, no npm, no bundler — ES modules loaded straight from `src/`
- No external assets; all art is pixel data in JS, baked once at startup
- Tile and sprite art is authored as arrays of **equal-length strings**, one character
  per pixel; `pixels.js` throws on a ragged row, so a typo fails loudly at startup
- Derive art instead of authoring it twice: `flipH` for left-facing frames,
  `silhouette` for the damage flash and for tinted text
- Entity coordinates live in **playfield space**; the HUD offset is added at draw time
- No allocation inside update or render
