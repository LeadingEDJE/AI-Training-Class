import { describe, expect, it } from 'vitest';
import {
  AA_NON_TEXT_MIN_RATIO,
  AA_NORMAL_TEXT_MIN_RATIO,
  compositeOver,
  contrastRatio,
  hexToRgb,
} from '../../src/lib/contrast';
import {
  BRAND_ACCENT_COLORS,
  BRAND_BLUE,
  BRAND_BLUE_ACCENT_TEXT,
  BRAND_CONTROL_BORDER_ALPHA,
  BRAND_CYAN,
  BRAND_DANGER_TEXT,
  BRAND_DANGER_TINT,
  BRAND_GRAY_MUTED_TEXT,
  BRAND_GRAY_TEXT,
  BRAND_GRAY_TINT,
  BRAND_GREEN,
  BRAND_GREEN_700,
  BRAND_GREEN_ACCENT_TEXT,
  BRAND_GREEN_TINT,
  BRAND_INK,
  BRAND_TAUPE,
  BRAND_TAUPE_ACCENT_TEXT,
  BRAND_TAUPE_TINT,
  BRAND_TEAL_ACCENT_TEXT,
  BRAND_TEAL_HEADING_TEXT,
  BRAND_TEXT,
} from '../../src/styles/brand-tokens';

/**
 * Enforces a known trap: the four EDJE brand accent colours all fail the
 * WCAG AA 4.5:1 minimum for normal body text on a white surface — only the brand gray clears it.
 * Following the brand instruction literally on a light surface produces a failure that *looks*
 * correct, which is exactly why this is asserted rather than merely documented.
 */
const WHITE: [number, number, number] = [255, 255, 255];

describe('brand colour contrast against a white surface', () => {
  it.each(BRAND_ACCENT_COLORS)(
    'rejects %s as body text on white — below the AA minimum',
    (accent) => {
      const ratio = contrastRatio(hexToRgb(accent), WHITE);
      expect(ratio).toBeLessThan(AA_NORMAL_TEXT_MIN_RATIO);
    },
  );

  it('accepts the brand gray as body text on white — the only brand value that clears AA', () => {
    const ratio = contrastRatio(hexToRgb(BRAND_GRAY_TEXT), WHITE);
    expect(ratio).toBeGreaterThanOrEqual(AA_NORMAL_TEXT_MIN_RATIO);
  });

  it('matches the previously measured ratios (±0.05)', () => {
    expect(contrastRatio(hexToRgb(BRAND_GREEN), WHITE)).toBeCloseTo(1.95, 1);
    expect(contrastRatio(hexToRgb(BRAND_CYAN), WHITE)).toBeCloseTo(2.0, 1);
    expect(contrastRatio(hexToRgb(BRAND_BLUE), WHITE)).toBeCloseTo(2.92, 1);
    expect(contrastRatio(hexToRgb(BRAND_GRAY_TEXT), WHITE)).toBeCloseTo(8.46, 1);
  });
});

/**
 * `BRAND_TEXT` (#68): the mockups' own `body{color:var(--text)}` — #2E2F30, distinct from
 * `BRAND_GRAY_TEXT` (`--le-gray`, #4C4D4F). The mockups use TWO grays, not one: `--le-gray` for
 * chrome (the topbar, `th`, `.field label`, `.btn.secondary`), `--text` for everything else — plain
 * page copy, table cells, links, toggle labels. The app had collapsed both onto `BRAND_GRAY_TEXT`,
 * which is why body copy read lighter than the mockup even after the topbar was fixed to the
 * correct (chrome) shade. Measured here rather than copied, per Principle X rule 2.
 */
describe('BRAND_TEXT — the mockups’ body-copy gray, distinct from the chrome gray', () => {
  const SHELL: [number, number, number] = hexToRgb('#f4f5f6');

  it('clears AA normal-text on both surfaces the app paints text on', () => {
    expect(contrastRatio(hexToRgb(BRAND_TEXT), WHITE)).toBeGreaterThanOrEqual(
      AA_NORMAL_TEXT_MIN_RATIO,
    );
    expect(contrastRatio(hexToRgb(BRAND_TEXT), SHELL)).toBeGreaterThanOrEqual(
      AA_NORMAL_TEXT_MIN_RATIO,
    );
  });

  it('matches the mockups’ own #2E2F30 value', () => {
    expect(BRAND_TEXT.toUpperCase()).toBe('#2E2F30');
  });

  it('is measurably darker than BRAND_GRAY_TEXT — they are not the same role wearing two names', () => {
    expect(contrastRatio(hexToRgb(BRAND_TEXT), WHITE)).toBeGreaterThan(
      contrastRatio(hexToRgb(BRAND_GRAY_TEXT), WHITE),
    );
  });
});

