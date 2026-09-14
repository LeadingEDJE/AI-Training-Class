import { useCallback, useEffect, useId, useRef, useState, type ReactNode } from 'react';
import { useCurrentUser } from '../hooks/useCurrentUser';
import { useIsNarrowViewport } from '../hooks/useIsNarrowViewport';
import {
  getCompassPrivileges,
  getVisibleCompassNavKeys,
  type CompassNavKey,
} from '../lib/compass-nav-permissions';
import { getInitials, isNavLinkActive } from '../lib/nav-identity';

interface CompassNavLink {
  key: CompassNavKey;
  label: string;
  to: string;
}

/**
 * Every Compass nav destination.
 *
 * These are forward-looking paths: Team Directory, Client Directory, Sales Dashboard and Reports are
 * later streams and currently resolve to the not-found route. That is deliberate and honest — the
 * requirement this component satisfies is which links RENDER for which role, not whether each
 * destination is built.
 *
 * **`admin-config` is the exception, and it is real.** It reaches the administration shell, whose
 * lookup and EDJEr screens shipped with feature 004.
 *
 * **There is deliberately no `assignments` entry (feature 006, owner direction 2026-08-14).**
 * Assignment management is reached from the EDJEr and Client records (mockup screen 5, AC-2), not from
 * a standalone top-nav destination — see FR-055a. An earlier revision of this component carried an
 * `assignments` link that 404'd because no route was ever registered for it; don't reintroduce it.
 *
 * These stay plain `<a href>` with the `/compass/` prefix, deliberately, even though a router exists
 * now (`src/routes/router.ts`, introduced by feature 004 — this comment used to say Compass had none).
 * A full page load is correct for a shell that sits OUTSIDE the routed outlet: `main.tsx` composes this
 * nav as a sibling of `<Outlet />`, so it is not re-rendered by a route change and has no router
 * context to navigate within. `AdminLayout` uses `<Link to>` with UNPREFIXED paths instead, because the
 * router's `basepath` supplies the prefix — mixing the two conventions yields `/compass/compass/…`.
 */
const allNavLinks: CompassNavLink[] = [
  { key: 'team-directory', label: 'Team Directory', to: '/compass/team-directory' },
  { key: 'client-directory', label: 'Clients', to: '/compass/client-directory' },
  { key: 'sales-dashboard', label: 'Sales Dashboard', to: '/compass/sales-dashboard' },
  { key: 'reports', label: 'Reports', to: '/compass/reports' },
  { key: 'admin-config', label: 'Admin', to: '/compass/admin' },
];

/**
 * One nav link. Extracted from the bar so a second rendering cannot drift on the things that are NOT
 * presentation — the `href`, the `aria-current` computation, and the focus ring.
 */
function NavLink({
  link,
  pathname,
  variant = 'bar',
}: {
  link: CompassNavLink;
  pathname: string;
  /** `panel` is the stacked disclosure rendering below `md`. It differs ONLY in layout. */
  variant?: 'bar' | 'panel';
}) {
  const active = isNavLinkActive(pathname, link.to);

  if (variant === 'panel') {
    return (
      <a
        href={link.to}
        aria-current={active ? 'page' : undefined}
        // WebKit/Safari's default keyboard behaviour does not include plain <a> elements in the Tab
        // cycle (only form controls are Tab-stops by default there, unlike Chromium and Firefox)
        // unless the OS-level "Full Keyboard Access" preference is on — not the default, and not set
        // for Playwright's bundled WebKit. An explicit tabIndex forces the link into the Tab order
        // regardless of that preference. Found via compass-accessibility.critical.spec.ts failing
        // only under --project=webkit-critical (issue #416).
        tabIndex={0}
        // `block`, and tall enough to be a real touch target: a link only as wide as its text is the
        // shape that makes a phone menu feel broken. The active marker is a LEFT border rather than
        // the bar's inset underline, because a stacked list has no shared baseline to underline.
        className={`block rounded px-3 py-3 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-white ${
          active
            ? 'border-l-4 border-brand-green bg-white/10 font-semibold text-white'
            : 'text-white/80 hover:bg-white/10 hover:text-white'
        }`}
      >
        {link.label}
      </a>
    );
  }

  return (
    <a
      href={link.to}
      aria-current={active ? 'page' : undefined}
      // See the `panel` branch above for why this is here (issue #416).
      tabIndex={0}
      className={`rounded px-2 py-1 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-white ${
        active
          ? 'font-semibold text-white shadow-[inset_0_-3px_0_var(--color-brand-green)]'
          : 'text-white/80 hover:bg-white/10 hover:text-white'
      }`}
    >
      {link.label}
    </a>
  );
}

