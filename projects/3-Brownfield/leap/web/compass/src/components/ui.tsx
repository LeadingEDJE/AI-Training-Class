import {
  Children,
  cloneElement,
  useEffect,
  useId,
  useState,
  type Key,
  type ReactElement,
  type ReactNode,
} from 'react';
import { createPortal } from 'react-dom';
import { useIsNarrowViewport } from '../hooks/useIsNarrowViewport';
import { ACCENT_SURFACE, CONTROL_BORDER, buttonClassName } from './ui-classes';

/**
 * The shared presentational primitives every Compass configuration screen is built from.
 *
 * **Where each decision comes from, because three sources disagree and the order matters.**
 * Layout, labels and structure come from `docs/design/edje-compass-mockups.html`. Colour roles,
 * typography and component semantics come from `docs/design/leading-edje-style-guide.html`. Contrast
 * overrides both, from `src/styles/brand-tokens.ts` and the measurements in `brand-contrast.test.ts` —
 * the mockups' own status pill is 4.33:1 and its green button has no visible boundary at all, so the
 * ratio decides and the mockup loses. The mockups' own banner agrees: they are "indicative input, not
 * specification", and where they disagree with an acceptance criterion the criterion wins.
 *
 * **Why they exist now.** `src/components/` held only `CompassNav` because the rule-of-three threshold
 * was not met with two screens. US2 adds the EDJEr list and form and US3 adds two client screens, so it
 * is met — and the alternative is deciding these eight elements four more times.
 *
 * **One rule that shapes all of them:** each is reachable by ROLE with an accessible name. Every existing
 * Compass unit test and Playwright spec locates by `getByRole('region', { name })` and friends, so a
 * primitive that renders a bare `div` would quietly break that pattern across every screen at once.
 * {@link Panel} is the one deliberate exception, and it earns it by carrying no name to expose — see its
 * own note.
 *
 * `.anno` annotation chips from the mockups are deliberately not reproduced — they are scaffolding, per
 * the mockups' own banner.
 */

/* ------------------------------------------------------------------------- PageHeader */

interface PageHeaderProps {
  /** The page's name, rendered as its only `h1`. */
  title: string;
  /** Trail above the title. Wrapped in a labelled landmark by this component. */
  breadcrumb?: ReactNode;
  /**
   * One line explaining what the page is for, rendered under the title.
   *
   * Added by feature 008. Its absence is why `LookupAdminPage` and `EdjerListPage` hand-rolled their
   * own headers, and that is how two different `h1` sizes came to ship — `text-xl` here against the
   * copies' `text-2xl`.
   */
  description?: string;
  /** A status indicator shown beside the title — usually a {@link StatusPill}. */
  status?: ReactNode;
  /** Actions for this page, aligned to the trailing edge. Secondary first, primary last. */
  actions?: ReactNode;
}

/**
 * The mockups' `.pagehead`: title, an optional status beside it, actions pushed to the trailing edge.
 *
 * The breadcrumb is wrapped in a **labelled navigation landmark**, where the mockups use a plain `div`.
 * A row of links with no landmark gives a screen-reader user no way to identify or skip it, and
 * breadcrumbs are the one navigation people most want to skip.
 */
export function PageHeader({ title, breadcrumb, description, status, actions }: PageHeaderProps) {
  return (
    <div className="flex flex-col gap-1">
      {breadcrumb !== undefined && (
        <nav aria-label="Breadcrumb" className="text-xs text-brand-gray-muted">
          {breadcrumb}
        </nav>
      )}

      {/* flex-wrap, not a fixed two-column split: at 390px the actions drop below the title rather than
          squeezing it, and no media query is needed to say so. */}
      <div className="flex flex-wrap items-center gap-3">
        <h1 className="text-xl font-semibold tracking-tight">{title}</h1>
        {status}
        {actions !== undefined && <div className="ml-auto flex flex-wrap gap-2">{actions}</div>}
      </div>

      {/* Rendered only when present. An empty <p> is still an element a screen reader steps through,
          and this header is read on every screen. */}
      {description !== undefined && <p className="text-sm text-brand-gray-muted">{description}</p>}
    </div>
  );
}

/* ------------------------------------------------------------------------------- Card */

interface CardProps {
  /** The section's name. Also the region's accessible name. */
  title: string;
  /** Optional prose under the heading, attached to the region as its description. */
  description?: string;
  /** An action belonging to this section, aligned to the heading's trailing edge. */
  action?: ReactNode;
  /**
   * The mockups' `.note` — one line UNDER the card's content, in muted 12px.
   *
   * A separate slot from {@link CardProps.description} because the two are different things in the
   * design source and read differently: a description introduces the section before its content, a
   * note qualifies the content after it. `#s-lookups` puts "Only active types appear when configuring
   * EDJErs." below the table, and that placement is load-bearing — as a description it pushed each
   * card's table down by however many lines the prose wrapped to, so two side-by-side cards started
   * their tables at different heights.
   *
   * Still attached to the region as part of its accessible description, so nothing is lost by moving
   * it: a footnote that is only text near a table is what this avoids.
   */
  footnote?: string;
  children: ReactNode;
}

/**
 * The mockups' `.card`, as a labelled region.
 *
 * The green tick before the heading is the mockups' `.card h2 .bar`. It is `aria-hidden` — it carries no
 * information a screen reader needs, and announcing "image" before every heading would be noise.
 *
 * The card edge stays `border-brand-taupe/40`. That tint deliberately does NOT clear the non-text
 * contrast minimum, which is correct: §1.4.11 exempts purely decorative boundaries, and a card is one.
 * An input or button edge is not, which is why {@link CONTROL_BORDER} is a different value.
 */
export function Card({ title, description, action, footnote, children }: CardProps) {
  const headingId = useId();
  const descriptionId = useId();
  const footnoteId = useId();

  // Both slots are part of the region's description when both are present, in reading order.
  // Built as a list rather than as two conditional attributes because `aria-describedby` takes one
  // space-separated value — a second attribute would overwrite the first rather than add to it.
  const describedBy = [
    ...(description !== undefined ? [descriptionId] : []),
    ...(footnote !== undefined ? [footnoteId] : []),
  ].join(' ');

  return (
    <section
      aria-labelledby={headingId}
      {...(describedBy !== '' && { 'aria-describedby': describedBy })}
      className="rounded-lg border border-brand-taupe/40 bg-white p-5"
    >
      {/* The heading's TEXT gives way, never the action's position (owner request 2026-08-21). With
          `flex-wrap` the heading kept its max-content width and pushed the ACTION onto a second row —
          at 375px "Invoice Frequency Types" did exactly that, leaving "+ Add Type" stranded below it
          and the card's rows out of step with its neighbour's.

          Three classes, each load-bearing, and the first two are not interchangeable:

          `flex-1` gives the heading a flex-basis of 0, so it never counts toward whether the row fits
          and therefore never forces a line break. `min-w-0` then lets it shrink past its min-content
          width, which a flex item's default `min-width:auto` would otherwise floor it at. Together
          they mean the heading absorbs whatever space is left and wraps its text at spaces.

          `break-words` is the guard those two need. Shrinking below min-content means the longest
          WORD no longer fits, and text that cannot wrap OVERFLOWS its box — rightwards, straight
          under a `flex-none` action, which is a heading overlapping a button rather than a layout
          that merely looks tight. `AssignmentDetailPage`'s "SOWs / Contracts" beside "+ Add SOW /
          Contract" reaches that point around 320px. `overflow-wrap: break-word` breaks the word
          instead, so the row cannot overflow at ANY width and every character stays on screen.

          No `ml-auto` on the action: with the heading at `flex-1` there is no free space left for an
          auto margin to consume, so it was doing nothing. The heading pushes the action right.

          `items-center` is the mockup's own `.card h2{align-items:center}`, kept — against a wrapped
          heading it centres the action on the block, which is why the green tick needs `flex-none`
          rather than being stretched to the block's height. */}
      <div className="flex items-center gap-2">
        <span aria-hidden="true" className="h-4 w-1 flex-none rounded-sm bg-brand-green" />
        <h2
          id={headingId}
          className="min-w-0 flex-1 text-lg font-semibold tracking-tight break-words"
        >
          {title}
        </h2>
        {action !== undefined && <span className="flex-none">{action}</span>}
      </div>

      {description !== undefined && (
        <p id={descriptionId} className="mt-1 text-sm text-brand-gray-muted">
          {description}
        </p>
      )}

      <div className="mt-4">{children}</div>

      {/* The mockups' `.note`: 12px, muted, 10px below the content. */}
      {footnote !== undefined && (
        <p id={footnoteId} className="mt-2.5 text-xs text-brand-gray-muted">
          {footnote}
        </p>
      )}
    </section>
  );
}

