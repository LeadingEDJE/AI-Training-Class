// Shared dimensions and tuning values. These mirror the NES original closely:
// a 256x240 screen, a 64px status panel, and a 16x11 playfield of 16px tiles.

export const TILE = 16;

export const SCREEN_W = 256;
export const SCREEN_H = 240;

export const HUD_H = 64;

export const COLS = 16;
export const ROWS = 11;

export const FIELD_W = COLS * TILE; // 256
export const FIELD_H = ROWS * TILE; // 176
export const FIELD_Y = HUD_H; // playfield starts below the HUD

export const STEP_HZ = 60;
export const STEP_MS = 1000 / STEP_HZ;

// Tuning. All durations are in 60Hz steps so the feel is frame-exact.
export const PLAYER_SPEED = 1.5; // px per step (~90 px/s, ~2.8s to cross a screen)
export const PLAYER_MAX_HEALTH = 6; // in half-hearts (3 full hearts)

export const SWING_STEPS = 12;
export const SWING_ACTIVE_FROM = 3;
export const SWING_ACTIVE_TO = 9;

export const INVULN_STEPS = 40;
export const KNOCKBACK_STEPS = 8;
export const KNOCKBACK_DIST = 16;

export const TRANSITION_STEPS = 45;

export const ENEMY_SPEED = 0.6;
export const ENEMY_ROCK_SPEED = 2;