/**
 * The mockup's `.logo`: `leading` at the body weight, `EDJE` bold in brand green, then the `Compass`
 * app label behind a divider. Font-weight 300 in the mockup isn't a loaded Manrope weight (400 is the
 * lightest imported), so `font-normal` is the nearest available step.
 *
 * THE SIZE IS 19px BECAUSE THE MOCKUP SAYS 19px, AND IT IS ALSO WHAT MAKES THE WORDMARK ACCESSIBLE —
 * do not "round" it to Tailwind's `text-lg`.
 *
 * Brand green on the brand-gray chrome is 4.33:1. That clears WCAG AA's 3:1 for LARGE text but misses
 * the 4.5:1 for normal text, and axe-core draws the line at 14pt bold — 18.66px. `.logo{font-size:19px}`
 * with `.logo b{font-weight:800}` is 14.25pt bold, so the green wordmark is large text and the pairing
 * passes. `text-lg` is 18px = 13.5pt, a third of a pixel under the threshold, which flips the same
 * colours to a serious axe violation.
 *
 * That is exactly what shipped: the size was rounded to `text-lg` and
 * `compass-accessibility.critical.spec.ts` failed on `color-contrast` with fgColor #98c93d / bgColor
 * #4c4d4f. The fidelity slip and the accessibility failure were one bug, and honouring the design
 * source fixed both. `CompassNav.test.tsx` pins the size for that reason.
 *
 * **Only the `leadingEDJE` portion links back to LEAP (issue #579, owner direction 2026-09-08).**
 * `Compass` stays plain text, deliberately — the owner asked for just the leadingEDJE wordmark to be
 * clickable, not the whole logo lockup. The link is a plain `<a href="/">`, not role-gated and not a
 * router `<Link>`: `CompassNav` renders full-page-load `<a href>`s throughout (see the module doc
 * comment above `allNavLinks`), and `/` is the LEAP shell root, outside Compass's own router basepath.
 * "Keep the current look" (owner direction) means no underline and no colour change — only a pointer
 * cursor on hover, since the wordmark otherwise gives no visual affordance that it is clickable.
 *
 * `tabIndex={0}` for the same reason it is on every {@link NavLink}: WebKit's default keyboard
 * behaviour does not include plain `<a>` elements in the Tab cycle, so a link without it is
 * unreachable by keyboard there (issue #416) — pinned by
 * `web/compass/tests/e2e/nav-keyboard.critical.spec.ts`, which walks every rendered nav link in order.
 */
function NavWordmark() {
  return (
    <span className="flex items-center whitespace-nowrap">
      <a
        href="/"
        tabIndex={0}
        className="flex cursor-pointer items-center text-[19px] font-normal tracking-wide"
      >
        leading
        <span className="font-extrabold text-brand-green">EDJE</span>
      </a>
      <span className="ml-2.5 border-l border-white/30 pl-3.5 text-[19px] font-semibold">
        Compass
      </span>
    </span>
  );
}

/**
 * The signed-in user's initials.
 *
 * This is a `role="img"`, so a blank name is an axe `role-img-alt` failure (serious, WCAG 1.1.1)
 * rather than a merely empty circle — the caller is responsible for never passing one.
 */
function NavAvatar({ label }: { label: string }) {
  return (
    <span
      role="img"
      aria-label={label}
      className="flex h-8 w-8 shrink-0 items-center justify-center rounded-full bg-brand-green text-sm font-bold text-brand-ink"
    >
      {getInitials(label)}
    </span>
  );
}