/* ------------------------------------------------------------------------------ Panel */

/**
 * The mockups' `.card` surface WITHOUT a heading — the white sheet a screen's own content sits on.
 *
 * **Why this is not just {@link Card}.** `Card` requires a title and renders it as an `h2` inside a
 * labelled `region`. Several screens need the surface but have no honest second heading to put on it:
 * the only thing the Team Directory's sheet could be called is "Team Directory", which the page's `h1`
 * already is, and a `region` labelled the same as the page is noise in a landmark list rather than
 * navigation. So this renders a plain `div` deliberately, and adds nothing to the accessibility tree —
 * the sheet is presentation, and the content inside it brings its own structure.
 *
 * **Why it exists now (owner request 2026-08-18).** Five screens shipped with their content directly on
 * the `#f4f5f6` shell where the design source puts it on a white sheet — the two admin lists, the
 * employee detail, and both client screens. `TeamDirectoryPage` had hand-rolled exactly this class run
 * to get it right, so the sixth need is what met the rule of three; without this they would be six
 * independent copies of a border tint that is already recorded as a measured choice.
 *
 * The edge stays `border-brand-taupe/40` for the reason {@link Card} records: §1.4.11 exempts a purely
 * decorative boundary, and a sheet is one.
 */
export function Panel({ children }: { children: ReactNode }) {
  return <div className="rounded-lg border border-brand-taupe/40 bg-white p-5">{children}</div>;
}

/* ----------------------------------------------------------------------------- Button */

interface ButtonProps {
  children: ReactNode;
  /** Visual weight only. A secondary button is not a lesser control. */
  variant?: 'primary' | 'secondary';
  size?: 'default' | 'small';
  /**
   * Defaults to `button`.
   *
   * The HTML default is `submit`, which makes a Cancel button placed inside a form submit it — so the
   * safe value is the default here and a real submit has to ask.
   */
  type?: 'button' | 'submit';
  disabled?: boolean;
  onClick?: () => void;
  /**
   * An explicit accessible name, for when several buttons on one screen share visible text.
   *
   * **Why this exists rather than an `sr-only` span inside the button.** The span approach makes the
   * accessible name depend on how the engine joins two text nodes, and they disagree: Chrome inserts a
   * space between them, jsdom does not. That produced a name that read correctly in one and wrongly in
   * the other, and no test could assert both without encoding an engine artifact. One node, one name.
   *
   * **Keep the visible text as a prefix** (WCAG 2.5.3, Label in Name), so a speech-input user saying
   * what they can see still activates the button.
   */
  ariaLabel?: string;
}

/** The mockups' `.btn` as a real `<button>`. Appearance comes from {@link buttonClassName}. */
export function Button({
  children,
  variant = 'primary',
  size = 'default',
  type = 'button',
  disabled = false,
  onClick,
  ariaLabel,
}: ButtonProps) {
  return (
    <button
      type={type}
      disabled={disabled}
      onClick={onClick}
      {...(ariaLabel !== undefined && { 'aria-label': ariaLabel })}
      className={buttonClassName({ variant, size })}
    >
      {children}
    </button>
  );
}

/* ------------------------------------------------------------------------------ Alert */

/**
 * The one-appearance alert slot.
 *
 * Extracted by feature 008 because this exact class run had been copied into **five** places —
 * `EdjerListPage` twice, `EdjerFormPage` twice, and a local copy inside `LookupSection`. Rule of Three
 * was met twice over.
 *
 * It takes **no tone prop**, deliberately. Every one of those five call sites renders the identical
 * appearance, so a variant axis would be structure nothing asks for — which Principle II forbids adding
 * speculatively. Add it against a real second caller, not in anticipation of one.
 *
 * `role="alert"` is the reason this is a component rather than a class constant: a write failure that
 * renders silently is a failure the user never learns about.
 *
 * It renders a `div`, not a `p`, because the most demanding of those five call sites — the
 * deactivation-blocked message on the EDJEr form — carries a paragraph plus a list of the assignments
 * that must be end-dated first. A `p` cannot legally contain either, so a `p` here would have forced
 * that one site to stay hand-rolled and defeated the extraction. `role="alert"` carries the semantics
 * regardless of the element.
 */
export function Alert({ children }: { children: ReactNode }) {
  return (
    <div
      role="alert"
      className="rounded border border-brand-gray/70 bg-brand-taupe/10 px-3 py-2 text-sm"
    >
      {children}
    </div>
  );
}

/* -------------------------------------------------------------------------- StatusPill */

interface StatusPillProps {
  /** `affirmative` for an active or current state; `neutral` for a retired or ended one. */
  tone: 'affirmative' | 'neutral';
  /** The state as a WORD. Required — there is no colour-only form of this component. */
  label: string;
}

/**
 * The mockups' `.pill`, repaired.
 *
 * Two things differ from the mockup, both required. Its own pairing — `#5c7c1e` on `#eef6dc` — measures
 * **4.33:1** and fails the normal-text minimum, so the tint is `--le-green-100` under brand-gray text at
 * 7.22:1 instead. And the state is always a **word**: the mockups distinguish pills by colour alone,
 * which conveys nothing in greyscale and nothing at all to a screen reader (AC-NFR-5).
 *
 * There is deliberately no `label`-less variant. A pill that means something only by its colour is the
 * defect this component exists to make unavailable.
 */
