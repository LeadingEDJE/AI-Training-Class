// An NES-flavored palette. Deliberately small: reusing a handful of colors
// across every tile and sprite is what makes the art read as one coherent set.

export const PALETTE = {
  // Ground
  sand: '#e8d8a0',
  sandDark: '#c8b070',

  // Foliage
  leaf: '#3ca63c',
  leafDark: '#1c7a1c',
  leafLight: '#68d068',

  // Rock / mountain
  rock: '#a05828',
  rockDark: '#6c3410',
  rockLight: '#c88848',

  // Water
  water: '#3058f8',
  waterDark: '#1830b0',
  waterLight: '#6890f8',

  // The hero
  tunic: '#3cbc38',
  tunicDark: '#1c7a1c',
  skin: '#f8b878',
  skinDark: '#c88848',

  // Enemies
  enemy: '#f83800',
  enemyDark: '#a81000',
  enemyPale: '#f8f8f8',

  // The Hermit
  robe: '#f8f8f8',
  robeDark: '#a8a8a8',

  // Items & UI
  sword: '#b8b8b8',
  swordHilt: '#c07020',
  rupee: '#f8d800',
  heart: '#f83800',
  heartEmpty: '#4a1010',
  gold: '#f8d800',

  // Structural
  black: '#000000',
  white: '#f8f8f8',
  gray: '#7c7c7c',
  grayDark: '#3c3c3c',
  caveMouth: '#000000',
  hudText: '#f8f8f8',
  hudRed: '#c81028',
  hudBlue: '#5878f8',
};

/** Resolve a palette key to a CSS color. Unknown keys are loud, not silent. */
export function color(key) {
  const c = PALETTE[key];
  if (!c) throw new Error(`Unknown palette color: ${key}`);
  return c;
}
