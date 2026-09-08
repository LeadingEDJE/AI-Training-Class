// The world, authored as 11 rows of 16 characters each.
//
// Legend (see art/tiles.js for the art and walkability):
//   .  sand           T  tree        B  bush       W  water    R  rock
//   C  cave mouth     S  stairs out  #  cave wall  _  cave floor
//
// Exits line up by row/column between neighbors on purpose: the clearing's west
// opening sits on rows 4-6 and so does the grove's east opening, so walking
// through lands you at the matching spot rather than somewhere arbitrary.

export const START_SCREEN = 'clearing';

export const screens = {
  clearing: {
    id: 'clearing',
    name: 'Thornvale Clearing',
    outdoors: true,
    mapPos: { x: 1, y: 0 },
    tiles: [
      'TTTTTTTTTTTTTTTT',
      'TTTTTC..TTTTTTTT',
      'TTTT....TTTTTTTT',
      'TT..........TTTT',
      '.....B..B.....TT',
      '..............TT',
      '.....B..B.....TT',
      'TT..........TTTT',
      'TTTT......TTTTTT',
      'TTTTTT..TTTTTTTT',
      'TTTTTT..TTTTTTTT',
    ],
    neighbors: { west: 'grove', south: 'sandflats' },
    spawns: [{ type: 'spitter', tx: 10, ty: 5 }],
  },

  grove: {
    id: 'grove',
    name: 'Bramble Grove',
    outdoors: true,
    mapPos: { x: 0, y: 0 },
    tiles: [
      'TTTTTTTTTTTTTTTT',
      'TTTTTTTTTTTTTTTT',
      'TT..........TTTT',
      'T....B..B.....TT',
      '.....B..B.......',
      'T...............',
      'T....B..B.......',
      'T....B..B.....TT',
      'TT..........TTTT',
      'TTTTTTTTTTTTTTTT',
      'TTTTTTTTTTTTTTTT',
    ],
    neighbors: { east: 'clearing' },
    spawns: [
      { type: 'spitter', tx: 3, ty: 3 },
      { type: 'spitter', tx: 11, ty: 6 },
    ],
  },

  sandflats: {
    id: 'sandflats',
    name: 'The Sandflats',
    outdoors: true,
    mapPos: { x: 1, y: 1 },
    tiles: [
      'TTTTTT..TTTTTTTT',
      'TTTT......TTTTTT',
      'TT..........TTTT',
      'T......RR.....TT',
      'T.....RRRR....TT',
      'T......RR......T',
      'T.....WWWW.....T',
      'T....WWWWWW....T',
      'TT....WWWW...TTT',
      'TTTT........TTTT',
      'TTTTTTTTTTTTTTTT',
    ],
    neighbors: { north: 'clearing' },
    spawns: [
      { type: 'spitter', tx: 2, ty: 4 },
      { type: 'spitter', tx: 13, ty: 3 },
      { type: 'spitter', tx: 6, ty: 9 },
    ],
  },

  swordCave: {
    id: 'swordCave',
    name: "The Hermit's Cave",
    outdoors: false,
    interior: true,
    tiles: [
      '################',
      '################',
      '##____________##',
      '##____________##',
      '##____________##',
      '##____________##',
      '##____________##',
      '##____________##',
      '##____________##',
      '#######SS#######',
      '################',
    ],
    neighbors: {},
    spawns: [],
    npcs: [
      {
        type: 'hermit',
        tx: 7,
        ty: 4,
        lines: [
          "IT'S DANGEROUS ALONE.",
          'TAKE THIS BLADE.',
        ],
        // Set once the sword has been handed over, so a second visit differs.
        afterLines: ['MIND THE SPITTERS.'],
        gives: 'sword',
      },
    ],
  },
};

/**
 * Where cave mouths lead, and where you come back out.
 * `returnTo` is the tile you're placed on when leaving the interior.
 */
export const caveLinks = {
  clearing: {
    // The 'C' tile in the clearing at column 5, row 1.
    from: { tx: 5, ty: 1 },
    to: 'swordCave',
    // Enter the cave standing on the stairs at the bottom.
    entryTile: { tx: 7, ty: 8 },
    returnTo: { tx: 5, ty: 2 },
    returnScreen: 'clearing',
  },
};

/** Interior -> the overworld screen and tile you pop back out at. */
export const exitLinks = {
  swordCave: { screen: 'clearing', tx: 5, ty: 2 },
};