export function StatusPill({ tone, label }: StatusPillProps) {
  // Surface AND text move together, because the mockups' two pills are two colourways rather than one
  // tint with a shared label colour: `.pill.green` is green-on-green and `.pill.gray` is grey-on-grey.
  // The affirmative pill previously borrowed brand gray, which is accessible at 7.22:1 but reads as a
  // grey chip on a green field (owner request 2026-08-18). `--color-brand-green-accent` is the
  // mockups' own hue darkened to 5.80:1 on the tint — see its token comment for why their #5c7c1e is
  // not used directly.
  const colourway =
    tone === 'affirmative'
      ? 'bg-brand-green-tint text-brand-green-accent'
      : 'bg-brand-gray-tint text-brand-gray';

  return (
    <span
      // whitespace-nowrap is the mockups' own `.pill` rule, and it is load-bearing rather than
      // cosmetic: inside a narrow table cell "Full Time" otherwise breaks across two lines and the
      // pill grows into a tall rounded box that no longer reads as a pill.
      className={`inline-block rounded-full px-2.5 py-0.5 text-xs font-semibold whitespace-nowrap ${colourway}`}
    >
      {label}
    </span>
  );
}

/* ------------------------------------------------------------------------------ Toggle */

interface ToggleProps {
  /** What the setting is. Part of the control's accessible name. */
  label: string;
  checked: boolean;
  /** Receives the NEW value, so a caller never has to negate the old one at the call site. */
  onChange: (checked: boolean) => void;
  disabled?: boolean;
  /** Guidance attached as the control's description. */
  hint?: string;
  /**
   * Hides {@link ToggleProps.label} visually while keeping it in the accessible name.
   *
   * For a switch inside a table row, the row already says which record it belongs to, so repeating the
   * name beside the track reads as a duplicate. Dropping it instead is not an option: a column of bare
   * toggles is indistinguishable to a screen-reader user, who hears "switch, on" twenty times.
   */
  labelHidden?: boolean;
  /**
   * The words for each state, when "On"/"Off" is not what the domain calls them.
   *
   * A lookup value is Active or Retired everywhere else in Compass, and this word is the part of the
   * control that survives greyscale and a screen reader (AC-NFR-5) — so it should match the vocabulary
   * the rest of the screen uses rather than the switch's mechanical one.
   */
  stateLabels?: { on: string; off: string };
  /**
   * Hides the state word visually while keeping it in the accessible name.
   *
   * **Owner request 2026-08-21, for the lookup screen specifically:** `#s-lookups` draws a bare
   * `.tswitch` with no word beside it, and the two columns of "Active" it produced here were the last
   * visible departure from the mockup on that screen.
   *
   * The word is HIDDEN, not dropped — `aria-checked` and the accessible name are unchanged, so a screen
   * reader still hears "Full Time, Active, switch, on". What the sighted viewer loses is the redundant
   * text; what remains is the knob's POSITION, which is a non-colour signal and survives greyscale, plus
   * the track's colour. AC-NFR-5 forbids colour ALONE and position is not colour, so the criterion is
   * still met — but the belt-and-braces margin the word gave is gone, which is why this is opt-in per
   * screen rather than the default.
   */
  stateLabelHidden?: boolean;
}

/**
 * The mockups' `.tswitch`, as an actual control.
 *
 * The mockup draws `<div class="tswitch">`. A div cannot be tabbed to, cannot be operated with the space
 * bar, and tells assistive technology nothing — so this is a real `<button role="switch">` carrying
 * `aria-checked`, which looks the same and works.
 *
 * **State is conveyed three ways, and only one of them is colour.** The track colour, the knob's
 * position, and the word "On"/"Off" in the accessible name. Colour alone fails greyscale and a screen
 * reader (AC-NFR-5), and the mockup relies on it alone. {@link ToggleProps.stateLabelHidden} removes the
 * word from SIGHT for one screen that asked for the mockup's bare switch; the position and the
 * accessible name are what carry the state there.
 *
 * Both track states carry a border because neither fill provides a boundary: the green is 1.95:1 on
 * white and the gray tint is 1.14:1. Without them the OFF state in particular is an invisible control.
 */
export function Toggle({
  label,
  checked,
  onChange,
  disabled = false,
  hint,
  labelHidden = false,
  stateLabels,
  stateLabelHidden = false,
}: ToggleProps) {
  const hintId = useId();
  const stateWord = checked ? (stateLabels?.on ?? 'On') : (stateLabels?.off ?? 'Off');

  return (
    <span className="flex flex-col gap-1">
      <button
        type="button"
        role="switch"
        aria-checked={checked}
        disabled={disabled}
        // The name is stated rather than left to be assembled from the two spans below. Computed from
        // contents it came out `"Full TimeActive"` — the accessible-name algorithm trims each text run
        // before joining, so the space BETWEEN two sibling elements is discarded, and a screen reader
        // announced one run-together token. It has always done that; it started mattering when
        // `stateLabelHidden` made this name the only place the state is stated in words.
        //
        // WCAG 2.5.3 holds: the visible text is `label` then `stateWord` in that order, and this is those
        // two in that order. When both are hidden there is no visible label for 2.5.3 to constrain.
        aria-label={`${label} ${stateWord}`}
        {...(hint !== undefined && { 'aria-describedby': hintId })}
        onClick={() => onChange(!checked)}
        className="flex items-center gap-2 text-sm font-medium text-brand-text disabled:cursor-not-allowed disabled:opacity-60"
      >
        {/* aria-hidden: the track is the picture. The state reaches assistive technology through
            aria-checked and through the word below, not through this. */}
        <span
          aria-hidden="true"
          className={`relative h-5 w-9 flex-none rounded-full border ${
            checked ? ACCENT_SURFACE : `${CONTROL_BORDER} bg-brand-gray-tint`
          }`}
        >
          <span
            className={`absolute top-0.5 h-3.5 w-3.5 rounded-full border ${CONTROL_BORDER} bg-white ${
              checked ? 'left-4.5' : 'left-0.5'
            }`}
          />
        </span>
        {/* **These two spans are what a viewer SEES. They no longer build the accessible name** — the
            `aria-label` above states it, and an `aria-label` overrides name-from-contents entirely.
            Until 2026-08-21 the name WAS assembled from them, and the comments here argued about their
            order on that basis; keeping that argument would have told a maintainer the opposite of what
            the code does.

            They still have to say the same thing in the same order as the label, because a viewer
            reading one and a voice-control user speaking the other must match (WCAG 2.5.3). Both are
            built from the same two variables as the label, so they cannot disagree by accident — but
            editing one side alone is now silent, which is exactly why this note is here.

            Either can be hidden from sight without touching the name: `labelHidden` for a switch in a
            table row whose record is already named, `stateLabelHidden` for the mockup's bare switch. */}
        <span className={labelHidden ? 'sr-only' : undefined}>{label}</span>
        <span className={stateLabelHidden ? 'sr-only' : 'text-brand-gray-muted'}>{stateWord}</span>
      </button>

      {hint !== undefined && (
        <span id={hintId} className="text-xs text-brand-gray-muted">
          {hint}
        </span>
      )}
    </span>
  );
}

/* --------------------------------------------------------------------------- FormField */

interface FormFieldProps {
  label: string;
  /**
   * Renders the control. Receives the id to put on it, so the label's `htmlFor` and the control's `id`
   * cannot disagree — the caller never invents either.
   *
   * Must return a SINGLE element, because this component clones it to attach `aria-required`,
   * `aria-invalid` and `aria-describedby`. See the note on {@link FormField} for why those are attached
   * here rather than handed to the caller to spread.
   */
  children: (id: string) => ReactElement;
  required?: boolean;
  hint?: string;
  /** A server rejection or a validation message. Its presence is what marks the control invalid. */
  error?: string;
}

