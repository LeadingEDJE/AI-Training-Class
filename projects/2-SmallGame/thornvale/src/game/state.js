// Game state machine and the player's run state.
//
// Only PLAYING steps the world. TRANSITION and DIALOGUE freeze entities and run
// their own timers, which is how the original behaves — enemies don't creep up
// on you while you're reading a text box.

import { PLAYER_MAX_HEALTH } from '../constants.js';

export const State = {
  PLAYING: 'playing',
  TRANSITION: 'transition',
  DIALOGUE: 'dialogue',
  DYING: 'dying',
  GAME_OVER: 'gameOver',
};

export function createRun() {
  return {
    health: PLAYER_MAX_HEALTH, // counted in half-hearts
    maxHealth: PLAYER_MAX_HEALTH,
    rupees: 0,
    keys: 0,
    bombs: 0,
    hasSword: false,
    // NPCs remember they've already given you something.
    given: new Set(),
  };
}

export function heartsFromHalves(halves) {
  return halves / 2;
}
