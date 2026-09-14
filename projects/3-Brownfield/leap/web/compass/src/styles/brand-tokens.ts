/**
 * The five EDJE brand values and their AA-safe roles. Measured against white (`#FFFFFF`): green,
 * cyan, blue and taupe all fail the WCAG AA 4.5:1 minimum for normal body text — only the brand gray
 * clears it (8.46:1). `web/compass/README.md` carries the full rationale;
 * `tests/unit/brand-contrast.test.ts` enforces every ratio in this file.
 */
export const BRAND_GREEN = '#98C93D';
export const BRAND_CYAN = '#49C6E5';
export const BRAND_BLUE = '#5C95FF';
export const BRAND_TAUPE = '#C4B1AE';

/** The only brand colour safe as default body text on a light surface. */
export const BRAND_GRAY_TEXT = '#4C4D4F';

/**
 * `--text` from `docs/design/edje-compass-mockups.html` — plain page body copy: headings, table cells,
 * links, toggle labels. **Not the same role as {@link BRAND_GRAY_TEXT}.** The mockups use TWO grays:
 * `--le-gray` (`BRAND_GRAY_TEXT`) for chrome — the topbar, `th`, `.field label`, `.btn.secondary` —
 * and this darker `--text` for `body{color}` and everything inheriting it. Collapsing both onto
 * `BRAND_GRAY_TEXT` reads visibly lighter than the mockup for ordinary copy.
 *
 * Not in `leading-edje-style-guide.html`'s grey ramp at all; adopted because it clears AA comfortably
 * on both surfaces this app paints text on.
 */
export const BRAND_TEXT = '#2E2F30';

/**
 * Secondary text — `--le-gray-600`. 6.98:1 on white, 6.40:1 on the `#f4f5f6` shell.
 *
 * It exists so primary and secondary text differ by COLOUR as well as size; collapsing both onto
 * {@link BRAND_GRAY_TEXT} is accessible but flattens the hierarchy.
 *
 * **NOT `--color-fg-muted` (`--le-gray-500`, #6F757C).** That step is 4.65:1 on white but **4.26:1 on
 * `#f4f5f6`**, and description text sits directly on the shell rather than inside a white card, so it
 * misses AA exactly where it is used. One step darker clears both surfaces.
 */
export const BRAND_GRAY_MUTED_TEXT = '#545A60';

/** Fill, border and accent contexts only — never default body text on a light surface. */
export const BRAND_ACCENT_COLORS = [BRAND_GREEN, BRAND_CYAN, BRAND_BLUE, BRAND_TAUPE] as const;

/*
 * ---------------------------------------------------------------------------------------------------
 * Style-guide RAMP values. Legitimate to use, NOT brand values — only the five above are the brand.
 *
 * `docs/design/leading-edje-style-guide.html` is the only source of semantic colour roles, the
 * tint/shade ramp, and usage.
 *
 * Every ratio below was MEASURED for the surface it is used on, not inherited from the mockups. Two of
 * the mockups' own pairings fail — its status pill at 4.33:1, and a solid-green button whose boundary
 * is 1.95:1 — so the ratios decide.
 * ---------------------------------------------------------------------------------------------------
 */

/**
 * `--le-charcoal-900`. Text on a brand-coloured fill, and the strong heading colour.
 *
 * **8.35:1 on the brand green**, which is what makes a green primary button readable.
 * {@link BRAND_GRAY_TEXT} on green is only 4.33:1 — it passes as large text and fails as a 14px
 * button label.
 */
export const BRAND_INK = '#1F2022';

/**
 * `--le-green-700`. The border a green-filled control needs.
 *
 * **3.37:1 on white and 3.09:1 on the `#f4f5f6` shell**, clearing WCAG 2.1 §1.4.11's 3:1 for a
 * control's visual boundary. The fill cannot provide that boundary — brand green is 1.95:1 on white.
 * Not `--le-green-600` (`#82B22F`), the step reached for first: 2.51:1.
 *
 * **3:1 is the NON-TEXT threshold, and that is the whole of this token's licence.** As link text the
 * same 3.37:1 misses §1.4.3; that is {@link BRAND_GREEN_ACCENT_TEXT}'s job.
 */
export const BRAND_GREEN_700 = '#6D9924';

/**
 * `--le-green-100`. The surface under an affirmative status pill or an accent chip.
 *
 * **7.22:1 under {@link BRAND_GRAY_TEXT}**. Opaque, so it reads the same inside a white card and
 * directly on the shell. Not the mockups' `.pill.green` (`#5c7c1e` on `#eef6dc`): 4.33:1, which
 * passes only as large text.
 */
export const BRAND_GREEN_TINT = '#E6F2CA';

/**
 * Text on {@link BRAND_GREEN_TINT}, and the hover colour of `tableLinkClass` — the mockups'
 * `.pill.green` hue, darkened until it clears AA.
 *
 * **5.80:1 on {@link BRAND_GREEN_TINT}, 6.80:1 on white, 5.95:1 on {@link BRAND_GRAY_TINT}.** The
 * mockups' own `#5c7c1e` is 4.11:1 on this tint and misses the normal-text minimum, so the hue is
 * honoured and the lightness is not. {@link BRAND_GREEN_700} is not a candidate for either role: it
 * is a border token, 2.88:1 on this tint and 3.37:1 as text.
 *
 * It exists because the affirmative pill otherwise wears {@link BRAND_GRAY_TEXT}, which is accessible
 * (7.22:1) but reads as a grey chip where every design source shows a green one. A neutral pill keeps
 * the grey — that one IS grey in the mockups.
 */