/**
 * The mockups' `.field`: label, required marker, control, hint — plus the parts a mockup cannot draw.
 *
 * The control receives its id from this component rather than choosing one, because two fields sharing an
 * id makes the second unreachable by its own label, and that defect is invisible until something clicks
 * a label. `useId` per instance guarantees they differ.
 *
 * **The hint and the error are BOTH in the description when both exist.** An error that replaces the hint
 * removes the guidance at the moment it is most needed.
 *
 * The required marker is `aria-hidden` and the signal is `aria-required` on the control. A red asterisk
 * is a visual convention, not information — and its colour is measured anyway
 * (`--color-brand-danger`, 6.15:1) so the convention is legible rather than merely present.
 *
 * **The three ARIA attributes are attached HERE, by cloning the control, rather than handed to the caller
 * to spread.** The first draft did the latter and its own tests caught why not: every call site then has
 * to remember `aria-required`, `aria-invalid` and `aria-describedby` on every field, and forgetting one is
 * silent — the field looks correct, validates correctly, and simply stops telling anyone. Attaching them
 * where the state lives makes forgetting impossible. The cost is that `children` must return a single
 * element, which the type now says.
 *
 * **The cost has a second half, and it bit: `required` must be false when the child is not a form
 * control.** The attributes are attached unconditionally, so a caller rendering read-only text — a `<p>`
 * for a value the screen has already fixed — gets `aria-required` on a paragraph, which is invalid ARIA
 * (axe `aria-allowed-attr`, critical impact) as well as a claim the reader must supply something they
 * cannot. `AssignmentForm`'s fixed EDJEr/Client sides do exactly that and shipped this way until the #84
 * sweep found it. This is deliberately NOT guarded here by sniffing `control.type`: a composed control
 * like `ClientPicker` is a function component, so any "is it a native input" heuristic would strip the
 * ARIA from the real controls to protect the fake ones.
 */
export function FormField({ label, children, required = false, hint, error }: FormFieldProps) {
  const fieldId = useId();
  const hintId = useId();
  const errorId = useId();

  const control = children(fieldId);

  // Merged rather than overwritten: a composed control may already describe itself, and clobbering that
  // would trade one missing description for another.
  const existingDescribedBy = (control.props as { 'aria-describedby'?: string })[
    'aria-describedby'
  ];
  const describedBy =
    [
      existingDescribedBy,
      hint !== undefined ? hintId : undefined,
      error !== undefined ? errorId : undefined,
    ]
      .filter((id): id is string => id !== undefined)
      .join(' ') || undefined;

  return (
    <div className="flex flex-col gap-1">
      <label htmlFor={fieldId} className="text-sm font-medium text-brand-gray">
        {label}
        {required && (
          <span aria-hidden="true" className="ml-1 text-brand-danger">
            *
          </span>
        )}
      </label>

      {cloneElement(control, {
        // `undefined` rather than `false`: aria-invalid="false" is a meaningful, DIFFERENT statement from
        // the attribute being absent, and asserting a field is valid before anyone has tried to submit it
        // is a claim this component is not in a position to make.
        ...(required && { 'aria-required': true }),
        ...(error !== undefined && { 'aria-invalid': true }),
        ...(describedBy !== undefined && { 'aria-describedby': describedBy }),
      } as Partial<typeof control.props>)}

      {hint !== undefined && (
        <span id={hintId} className="text-xs text-brand-gray-muted">
          {hint}
        </span>
      )}

      {/* role="alert" so a rejection arriving after a save is announced rather than silently appearing
          below the field the user has already left. */}
      {error !== undefined && (
        <span id={errorId} role="alert" className="text-xs font-medium text-brand-danger">
          {error}
        </span>
      )}
    </div>
  );
}

/* ---------------------------------------------------------------------------- FormGrid */

/**
 * The mockups' `.formgrid`: two columns, collapsing to one on a narrow viewport.
 *
 * `md:` rather than the mockups' `@media(max-width:900px)`. Tailwind's breakpoint is 768px, and the
 * project already fixed ONE breakpoint for layout transitions — introducing a second at 900px creates a
 * band where this grid and the rest of the shell disagree about how wide the screen is.
 */
export function FormGrid({ children }: { children: ReactNode }) {
  return <div className="grid grid-cols-1 gap-x-7 gap-y-4 md:grid-cols-2">{children}</div>;
}

/* --------------------------------------------------------------- ScrollableRegion */

interface ScrollableRegionProps {
  /**
   * The region's accessible name. Required — a focusable region with no name is announced as an
   * unlabelled group, which is a different accessibility problem from the one this solves.
   */
  label: string;
  children: ReactNode;
}

/**
 * A horizontally scrollable region that a keyboard can reach and whose clipped edge is visible.
 *
 * **Why this exists (issue #83, AC-NFR-7).** The three `overflow-x-auto` wrappers in Compass were bare
 * `div`s. Two consequences, both measured:
 *
 * 1. **No keyboard could reach the off-screen columns.** A bare scroll container is not focusable, so
 *    the only way to see hidden content was a pointer. axe reports this as
 *    `scrollable-region-focusable`, impact `serious` — three nodes on the availability report at 390px.
 *    Issue #84 diagnosed it correctly and *excluded the selector from the sweep* rather than fixing it,
 *    which is why it shipped: the sweep walked 23 screens and reported nothing.
 * 2. **A clipped value looked complete.** On an internal client the assignment-history table was cut
 *    20px short, so `09/09/2026` rendered as `09/09/202` — a truncated date that reads as a valid,
 *    wrong one. Every other instance lost a whole column or an obviously-clipped string; this one
 *    silently changed a displayed value.
 *
 * `tabIndex={0}` plus `role="region"` and the name fix (1). The right-edge mask fixes (2): the final
 * glyphs fade, so a cut value looks cut. **The mask is not the only signal** — WCAG 1.4.1 forbids
 * colour as the sole carrier of information, and the focusable region with its accessible name is the
 * non-visual one.
 *
 * **The mask is `aria-hidden` and `pointer-events-none`.** It is decoration over content, so it must
 * not be announced and must not swallow a click on the cell underneath.
 *
 * **What this deliberately does NOT do: make the content fit.** Overflow stays contained inside the
 * region — the page never scrolls sideways, which is the design goal the T118 investigation
 * established and which a remedy must not trade away. Where a value must never be clipped at all, the answer is
 * {@link StackedRows}, not a wider table.
 */
