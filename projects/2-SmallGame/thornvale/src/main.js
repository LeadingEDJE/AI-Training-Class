// Bootstrap and the top-level update/render dispatch.
//
// Art is baked before the first frame, then the fixed-timestep loop runs. All
// entity coordinates are in PLAYFIELD space (0,0 = top-left of the field), and
// FIELD_Y is added at draw time - which is what lets the screen-slide pass an
// extra offset without every module needing to know about the HUD.

import { FIELD_Y, SCREEN_W, SCREEN_H } from './constants.js';
import { Renderer } from './engine/renderer.js';
import { Input } from './engine/input.js';
import { startLoop } from './engine/loop.js';
import { bakeTiles } from './art/tiles.js';
import { bakeSprites, art } from './art/sprites.js';
import { bakeFont, drawText, textWidth } from './art/font.js';
import { State, createRun } from './game/state.js';
import { createPlayer, updatePlayer, drawPlayer, placePlayerAtTile } from './game/player.js';
import { loadScreen, drawScreen, updateTransition, transitionOffsets } from './game/world.js';
import {
  updateEnemies,
  updateProjectiles,
  updateEffects,
  drawEnemies,
  drawProjectiles,
  drawPickups,
  drawEffects,
} from './game/enemies.js';
import { resolveCombat } from './game/combat.js';
import { drawHud } from './game/hud.js';
import { createDialogue, openDialogue, updateDialogue, drawDialogue } from './game/dialogue.js';
import { drawDebug } from './game/debug.js';
import { START_SCREEN } from './game/screens.js';

const START_TILE = { tx: 7, ty: 5 };

const canvas = document.getElementById('screen');
const renderer = new Renderer(canvas);
const input = new Input();

bakeTiles();
bakeSprites();
bakeFont();

const game = {
  renderer,
  input,
  state: State.PLAYING,
  run: createRun(),
  player: createPlayer(),
  screen: null,
  enemies: [],
  projectiles: [],
  pickups: [],
  effects: [],
  npcs: [],
  transition: null,
  dialogue: createDialogue(),
  visited: new Set(),
  lastOutdoorScreen: START_SCREEN,
  triggerLock: false,
  deathTimer: 0,
  debug: false,
  startDialogue: null,
};

game.startDialogue = (npc) => {
  const alreadyGiven = npc.gives && game.run.given.has(npc.gives);
  const lines = alreadyGiven && npc.afterLines ? npc.afterLines : npc.lines;
  openDialogue(game.dialogue, npc, lines);
  game.state = State.DIALOGUE;
};

function noteScreen() {
  if (game.screen.outdoors) {
    game.visited.add(game.screen.id);
    game.lastOutdoorScreen = game.screen.id;
  }
}

function enterScreen(id, tile) {
  loadScreen(game, id);
  if (tile) placePlayerAtTile(game.player, tile.tx, tile.ty);
  noteScreen();
}

function restart() {
  game.run = createRun();
  game.state = State.PLAYING;
  game.player = createPlayer();
  game.visited.clear();
  game.lastOutdoorScreen = START_SCREEN;
  game.transition = null;
  game.dialogue = createDialogue();
  game.deathTimer = 0;
  enterScreen(START_SCREEN, START_TILE);
}

restart();

// --- Update -----------------------------------------------------------------

function update() {
  if (input.consumeDebugToggle()) game.debug = !game.debug;

  if (input.consumeRestart() && game.state !== State.GAME_OVER) {
    restart();
    return;
  }

  switch (game.state) {
    case State.PLAYING: {
      const before = game.screen;
      updatePlayer(game);
      if (game.screen !== before) {
        noteScreen(); // a cave trigger changed rooms
        return;
      }
      if (game.state !== State.PLAYING) return; // dialogue or death just started
      updateEnemies(game);
      updateProjectiles(game);
      updateEffects(game);
      resolveCombat(game);
      break;
    }

    case State.TRANSITION:
      updateTransition(game);
      if (game.state === State.PLAYING) noteScreen();
      break;

    case State.DIALOGUE: {
      const finished = updateDialogue(game);
      if (finished) {
        const npc = game.dialogue.npc;
        if (npc && npc.gives === 'sword' && !game.run.given.has('sword')) {
          game.run.hasSword = true;
          game.run.given.add('sword');
        }
        game.state = State.PLAYING;
      }
      break;
    }

    case State.DYING:
      game.deathTimer--;
      updateEffects(game);
      if (game.deathTimer <= 0) game.state = State.GAME_OVER;
      break;

    case State.GAME_OVER:
      if (input.consumeAction() || input.consumeRestart()) restart();
      break;
  }
}

// --- Render -----------------------------------------------------------------

function render() {
  renderer.clear('black');

  if (game.state === State.TRANSITION) {
    renderTransition();
  } else {
    drawScreen(renderer, game.screen);
    drawWorldEntities(0, FIELD_Y);
  }

  drawHud(renderer, game);
  drawDialogue(renderer, game);

  if (game.state === State.GAME_OVER) drawGameOver();
  if (game.debug) drawDebug(renderer, game, stats);

  renderer.present();
}

function renderTransition() {
  const t = game.transition;
  const offsets = transitionOffsets(t);
  const out = offsets.out;
  const incoming = offsets.in;
  const ratio = offsets.ratio;

  // Clip to the playfield so the sliding screens never bleed into the HUD.
  const ctx = renderer.ctx;
  ctx.save();
  ctx.beginPath();
  ctx.rect(0, FIELD_Y, SCREEN_W, SCREEN_H - FIELD_Y);
  ctx.clip();

  drawScreen(renderer, t.from, out.x, out.y);
  drawScreen(renderer, t.to, incoming.x, incoming.y);

  // Wren rides between the two screens, converging on the same spot.
  const p = game.player;
  const lerp = (a, b) => a + (b - a) * ratio;
  const saved = { x: p.x, y: p.y };
  p.x = lerp(t.fromPos.x + out.x, t.toPos.x + incoming.x);
  p.y = lerp(t.fromPos.y + out.y, t.toPos.y + incoming.y);
  drawPlayer(renderer, game, 0, FIELD_Y);
  p.x = saved.x;
  p.y = saved.y;

  ctx.restore();
}

function drawWorldEntities(ox, oy) {
  drawPickups(renderer, game, ox, oy);
  for (const npc of game.npcs) renderer.sprite(art.hermit, npc.x + ox, npc.y + oy);
  drawEnemies(renderer, game, ox, oy);
  if (game.state !== State.DYING || Math.floor(game.deathTimer / 5) % 2 === 0) {
    drawPlayer(renderer, game, ox, oy);
  }
  drawProjectiles(renderer, game, ox, oy);
  drawEffects(renderer, game, ox, oy);
}

function drawGameOver() {
  renderer.rect(0, FIELD_Y, SCREEN_W, SCREEN_H - FIELD_Y, 'black');
  const title = 'GAME OVER';
  const hint = 'PRESS SPACE TO TRY AGAIN';
  drawText(renderer, title, (SCREEN_W - textWidth(title)) / 2, 120, 'hudRed');
  drawText(renderer, hint, (SCREEN_W - textWidth(hint)) / 2, 150, 'white');
}

// Exposed so you can poke at state from the browser console while tuning -
// e.g. `game.run.hasSword = true` or `game.run.health = 1`.
window.game = game;

const stats = startLoop({ update, render });
