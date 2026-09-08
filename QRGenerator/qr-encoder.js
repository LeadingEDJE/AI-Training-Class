/*
 * qr-encoder.js — a QR Code encoder written from scratch.
 *
 * No dependencies, no DOM. The only export is generateQR(text, options),
 * which returns a plain 2D grid of booleans. Rendering is somebody else's job.
 *
 * Scope: byte mode (mode 0100) only, versions 1-40, all four EC levels.
 * Byte mode handles any UTF-8 text, which is what we want for URLs. Numeric
 * and alphanumeric modes would pack digits more tightly, but they add a lot of
 * machinery for a case we don't have.
 *
 * The spec is ISO/IEC 18004. The steps below follow it in order.
 */

(function (root, factory) {
  // Works as a <script> tag in the browser and as an ES/CommonJS module in Node,
  // so the encoder can be unit-tested headlessly.
  const api = factory();
  if (typeof module === 'object' && module.exports) module.exports = api;
  else root.QREncoder = api;
})(typeof globalThis !== 'undefined' ? globalThis : this, function () {
  'use strict';

  // ==========================================================================
  // 1. Galois field GF(256)
  // ==========================================================================
  //
  // Reed-Solomon does its arithmetic in a finite field of 256 elements. Adding
  // two field elements is XOR. Multiplying is harder, so we precompute a log
  // table: every nonzero element is a power of the generator 2, which turns
  // multiplication into addition of exponents.
  //
  // 0x11D is the primitive polynomial x^8 + x^4 + x^3 + x^2 + 1 that QR uses.

  const GF_EXP = new Uint8Array(512); // antilog: exponent -> value (doubled to avoid % 255)
  const GF_LOG = new Uint8Array(256); // log: value -> exponent

  (function buildGaloisTables() {
    let x = 1;
    for (let i = 0; i < 255; i++) {
      GF_EXP[i] = x;
      GF_LOG[x] = i;
      x <<= 1;
      if (x & 0x100) x ^= 0x11d; // stay inside the field
    }
    // Mirror the table so exponent lookups up to 510 need no modulo.
    for (let i = 255; i < 512; i++) GF_EXP[i] = GF_EXP[i - 255];
  })();

  function gfMul(a, b) {
    if (a === 0 || b === 0) return 0;
    return GF_EXP[GF_LOG[a] + GF_LOG[b]];
  }

  // ==========================================================================
  // 2. Reed-Solomon error correction
  // ==========================================================================

  /**
   * Generator polynomial for `degree` error-correction codewords:
   * (x - a^0)(x - a^1)...(x - a^(degree-1)), expanded in GF(256).
   * Coefficients are returned highest-power-first.
   */
  function rsGeneratorPoly(degree) {
    let poly = [1];
    for (let i = 0; i < degree; i++) {
      // Multiply the running product by (x - a^i).
      const next = new Array(poly.length + 1).fill(0);
      for (let j = 0; j < poly.length; j++) {
        next[j] ^= poly[j];                       // the x term
        next[j + 1] ^= gfMul(poly[j], GF_EXP[i]); // the a^i term
      }
      poly = next;
    }
    return poly;
  }

  /**
   * The EC codewords are the remainder of dividing the data (shifted left by
   * `ecCount` places) by the generator polynomial. This is synthetic division
   * done in place.
   */
  function rsComputeEC(data, ecCount) {
    const gen = rsGeneratorPoly(ecCount);
    const remainder = new Array(ecCount).fill(0);

    for (let i = 0; i < data.length; i++) {
      const factor = data[i] ^ remainder[0];
      remainder.shift();
      remainder.push(0);
      if (factor !== 0) {
        for (let j = 0; j < ecCount; j++) {
          remainder[j] ^= gfMul(gen[j + 1], factor);
        }
      }
    }
    return remainder;
  }

  // ==========================================================================
  // 3. Capacity tables
  // ==========================================================================
  //
  // One row per (version, EC level), indexed [version - 1] then by EC level:
  //   [ecCodewordsPerBlock, group1Blocks, group1DataCodewords,
  //                         group2Blocks, group2DataCodewords]
  //
  // Group 2 blocks always hold exactly one more data codeword than group 1.
  // Total data capacity is derived from this, so there is no second table.

  const EC_LEVELS = ['L', 'M', 'Q', 'H'];

  // prettier-ignore
  const EC_BLOCK_TABLE = [
    // L                      M                       Q                       H
    [[ 7, 1,  19, 0,  0], [10, 1,  16, 0,  0], [13, 1,  13, 0,  0], [17, 1,   9, 0,  0]], //  1
    [[10, 1,  34, 0,  0], [16, 1,  28, 0,  0], [22, 1,  22, 0,  0], [28, 1,  16, 0,  0]], //  2
    [[15, 1,  55, 0,  0], [26, 1,  44, 0,  0], [18, 2,  17, 0,  0], [22, 2,  13, 0,  0]], //  3
    [[20, 1,  80, 0,  0], [18, 2,  32, 0,  0], [26, 2,  24, 0,  0], [16, 4,   9, 0,  0]], //  4
    [[26, 1, 108, 0,  0], [24, 2,  43, 0,  0], [18, 2,  15, 2, 16], [22, 2,  11, 2, 12]], //  5
    [[18, 2,  68, 0,  0], [16, 4,  27, 0,  0], [24, 4,  19, 0,  0], [28, 4,  15, 0,  0]], //  6
    [[20, 2,  78, 0,  0], [18, 4,  31, 0,  0], [18, 2,  14, 4, 15], [26, 4,  13, 1, 14]], //  7
    [[24, 2,  97, 0,  0], [22, 2,  38, 2, 39], [22, 4,  18, 2, 19], [26, 4,  14, 2, 15]], //  8
    [[30, 2, 116, 0,  0], [22, 3,  36, 2, 37], [20, 4,  16, 4, 17], [24, 4,  12, 4, 13]], //  9
    [[18, 2,  68, 2, 69], [26, 4,  43, 1, 44], [24, 6,  19, 2, 20], [28, 6,  15, 2, 16]], // 10
    [[20, 4,  81, 0,  0], [30, 1,  50, 4, 51], [28, 4,  22, 4, 23], [24, 3,  12, 8, 13]], // 11
    [[24, 2,  92, 2, 93], [22, 6,  36, 2, 37], [26, 4,  20, 6, 21], [28, 7,  14, 4, 15]], // 12
    [[26, 4, 107, 0,  0], [22, 8,  37, 1, 38], [24, 8,  20, 4, 21], [22,12,  11, 4, 12]], // 13
    [[30, 3, 115, 1,116], [24, 4,  40, 5, 41], [20,11,  16, 5, 17], [24,11,  12, 5, 13]], // 14
    [[22, 5,  87, 1, 88], [24, 5,  41, 5, 42], [30, 5,  24, 7, 25], [24,11,  12, 7, 13]], // 15
    [[24, 5,  98, 1, 99], [28, 7,  45, 3, 46], [24,15,  19, 2, 20], [30, 3,  15,13, 16]], // 16
    [[28, 1, 107, 5,108], [28,10,  46, 1, 47], [28, 1,  22,15, 23], [28, 2,  14,17, 15]], // 17
    [[30, 5, 120, 1,121], [26, 9,  43, 4, 44], [28,17,  22, 1, 23], [28, 2,  14,19, 15]], // 18
    [[28, 3, 113, 4,114], [26, 3,  44,11, 45], [26,17,  21, 4, 22], [26, 9,  13,16, 14]], // 19
    [[28, 3, 107, 5,108], [26, 3,  41,13, 42], [30,15,  24, 5, 25], [28,15,  15,10, 16]], // 20
    [[28, 4, 116, 4,117], [26,17,  42, 0,  0], [28,17,  22, 6, 23], [30,19,  16, 6, 17]], // 21
    [[28, 2, 111, 7,112], [28,17,  46, 0,  0], [30, 7,  24,16, 25], [24,34,  13, 0,  0]], // 22
    [[30, 4, 121, 5,122], [28, 4,  47,14, 48], [30,11,  24,14, 25], [30,16,  15,14, 16]], // 23
    [[30, 6, 117, 4,118], [28, 6,  45,14, 46], [30,11,  24,16, 25], [30,30,  16, 2, 17]], // 24
    [[26, 8, 106, 4,107], [28, 8,  47,13, 48], [30, 7,  24,22, 25], [30,22,  15,13, 16]], // 25
    [[28,10, 114, 2,115], [28,19,  46, 4, 47], [28,28,  22, 6, 23], [30,33,  16, 4, 17]], // 26
    [[30, 8, 122, 4,123], [28,22,  45, 3, 46], [30, 8,  23,26, 24], [30,12,  15,28, 16]], // 27
    [[30, 3, 117,10,118], [28, 3,  45,23, 46], [30, 4,  24,31, 25], [30,11,  15,31, 16]], // 28
    [[30, 7, 116, 7,117], [28,21,  45, 7, 46], [30, 1,  23,37, 24], [30,19,  15,26, 16]], // 29
    [[30, 5, 115,10,116], [28,19,  47,10, 48], [30,15,  24,25, 25], [30,23,  15,25, 16]], // 30
    [[30,13, 115, 3,116], [28, 2,  46,29, 47], [30,42,  24, 1, 25], [30,23,  15,28, 16]], // 31
    [[30,17, 115, 0,  0], [28,10,  46,23, 47], [30,10,  24,35, 25], [30,19,  15,35, 16]], // 32
    [[30,17, 115, 1,116], [28,14,  46,21, 47], [30,29,  24,19, 25], [30,11,  15,46, 16]], // 33
    [[30,13, 115, 6,116], [28,14,  46,23, 47], [30,44,  24, 7, 25], [30,59,  16, 1, 17]], // 34
    [[30,12, 121, 7,122], [28,12,  47,26, 48], [30,39,  24,14, 25], [30,22,  15,41, 16]], // 35
    [[30, 6, 121,14,122], [28, 6,  47,34, 48], [30,46,  24,10, 25], [30, 2,  15,64, 16]], // 36
    [[30,17, 122, 4,123], [28,29,  46,14, 47], [30,49,  24,10, 25], [30,24,  15,46, 16]], // 37
    [[30, 4, 122,18,123], [28,13,  46,32, 47], [30,48,  24,14, 25], [30,42,  15,32, 16]], // 38
    [[30,20, 117, 4,118], [28,40,  47, 7, 48], [30,43,  24,22, 25], [30,10,  15,67, 16]], // 39
    [[30,19, 118, 6,119], [28,18,  47,31, 48], [30,34,  24,34, 25], [30,20,  15,61, 16]], // 40
  ];

  // Row/column centers for alignment patterns, per version. Version 1 has none.
  // prettier-ignore
  const ALIGNMENT_CENTERS = [
    [], [6,18], [6,22], [6,26], [6,30], [6,34], [6,22,38], [6,24,42], [6,26,46],
    [6,28,50], [6,30,54], [6,32,58], [6,34,62], [6,26,46,66], [6,26,48,70],
    [6,26,50,74], [6,30,54,78], [6,30,56,82], [6,30,58,86], [6,34,62,90],
    [6,28,50,72,94], [6,26,50,74,98], [6,30,54,78,102], [6,28,54,80,106],
    [6,32,58,84,110], [6,30,58,86,114], [6,34,62,90,118], [6,26,50,74,98,122],
    [6,30,54,78,102,126], [6,26,52,78,104,130], [6,30,56,82,108,134],
    [6,34,60,86,112,138], [6,30,58,86,114,142], [6,34,62,90,118,146],
    [6,30,54,78,102,126,150], [6,24,50,76,102,128,154], [6,28,54,80,106,132,158],
    [6,32,58,84,110,136,162], [6,26,54,82,110,138,166], [6,30,58,86,114,142,170],
  ];

  // Leftover bits after the interleaved codewords, per version. These are
  // always zero; they just pad the symbol out to a whole number of modules.
  // prettier-ignore
  const REMAINDER_BITS = [
    0, 7, 7, 7, 7, 7, 0, 0, 0, 0, 0, 0, 0, 3, 3, 3, 3, 3, 3, 3,
    4, 4, 4, 4, 4, 4, 4, 3, 3, 3, 3, 3, 3, 3, 0, 0, 0, 0, 0, 0,
  ];

  /** Total data codewords available at this version and EC level. */
  function dataCapacity(version, ecLevel) {
    const [, g1Blocks, g1Words, g2Blocks, g2Words] = blockSpec(version, ecLevel);
    return g1Blocks * g1Words + g2Blocks * g2Words;
  }

  function blockSpec(version, ecLevel) {
    return EC_BLOCK_TABLE[version - 1][EC_LEVELS.indexOf(ecLevel)];
  }

  // ==========================================================================
  // 4. Data encoding (byte mode)
  // ==========================================================================

  /** Minimal bit accumulator. Bits go in most-significant-first. */
  class BitBuffer {
    constructor() {
      this.bits = [];
    }
    put(value, length) {
      for (let i = length - 1; i >= 0; i--) {
        this.bits.push((value >>> i) & 1);
      }
    }
    get length() {
      return this.bits.length;
    }
    /** Pack into bytes; the caller guarantees the length is a multiple of 8. */
    toBytes() {
      const bytes = [];
      for (let i = 0; i < this.bits.length; i += 8) {
        let b = 0;
        for (let j = 0; j < 8; j++) b = (b << 1) | this.bits[i + j];
        bytes.push(b);
      }
      return bytes;
    }
  }

  function toUtf8Bytes(text) {
    if (typeof TextEncoder !== 'undefined') {
      return Array.from(new TextEncoder().encode(text));
    }
    // Fallback for ancient environments.
    return Array.from(unescape(encodeURIComponent(text)), (c) => c.charCodeAt(0));
  }

  /**
   * The character count indicator is 8 bits for versions 1-9 and 16 bits for
   * 10-40 (in byte mode). This is a classic source of bugs: the width changes
   * mid-table, and it feeds back into which version you need.
   */
  function charCountBits(version) {
    return version <= 9 ? 8 : 16;
  }

  function chooseVersion(byteLength, ecLevel) {
    for (let version = 1; version <= 40; version++) {
      const capacity = dataCapacity(version, ecLevel);
      // 4 bits of mode indicator + the count indicator + the payload.
      const needed = 4 + charCountBits(version) + byteLength * 8;
      if (needed <= capacity * 8) return version;
    }
    return null;
  }

  function buildDataCodewords(bytes, version, ecLevel) {
    const capacity = dataCapacity(version, ecLevel);
    const totalBits = capacity * 8;
    const buf = new BitBuffer();

    buf.put(0b0100, 4);                            // byte mode
    buf.put(bytes.length, charCountBits(version)); // character count
    for (const b of bytes) buf.put(b, 8);          // payload

    // Terminator: up to four zero bits, truncated if we're near the limit.
    buf.put(0, Math.min(4, totalBits - buf.length));
    // Pad to a byte boundary.
    if (buf.length % 8 !== 0) buf.put(0, 8 - (buf.length % 8));

    const codewords = buf.toBytes();
    // Fill the rest with the spec's alternating pad bytes.
    const PADS = [0xec, 0x11];
    for (let i = 0; codewords.length < capacity; i++) {
      codewords.push(PADS[i % 2]);
    }
    return codewords;
  }

  // ==========================================================================
  // 5. Block splitting and interleaving
  // ==========================================================================
  //
  // Data is split into blocks, each gets its own EC codewords, and then
  // everything is interleaved by position: block0[0], block1[0], block2[0],
  // block0[1], ... This is what makes the code resilient to a burst of damage
  // in one physical area — the damage spreads thinly across every block.

  function interleaveCodewords(dataCodewords, version, ecLevel) {
    const [ecPerBlock, g1Blocks, g1Words, g2Blocks, g2Words] = blockSpec(version, ecLevel);

    const dataBlocks = [];
    const ecBlocks = [];
    let offset = 0;

    for (let i = 0; i < g1Blocks + g2Blocks; i++) {
      const size = i < g1Blocks ? g1Words : g2Words;
      const block = dataCodewords.slice(offset, offset + size);
      offset += size;
      dataBlocks.push(block);
      ecBlocks.push(rsComputeEC(block, ecPerBlock));
    }

    const result = [];
    // Data first, column by column. Group 1 blocks are shorter, so they simply
    // run out and get skipped on the last pass.
    const maxDataLen = Math.max(g1Words, g2Words);
    for (let i = 0; i < maxDataLen; i++) {
      for (const block of dataBlocks) {
        if (i < block.length) result.push(block[i]);
      }
    }
    // Then the EC codewords, same interleaving. Every EC block is the same
    // length, so this one is a clean rectangle.
    for (let i = 0; i < ecPerBlock; i++) {
      for (const block of ecBlocks) result.push(block[i]);
    }
    return result;
  }

  // ==========================================================================
  // 6. Matrix construction — function patterns
  // ==========================================================================
  //
  // Two parallel grids: `modules` holds the dark/light value, `reserved` marks
  // cells that belong to function patterns. Data placement only writes where
  // reserved is false, which keeps step 7 simple.

  function createMatrix(version) {
    const size = version * 4 + 17;
    const modules = [];
    const reserved = [];
    for (let r = 0; r < size; r++) {
      modules.push(new Array(size).fill(false));
      reserved.push(new Array(size).fill(false));
    }
    return { size, modules, reserved };
  }

  function placeFinderPatterns(m) {
    const corners = [
      [0, 0],
      [0, m.size - 7],
      [m.size - 7, 0],
    ];
    for (const [row, col] of corners) {
      // The 7x7 pattern itself: a 3x3 solid core, a light ring, a dark border.
      for (let r = -1; r <= 7; r++) {
        for (let c = -1; c <= 7; c++) {
          const rr = row + r;
          const cc = col + c;
          if (rr < 0 || rr >= m.size || cc < 0 || cc >= m.size) continue;
          const inBorder = (r === 0 || r === 6) && c >= 0 && c <= 6;
          const inSide = (c === 0 || c === 6) && r >= 0 && r <= 6;
          const inCore = r >= 2 && r <= 4 && c >= 2 && c <= 4;
          m.modules[rr][cc] = inBorder || inSide || inCore;
          m.reserved[rr][cc] = true; // the -1/7 ring is the separator: reserved and light
        }
      }
    }
  }

  function placeAlignmentPatterns(m, version) {
    const centers = ALIGNMENT_CENTERS[version - 1];
    if (centers.length === 0) return;
    const first = centers[0];
    const last = centers[centers.length - 1];

    for (const row of centers) {
      for (const col of centers) {
        // Skip exactly the three corners occupied by finder patterns. Note we
        // can't just test `reserved` here: alignment patterns are *supposed* to
        // overlap the timing rows, and the spec makes them agree there anyway.
        const onFinder =
          (row === first && col === first) ||
          (row === first && col === last) ||
          (row === last && col === first);
        if (onFinder) continue;
        for (let r = -2; r <= 2; r++) {
          for (let c = -2; c <= 2; c++) {
            const ring = Math.max(Math.abs(r), Math.abs(c));
            m.modules[row + r][col + c] = ring !== 1; // dark border, light ring, dark center
            m.reserved[row + r][col + c] = true;
          }
        }
      }
    }
  }

  function placeTimingPatterns(m) {
    // Row 6 and column 6 alternate dark/light, giving the scanner a ruler.
    for (let i = 8; i < m.size - 8; i++) {
      const dark = i % 2 === 0;
      m.modules[6][i] = dark;
      m.reserved[6][i] = true;
      m.modules[i][6] = dark;
      m.reserved[i][6] = true;
    }
  }

  function reserveFormatAreas(m, version) {
    // The always-dark module just above the bottom-left finder.
    m.modules[m.size - 8][8] = true;
    m.reserved[m.size - 8][8] = true;

    // Format information: a strip around the top-left finder, mirrored along
    // the right edge of the top-right finder and below the bottom-left one.
    for (let i = 0; i < 9; i++) {
      if (!m.reserved[8][i]) m.reserved[8][i] = true;
      if (!m.reserved[i][8]) m.reserved[i][8] = true;
    }
    for (let i = 0; i < 8; i++) {
      m.reserved[8][m.size - 1 - i] = true;
      m.reserved[m.size - 1 - i][8] = true;
    }

    // Version information: two 6x3 blocks, only for version 7 and up.
    if (version >= 7) {
      for (let i = 0; i < 6; i++) {
        for (let j = 0; j < 3; j++) {
          m.reserved[i][m.size - 11 + j] = true;
          m.reserved[m.size - 11 + j][i] = true;
        }
      }
    }
  }

  // ==========================================================================
  // 7. Data placement
  // ==========================================================================

  /**
   * Walk two-module-wide columns from the right edge leftward, zigzagging up
   * then down. Column 6 is the vertical timing pattern and is skipped entirely,
   * which shifts every column index to its left by one.
   */
  function placeData(m, bits) {
    let bitIndex = 0;
    let upward = true;

    for (let right = m.size - 1; right >= 1; right -= 2) {
      if (right === 6) right = 5; // skip the timing column

      for (let vert = 0; vert < m.size; vert++) {
        const row = upward ? m.size - 1 - vert : vert;
        for (let c = 0; c < 2; c++) {
          const col = right - c;
          if (m.reserved[row][col]) continue;
          // Bits past the end of the stream are the remainder bits: leave false.
          m.modules[row][col] = bitIndex < bits.length && bits[bitIndex] === 1;
          bitIndex++;
        }
      }
      upward = !upward;
    }
  }

  // ==========================================================================
  // 8. Masking
  // ==========================================================================
  //
  // A raw symbol can end up with large blank areas or accidental finder-like
  // patterns, both of which confuse scanners. So we try all 8 masks, score each
  // with four penalty rules, and keep the least-bad one.

  const MASK_FUNCTIONS = [
    (r, c) => (r + c) % 2 === 0,
    (r) => r % 2 === 0,
    (r, c) => c % 3 === 0,
    (r, c) => (r + c) % 3 === 0,
    (r, c) => (Math.floor(r / 2) + Math.floor(c / 3)) % 2 === 0,
    (r, c) => ((r * c) % 2) + ((r * c) % 3) === 0,
    (r, c) => (((r * c) % 2) + ((r * c) % 3)) % 2 === 0,
    (r, c) => (((r + c) % 2) + ((r * c) % 3)) % 2 === 0,
  ];

  function applyMask(m, maskId) {
    const fn = MASK_FUNCTIONS[maskId];
    for (let r = 0; r < m.size; r++) {
      for (let c = 0; c < m.size; c++) {
        if (m.reserved[r][c]) continue; // function patterns are never masked
        if (fn(r, c)) m.modules[r][c] = !m.modules[r][c];
      }
    }
  }

  /** Rule 1: runs of 5 or more same-colored modules in a row or column. */
  function penaltyRule1(modules, size) {
    let penalty = 0;
    for (let pass = 0; pass < 2; pass++) {
      for (let a = 0; a < size; a++) {
        let runColor = null;
        let runLength = 0;
        for (let b = 0; b < size; b++) {
          const value = pass === 0 ? modules[a][b] : modules[b][a];
          if (value === runColor) {
            runLength++;
          } else {
            if (runLength >= 5) penalty += runLength - 2;
            runColor = value;
            runLength = 1;
          }
        }
        if (runLength >= 5) penalty += runLength - 2;
      }
    }
    return penalty;
  }

  /** Rule 2: every 2x2 block of a single color costs 3. */
  function penaltyRule2(modules, size) {
    let penalty = 0;
    for (let r = 0; r < size - 1; r++) {
      for (let c = 0; c < size - 1; c++) {
        const v = modules[r][c];
        if (v === modules[r][c + 1] && v === modules[r + 1][c] && v === modules[r + 1][c + 1]) {
          penalty += 3;
        }
      }
    }
    return penalty;
  }

  /**
   * Rule 3: the finder-like sequence 1:1:3:1:1 with four light modules on
   * either side, in any row or column, costs 40 each.
   */
  function penaltyRule3(modules, size) {
    const D = true;
    const L = false;
    const PATTERN_A = [D, L, D, D, D, L, D, L, L, L, L];
    const PATTERN_B = [L, L, L, L, D, L, D, D, D, L, D];
    let penalty = 0;

    const matches = (line, start, pattern) => {
      for (let i = 0; i < pattern.length; i++) {
        if (line[start + i] !== pattern[i]) return false;
      }
      return true;
    };

    for (let pass = 0; pass < 2; pass++) {
      for (let a = 0; a < size; a++) {
        const line = [];
        for (let b = 0; b < size; b++) {
          line.push(pass === 0 ? modules[a][b] : modules[b][a]);
        }
        for (let start = 0; start + 11 <= size; start++) {
          if (matches(line, start, PATTERN_A)) penalty += 40;
          if (matches(line, start, PATTERN_B)) penalty += 40;
        }
      }
    }
    return penalty;
  }

  /** Rule 4: penalize drift away from a 50/50 dark-to-light ratio. */
  function penaltyRule4(modules, size) {
    let dark = 0;
    for (let r = 0; r < size; r++) {
      for (let c = 0; c < size; c++) if (modules[r][c]) dark++;
    }
    const percent = (dark * 100) / (size * size);
    // Distance to the nearest multiple of 5, in steps of 5, times 10.
    const k = Math.floor(Math.abs(percent - 50) / 5);
    return k * 10;
  }

  function totalPenalty(modules, size) {
    return (
      penaltyRule1(modules, size) +
      penaltyRule2(modules, size) +
      penaltyRule3(modules, size) +
      penaltyRule4(modules, size)
    );
  }

  // ==========================================================================
  // 9. Format and version information
  // ==========================================================================

  // Not 0,1,2,3! The format string uses its own ordering, and getting this
  // wrong produces a symbol that looks perfect and scans as nothing.
  const EC_FORMAT_BITS = { L: 0b01, M: 0b00, Q: 0b11, H: 0b10 };

  /**
   * Polynomial remainder of `value` divided by `generator` over GF(2), i.e.
   * repeatedly XOR the generator in, aligned to the highest set bit, until
   * nothing is left above the generator's degree.
   */
  function bchRemainder(value, generator) {
    const genDegree = 31 - Math.clz32(generator);
    let remainder = value;
    // clz32(0) is 32, so this terminates cleanly at remainder === 0.
    for (let top = 31 - Math.clz32(remainder); top >= genDegree; top = 31 - Math.clz32(remainder)) {
      remainder ^= generator << (top - genDegree);
    }
    return remainder;
  }

  function formatBits(ecLevel, maskId) {
    const data = (EC_FORMAT_BITS[ecLevel] << 3) | maskId; // 5 bits
    const ec = bchRemainder(data << 10, 0b10100110111); // BCH(15,5), generator 0x537
    return ((data << 10) | ec) ^ 0b101010000010010; // XOR mask keeps it from being all-zero
  }

  function versionBits(version) {
    const ec = bchRemainder(version << 12, 0b1111100100101); // BCH(18,6), generator 0x1F25
    return (version << 12) | ec;
  }

  function placeFormatInfo(m, ecLevel, maskId) {
    const bits = formatBits(ecLevel, maskId);
    const bit = (i) => ((bits >> i) & 1) === 1;
    const last = m.size - 1;

    // The 15 format bits are written MSB-first: bit 14 goes down first in each
    // run. Placing them LSB-first yields a symbol that looks perfectly correct
    // and scans as absolutely nothing — the two copies stay self-consistent, so
    // only a real decoder catches it.
    //
    // Copy 1 wraps the top-left finder: along row 8 from the left, then up
    // column 8 from row 5 to row 0.
    for (let i = 0; i <= 5; i++) {
      m.modules[8][i] = bit(14 - i);
      m.modules[i][8] = bit(i);
    }
    m.modules[8][7] = bit(8);
    m.modules[8][8] = bit(7);
    m.modules[7][8] = bit(6);

    // Copy 2 is split: seven bits climbing column 8 from the bottom edge, then
    // eight running along row 8 to the right edge. The vertical run stops one
    // short of the always-dark module at (size - 8, 8) — overwrite that and the
    // symbol dies.
    for (let i = 0; i <= 6; i++) m.modules[last - i][8] = bit(14 - i);
    for (let i = 0; i <= 7; i++) m.modules[8][m.size - 8 + i] = bit(7 - i);
  }

  function placeVersionInfo(m, version) {
    if (version < 7) return;
    const bits = versionBits(version);
    for (let i = 0; i < 18; i++) {
      const dark = ((bits >> i) & 1) === 1;
      const row = Math.floor(i / 3);
      const col = m.size - 11 + (i % 3);
      m.modules[row][col] = dark; // above the top-right finder
      m.modules[col][row] = dark; // and its transpose, left of the bottom-left one
    }
  }

  // ==========================================================================
  // Public API
  // ==========================================================================

  /** Largest byte payload that fits at this EC level (version 40). */
  function maxBytes(ecLevel) {
    // 4 bits mode + 16 bits count at version 40.
    return dataCapacity(40, ecLevel) - Math.ceil((4 + 16) / 8);
  }

  /**
   * Encode `text` as a QR symbol.
   *
   * @param {string} text
   * @param {{ecLevel?: 'L'|'M'|'Q'|'H'}} [options]
   * @returns {{size: number, modules: boolean[][], version: number,
   *            maskId: number, ecLevel: string, byteLength: number}}
   */
  function generateQR(text, options) {
    const ecLevel = (options && options.ecLevel) || 'M';
    if (EC_LEVELS.indexOf(ecLevel) === -1) {
      throw new Error(`Unknown error correction level: ${ecLevel}`);
    }
    if (typeof text !== 'string' || text.length === 0) {
      throw new Error('Nothing to encode.');
    }

    const bytes = toUtf8Bytes(text);
    const version = chooseVersion(bytes.length, ecLevel);
    if (version === null) {
      throw new Error(
        `Too much data: ${bytes.length} bytes, but level ${ecLevel} tops out at ${maxBytes(ecLevel)}.`
      );
    }

    const dataCodewords = buildDataCodewords(bytes, version, ecLevel);
    const finalCodewords = interleaveCodewords(dataCodewords, version, ecLevel);

    // Codewords to a bit stream, plus the version's remainder bits.
    const bits = [];
    for (const cw of finalCodewords) {
      for (let i = 7; i >= 0; i--) bits.push((cw >> i) & 1);
    }
    for (let i = 0; i < REMAINDER_BITS[version - 1]; i++) bits.push(0);

    // Lay out everything except the format/version info, which depends on the
    // mask we haven't chosen yet.
    const base = createMatrix(version);
    placeFinderPatterns(base);
    placeTimingPatterns(base);
    placeAlignmentPatterns(base, version); // after timing: alignment wins where they overlap
    reserveFormatAreas(base, version);
    placeData(base, bits);

    // Try all 8 masks and keep the one with the lowest penalty. The format and
    // version info must be in place before scoring, since they are part of the
    // symbol the scanner sees.
    let best = null;
    for (let maskId = 0; maskId < 8; maskId++) {
      const candidate = {
        size: base.size,
        modules: base.modules.map((row) => row.slice()),
        reserved: base.reserved,
      };
      applyMask(candidate, maskId);
      placeFormatInfo(candidate, ecLevel, maskId);
      placeVersionInfo(candidate, version);

      const score = totalPenalty(candidate.modules, candidate.size);
      if (best === null || score < best.score) {
        best = { score, maskId, modules: candidate.modules };
      }
    }

    return {
      size: base.size,
      modules: best.modules,
      version,
      maskId: best.maskId,
      ecLevel,
      byteLength: bytes.length,
    };
  }

  return { generateQR, maxBytes, EC_LEVELS };
});