export function ScrollableRegion({ label, children }: ScrollableRegionProps) {
  return (
    <div className="relative">
      <div
        // **`group`, NOT `region`.** `region` is a LANDMARK, and one per scrollable table pollutes the
        // landmark structure a screen-reader user navigates by — a `Card` is already a named region, so
        // a nested one with a similar name is noise. It also broke 20 `LookupAdminPage` tests outright:
        // `getByRole('region', { name: /employee types/i })` found two. `group` supports an accessible
        // name without being a landmark, which is exactly what a scroll container is. axe's
        // `scrollable-region-focusable` only requires the element be FOCUSABLE, which `tabIndex` gives
        // it — the role was never what satisfied that rule.
        role="group"
        aria-label={label}
        tabIndex={0}
        className="overflow-x-auto focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-brand-gray"
      >
        {children}
      </div>

      {/*
        The edge mask. `from-white` matches every surface these tables sit on (`Card` and `Panel` are
        both `bg-white`); a token would be wrong here because the gradient has to disappear INTO the
        surface, and picking the wrong one leaves a visible seam rather than a fade.

        Width is 1.5rem: wide enough that a clipped glyph is unmistakably fading, narrow enough that it
        does not obscure a value that happens to end near the edge.
      */}
      <div
        aria-hidden="true"
        // `data-scroll-edge-mask` is the hook the Gate A assertion looks for. A test that sniffed the
        // computed gradient instead would break on any cosmetic restyle while proving no more, and a
        // mask that is present but not detectable is a requirement with no enforcement.
        data-scroll-edge-mask=""
        className="pointer-events-none absolute inset-y-0 right-0 w-6 bg-gradient-to-l from-white to-transparent"
      />
    </div>
  );
}

/* ------------------------------------------------------------------------------- Table */

/** A column whose header orders the table. */
export interface TableSortableColumn {
  /** The header text. Also the basis of the control's accessible name. */
  label: string;
  /**
   * Which way this column currently orders the table, or `undefined` when another column does.
   *
   * The ARIA vocabulary rather than a boolean: `aria-sort` takes these words, and a `descending` flag
   * cannot say "not the sorted column" without a second one to disambiguate it.
   */
  sortDirection?: 'ascending' | 'descending';
  onSort: () => void;
}

/** A header that is either plain text or a sort control. */
export type TableColumn = string | TableSortableColumn;

interface TableProps {
  /** The table's accessible name. Required — an unnamed table cannot be navigated as one. */
  caption: string;
  columns: TableColumn[];
  /** Rendered when there are no rows. Shown INSTEAD of the table, not inside it. */
  emptyMessage?: string;
  children?: ReactNode;
}

/**
 * The mockups' `table`, with a caption and an honest empty state.
 *
 * The caption is visually hidden but present: assistive technology announces it, and a table with no
 * accessible name is one a screen-reader user cannot tell apart from any other on the page.
 *
 * **An empty table is not rendered as a header row.** Headers with no body read as "loaded, nothing
 * here" to a sighted user and as nothing at all to everyone else, so the message replaces the table.
 * "Empty" means `Children.toArray(children)` is empty, which covers all four shapes a caller produces:
 * no `children` prop at all, `null`, a `false` from a short-circuit, and — the one this used to get
 * wrong — an EMPTY ARRAY from `rows.map(...)` over a zero-length list.
 *
 * That last case is why the check is `Children.toArray` and not a chain of `===` comparisons. `[]` is
 * not `undefined`, `null` or `false`, so the previous form declared a computed-empty list NON-empty and
 * rendered a bare header row — the exact opposite of what this doc promised. Every one of the nine call
 * sites in `src/features/` had independently worked around it with `list.length > 0 ? … : undefined`,
 * which is the clearest possible signal the primitive was wrong rather than the callers. Those guards
 * were removed when this was fixed; do not reintroduce one.
 *
 * `Children.toArray` is used for the COUNT only — `children` is still rendered as passed, so no key is
 * rewritten. Note it treats a fragment as a single node, so `<>{[]}</>` counts as one; pass the array
 * directly (every caller does) rather than wrapping it.
 *
 * The wrapper scrolls horizontally rather than letting a wide table push the page sideways at 390px.
 *
 * **A sortable column's header is a BUTTON, and its state is `aria-sort`.** The mockups put
 * `cursor:pointer` on every `th` and mark the ordered one with a caret. A cell with a click handler
 * cannot be tabbed to or operated from the keyboard, and a caret in a span says nothing to a screen
 * reader — so the affordance is a real control and the direction is on the header cell where assistive
 * technology looks for it. The caret stays, `aria-hidden`, as the picture.
 */
export function Table({ caption, columns, emptyMessage, children }: TableProps) {
  const isEmpty = Children.toArray(children).length === 0;

  if (isEmpty && emptyMessage !== undefined) {
    return <p className="text-sm text-brand-gray-muted">{emptyMessage}</p>;
  }

  return (
    // The caption doubles as the region's accessible name: it already names the table for assistive
    // technology, so a second string would be one more thing to keep in step for no gain.
    <ScrollableRegion label={caption}>
      <table className="w-full border-collapse text-sm">
        <caption className="sr-only">{caption}</caption>
        <thead>
          <tr>
            {columns.map((column, columnIndex) => (
              <th
                // The index, not the label — the same reason as `StackedRows`' cells: two columns may
                // legitimately share a label, and `columns` is a fixed-order list rendered straight
                // through, so the index is unique and stable by construction. This is the header half
                // of that fix; missing it left the collision in place even after the cells were keyed
                // correctly, because the warning came from `<thead>` rather than `<tbody>`.
                key={columnIndex}
                scope="col"
                // Absent, not "none", on a column that cannot be sorted: aria-sort="none" states that
                // this header COULD order the table and currently does not, which is a different and
                // untrue claim.
                {...(typeof column !== 'string' && {
                  'aria-sort': column.sortDirection ?? 'none',
                })}
                // whitespace-nowrap is the mockups' `th` rule: without it "First Name" and "Hire Date"
                // break across two lines and the header row is twice the height of the mockup's.
                className="border-b-2 border-brand-taupe/40 bg-brand-gray-tint px-3 py-2 text-left text-xs font-semibold tracking-wide whitespace-nowrap text-brand-gray uppercase"
              >
                {typeof column === 'string' ? column : <SortButton column={column} />}
              </th>
            ))}
          </tr>
        </thead>
        <tbody className="divide-y divide-brand-taupe/40">{children}</tbody>
      </table>
    </ScrollableRegion>
  );
}

/**
 * A sortable header's control.
 *
 * The accessible name EXTENDS the visible label ("Sort by hire date" contains "Hire Date") rather than
 * replacing it, which is what keeps WCAG 2.5.3 satisfied for a voice-control user reading the screen.
 * The label is lowercased in that name because the header is uppercased by CSS, not by content — the
 * words a viewer sees are the ones in the button.
 */
function SortButton({ column }: { column: TableSortableColumn }) {
  const caret = column.sortDirection === 'ascending' ? '▲' : '▼';

  return (
    <button
      type="button"
      onClick={column.onSort}
      aria-label={`Sort by ${column.label.toLowerCase()}`}
      className="flex items-center gap-1 rounded text-xs font-semibold tracking-wide uppercase underline-offset-2 hover:underline"
    >
      {column.label}
      {/* Brand gray, not the mockups' green caret: #98C93D is 1.95:1 on white and 2.51:1 even at the
          600 step, so a green glyph is the one part of the ordering a sighted viewer could not see.
          aria-hidden because aria-sort on the header already carries the fact. */}
      {column.sortDirection !== undefined && (
        <span aria-hidden="true" className="text-[0.625rem] leading-none">
          {caret}
        </span>
      )}
    </button>
  );
}

/* ------------------------------------------------------------------- StackedRows */