/**
 * WCAG 2.1 §1.4.11 Non-text Contrast (AA) — a control's visual boundary needs 3:1, not 4.5:1, but it
 * DOES need 3:1. Nothing was enforcing that: the lookup screens shipped with `border-slate-400`,
 * which measures 2.56:1 on white and therefore missed it. Decorative separators are exempt and keep
 * using the lighter taupe; an input or button edge is not decorative.
 */
describe('interactive control borders meet the non-text contrast minimum', () => {
  // The shell background, `docs/design/edje-compass-mockups.html`'s own `--bg`. Kept in step with
  // `html { background-color }` in `index.css` — a value changed there alone is a value nothing here
  // measures.
  const PAGE_BACKGROUND: [number, number, number] = hexToRgb('#f4f5f6');

  it('accepts the brand gray at the control-border alpha, on white AND on the page background', () => {
    const border = compositeOver(hexToRgb(BRAND_GRAY_TEXT), BRAND_CONTROL_BORDER_ALPHA, WHITE);
    const onPage = compositeOver(
      hexToRgb(BRAND_GRAY_TEXT),
      BRAND_CONTROL_BORDER_ALPHA,
      PAGE_BACKGROUND,
    );

    // Both surfaces, because a card sits on white while the shell sits on #f4f5f6 — an alpha chosen
    // against only one of them can miss on the other. /60 does exactly that: 3.03:1 then 2.93:1.
    expect(contrastRatio(border, WHITE)).toBeGreaterThanOrEqual(AA_NON_TEXT_MIN_RATIO);
    expect(contrastRatio(onPage, PAGE_BACKGROUND)).toBeGreaterThanOrEqual(AA_NON_TEXT_MIN_RATIO);
  });

  it('rejects the taupe separator tint as a control border — decorative only', () => {
    // Guards the split. border-brand-taupe/40 is right for a card edge and wrong for an input, and
    // this is what stops the two being conflated the next time someone wants a softer field.
    const separator = compositeOver(hexToRgb(BRAND_TAUPE), 0.4, WHITE);

    expect(contrastRatio(separator, WHITE)).toBeLessThan(AA_NON_TEXT_MIN_RATIO);
  });

  it('accepts the muted grey as secondary text on BOTH surfaces, not just white', () => {
    // Secondary text comes from the style guide's grey ramp — `leading-edje-style-guide.html` is
    // documented as "the only source of semantic colour roles, the tint/shade ramp, and usage" — so the
    // hierarchy is restored from the documented palette rather than an invented tint.
    //
    // BOTH surfaces are asserted because this test previously checked only white and PASSED, while real
    // axe-core failed the shell: the style guide's own `--color-fg-muted` (`--le-gray-500`, #6F757C) is
    // 4.65:1 on white but 4.26:1 on the `#f4f5f6` page background, and description text sits directly on
    // that shell rather than inside a white card. One step darker on the same ramp clears both.
    expect(contrastRatio(hexToRgb(BRAND_GRAY_MUTED_TEXT), WHITE)).toBeGreaterThanOrEqual(
      AA_NORMAL_TEXT_MIN_RATIO,
    );
    expect(contrastRatio(hexToRgb(BRAND_GRAY_MUTED_TEXT), PAGE_BACKGROUND)).toBeGreaterThanOrEqual(
      AA_NORMAL_TEXT_MIN_RATIO,
    );
  });

  it("rejects all three of RPT-3's own subheading colours, and accepts the steps used instead", () => {
    // Feature 007 US2. `#s-reports` colour-codes the Availability Report's three subheadings and sets
    // every one at `font-size:13px` -- normal text, so 4.5:1 applies. All three of its own values miss:
    // §1 `--green-dark` #7FAE2C is 2.63:1, §2 `--le-blue` #5C95FF is 2.92:1, §3 #2596b3 is 3.45:1.
    // Copying the design source verbatim here would have shipped three AC-NFR-5 failures that looked
    // exactly like the mockup -- the case Principle IX's rationale names as most likely to ship.
    //
    // Accessibility outranks fidelity (Principle X rule 2), so each hue is darkened until it passes and
    // the colour-coding survives. TWO of the three needed no new token: BRAND_BLUE_ACCENT_TEXT already
    // existed at 5.44:1 for §2, and BRAND_GREEN_ACCENT_TEXT for §1 -- US2 first added its own green at
    // #5C7C1E (4.82:1 on white) and it was dropped on rebase in favour of the one main had landed
    // independently at #4A6318, which is darker (6.80:1) and also clears the green TINT at 5.80:1 where
    // #5C7C1E measures 4.11:1 and fails. Same decision, reached twice; main's is strictly better. BRAND_TEAL_ACCENT_TEXT (#2487A0, 4.16:1) is deliberately NOT reused -- it is the
    // dashboard's cyan TILE accent, where the number is large text and 3:1 suffices; at 13px it fails.
    for (const failing of ['#7FAE2C', '#5C95FF', '#2596b3']) {
      expect(contrastRatio(hexToRgb(failing), WHITE)).toBeLessThan(AA_NORMAL_TEXT_MIN_RATIO);
    }

    for (const passing of [
      BRAND_GREEN_ACCENT_TEXT,
      BRAND_BLUE_ACCENT_TEXT,
      BRAND_TEAL_HEADING_TEXT,
    ]) {
      expect(contrastRatio(hexToRgb(passing), WHITE)).toBeGreaterThanOrEqual(
        AA_NORMAL_TEXT_MIN_RATIO,
      );
    }

    // And the existing teal is named explicitly as the one that must NOT be reused for this, so nobody
    // "simplifies" the new token away into it.
    expect(contrastRatio(hexToRgb(BRAND_TEAL_ACCENT_TEXT), WHITE)).toBeLessThan(
      AA_NORMAL_TEXT_MIN_RATIO,
    );
  });

  it("rejects the reports mockup's own inactive-tab grey, and accepts the step used instead", () => {
    // Feature 007 US2, T057. `#s-reports` styles an inactive tab `var(--muted)` = `#7A7C7F` at 13px,
    // which is **4.20:1 on white** — under the 4.5:1 normal-text minimum. Copying it verbatim would
    // have shipped an AC-NFR-5 failure that looked exactly like the design, which is the case
    // Principle IX's rationale names as the one most likely to ship.
    //
    // Accessibility outranks fidelity (Principle X rule 2), so `ReportsLayout` uses the existing
    // `BRAND_GRAY_MUTED_TEXT` step instead. **No new token was added** — the darkened value that
    // already exists for secondary text is the correct one here, so this departure costs nothing in
    // `brand-tokens.ts` and nothing in `index.css`.
    expect(contrastRatio(hexToRgb('#7A7C7F'), WHITE)).toBeLessThan(AA_NORMAL_TEXT_MIN_RATIO);
    expect(contrastRatio(hexToRgb(BRAND_GRAY_MUTED_TEXT), WHITE)).toBeGreaterThanOrEqual(
      AA_NORMAL_TEXT_MIN_RATIO,
    );
  });

  it("rejects the style guide's own fg-muted step, which misses on the shell", () => {
    // Named explicitly so nobody "simplifies" back to it. A documented design token is not
    // automatically accessible on every documented surface.
    expect(contrastRatio(hexToRgb('#6F757C'), PAGE_BACKGROUND)).toBeLessThan(
      AA_NORMAL_TEXT_MIN_RATIO,
    );
  });

  it("does NOT adopt the style guide's border tokens, which miss the non-text minimum", () => {
    // The reason the control border is a measured brand-gray alpha rather than the style guide's own
    // `--color-border` / `--color-border-strong`: those are `--le-gray-200` and `--le-gray-300`, and
    // they measure 1.31:1 and 1.65:1 against white. This is the same trap the project already recorded
    // for the mockups' pale-green pill — a documented design token is not automatically accessible,
    // so the ratio decides.
    expect(contrastRatio(hexToRgb('#DDE1E5'), WHITE)).toBeLessThan(AA_NON_TEXT_MIN_RATIO);
    expect(contrastRatio(hexToRgb('#C4CAD0'), WHITE)).toBeLessThan(AA_NON_TEXT_MIN_RATIO);
  });

  it('rejects the slate border the screens used to carry', () => {
    // The measurement that turned this from a token-consistency tidy-up into an accessibility fix.
    expect(contrastRatio(hexToRgb('#94a3b8'), WHITE)).toBeLessThan(AA_NON_TEXT_MIN_RATIO);
  });

  it('keeps brand-gray text readable on every fill the screens place it on', () => {
    const fills: [string, [number, number, number]][] = [
      ['brand-green/15 button', compositeOver(hexToRgb(BRAND_GREEN), 0.15, WHITE)],
      ['brand-green/25 button hover', compositeOver(hexToRgb(BRAND_GREEN), 0.25, WHITE)],
      ['brand-green/10 secondary hover', compositeOver(hexToRgb(BRAND_GREEN), 0.1, WHITE)],
      ['brand-taupe/10 alert', compositeOver(hexToRgb(BRAND_TAUPE), 0.1, WHITE)],
      // The same alert on the PAGE ground rather than a card's white: `AssignmentDetailPage`'s
      // assignment-delete slot sits directly on `<main>` (PR #604 review).
      [
        'brand-taupe/10 alert on the page',
        compositeOver(hexToRgb(BRAND_TAUPE), 0.1, PAGE_BACKGROUND),
      ],
      ['brand-taupe/20 code chip', compositeOver(hexToRgb(BRAND_TAUPE), 0.2, WHITE)],
    ];

    for (const [name, fill] of fills) {
      const ratio = contrastRatio(hexToRgb(BRAND_GRAY_TEXT), fill);
      expect(ratio, `brand-gray text on ${name}`).toBeGreaterThanOrEqual(AA_NORMAL_TEXT_MIN_RATIO);
    }
  });
});

