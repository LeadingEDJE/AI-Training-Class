import { describe, expect, it } from 'vitest';
import {
  AA_NORMAL_TEXT_MIN_RATIO,
  contrastRatio,
  hexToRgb,
  parseRgbString,
  relativeLuminance,
} from '../../../src/lib/contrast';

describe('relativeLuminance', () => {
  it('is 1 for white and 0 for black', () => {
    expect(relativeLuminance(255, 255, 255)).toBeCloseTo(1, 5);
    expect(relativeLuminance(0, 0, 0)).toBeCloseTo(0, 5);
  });

  it('linearizes low channel values without the gamma curve (the <= 0.03928 branch)', () => {
    // 10/255 = 0.0392..., just inside the linear-segment branch of the sRGB transfer function.
    expect(relativeLuminance(10, 10, 10)).toBeGreaterThan(0);
    expect(relativeLuminance(10, 10, 10)).toBeLessThan(relativeLuminance(20, 20, 20));
  });
});

describe('contrastRatio', () => {
  it('is 21:1 for black on white, the maximum possible ratio', () => {
    expect(contrastRatio([0, 0, 0], [255, 255, 255])).toBeCloseTo(21, 0);
  });

  it('is 1:1 for a colour against itself', () => {
    expect(contrastRatio([100, 150, 200], [100, 150, 200])).toBeCloseTo(1, 5);
  });

  it('is symmetric regardless of argument order', () => {
    const a = contrastRatio([0, 0, 0], [255, 255, 255]);
    const b = contrastRatio([255, 255, 255], [0, 0, 0]);
    expect(a).toBeCloseTo(b, 10);
  });
});

describe('hexToRgb', () => {
  it('parses a 6-digit hex colour', () => {
    expect(hexToRgb('#98C93D')).toEqual([152, 201, 61]);
  });

  it('parses a hex colour without the leading #', () => {
    expect(hexToRgb('FFFFFF')).toEqual([255, 255, 255]);
  });

  it('expands a 3-digit shorthand hex colour', () => {
    expect(hexToRgb('#fff')).toEqual([255, 255, 255]);
    expect(hexToRgb('#000')).toEqual([0, 0, 0]);
  });
});

describe('parseRgbString', () => {
  it('parses an rgb(...) string', () => {
    expect(parseRgbString('rgb(152, 201, 61)')).toEqual([152, 201, 61]);
  });

  it('parses an rgba(...) string with a non-zero alpha', () => {
    expect(parseRgbString('rgba(152, 201, 61, 0.8)')).toEqual([152, 201, 61]);
  });

  it('returns null for a fully transparent colour', () => {
    expect(parseRgbString('rgba(152, 201, 61, 0)')).toBeNull();
  });

  it('returns null for an unparseable string, such as the jsdom default of an unset colour', () => {
    expect(parseRgbString('')).toBeNull();
    expect(parseRgbString('transparent')).toBeNull();
  });
});

describe('AA_NORMAL_TEXT_MIN_RATIO', () => {
  it('is the WCAG 2.1 AA minimum for normal-size body text', () => {
    expect(AA_NORMAL_TEXT_MIN_RATIO).toBe(4.5);
  });
});