interface StackedRowsProps<TRow> {
  /** The table's accessible name, and the scroll region's, at >= md. */
  caption: string;
  columns: TableColumn[];
  rows: TRow[];
  /**
   * One row's cells, in column order. Length MUST match `columns` — the card layout pairs them by
   * index, so a mismatch mislabels values rather than failing loudly.
   */
  cells: (row: TRow) => ReactNode[];
  rowKey: (row: TRow, index: number) => Key;
  /**
   * Optional per-column class for the CELL box, one entry per column.
   *
   * **This exists so `tabular-nums` lands on the `<td>`, not on a span inside it.**
   *
   * That was originally a GATE property: the Compass visual baselines masked `.tabular-nums`, and
   * masking the cell box meant a column that MOVED still failed while only the volatile value was
   * excluded — wrapping the value in a span instead shrank the mask to the text and let a moved
   * column hide inside it. Found the hard way, by three baselines going red during feature 011's
   * migration.
   *
   * **Issue #574 deleted that gate, so nothing mechanical depends on this shape any more.** It is
   * kept, and still pinned by `ui.test.tsx`, for two reasons: on the `<td>` is where the class
   * belongs regardless (the whole cell gets stable figure widths, not just one span), and a future
   * rule-3 gate would need it back. Do not "simplify" it into a span.
   */
  cellClassNames?: (string | undefined)[];
  /** Rendered instead of the table or the cards when there are no rows. */
  emptyMessage?: string;
}

/**
 * A table at >= `md`, and stacked label-value cards below it (feature 011 FR-006a, issue #83).
 *
 * **Why this is not a prop on {@link Table}.** `Table` takes pre-rendered `<tr>` elements as opaque
 * `children`, so it cannot re-map them into cards — it never sees the individual values. Cards need row
 * DATA plus a cell renderer, which is a different contract, so this is a different component.
 *
 * **Why cards below `md` at all, rather than just a scrollable table.** On a phone these tables hid
 * 120-400px of content. {@link ScrollableRegion} makes that reachable and marks the clip, which is the
 * right answer where a table must stay a table — but on the read surfaces a salesperson or coach opens
 * on a phone, removing the off-screen axis entirely is better than making it navigable. There is
 * nothing to strand if nothing is off-screen.
 *
 * **`md` (768px), matching `FormGrid`.** Compass fixed ONE breakpoint for layout transitions and
 * defines no custom token; the mockups' own `@media(max-width:900px)` covers tiles and form grids only
 * and says nothing about tables at any width. A second breakpoint would create a band where this and
 * the rest of the shell disagree about how wide the screen is.
 *
 * **Exactly ONE layout is in the DOM, chosen by {@link useIsNarrowViewport}.** The first attempt used
 * `hidden md:block` on both branches and let CSS hide one. In a browser that is fine, but it puts
 * duplicate rows in the DOM — and **jsdom applies no CSS**, so six existing report specs suddenly saw
 * every row twice and broke. Scoping those six queries would have left the duplication for every future
 * test and reader of these screens to rediscover, which is the shape of trap this feature exists to
 * remove. The hook defaults to the table when `matchMedia` is unavailable, so those specs keep asserting
 * table semantics unchanged.
 *
 * **Field parity is a requirement, not a nicety** (CHK010): a card shows the same fields, in the same
 * order, with the same labels. A card is a re-arrangement of the row, never a reduced version of it —
 * asserted at 767 against 1280 by `responsive-stranding.critical.spec.ts`.
 */
export function StackedRows<TRow>({
  caption,
  columns,
  rows,
  cells,
  rowKey,
  cellClassNames,
  emptyMessage,
}: StackedRowsProps<TRow>) {
  const isNarrow = useIsNarrowViewport();

  if (rows.length === 0 && emptyMessage !== undefined) {
    return <p className="text-sm text-brand-gray-muted">{emptyMessage}</p>;
  }

  const labels = columns.map((column) => (typeof column === 'string' ? column : column.label));

  if (!isNarrow) {
    // >= md: the table, unchanged, sortable headers and all. Nothing about the desktop presentation
    // moves. That mattered originally because 1280x800 screenshot baselines would have caught it;
    // #574 deleted those, so the guarantee now rests on this branch being the only one in the DOM
    // (see useIsNarrowViewport) and on the desktop unit tests.
    return (
      <Table caption={caption} columns={columns} emptyMessage={emptyMessage}>
        {rows.map((row, index) => (
          <tr key={rowKey(row, index)} className="border-b border-brand-taupe/40">
            {cells(row).map((cell, cellIndex) => {
              const extra = cellClassNames?.[cellIndex];
              return (
                <td
                  // The INDEX, not the label. `columns` is a fixed-order, fixed-length list rendered
                  // straight through, so the index is unique and stable by construction; a label is
                  // neither — two columns may legitimately share one.
                  key={cellIndex}
                  className={extra ? `px-3 py-2 ${extra}` : 'px-3 py-2'}
                >
                  {cell}
                </td>
              );
            })}
          </tr>
        ))}
      </Table>
    );
  }

  // < md: one card per row. `dl` because these ARE label-value pairs and a screen reader announces
  // them as such; a stack of divs would lose that. The list is named so it is identifiable as the same
  // dataset the table shows at wider widths.
  return (
    <ul aria-label={caption} className="flex list-none flex-col gap-3">
      {rows.map((row, index) => {
        const values = cells(row);
        return (
          <li
            key={rowKey(row, index)}
            className="rounded-lg border border-brand-taupe/40 bg-white p-3"
          >
            <dl className="flex flex-col gap-1 text-sm">
              {labels.map((label, labelIndex) => (
                // Keyed on the index for the same reason as the `<td>` above: duplicate labels are
                // legal, and a collision here would drop a label-value pair, silently losing a value.
                <div key={labelIndex} className="flex flex-wrap gap-x-2">
                  <dt className="font-semibold text-brand-gray">{label}</dt>
                  <dd
                    className={
                      cellClassNames?.[labelIndex]
                        ? `text-brand-text ${cellClassNames[labelIndex]}`
                        : 'text-brand-text'
                    }
                  >
                    {values[labelIndex]}
                  </dd>
                </div>
              ))}
            </dl>
          </li>
        );
      })}
    </ul>
  );
}

/* ------------------------------------------------------------------------------- Modal */

interface ModalProps {
  /** The dialog's name. Also its accessible name via `aria-labelledby`. */
  title: string;
  /** Called on the close affordance, the Escape key, or a click on the backdrop. */
  onClose: () => void;
  children: ReactNode;
  /** Actions for the dialog — usually Cancel and a primary Save, in that order (mockup screen 6). */
  footer?: ReactNode;
}

/**
 * The controls inside the dialog that a Tab can land on, in document order.
 *
 * **Deliberately not filtered by visibility.** jsdom performs no layout, so every geometric test for
 * "is this on screen" (`offsetParent`, `getClientRects`, `getComputedStyle().display`) reports every
 * element as hidden — a trap written that way traps nothing in the one environment that tests it, and
 * would pass its tests by never engaging. `disabled`, `tabindex="-1"` and `type="hidden"` are the
 * exclusions that actually occur — a hidden input is in the DOM, is not `disabled`, and `focus()` on
 * it is a silent no-op, so leaving it in would make a wrap onto it a dead keypress.
 *
 * **This list does NOT reproduce the browser's own tab order, and nothing here assumes it does.**
 * Three measured divergences (2026-08-28, all three engines): a radio group is ONE stop and it is the
 * CHECKED member, not each radio in document order; WebKit leaves plain `<a href>` out of the
 * sequence entirely unless the OS-level Full Keyboard Access preference is on (issue #416); and
 * WebKit leaves radios out of it too. A positive `tabindex` would reorder the sequence as well. That
 * is why the trap below has a `focusin` net and does not rest on `first`/`last` being the browser's
 * real ends — this list only has to name somewhere sensible to PUT focus, never to predict where the
 * browser would have taken it.
 */
