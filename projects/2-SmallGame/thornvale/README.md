# Thornvale

A small top-down 2D game in vanilla HTML, CSS, and JavaScript — a homage to the opening
of the NES Legend of Zelda overworld. Walk out of a clearing, find a cave, take a sword
from a hermit, and fight rock-spitting enemies across a handful of screens.

No build step, no npm, no bundler, no external assets. Every sprite and tile is pixel
data written out in JavaScript and baked once into offscreen canvases at startup.

## Running it

**ES modules will not load over `file://`** — the browser blocks them. Opening
`index.html` by double-clicking it will show a blank page and a CORS error in the
console. You need any static web server. From this folder:

```
python -m http.server 8000
```

Then open <http://localhost:8000>.

Other options that work equally well:

```
npx serve .          # if you have node
php -S localhost:8000
```

Or, in VS Code, install the **Live Server** extension, right-click `index.html`, and
choose *Open with Live Server*.

## Controls

| Action | Keys |
| --- | --- |
| Move | `W` `A` `S` `D`, arrow keys, or numpad `8` `4` `2` `6` |
| Sword / talk / advance dialogue | `Space`, `Ctrl`, or `Enter` |
| Toggle debug overlay | `F1` |
| Restart | `R` |

Movement is strictly four-directional — no diagonals — which is how the original played.
The last direction you press wins, so you can hold one key and tap another to turn.

`Ctrl` is treated as an attack only on its own; `Ctrl+R` and `Ctrl+W` still do what your
browser normally does with them.

## What's in it

Three overworld screens and one cave:

- **Thornvale Clearing** — where you start, with a cave mouth to the north
- **Bramble Grove** — west of the clearing
- **The Sandflats** — south of the clearing, with water and rocks
- **The Hermit's Cave** — inside the cave mouth; talk to the Hermit for a sword

Wren starts with three hearts and no sword. Spitters wander and throw rocks when they
line up with you. Contact or a rock costs half a heart, knocks you back, and grants a
brief window of invulnerability with a white flash. A killed Spitter sometimes drops a
rupee. Run out of hearts and you get a game over screen; any action key or `R` restarts.

Press `F1` for the debug overlay: FPS and steps, current state and screen, your position
and tile, facing and timers, and every hitbox drawn on top of the world with a red tint
over the tiles you can't walk through.

## How it's put together

The whole game draws into a **256x240** backbuffer — the NES resolution — then blits once
to the visible canvas at the largest whole-number scale that fits your window. Integer
scaling is what keeps the pixels sharp; a fractional scale would blur them. The top
256x64 is the HUD panel and the rest is a playfield of 16x11 tiles at 16px each.

Updates run at a fixed 60 Hz on an accumulator, with rendering decoupled and happening
once per animation frame. Fixing the timestep is what makes knockback windows,
invulnerability frames, and animation timing exact rather than framerate-dependent.

```
src/
  constants.js        dimensions and tuning numbers, all in one place
  engine/
    loop.js           fixed-timestep loop
    input.js          the three movement schemes and three action keys
    renderer.js       backbuffer and integer-scale blit
    pixels.js         string-row pixel data -> offscreen canvas
    collision.js      AABB overlap and tile resolution with wall sliding
  art/
    palette.js        named NES-ish colors
    tiles.js          tile art and the char -> behavior table
    sprites.js        Wren, Spitter, Hermit, sword, rock, rupee, hearts
    font.js           5x7 pixel font
  game/
    state.js          state machine and run state
    screens.js        the world, as character grids
    world.js          screen loading, edge transitions, cave triggers
    player.js         movement, facing, swinging, taking damage
    enemies.js        wander AI, projectiles, drops
    combat.js         hit resolution
    hud.js            the NES panel
    dialogue.js       typewriter text box
    debug.js          the F1 overlay
```

Screens are authored as readable character grids, so editing the world means editing
text:

```js
tiles: [
  'TTTTTTTTTTTTTTTT',
  'TTTTTC..TTTTTTTT',   // C is the cave mouth
  'TTTT....TTTTTTTT',
  ...
],
```

`T` tree, `.` sand, `B` bush, `W` water, `R` rock, `C` cave mouth, `S` stairs out,
`#` cave wall, `_` cave floor. Walkability and triggers are declared once per character
in `art/tiles.js`, so the screen definitions stay pure layout.

Sprites use the same idea with a per-sprite palette map — one character per pixel,
mapped to a named color:

```js
const WREN_DOWN = {
  palette: { g: 'tunic', G: 'tunicDark', s: 'skin', k: 'black' },
  frames: [[
    '.....GGGGGG.....',
    '....GggggggG....',
    ...
  ]],
};
```

Rows must all be the same length; `pixels.js` throws and names the offending row if they
aren't, so a typo fails loudly at startup rather than drawing something subtly wrong.
Left-facing frames are mirrored from the right-facing ones and the damage-flash frames
are silhouettes of the normal ones, so there is only ever one copy to maintain.

## Poking at it

`window.game` is exposed on purpose, so you can tune from the browser console:

```js
game.run.hasSword = true    // skip the cave
game.run.health = 1         // one half-heart from death
game.run.rupees = 99
game.debug = true           // same as pressing F1
```

## Where to go next

`BACKLOG.md` has the work sliced up with checkboxes, including a slice 7 of polish that
is deliberately left open — a title screen, sound, more screens, a dungeon. It is a
reasonable place to pick up if you want to extend this.

## A note on the homage

This recreates the *feel* of a game a lot of people have fond memories of, but every name
and every pixel here is original. Nothing from Nintendo is included or redistributed.
