// The text box. Reveals a character at a time, then waits for an action press to
// advance — the typewriter is most of what makes NPC text feel like the era.

import { SCREEN_W, FIELD_Y, FIELD_H } from '../constants.js';
import { drawText, GLYPH_W } from '../art/font.js';

const REVEAL_STEPS_PER_CHAR = 2;
const BOX_H = 48;
const PAD = 12;

export function createDialogue() {
  return { active: false, npc: null, lines: [], index: 0, revealed: 0, clock: 0 };
}

export function openDialogue(dialogue, npc, lines) {
  dialogue.active = true;
  dialogue.npc = npc;
  dialogue.lines = lines;
  dialogue.index = 0;
  dialogue.revealed = 0;
  dialogue.clock = 0;
}

/** Returns true when the conversation has finished. */
export function updateDialogue(game) {
  const d = game.dialogue;
  const line = d.lines[d.index] ?? '';

  const fullyRevealed = d.revealed >= line.length;
  if (!fullyRevealed) {
    d.clock++;
    if (d.clock >= REVEAL_STEPS_PER_CHAR) {
      d.clock = 0;
      d.revealed++;
    }
  }

  if (game.input.consumeAction()) {
    if (!fullyRevealed) {
      d.revealed = line.length; // impatient players get the whole line
      return false;
    }
    d.index++;
    d.revealed = 0;
    d.clock = 0;
    if (d.index >= d.lines.length) {
      d.active = false;
      return true;
    }
  }

  return false;
}

export function drawDialogue(renderer, game) {
  const d = game.dialogue;
  if (!d.active) return;

  const y = FIELD_Y + FIELD_H - BOX_H - 8;
  renderer.rect(8, y, SCREEN_W - 16, BOX_H, 'black');
  renderer.outline(8, y, SCREEN_W - 16, BOX_H, '#f8f8f8');

  const line = d.lines[d.index] ?? '';
  const shown = line.slice(0, d.revealed);
  drawText(renderer, shown, PAD + 4, y + 14, 'white');

  // Blinking "press to continue" nub, once the line is fully out.
  if (d.revealed >= line.length && Math.floor(performance.now() / 300) % 2 === 0) {
    renderer.rect(SCREEN_W - 24, y + BOX_H - 12, GLYPH_W, 3, 'white');
  }
}