const FOCUSABLE_IN_DIALOG = [
  'a[href]',
  'button:not([disabled])',
  'input:not([disabled]):not([type="hidden"])',
  'select:not([disabled])',
  'textarea:not([disabled])',
  '[tabindex]:not([tabindex="-1"])',
].join(', ');

/**
 * The mockups' `.modalwrap`/`.modal` (screen 6, "Add SOW"), as a real dialog.
 *
 * A `div` overlay conveys nothing to assistive technology — this renders `role="dialog"` with
 * `aria-modal="true"`, named by its own heading via `aria-labelledby` (the same id-linking pattern
 * {@link Card} already uses), so a screen reader announces it as a dialog rather than as more page
 * content.
 *
 * **Focus is trapped inside it, and handed back on close.** `aria-modal="true"` *tells* assistive
 * technology that the rest of the page is inert; it enforces nothing, and the browser's Tab order is
 * unchanged by it. Both halves were missing until an adversarial review of #448 found them: Tab walked
 * out into the controls this dialog is supposed to be covering, and closing left focus on
 * `document.body` — returning a keyboard user to the top of the document instead of to the control
 * they opened the dialog from. See the two effects below for how each is done and why there.
 *
 * **Escape closes it.** The mockup's only affordance is the header's `✕`; a keyboard user with no mouse
 * needs a second way out, and Escape is the WAI-ARIA dialog pattern's documented one.
 *
 * **Portaled to `document.body`.** Every other Compass screen nests this component several levels deep
 * inside a page's layout; without a portal the overlay would inherit whatever `overflow`/`position`
 * ancestors impose, which is exactly the clipping a full-screen overlay must not risk.
 *
 * **No scroll container, and here is the margin that decision rests on.** The overlay centres the
 * dialog inside its own padding, so a body taller than the viewport is clipped at BOTH edges with no
 * way to reach either. Driven to its tallest realistic state — a SOW Extension (which adds the Rate
 * Increase group), a note wrapped over three lines, a field-level date-order error and a real server
 * overlap alert, all at once — `SowForm` measures **584px**.
 *
 * **The padding does not add to that threshold.** With `items-center` inside `p-4` the item's top is
 * `16 + (viewport − 32 − height) / 2`, so the 32px cancels and it clips exactly when the viewport is
 * shorter than the dialog: **584px**, swept and confirmed (clean at 585, clipped at 583). An earlier
 * version of this comment said 616 by adding the padding, which is the arithmetic rather than the
 * measurement.
 *
 * **Quote 664, not 844.** The NFR catalog's shortest viewport is 390x844, but this repository's own
 * `compass-iphone-critical` project runs the `iPhone 14` descriptor, which is **390x664** — the
 * narrowest margin any project actually exercises, and it is **80px**. That is why nothing is added
 * here, and it is thin enough that a caller with UNBOUNDED content is where it stops holding. Note
 * also that `responsive-stranding.critical.spec.ts` never opens a dialog, so no gate watches this.
 *
 * Whatever gets added then needs a focus target and an accessible name, or axe reports
 * `scrollable-region-focusable` (`serious`) — {@link ScrollableRegion} exists for precisely that, and
 * is the defect feature 011 closed rather than one to reintroduce.
 */