/**
 * The pairings the mockups' own component styles imply, measured rather than copied (US2 / #59).
 *
 * `docs/design/edje-compass-mockups.html` binds on presentation and is "indicative input, never a
 * specification of behaviour or data" (constitution Principle I, as amended to v1.3.0). Its component
 * CSS is therefore followed for LAYOUT and rejected wherever the ratio says so — and it says so more
 * than once. That ordering is not a local convention: it is Principle X's tie-break, where
 * accessibility (rule 2) outranks fidelity to the design source (rule 3), and THIS FILE is the gate
 * that rule 2 names. The mockups' own pale-green status pill (`#5c7c1e` on `#eef6dc`) is the case the
 * constitution records at 4.33:1, and a solid-green button on white misses the non-text minimum by a
 * factor of one and a half.
 */
describe('the mockup-derived component pairings', () => {
  // Matches the `PAGE_BACKGROUND` measured above — the mockups' own `--bg`, and `index.css`'s
  // `html { background-color }`.
  const SHELL: [number, number, number] = hexToRgb('#f4f5f6');

  it('puts INK on a brand-green fill, not the brand gray', () => {
    // The primary button. The mockups use `#26300f` (7.11:1, which is fine); the style guide's own
    // `--color-accent-fg` is `#1F2022` and measures better, so the documented token wins on merit rather
    // than on provenance. Brand gray on green is only 4.33:1 — it passes for large text and fails for a
    // 14px button label, which is exactly the "looks correct" failure Principle IX warns about.
    expect(contrastRatio(hexToRgb(BRAND_INK), hexToRgb(BRAND_GREEN))).toBeGreaterThanOrEqual(
      AA_NORMAL_TEXT_MIN_RATIO,
    );
    expect(contrastRatio(hexToRgb(BRAND_GRAY_TEXT), hexToRgb(BRAND_GREEN))).toBeLessThan(
      AA_NORMAL_TEXT_MIN_RATIO,
    );
  });

  it('gives a green-filled control a green-700 border, because the fill itself has no edge', () => {
    // WCAG 2.1 §1.4.11: a control's visual boundary needs 3:1. A solid `--le-green` button on white is
    // 1.95:1, so a green button with no border is invisible as a CONTROL however readable its label is.
    // This is the trap a mockup cannot show, since its own page background is `#F4F5F6` and it draws no
    // button border at all.
    expect(contrastRatio(hexToRgb(BRAND_GREEN), WHITE)).toBeLessThan(AA_NON_TEXT_MIN_RATIO);

    expect(contrastRatio(hexToRgb(BRAND_GREEN_700), WHITE)).toBeGreaterThanOrEqual(
      AA_NON_TEXT_MIN_RATIO,
    );
    expect(contrastRatio(hexToRgb(BRAND_GREEN_700), SHELL)).toBeGreaterThanOrEqual(
      AA_NON_TEXT_MIN_RATIO,
    );
  });

  it('rejects green-600, which is the step someone reaches for first', () => {
    // Named so nobody "corrects" the border to the style guide's hover colour. `--le-green-600`
    // (`#82B22F`) is 2.51:1 and misses. Only the 700 step clears it.
    expect(contrastRatio(hexToRgb('#82B22F'), WHITE)).toBeLessThan(AA_NON_TEXT_MIN_RATIO);
  });

  it('puts green-accent text on the green tint for a status pill, on both surfaces', () => {
    // A pill sits inside a white card AND directly on the shell, so both are asserted — the mistake this
    // repository has already made once was checking only white.
    //
    // The text was BRAND_GRAY_TEXT until 2026-08-18. That pairing is accessible at 7.22:1 and was
    // rejected on appearance, not contrast: it read as a grey chip where every design source shows a
    // green one. The replacement had to clear the same bar, which is what this asserts.
    for (const surface of [WHITE, SHELL]) {
      expect(
        contrastRatio(hexToRgb(BRAND_GREEN_ACCENT_TEXT), hexToRgb(BRAND_GREEN_TINT)),
      ).toBeGreaterThanOrEqual(AA_NORMAL_TEXT_MIN_RATIO);
      // The tint is opaque, so the surface behind it cannot change the ratio — asserted anyway so the
      // loop is honest about what it covers rather than implying a composite it does not compute.
      expect(contrastRatio(hexToRgb(BRAND_GREEN_TINT), surface)).toBeGreaterThan(1);
    }
  });

  it('matches the green-accent ratios recorded on the token (±0.05)', () => {
    expect(
      contrastRatio(hexToRgb(BRAND_GREEN_ACCENT_TEXT), hexToRgb(BRAND_GREEN_TINT)),
    ).toBeCloseTo(5.8, 1);
    expect(contrastRatio(hexToRgb(BRAND_GREEN_ACCENT_TEXT), WHITE)).toBeCloseTo(6.8, 1);
  });

  it('gives a hovered table link a green that clears AA as normal text, on both surfaces', () => {
    // `tableLinkClass`'s hover state (owner request 2026-08-21). The baseline hovered to
    // BRAND_GREEN_700, a BORDER token at 3.37:1, so the link's own text failed §1.4.3 while hovered —
    // and nothing caught it: axe scans a resting page and a screenshot has no hover.
    //
    // All three surfaces, because a table sits in a white card, on the shell, and its header band is
    // the gray tint. Same hue, two steps darker, so the brand cue survives.
    for (const surface of [WHITE, SHELL, hexToRgb(BRAND_GRAY_TINT)]) {
      expect(contrastRatio(hexToRgb(BRAND_GREEN_ACCENT_TEXT), surface)).toBeGreaterThanOrEqual(
        AA_NORMAL_TEXT_MIN_RATIO,
      );
      expect(contrastRatio(hexToRgb(BRAND_GREEN_700), surface)).toBeLessThan(
        AA_NORMAL_TEXT_MIN_RATIO,
      );
    }
  });

  it('rejects green-700 as the pill text, which is the token someone reaches for first', () => {
    // It is the green already in the palette, and it is a BORDER token: 2.88:1 on the tint. Named here
    // so the pill is not "simplified" onto it later — that swap looks like reuse and is a regression.
    expect(contrastRatio(hexToRgb(BRAND_GREEN_700), hexToRgb(BRAND_GREEN_TINT))).toBeLessThan(
      AA_NORMAL_TEXT_MIN_RATIO,
    );
  });

  it("rejects the mockups' own pale-green pill pairing", () => {
    // `.pill.green { background:#eef6dc; color:#5c7c1e }`, verbatim from the mockups, measures 4.33:1 —
    // under the 4.5:1 normal-text threshold and passing only as large text. Copying a mockup pairing is
    // the failure mode; this is the specific instance the constitution cites.
    expect(contrastRatio(hexToRgb('#5c7c1e'), hexToRgb('#eef6dc'))).toBeLessThan(
      AA_NORMAL_TEXT_MIN_RATIO,
    );

    // And it is no better on OUR tint, which is the substitution the 2026-08-18 pill change makes
    // tempting: honouring the mockup's hue does not mean adopting its lightness.
    expect(contrastRatio(hexToRgb('#5c7c1e'), hexToRgb(BRAND_GREEN_TINT))).toBeLessThan(
      AA_NORMAL_TEXT_MIN_RATIO,
    );
  });

  it('puts brand-gray text on the gray tint for a neutral pill and a table header', () => {
    expect(
      contrastRatio(hexToRgb(BRAND_GRAY_TEXT), hexToRgb(BRAND_GRAY_TINT)),
    ).toBeGreaterThanOrEqual(AA_NORMAL_TEXT_MIN_RATIO);
  });

  it('keeps the toggle distinguishable in BOTH states by boundary, not only by fill', () => {
    // A switch conveys state by position and colour, so its track needs a 3:1 boundary in each state or
    // the off state is an invisible control. Neither fill provides one on its own: the green is 1.95:1
    // and the gray tint is lighter still. The borders do — green-700 when on, the measured brand-gray
    // alpha when off. State is ALSO conveyed as a word, so none of this is colour-only.
    expect(contrastRatio(hexToRgb(BRAND_GRAY_TINT), WHITE)).toBeLessThan(AA_NON_TEXT_MIN_RATIO);

    const offBorder = compositeOver(hexToRgb(BRAND_GRAY_TEXT), BRAND_CONTROL_BORDER_ALPHA, WHITE);
    expect(contrastRatio(offBorder, WHITE)).toBeGreaterThanOrEqual(AA_NON_TEXT_MIN_RATIO);
    expect(contrastRatio(hexToRgb(BRAND_GREEN_700), WHITE)).toBeGreaterThanOrEqual(
      AA_NON_TEXT_MIN_RATIO,
    );
  });

  it('keeps the primary button label legible on its HOVER fill, not only at rest (#78)', () => {
    // The regression this exists for. The primary button darkens to green-700 on hover, and its label
    // used to switch to white at the same time: 3.37:1, below the AA minimum, so the label failed
    // contrast for exactly as long as the pointer was on it.
    //
    // It survived because contrast was only ever measured in the RESTING state -- brand-ink on brand
    // green, a documented 8.35:1 -- and because every axe sweep before #78's scanned a screen with
    // nothing hovered. #78's clicks Run before scanning, which put the button in its hover state and
    // surfaced it immediately.
    //
    // Ink is the fix and needs no new token: it clears AA on BOTH fills.
    expect(contrastRatio(hexToRgb(BRAND_INK), hexToRgb(BRAND_GREEN))).toBeGreaterThanOrEqual(
      AA_NORMAL_TEXT_MIN_RATIO,
    );
    expect(contrastRatio(hexToRgb(BRAND_INK), hexToRgb(BRAND_GREEN_700))).toBeGreaterThanOrEqual(
      AA_NORMAL_TEXT_MIN_RATIO,
    );

    // And the pairing that was shipped is asserted to FAIL, so "restore hover:text-white because it
    // looks punchier" cannot pass this suite by accident.
    expect(contrastRatio(WHITE, hexToRgb(BRAND_GREEN_700))).toBeLessThan(AA_NORMAL_TEXT_MIN_RATIO);
  });

  it('accepts the Compass nav topbar: white text on the brand-gray chrome (#67)', () => {
    // `docs/design/edje-compass-mockups.html`'s `.topbar` sits on `var(--le-gray)` — the exact hex
    // BRAND_GRAY_TEXT already names for body text — so this reuses that token rather than inventing
    // a second one. Contrast is symmetric, so white-on-gray is the same 8.46:1 already measured
    // above as gray-on-white, just read the other direction.
    expect(contrastRatio(WHITE, hexToRgb(BRAND_GRAY_TEXT))).toBeCloseTo(8.46, 1);

    // The inactive link colour (`text-white/80`) composited over that same background.
    const inactiveLink = compositeOver(WHITE, 0.8, hexToRgb(BRAND_GRAY_TEXT));
    expect(contrastRatio(inactiveLink, hexToRgb(BRAND_GRAY_TEXT))).toBeGreaterThanOrEqual(
      AA_NORMAL_TEXT_MIN_RATIO,
    );
  });

  it("rejects the browser's default focus-ring blue on the nav topbar — why the ring is white", () => {
    // The trap a plain background swap would otherwise ship silently: Chromium's default focus ring
    // (~#1a73e8) measures 4.51:1 on the white bar the topbar replaces — comfortably over the 3:1
    // §1.4.11 non-text minimum — but only 1.88:1 on the new dark topbar, i.e. invisible to a keyboard
    // user. `focus-visible:outline-white` on `CompassNav`'s links avoids it.
    expect(contrastRatio(hexToRgb('#1a73e8'), hexToRgb(BRAND_GRAY_TEXT))).toBeLessThan(
      AA_NON_TEXT_MIN_RATIO,
    );
    expect(contrastRatio(WHITE, hexToRgb(BRAND_GRAY_TEXT))).toBeGreaterThanOrEqual(
      AA_NON_TEXT_MIN_RATIO,
    );
  });

  it('keeps the required-field marker readable, since it is not conveyed by colour alone anyway', () => {
    // The mockups mark required fields with a red asterisk (`#c0392b`). The colour is decorative here —
    // the marker is also announced, and the field carries `aria-required` — but a decorative colour that
    // cannot be read is still a defect, so it is measured.
    expect(contrastRatio(hexToRgb(BRAND_DANGER_TEXT), WHITE)).toBeGreaterThanOrEqual(
      AA_NORMAL_TEXT_MIN_RATIO,
    );
    expect(contrastRatio(hexToRgb(BRAND_DANGER_TEXT), SHELL)).toBeGreaterThanOrEqual(
      AA_NORMAL_TEXT_MIN_RATIO,
    );
  });
});

