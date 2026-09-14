import { describe, expect, it } from 'vitest';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import * as tokens from '../../src/styles/brand-tokens';

/**
 * The palette is declared TWICE, and nothing was checking that the two agree.
 *
 * `src/index.css`'s `@theme` block is what the browser paints from; `src/styles/brand-tokens.ts` is what
 * `brand-contrast.test.ts` measures. Both files have claimed "kept in step with" the other since spec
 * 002, and that claim was enforced by nothing at all — so a value changed in the CSS alone would be a
 * value **no test measures**, and one changed in the TypeScript alone would be measured but never
 * painted. Either way every gate stays green while the shipped colour is not the audited one.
 *
 * US2 grew the shared set from six declarations to eleven, which is what made this worth closing rather
 * than noting.
 *
 * The mapping below is deliberately explicit. Three names genuinely differ — the CSS says `gray`,
 * `gray-muted` and `danger` where the TypeScript says `TEXT` — so a derivation rule would need special
 * cases anyway, and a table that must be edited is more honest than a regex that quietly stops matching.
 * The final test is what stops the table itself rotting: every `--color-brand-*` declaration found in the
 * CSS must appear in it.
 */
const CSS_PATH = join(__dirname, '../../src/index.css');

/** CSS custom property → the constant in `brand-tokens.ts` that must carry the same value. */
const PARITY: Record<string, string> = {
  '--color-brand-green': 'BRAND_GREEN',
  '--color-brand-cyan': 'BRAND_CYAN',
  '--color-brand-blue': 'BRAND_BLUE',
  '--color-brand-taupe': 'BRAND_TAUPE',
  '--color-brand-gray': 'BRAND_GRAY_TEXT',
  '--color-brand-text': 'BRAND_TEXT',
  '--color-brand-gray-muted': 'BRAND_GRAY_MUTED_TEXT',
  '--color-brand-ink': 'BRAND_INK',
  '--color-brand-green-700': 'BRAND_GREEN_700',
  '--color-brand-green-tint': 'BRAND_GREEN_TINT',
  '--color-brand-green-accent': 'BRAND_GREEN_ACCENT_TEXT',
  '--color-brand-gray-tint': 'BRAND_GRAY_TINT',
  '--color-brand-danger': 'BRAND_DANGER_TEXT',
  '--color-brand-danger-tint': 'BRAND_DANGER_TINT',
  '--color-brand-taupe-tint': 'BRAND_TAUPE_TINT',
  '--color-brand-taupe-accent': 'BRAND_TAUPE_ACCENT_TEXT',
  '--color-brand-green-accent': 'BRAND_GREEN_ACCENT_TEXT',
  '--color-brand-teal-heading': 'BRAND_TEAL_HEADING_TEXT',
  '--color-brand-blue-accent': 'BRAND_BLUE_ACCENT_TEXT',
  '--color-brand-teal-accent': 'BRAND_TEAL_ACCENT_TEXT',
};

/**
 * Every `--color-brand-*` declaration in the stylesheet, lower-cased.
 *
 * Read from the file rather than from a running browser: Tailwind v4 compiles `@theme` at build time, so
 * jsdom never sees these as computed values. The file is the only place the declaration exists in a form
 * a unit test can read.
 */
function declaredInCss(): Map<string, string> {
  const css = readFileSync(CSS_PATH, 'utf8');
  const found = new Map<string, string>();

  for (const match of css.matchAll(/(--color-brand-[\w-]*)\s*:\s*(#[0-9a-fA-F]{3,8})\s*;/g)) {
    found.set(match[1], match[2].toLowerCase());
  }

  return found;
}

describe('the palette agrees between the stylesheet and the measured tokens', () => {
  it('finds the @theme declarations at all', () => {
    // Non-vacuity, first. A regex that matches nothing passes every parity assertion below for free,
    // which is the fail-open shape this repository has been bitten by repeatedly — and this scan reads a
    // path, so a moved or renamed stylesheet is exactly how it would happen.
    const declared = declaredInCss();

    expect(declared.size).toBeGreaterThanOrEqual(
      Object.keys(PARITY).length,
      'the stylesheet parse found fewer brand declarations than the parity table expects — ' +
        'it read nothing useful and would have passed for free',
    );
  });

  it.each(Object.entries(PARITY))('%s matches %s', (property, constantName) => {
    const declared = declaredInCss();
    const cssValue = declared.get(property);
    const tsValue = (tokens as unknown as Record<string, string>)[constantName];

    expect(cssValue, `${property} is not declared in src/index.css`).toBeDefined();
    expect(
      tsValue,
      `${constantName} is not exported from src/styles/brand-tokens.ts`,
    ).toBeDefined();

    // Compared case-insensitively: the CSS uses lower-case hex and the TypeScript upper-cases the
    // original brand values. That is a spelling difference, not a colour difference.
    expect(cssValue).toBe(tsValue.toLowerCase());
  });

  it('has a parity entry for every brand declaration in the stylesheet', () => {
    // What stops the table above rotting. Adding a token to the CSS and forgetting the table would
    // otherwise leave it unmeasured — the exact failure this file exists to prevent, reintroduced one
    // level up.
    const unmapped = [...declaredInCss().keys()].filter((property) => !(property in PARITY));

    expect(unmapped).toEqual([]);
  });

  it('detects a mismatch rather than reporting one whatever it reads', () => {
    // Proves the comparison can fail. Without this, a parity check whose two sides were both read from
    // the same place — or whose lookup silently returned undefined on both — would pass identically.
    const declared = declaredInCss();

    expect(declared.get('--color-brand-green')).not.toBe(tokens.BRAND_CYAN.toLowerCase());
  });
});