export function Modal({ title, onClose, children, footer }: ModalProps) {
  const titleId = useId();
  /**
   * The dialog element, held in STATE rather than in a `useRef`, so that "not mounted yet" is a
   * branch the tests actually execute.
   *
   * With a ref it is unreachable: the effect below runs after React has attached the ref, so an
   * `if (dialog === null) return;` guard would be a statement no test can reach, and
   * `scripts/check-coverage.sh` holds a changed `web/compass` file to 100% STATEMENT coverage with
   * zero exclusions. Writing every read as `dialog?.` passes that gate but only by trading the
   * statement for two permanently-dead branch arms. A state setter used as the ref callback costs
   * one extra render and makes the guard real — it runs on the first render, in every test here.
   */
  const [dialog, setDialog] = useState<HTMLDivElement | null>(null);

  /**
   * Focus in on open, trapped while open, and back where it came from on close.
   *
   * **Keyed on `dialog` only, and NOT folded into the Escape effect below.** `onClose` is a fresh
   * closure on every render of the one live caller (`SowForm` passes an inline arrow), so that effect
   * re-runs constantly; capturing the opener there would recapture the DIALOG as its own opener on
   * the next render and re-steal focus on every render after. Nothing here needs `onClose`.
   *
   * **The dialog itself takes focus, not its first control.** The dialog's accessible name is then
   * what a screen reader announces on open, which is the point of having named it — and focusing the
   * ✕ instead arms "close without saving" under the very next space bar.
   *
   * **No `isConnected` check on the way back out.** `focus()` on a disconnected node is a no-op, so
   * a screen that replaced the opener while the dialog was up needs no special case: focus falls to
   * `body`, which is what a browser does anyway when the element holding it is removed.
   *
   * **In Safari, a MOUSE click does not focus the button it hits**, so there the captured opener is
   * `body` and there is nothing to hand focus back to. Verified in WebKit, where the same dialog
   * opened from the KEYBOARD restores to the opener correctly (2026-08-28). That is Safari behaving
   * as designed rather than this failing, and the keyboard path is the one this exists for — but it
   * does mean a WebKit run that opens the dialog by clicking will show focus on `body` afterwards.
   */
  useEffect(() => {
    if (dialog === null) {
      return;
    }

    // Captured after the guard so the handlers below need no null-narrowing of their own: they are
    // hoisted `function` declarations, and TypeScript will not carry a narrowing into one.
    const node = dialog;
    const opener = document.activeElement;
    node.focus();

    /** Which way the last Tab went, so an escape is put back at the end it left from. */
    let shiftHeld = false;

    function controls(): HTMLElement[] {
      // Re-queried per keypress rather than captured once: `SowForm`'s Rate Increase radios mount
      // and unmount as the contract type changes, so the ring is not fixed for the dialog's life.
      return [...node.querySelectorAll<HTMLElement>(FOCUSABLE_IN_DIALOG)];
    }

    function focusRingEnd(ring: HTMLElement[]): void {
      // Never empty, so never `undefined`: the header's ✕ is unconditional, 40 lines below. Make it
      // conditional and this needs a fallback to `node` itself.
      ring[shiftHeld ? ring.length - 1 : 0].focus();
    }

    function handleTab(event: KeyboardEvent) {
      if (event.key !== 'Tab') {
        return;
      }

      shiftHeld = event.shiftKey;
      const ring = controls();
      const index = ring.findIndex((control) => control === document.activeElement);
      const edge = event.shiftKey ? 0 : ring.length - 1;

      // Anywhere in the middle of the ring is left to the browser, whose order is subtler than this
      // flat list — a radio group is one stop rather than one per radio. That delegation is a
      // COURTESY, not the trap: what makes the trap sound is `handleFocusIn` below, which catches
      // every case this arithmetic gets wrong. `index === -1` is both "focus is on the dialog
      // container, because it has just opened" and "focus is no longer in the dialog at all" — the
      // second reachable without touching an edge, because a browser drops focus to `body` when the
      // element holding it is removed, which is what unmounting those radios does.
      if (index !== -1 && index !== edge) {
        return;
      }

      // `preventDefault` is what makes this a trap rather than a suggestion, and is also why it is
      // assertable in jsdom: `userEvent.tab()` honours it.
      event.preventDefault();
      focusRingEnd(ring);
    }

    /**
     * The net, and the reason the arithmetic above is allowed to be approximate.
     *
     * `aria-modal="true"` promises the rest of the page is inert, and this is the only line that
     * makes it true for every input shape rather than for the ones a flat selector predicts. It was
     * added after a review found the shape that defeats edge-detection alone: a dialog whose last
     * control is a radio group. Measured in all three engines (2026-08-28) — Chromium tabs
     * `✕ → checked radio → OUT OF THE DIALOG`, and WebKit skips the radios entirely and leaves on
     * the first Tab. `SowForm` is bracketed by its ✕ and its footer so it never hit this, but
     * `footer` is optional and `SowForm` has two radio groups, so it was one prop away.
     *
     * `focusin` fires on the escaped element in all three engines, which is what makes this
     * workable. It costs a transient focus on the control outside before it is pulled back — the
     * same trade every focus-trap implementation makes — which is why `handleTab` still handles the
     * common case directly instead of leaving everything to this.
     *
     * **ONE dialog at a time, and the failure is not the one you would guess.** Two of these open
     * together each read the other's content as "outside" and volley focus. Measured (2026-08-28):
     * it TERMINATES rather than hanging — 43 to 1137 focus transitions, then it settles on one
     * dialog's ✕, with uncaught script errors in WebKit, a stack overflow in jsdom, and the engines
     * disagreeing about which dialog wins. Worth stating precisely, because "it hangs" implies you
     * cannot miss it, whereas "thrashes briefly, then looks fine" is the shape under which someone
     * actually ships a second dialog. The one-at-a-time assumption is not new — the Escape handler
     * below would close both, and the backdrop is a single full-screen layer — and Compass has
     * exactly one caller. A second concurrent dialog needs this component's focus management
     * redesigned, not a guard bolted onto this line.
     *
     * **It overrides any outside focus owner, not only an escaped Tab.** The override reproduces in
     * all three engines at 390px: with the collapsed nav menu open, its own Escape handler focuses
     * the Menu button — legitimately, it is that menu's disclosure control — and this net takes focus
     * straight back. Three focus moves for one keypress in Chromium and Firefox, ending on the
     * opener; two in WebKit, ending on `body` for the mouse-focus reason below. Either way the end
     * state is only right because the dialog unmounts on that same Escape.
     *
     * **What this does NOT catch, measured rather than assumed: focus parking on `body` in Safari.**
     * `focusin` fires only when something GAINS focus, and `body` does not, so a Tab that runs off
     * the end of WebKit's own order leaves focus on `body` for one keypress; `handleTab`'s
     * `index === -1` branch then pulls it back on the next one. Walking the real SOW dialog's ring
     * 16 times per direction (2026-08-28): Chromium and Firefox never leave, WebKit parks on `body`
     * once per forward cycle, and **no engine ever reached a control on the page behind** — which is
     * the defect this exists to prevent. The cause is Safari's own Tab model, where buttons are not
     * Tab stops at all unless Full Keyboard Access is on, so the ✕/Cancel/Save are unreachable by
     * Tab there with or without this dialog.
     *
     * **`focusout` would close that gap and is not adopted.** It fires with `relatedTarget: null` on
     * the way to `body` in all three engines, so it sees what `focusin` cannot. It also fires during
     * React's teardown — but only in Chromium, and with the dialog still connected, so the
     * `isConnected` guard an earlier version of this comment claimed it needs would never take its
     * early return. That argument was wrong and is not the reason. The reason is narrower: the net
     * above already prevents every escape to a real CONTROL, the residual is one keypress on `body`
     * that `handleTab` reclaims, and a third document listener that fires while the dialog is being
     * removed is more teardown surface than that residual is worth.
     */
    function handleFocusIn(event: FocusEvent) {
      if (node.contains(event.target as Node)) {
        return;
      }

      focusRingEnd(controls());
    }

    // On `document` rather than on the dialog: a listener bound to the dialog cannot fire once focus
    // has left it, which is the one case each of these exists for.
    document.addEventListener('keydown', handleTab);
    document.addEventListener('focusin', handleFocusIn);

    return () => {
      // Removed BEFORE the opener is refocused, so the hand-back cannot be read as an escape. By
      // this point the dialog is already detached, so a net firing here would no-op rather than
      // fight — the ordering is defence, not a fix for something observed. What actually holds the
      // removal is the listener-hygiene test: a leaked handler is invisible to every behavioural
      // test, because it holds the detached dialog and its pull-back is silent.
      document.removeEventListener('keydown', handleTab);
      document.removeEventListener('focusin', handleFocusIn);
      if (opener instanceof HTMLElement) {
        opener.focus();
      }
    };
  }, [dialog]);

  useEffect(() => {
    function handleKeyDown(event: KeyboardEvent) {
      if (event.key === 'Escape') {
        onClose();
      }
    }

    document.addEventListener('keydown', handleKeyDown);
    return () => document.removeEventListener('keydown', handleKeyDown);
  }, [onClose]);

  return createPortal(
    <div
      className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 p-4"
      onClick={onClose}
    >
      <div
        ref={setDialog}
        role="dialog"
        aria-modal="true"
        aria-labelledby={titleId}
        // Programmatically focusable, and no more: `-1` keeps the container out of the Tab ring it
        // is the wrapper for, while letting the effect above move focus onto it when the dialog
        // opens. A `div` cannot be focused at all without it.
        tabIndex={-1}
        onClick={(event) => event.stopPropagation()}
        // React portals bubble through the React tree, not the DOM tree, so a form inside this
        // dialog would otherwise also submit the form that rendered the modal.
        onSubmit={(event) => event.stopPropagation()}
        className="w-full max-w-lg rounded-lg bg-white shadow-lg"
      >
        <div className="flex items-center justify-between gap-3 border-b border-brand-taupe/40 px-5 py-3">
          <h2 id={titleId} className="text-lg font-semibold tracking-tight">
            {title}
          </h2>
          <button
            type="button"
            onClick={onClose}
            aria-label="Close"
            className="rounded text-brand-gray hover:text-brand-text"
          >
            {/* aria-hidden: the accessible name comes from aria-label above, not from this glyph. */}
            <span aria-hidden="true">✕</span>
          </button>
        </div>

        <div className="p-5">{children}</div>

        {footer !== undefined && (
          <div className="flex justify-end gap-2 border-t border-brand-taupe/40 px-5 py-3">
            {footer}
          </div>
        )}
      </div>
    </div>,
    document.body,
  );
}