describe('feature 007 (Sales Dashboard) — urgency pills and tile accents', () => {
  const SHELL: [number, number, number] = hexToRgb('#f4f5f6');

  it('puts brand-danger text on the mockups’ own red pill surface, kept verbatim', () => {
    expect(
      contrastRatio(hexToRgb(BRAND_DANGER_TEXT), hexToRgb(BRAND_DANGER_TINT)),
    ).toBeGreaterThanOrEqual(AA_NORMAL_TEXT_MIN_RATIO);
  });

  it('rejects the mockups’ own taupe pill pairing, and accepts the darkened replacement', () => {
    // `.pill.taupe { background:#f3ecea; color:#8a6f69 }`, verbatim from the mockups, measures
    // 3.95:1 — the same shape as the pale-green pill Principle IX already names.
    expect(contrastRatio(hexToRgb('#8a6f69'), hexToRgb('#f3ecea'))).toBeLessThan(
      AA_NORMAL_TEXT_MIN_RATIO,
    );

    expect(
      contrastRatio(hexToRgb(BRAND_TAUPE_ACCENT_TEXT), hexToRgb(BRAND_TAUPE_TINT)),
    ).toBeGreaterThanOrEqual(AA_NORMAL_TEXT_MIN_RATIO);
  });

  it('rejects the style guide’s le-blue-600 as a link colour, and accepts the darkened replacement', () => {
    // 4.04:1 on white — clears the 3:1 large-text/border minimum but misses 4.5:1 for a normal-size
    // link, which is exactly what a client-name cell is.
    expect(contrastRatio(hexToRgb('#4A7CDB'), WHITE)).toBeLessThan(AA_NORMAL_TEXT_MIN_RATIO);

    for (const surface of [WHITE, SHELL]) {
      expect(contrastRatio(hexToRgb(BRAND_BLUE_ACCENT_TEXT), surface)).toBeGreaterThanOrEqual(
        AA_NORMAL_TEXT_MIN_RATIO,
      );
    }
  });

  it('gives the Confirmed Rollouts tile a border/number blue that clears the 3:1 non-text minimum', () => {
    // Large text (34px bold) and a 4px border both need only 3:1, but the link-safe value above
    // clears this too, so one blue token serves both jobs on this screen.
    for (const surface of [WHITE, SHELL]) {
      expect(contrastRatio(hexToRgb(BRAND_BLUE_ACCENT_TEXT), surface)).toBeGreaterThanOrEqual(
        AA_NON_TEXT_MIN_RATIO,
      );
    }
  });

  it('rejects the style guide’s le-teal-600 as the Beach tile accent, and accepts the darkened replacement', () => {
    // 2.75:1 on white — below even the 3:1 large-text/non-text minimum this tile's border and
    // number both need.
    expect(contrastRatio(hexToRgb('#2EA9C8'), WHITE)).toBeLessThan(AA_NON_TEXT_MIN_RATIO);

    for (const surface of [WHITE, SHELL]) {
      expect(contrastRatio(hexToRgb(BRAND_TEAL_ACCENT_TEXT), surface)).toBeGreaterThanOrEqual(
        AA_NON_TEXT_MIN_RATIO,
      );
    }
  });
});