/**
 * The nav below `md`: wordmark and userchip in the bar, links behind a **Menu** disclosure.
 *
 * **Why this is its own component and not a branch inside {@link CompassNav}.** Its open state and its
 * document-level Escape listener are scoped to a viewport that HAS a panel. As a branch, both outlived
 * the branch — `isOpen` survived a widen/narrow round trip, so the panel came back already open with
 * `aria-expanded="true"` on a control nobody pressed, and the Escape effect (which guarded only on
 * `isOpen`) kept a document listener registered at desktop width for a panel that was not on the page,
 * whose `close()` focused a null ref. Mounting only while narrow makes both impossible rather than
 * guarded against. Both are pinned by tests.
 *
 * **Not a focus trap.** The links are plain `<a href>` full page loads sitting after the button in DOM
 * order, so Tab reaches them (via the explicit `tabIndex={0}` on {@link NavLink} — WebKit does NOT
 * Tab to plain links by default, see issue #416) and any click ends the page anyway. Escape closes
 * and *returns focus to the button* — without that, dismissing drops focus to `<body>` and a keyboard
 * user restarts the tab order at the top of the document.
 */
function CollapsedNav({
  visibleLinks,
  pathname,
  avatar,
}: {
  visibleLinks: CompassNavLink[];
  pathname: string;
  avatar: ReactNode;
}) {
  const [isOpen, setIsOpen] = useState(false);
  // Generated, not a literal: `aria-controls` must resolve to a real element, and a second nav on the
  // page would collide on a hard-coded id.
  const panelId = useId();
  const buttonRef = useRef<HTMLButtonElement>(null);

  const close = useCallback(() => {
    setIsOpen(false);
    buttonRef.current?.focus();
  }, []);

  useEffect(() => {
    if (!isOpen) {
      return;
    }
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        close();
      }
    };
    // Bound on `document`, not the panel: Escape has to work wherever focus happens to be — the
    // button, a link inside the panel, or nothing in particular.
    document.addEventListener('keydown', onKeyDown);
    return () => document.removeEventListener('keydown', onKeyDown);
  }, [isOpen, close]);

  return (
    <nav aria-label="Compass navigation" className="bg-brand-gray text-sm font-medium text-white">
      <div className="flex items-center gap-4 px-6 py-3">
        <NavWordmark />

        <div className="ml-auto flex items-center gap-3">
          {avatar}

          <button
            ref={buttonRef}
            type="button"
            aria-expanded={isOpen}
            aria-controls={panelId}
            onClick={() => setIsOpen((open) => !open)}
            // The accessible name is the WORD "Menu" in an `sr-only` span; the three bars are
            // `aria-hidden` decoration. An icon-only control naming itself from an unlabelled span is
            // an axe `button-name` failure, and "☰" read aloud is not a label.
            //
            // 44x44 (h-11 w-11) is deliberate but is NOT an AA requirement — WCAG 2.1's target-size
            // criterion (2.5.5) is AAA, and 2.2's AA one (2.5.8) asks only 24x24. 44 is the Apple HIG
            // figure and clears both, which is the right call for the single control a phone user
            // needs to reach every other screen.
            className="-mr-2 flex h-11 w-11 shrink-0 cursor-pointer items-center justify-center rounded focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-white"
          >
            <span className="sr-only">Menu</span>
            <span aria-hidden="true" className="flex w-5 flex-col gap-[3px]">
              <span className="h-[2px] rounded-full bg-current" />
              <span className="h-[2px] rounded-full bg-current" />
              <span className="h-[2px] rounded-full bg-current" />
            </span>
          </button>
        </div>
      </div>

      {isOpen && (
        <div id={panelId} className="flex flex-col gap-1 border-t border-white/15 px-4 py-2">
          {visibleLinks.map((link) => (
            <NavLink key={link.key} link={link} pathname={pathname} variant="panel" />
          ))}
        </div>
      )}
    </nav>
  );
}