export const BRAND_GREEN_ACCENT_TEXT = '#4A6318';

/**
 * `--le-gray-100`. The surface under a neutral status pill and a table's header row.
 *
 * **7.41:1 under {@link BRAND_GRAY_TEXT}**. It is a SURFACE, not a border: at 1.14:1 against white it
 * cannot delimit a control, so an interactive element filled with it still needs
 * {@link BRAND_CONTROL_BORDER_ALPHA} or {@link BRAND_GREEN_700} for its edge.
 */
export const BRAND_GRAY_TINT = '#EEF0F2';

/**
 * `--color-danger`, one step darker for text use. Validation messages and the required-field marker.
 *
 * **6.15:1 on white, 5.64:1 on the shell.** The style guide's own `--color-danger` (`#D9534F`) is
 * 3.96:1 and misses normal-text AA. Also not the mockups' `.req` marker (`#c0392b`), which does clear
 * AA at 5.44:1 — this step is preferred for consistency with the ramp.
 *
 * Colour is never the only signal: an invalid field carries `aria-invalid` and a text message, and a
 * required one is announced through `aria-required`.
 */
export const BRAND_DANGER_TEXT = '#B3322E';

/**
 * The mockups' own `.pill.red` surface — kept verbatim, unlike its paired text colour.
 *
 * **5.24:1 under {@link BRAND_DANGER_TEXT}.** The mockups pair it with `#b03a3a`;
 * {@link BRAND_DANGER_TEXT} is used instead to keep one danger text colour rather than two
 * near-duplicates.
 */
export const BRAND_DANGER_TINT = '#FDE8E8';

/**
 * `--le-taupe-100`. The surface under the dashboard's "later" urgency pill.
 *
 * **6.84:1 under {@link BRAND_TAUPE_ACCENT_TEXT}.**
 */
export const BRAND_TAUPE_TINT = '#EFE7E5';

/**
 * A darkened taupe, for text on {@link BRAND_TAUPE_TINT} — the "later" urgency pill and the
 * days-until-expiration column.
 *
 * **5.61:1 on {@link BRAND_TAUPE_TINT}**, 5.86:1 on the mockups' own `#f3ecea`. Not the mockups' own
 * pill text (`#8a6f69`): 3.95:1. No taupe ramp step exists in the style guide (only
 * `le-taupe-50`/`le-taupe-100`, both light surfaces), so this is derived by darkening until it clears.
 */
export const BRAND_TAUPE_ACCENT_TEXT = '#6B5650';

/**
 * A darkened `--le-blue-600` (`#4A7CDB`), for the dashboard's Confirmed Rollouts tile and for
 * client-name links.
 *
 * **5.44:1 on white, 4.98:1 on the `#f4f5f6` shell.** Not `le-blue-600` itself: 4.04:1, which clears
 * the 3:1 large-text/non-text minimum but misses 4.5:1 for a normal-size link. Not raw
 * {@link BRAND_BLUE} either: 2.92:1. No darker blue ramp step exists in the style guide.
 */
export const BRAND_BLUE_ACCENT_TEXT = '#3A66C2';

/**
 * A darkened `--le-teal-600` (`#2EA9C8`, the closest ramp analogue to the mockups' "cyan" accent),
 * for the dashboard's EDJErs-on-the-Beach tile.
 *
 * **4.16:1 on white, 3.81:1 on the shell** — a large-count and border accent, where 3:1 applies. Not
 * `le-teal-600` itself: 2.75:1, below even 3:1. Not raw {@link BRAND_CYAN}: 2.0:1. No darker teal
 * step exists in the style guide.
 */
export const BRAND_TEAL_ACCENT_TEXT = '#2487A0';

/**
 * A darkened teal for **normal-size text** on white — the Availability Report's third subheading.
 *
 * **5.82:1 on white.** Not {@link BRAND_TEAL_ACCENT_TEXT}, deliberately: at 4.16:1 that value is the
 * dashboard's tile accent, where 3:1 is the applicable minimum, and at 13px it misses AA. The
 * mockup's own `#2596b3` is worse still at 3.45:1.
 */
export const BRAND_TEAL_HEADING_TEXT = '#1E6E82';

/**
 * The alpha the brand gray is used at for an interactive control's border (`border-brand-gray/70`).
 *
 * Chosen by measurement, not by eye: **3.84:1 on white and 3.67:1 on the `#f4f5f6` shell**, clearing
 * WCAG 2.1 §1.4.11's 3:1. `/60` looks similar and does NOT clear it on the shell (3.03:1 then
 * 2.93:1). Decorative separators are exempt from §1.4.11 and keep `border-brand-taupe/40`.
 */
export const BRAND_CONTROL_BORDER_ALPHA = 0.7;
