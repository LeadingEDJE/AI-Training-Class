/**
 * The shared Tailwind class strings the UI primitives and the screens both need.
 *
 * Separate from `ui.tsx` because `react-refresh/only-export-components` requires a component module
 * to export components only, so a class-string constant beside them fails lint. `admin-sections.ts`
 * sits apart from `AdminLayout.tsx` for the same reason.
 *
 * Every value here is MEASURED, and the ratios are asserted in `tests/unit/brand-contrast.test.ts`.
 * Changing one without re-measuring is how an accessibility regression ships looking correct.
 */

/**
 * An interactive control's edge.
 *
 * `brand-gray` at the alpha measured to clear WCAG 2.1 §1.4.11's 3:1 on BOTH surfaces this app paints
 * — white inside a card (3.84:1) and the `#f4f5f6` shell (3.67:1). `/60` looks identical and misses
 * on the shell (3.03:1 then 2.93:1). Decorative separators are exempt from §1.4.11 and keep
 * `border-brand-taupe/40`.
 */
export const CONTROL_BORDER = 'border-brand-gray/70';

/**
 * The surface every text input, select and textarea carries.
 *
 * Applied by the caller rather than injected by `FormField`, unlike the ARIA attributes: a missing
 * class shows up in the first screenshot, a missing `aria-describedby` is silent forever. Only the
 * silent failures are worth taking out of the caller's hands.
 */
export const fieldControlClass = `w-full rounded border ${CONTROL_BORDER} bg-white px-2 py-1.5 text-sm text-brand-text`;

/**
 * The accent fill together with the edge §1.4.11 requires of it.
 *
 * Brand green is 1.95:1 on white, so the fill alone gives a control no boundary and the border is not
 * decoration. `Toggle`'s "on" track needs the same pairing as a primary button, which is why this is
 * shared rather than written twice.
 *
 * The fill on its OWN is a different thing and stays inline where it appears: `CompassNav`'s avatar
 * circle and `Card`'s accent bar are not controls and need no edge.
 */
export const ACCENT_SURFACE = 'border-brand-green-700 bg-brand-green';

/**
 * The single definition of a button's appearance — the mockups' `.btn`, with the boundary they omit.
 *
 * A solid brand-green fill is 1.95:1 on white, so a green button with no border is invisible **as a
 * control** however readable its label. Hence `border-brand-green-700` (3.37:1 on white, 3.22:1 on
 * the shell). The label is `text-brand-ink` (8.35:1 on green), not brand gray — that pairing is
 * 4.33:1, which passes as large text and fails as a 14px button label.
 *
 * Exported so a NAVIGATION control can wear it. `Button` renders a `<button>`, which a link cannot
 * be; the appearance is shared as a class and the element stays the caller's TanStack `<Link to>`,
 * because a `Button` rendering a plain `<a href>` would full-page reload inside the SPA.
 *
 * **Which `size` to pass:**
 *
 * | Where the control sits                                  | `size`      |
 * | ------------------------------------------------------- | ----------- |
 * | A screen's own header — `PageHeader`'s `actions`         | `'default'` |
 * | Anything nested inside it — a `Card`/`Panel` action, a row, a form section | `'small'` |
 *
 * Not enforced mechanically, for the reason the `fieldControlClass` note gives: a wrong size is
 * visible in the first screenshot.
 */
export function buttonClassName({
  variant,
  size = 'default',
}: {
  variant: 'primary' | 'secondary';
  size?: 'default' | 'small';
}): string {
  const padding = size === 'small' ? 'px-3 py-1 text-xs' : 'px-3 py-1.5 text-sm';

  const tone =
    variant === 'primary'
      ? // `hover:text-brand-ink`, NOT `hover:text-white`. The hover fill darkens to green-700, and
        // white on green-700 measures 3.37:1 -- below the AA 4.5:1 minimum, so the label would fail
        // contrast for as long as the pointer was on the button. Ink stays legible on both fills:
        // 8.35:1 at rest, 4.84:1 on hover.
        `${ACCENT_SURFACE} text-brand-ink hover:bg-brand-green-700`
      : `${CONTROL_BORDER} bg-white text-brand-gray hover:bg-brand-green/10`;

  // `font-semibold`, not `font-medium`: 600 is the step the headings, the active nav item and the
  // table headers already use, so a button's label reuses the type scale rather than introducing a
  // heavier one.
  return `rounded border font-semibold disabled:cursor-not-allowed disabled:opacity-60 ${padding} ${tone}`;
}

/**
 * The single definition of a link in a table cell. `tests/unit/table-link-consistency.test.ts` fails
 * on a bare `underline` anywhere else under `src/`, with no allowlist.
 *
 * Colour and decoration only. Weight, wrapping and size belong to the CELL, so callers compose them
 * alongside — `` `${tableLinkClass} whitespace-nowrap` ``.
 *
 * The hover is `brand-green-accent`, NOT `brand-green-700`: that is a border token at 3.37:1, so a
 * hovered link's own text would miss §1.4.3. The accent step is the same hue at 6.80:1 on white,
 * 5.95:1 on a table header band.
 *
 * Also correct for a `<button>` that acts rather than navigates: `SowList` and `LookupSection` open
 * inline editors and must stay buttons, but read as the same affordance.
 */
export const tableLinkClass =
  'text-brand-text underline decoration-brand-green-700 decoration-2 underline-offset-2 hover:text-brand-green-accent';
