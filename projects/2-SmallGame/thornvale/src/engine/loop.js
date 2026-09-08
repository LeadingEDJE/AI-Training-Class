// Fixed-timestep loop: updates run at exactly 60Hz, rendering runs once per
// animation frame. Keeping update deterministic means knockback, invulnerability
// windows and animation timings are frame-exact rather than framerate-dependent.

import { STEP_MS } from '../constants.js';

const MAX_STEPS_PER_FRAME = 5; // clamp so a backgrounded tab doesn't stampede

export function startLoop({ update, render }) {
  let last = performance.now();
  let accumulator = 0;

  // Rolling FPS estimate for the debug overlay.
  const stats = { fps: 0, steps: 0 };
  let frames = 0;
  let fpsClock = last;

  function frame(now) {
    let delta = now - last;
    last = now;

    // A tab-switch can produce an enormous delta; discard rather than catch up.
    if (delta > 1000) delta = STEP_MS;

    accumulator += delta;

    let steps = 0;
    while (accumulator >= STEP_MS && steps < MAX_STEPS_PER_FRAME) {
      update();
      accumulator -= STEP_MS;
      steps++;
    }
    if (steps === MAX_STEPS_PER_FRAME) accumulator = 0;
    stats.steps = steps;

    render();

    frames++;
    if (now - fpsClock >= 500) {
      stats.fps = Math.round((frames * 1000) / (now - fpsClock));
      frames = 0;
      fpsClock = now;
    }

    requestAnimationFrame(frame);
  }

  requestAnimationFrame(frame);
  return stats;
}