/**
 * The Compass role-gated navigation shell (AC-4, BR-15). Renders the `<nav>` landmark
 * unconditionally — even with zero visible links (unauthenticated) — for a consistent layout and
 * accessibility tree; only its contents change per role. Offers no link to Timesheet or OOTO by
 * construction: every entry above is a Compass-only destination.
 *
 * <h3>Below `md` the links collapse behind a disclosure button (owner request, 2026-08-25)</h3>
 *
 * **A recorded DEPARTURE, not a fidelity fix.** `docs/design/edje-compass-mockups.html` is silent on
 * mobile navigation: its only media query is `@media(max-width:900px)` and it touches `.tiles`,
 * `.apps`, `.formgrid` and `.two-col` — never `.topbar` or `.nav`. That file's own provenance banner
 * grants *"design liberty ... on layout and interaction"*, so this is a permitted choice that has to be
 * written down rather than a correction of something the source specified (Principle X rule 3). What it
 * replaces: `flex-wrap` at 390px put five links onto three lines and pushed page content down by
 * roughly a third of the viewport.
 *
 * **The breakpoint is `md`/768 via {@link useIsNarrowViewport}, not the mockup's 900** — 768 is the
 * project's one layout breakpoint (`FormGrid`'s docstring records why) and 900 is not a token. Going
 * through the hook also means exactly ONE branch is ever in the DOM, which is what kept all 25
 * pre-existing `CompassNav` tests untouched. It also kept the 1280x800 screenshot baselines
 * untouched, but #574 deleted those on 2026-09-08 — the unit tests are the whole of it now.
 *
 * **Three decisions worth not re-litigating:**
 *
 * 1. **The wordmark and the userchip stay in the bar.** Identity should not need a tap, and the
 *    wordmark carries the 19px large-text contrast constraint documented on {@link NavWordmark} —
 *    moving it behind a toggle would put that pairing somewhere no test is looking.
 * 2. **Zero permitted links renders NO button.** A control that opens an empty panel advertises
 *    navigation that does not exist. The `<nav>` landmark still renders unconditionally, as ever.
 * 3. **The panel is not a focus trap.** Its links are plain `<a href>` full page loads sitting after
 *    the button in DOM order, so Tab reaches them (via the explicit `tabIndex={0}` on {@link NavLink}
 *    — WebKit does NOT Tab to plain links by default, see issue #416) and any click ends the page
 *    anyway. Escape closes and *returns focus to the button* — without that, dismissing the panel
 *    drops focus to `<body>` and a keyboard user restarts the tab order from the top of the document.
 */
export function CompassNav() {
  const { data: user } = useCurrentUser();
  const isNarrow = useIsNarrowViewport();
  const compassPrivileges = getCompassPrivileges(user?.privileges);
  const visibleKeys = getVisibleCompassNavKeys(user != null, compassPrivileges);
  const visibleLinks = allNavLinks.filter((link) => visibleKeys.includes(link.key));
  const pathname = window.location.pathname;
  // `/api/me` returns `displayName: ""` whenever the DisplayName claim is absent — which is precisely what DevBypassMiddleware produces — so the email
  // is the fallback. The real sign-in path already guards this upstream (SignInService.BuildIdentity),
  // but a shared chrome component must not be the only thing standing between an unset claim and an
  // unlabelled image.
  const avatarLabel = user ? user.displayName.trim() || user.email : '';
  const avatar = user ? <NavAvatar label={avatarLabel} /> : null;

  // Narrow AND something to collapse: a button that opens an empty panel advertises navigation that
  // does not exist.
  //
  // A SEPARATE COMPONENT, not another branch in this one, and that is the fix for a real bug rather
  // than tidiness. The disclosure's open state and its document-level Escape listener live inside
  // `CollapsedNav`, so widening past `md` UNMOUNTS them: the panel cannot come back already open after
  // a rotation, and no keydown handler survives into a width that has no panel. Keeping the state here
  // meant it outlived the branch that used it — see `CompassNav.test.tsx`'s two round-trip cases.
  if (isNarrow && visibleLinks.length > 0) {
    return <CollapsedNav visibleLinks={visibleLinks} pathname={pathname} avatar={avatar} />;
  }

  return (
    <nav
      aria-label="Compass navigation"
      className="flex flex-wrap items-center gap-4 bg-brand-gray px-6 py-3 text-sm font-medium text-white"
    >
      <NavWordmark />

      {visibleLinks.map((link) => (
        <NavLink key={link.key} link={link} pathname={pathname} />
      ))}

      {avatar && <div className="ml-auto flex items-center gap-2.5">{avatar}</div>}
    </nav>
  );
}
