import { act, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import type { CompassNavKey } from '../../../src/lib/compass-nav-permissions';
import type { CurrentUser } from '../../../src/hooks/useCurrentUser';

vi.mock('../../../src/lib/compass-nav-permissions', () => ({
  getVisibleCompassNavKeys: vi.fn(),
  getCompassPrivileges: vi.fn(() => []),
}));

vi.mock('../../../src/hooks/useCurrentUser', () => ({
  useCurrentUser: vi.fn(() => ({ data: null })),
  currentUserQueryKey: ['current-user'],
}));

import {
  getVisibleCompassNavKeys,
  getCompassPrivileges,
} from '../../../src/lib/compass-nav-permissions';
import { useCurrentUser } from '../../../src/hooks/useCurrentUser';
import { CompassNav } from '../../../src/components/CompassNav';
import { checkResponsiveOverflow } from '../../../src/lib/responsive-check';

const mockGetVisibleCompassNavKeys = vi.mocked(getVisibleCompassNavKeys);
const mockGetCompassPrivileges = vi.mocked(getCompassPrivileges);
const mockUseCurrentUser = vi.mocked(useCurrentUser);

const ALL_KEYS: CompassNavKey[] = [
  'team-directory',
  'client-directory',
  'sales-dashboard',
  'reports',
  'admin-config',
];

/**
 * Installs a controllable `matchMedia`, mirroring `tests/unit/hooks/useIsNarrowViewport.test.ts`.
 *
 * jsdom ships no `matchMedia`, so `useIsNarrowViewport` returns its DESKTOP default and every test
 * above this line keeps asserting the link bar without being touched. Only the tests that opt in by
 * calling this see the hamburger — which is the same property that let `StackedRows` ship without
 * rewriting six report specs.
 */
function stubNarrowViewport(isNarrow: boolean) {
  const listeners = new Set<(event: MediaQueryListEvent) => void>();
  let matches = isNarrow;

  const query = {
    get matches() {
      return matches;
    },
    addEventListener: (_: 'change', listener: (event: MediaQueryListEvent) => void) => {
      listeners.add(listener);
    },
    removeEventListener: (_: 'change', listener: (event: MediaQueryListEvent) => void) => {
      listeners.delete(listener);
    },
  };

  vi.stubGlobal(
    'matchMedia',
    vi.fn(() => query),
  );

  /** Fires the change listeners, so a test can rotate the device mid-render like a real one does. */
  return {
    set(next: boolean) {
      matches = next;
      for (const listener of listeners) {
        listener({ matches: next } as MediaQueryListEvent);
      }
    },
  };
}

function renderNav() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={queryClient}>
      <CompassNav />
    </QueryClientProvider>,
  );
}

function stubSignedInUser(overrides: Partial<CurrentUser> = {}) {
  mockUseCurrentUser.mockReturnValue({
    data: {
      edjeId: 'e1',
      email: 'sam.park@example.test',
      displayName: 'Sam Park',
      privileges: ['Compass Super Admin'],
      impersonator: null,
      ...overrides,
    },
  } as ReturnType<typeof useCurrentUser>);
}

describe('CompassNav', () => {
  beforeEach(() => {
    // Default: Compass Super Admin sees every area.
    mockGetVisibleCompassNavKeys.mockReturnValue(ALL_KEYS);
    mockGetCompassPrivileges.mockReturnValue([]);
    mockUseCurrentUser.mockReturnValue({ data: null } as ReturnType<typeof useCurrentUser>);
  });

  it('renders the Compass navigation landmark', () => {
    renderNav();
    expect(screen.getByRole('navigation', { name: /compass navigation/i })).toBeInTheDocument();
  });

  it('renders all five links for Compass Super Admin', () => {
    renderNav();
    expect(screen.getByRole('link', { name: /team directory/i })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /clients/i })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /sales dashboard/i })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /reports/i })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /admin/i })).toBeInTheDocument();
  });

  it('never renders an Assignments link, for any role (feature 006, owner direction 2026-08-14)', () => {
    // Assignment management is reached from the EDJEr/Client record, not a standalone nav
    // destination — see CompassNav.tsx's doc comment and spec 006's FR-055a.
    renderNav();

    expect(screen.queryByRole('link', { name: /assignments/i })).not.toBeInTheDocument();
  });

  it('renders only the two baseline directories for a user with no elevated role', () => {
    mockGetVisibleCompassNavKeys.mockReturnValue(['team-directory', 'client-directory']);
    renderNav();

    expect(screen.getByRole('link', { name: /team directory/i })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /clients/i })).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: /sales dashboard/i })).not.toBeInTheDocument();
    expect(screen.queryByRole('link', { name: /reports/i })).not.toBeInTheDocument();
    expect(screen.queryByRole('link', { name: /admin/i })).not.toBeInTheDocument();
  });

  it('renders EXACTLY baseline for Compass Admin — read-only despite the name, never elevated nav', () => {
    // Compass Admin's own gating logic (compass-nav-permissions.ts) never adds anything beyond
    // baseline for the Admin role — this test proves the component renders whatever the
    // permissions function returns, so the trap lives in the (separately unit-tested) function,
    // not the component. Reproduced here as the same baseline-only set to keep it visible at the
    // component level too, since a future refactor could accidentally special-case "Admin" here.
    mockGetVisibleCompassNavKeys.mockReturnValue(['team-directory', 'client-directory']);
    renderNav();

    // +1 for the persistent leadingEDJE home link (issue #579), which is not role-gated.
    expect(screen.getAllByRole('link')).toHaveLength(3);
  });

  it('renders the Ops set: baseline + sales-dashboard + reports, no admin', () => {
    mockGetVisibleCompassNavKeys.mockReturnValue([
      'team-directory',
      'client-directory',
      'sales-dashboard',
      'reports',
    ]);
    renderNav();

    expect(screen.getByRole('link', { name: /sales dashboard/i })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /reports/i })).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: /admin/i })).not.toBeInTheDocument();
  });

  it('renders the nav landmark with zero role-gated links when unauthenticated, but keeps the leadingEDJE home link', () => {
    mockGetVisibleCompassNavKeys.mockReturnValue([]);
    renderNav();

    expect(screen.getByRole('navigation', { name: /compass navigation/i })).toBeInTheDocument();
    // The leadingEDJE wordmark always links back to LEAP (issue #579) — it is not role-gated, so
    // it survives even when every permissioned nav destination is hidden.
    expect(screen.getAllByRole('link')).toHaveLength(1);
    expect(screen.getByRole('link', { name: 'leadingEDJE' })).toHaveAttribute('href', '/');
  });

  it('uses the mockup dark topbar chrome — bg-brand-gray with white text, not the pre-brand slate palette', () => {
    // `docs/design/edje-compass-mockups.html`'s `.topbar` is `background: var(--le-gray)` (#4C4D4F)
    // with white/light text. The nav previously shipped a plain white bar instead — a gap the
    // design-fidelity checklist (008) never scoped, since it only checked nav LABELS. White on
    // brand-gray measures 8.46:1 (`brand-contrast.test.ts`) — the same ratio already measured for
    // brand-gray-on-white, read the other way, since contrast is symmetric. No new token needed.
    renderNav();

    const nav = screen.getByRole('navigation', { name: /compass navigation/i });
    expect(nav.className).toMatch(/\bbg-brand-gray\b/);
    expect(nav.className).toMatch(/\btext-white\b/);
    expect(nav.className).not.toMatch(/\bbg-white\b/);
    expect(nav.className).not.toMatch(/\bslate-/);

    const link = screen.getByRole('link', { name: /team directory/i });
    expect(link.className).toMatch(/\btext-white\/80\b/);
    // The browser's default focus ring is ~1.88:1 on this background (fails §1.4.11's 3:1) though it
    // was ~4.51:1 on the white bar this replaces — an explicit white ring keeps it visible.
    expect(link.className).toMatch(/\bfocus-visible:outline-white\b/);
    expect(link.className).not.toMatch(/\bslate-/);
  });

  it('never renders a link to Timesheet or OOTO, for the fullest-access role (BR-15)', () => {
    renderNav();

    const links = screen.getAllByRole('link');
    for (const link of links) {
      expect(link.getAttribute('href')).not.toMatch(/\/timesheet\//i);
      expect(link.getAttribute('href')).not.toMatch(/\/ooto\//i);
    }
    expect(screen.queryByText(/timesheet/i)).not.toBeInTheDocument();
    expect(screen.queryByText(/\bOOTO\b/i)).not.toBeInTheDocument();
  });

  it('renders the base EDJEr navigation for a user whose only Compass group is `-dev`-suffixed (#57, T079)', () => {
    // Environment scoping is enforced entirely server-side, in `GoogleAuthService.MapGroupsToRoles`
    // — proven role-agnostically by
    // `MapGroupsToRoles_OnlyDevGroupAssertedInProduction_ReturnsEmpty_NotSubstringMatched` in
    // `tests/unit/Services/GoogleAuthServiceTests.cs`: a `-dev`-only group resolves to ZERO roles in
    // Production. No `-dev`-suffixed string, nor any role derived from one, ever reaches
    // `GET /api/me`'s `privileges` array — `SignInService` persists only the resolved roles, and
    // `CurrentUserContext.Privileges` reads those back as claims. `CompassNav` has no environment
    // awareness and needs none: it renders from whatever `privileges` it's given, so "a `-dev`-only
    // user in Production" and "a user with no elevated Compass group at all" are, by construction,
    // the exact same input to this component. This test names that scenario explicitly rather than
    // leaving the equivalence implicit.
    mockGetVisibleCompassNavKeys.mockReturnValue(['team-directory', 'client-directory']);
    renderNav();

    expect(screen.getByRole('link', { name: /team directory/i })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /clients/i })).toBeInTheDocument();
    // +1 for the persistent leadingEDJE home link (issue #579), which is not role-gated.
    expect(screen.getAllByRole('link')).toHaveLength(3);
  });

  it('meets the US7 keyboard and touch-operability baseline at 390px width (#57, T081)', () => {
    const { container } = renderNav();

    // Keyboard: every visible link is a real, natively-focusable <a href> — none opted out of
    // the tab order. Real screen-reader/AT keyboard traversal is Stream 6's manual pass; this is
    // the automatable floor.
    for (const link of screen.getAllByRole('link')) {
      expect(link.tagName).toBe('A');
      expect(link.hasAttribute('href')).toBe(true);
      expect(link.tabIndex).not.toBe(-1);
    }

    // Touch/responsive: no element in the nav declares an inline width wider than the narrowest
    // required viewport. Real tap-target-size measurement needs actual layout, which jsdom does
    // not have — see `src/lib/responsive-check.ts`'s documented limitation.
    expect(checkResponsiveOverflow(container, 390)).toEqual([]);
  });

  /**
   * Below `md` the five links collapse behind a disclosure button (owner request, 2026-08-25).
   *
   * **Why a departure and not a fidelity fix.** `docs/design/edje-compass-mockups.html` is SILENT on
   * mobile navigation: its only media query is `@media(max-width:900px)`, and it touches `.tiles`,
   * `.apps`, `.formgrid` and `.two-col` — never `.topbar` or `.nav`. The file's own provenance banner
   * grants "design liberty ... on layout and interaction", so this is a permitted choice that has to
   * be RECORDED rather than a correction of something the source specified (Principle X rule 3).
   *
   * The breakpoint is `md`/768 through `useIsNarrowViewport`, not the mockup's 900 — 768 is the
   * project's one layout breakpoint (`FormGrid`'s docstring) and 900 is not a token. Reusing the hook
   * also means ONE branch is in the DOM at a time, so the desktop rendering is untouched. That used
   * to be underwritten by the 1280x800 screenshot baselines as well; #574 deleted them, so these
   * tests are what hold it.
   */
  describe('below md the links collapse behind a menu button (owner request, 2026-08-25)', () => {
    afterEach(() => {
      vi.unstubAllGlobals();
    });

    it('renders a menu button instead of the bare link bar', () => {
      stubNarrowViewport(true);
      stubSignedInUser();

      renderNav();

      expect(screen.getByRole('button', { name: /menu/i })).toBeInTheDocument();
      // Collapsed by default: the links are not merely visually hidden, they are OUT OF THE DOM, so
      // there is nothing for a screen reader to reach past the button either.
      expect(screen.queryByRole('link', { name: 'Team Directory' })).not.toBeInTheDocument();
    });

    it('keeps the wordmark and the userchip in the bar, not behind the button', () => {
      stubNarrowViewport(true);
      stubSignedInUser();

      renderNav();

      // Identity should not need a tap to see. The wordmark also carries the 19px contrast
      // constraint the topbar-chrome tests above pin, so it must not move into the panel.
      expect(screen.getByText('EDJE')).toBeInTheDocument();
      expect(screen.getByRole('img', { name: 'Sam Park' })).toBeInTheDocument();
    });

    it('reports its collapsed state through aria-expanded, and points at the panel it controls', () => {
      stubNarrowViewport(true);
      stubSignedInUser();

      renderNav();

      const button = screen.getByRole('button', { name: /menu/i });
      expect(button).toHaveAttribute('aria-expanded', 'false');
      // aria-controls must NAME A REAL ELEMENT once open -- a dangling idref is an axe failure and
      // announces a relationship that does not exist.
      expect(button.getAttribute('aria-controls')).toBeTruthy();
    });

    it('reveals every permitted link when opened, and flips aria-expanded', async () => {
      stubNarrowViewport(true);
      stubSignedInUser();

      renderNav();
      const button = screen.getByRole('button', { name: /menu/i });
      await userEvent.click(button);

      expect(button).toHaveAttribute('aria-expanded', 'true');
      // +1 for the persistent leadingEDJE home link in the bar (issue #579), alongside the 5 panel links.
      expect(screen.getAllByRole('link')).toHaveLength(6);
      expect(document.getElementById(button.getAttribute('aria-controls') ?? '')).not.toBeNull();
    });

    it('honours the role gate inside the panel -- it is the same set, not every link', async () => {
      stubNarrowViewport(true);
      stubSignedInUser();
      mockGetVisibleCompassNavKeys.mockReturnValue(['team-directory', 'client-directory']);

      renderNav();
      await userEvent.click(screen.getByRole('button', { name: /menu/i }));

      // The disclosure is presentation. It must not become a second, laxer permission path.
      // +1 for the persistent leadingEDJE home link, which is not role-gated (issue #579).
      expect(screen.getAllByRole('link')).toHaveLength(3);
      expect(screen.queryByRole('link', { name: 'Admin' })).not.toBeInTheDocument();
    });

    it('closes again on a second press', async () => {
      stubNarrowViewport(true);
      stubSignedInUser();

      renderNav();
      const button = screen.getByRole('button', { name: /menu/i });
      await userEvent.click(button);
      await userEvent.click(button);

      expect(button).toHaveAttribute('aria-expanded', 'false');
      expect(screen.queryByRole('link', { name: 'Team Directory' })).not.toBeInTheDocument();
    });

    it('closes on Escape and returns focus to the button, so the keyboard is never stranded', async () => {
      stubNarrowViewport(true);
      stubSignedInUser();

      renderNav();
      const button = screen.getByRole('button', { name: /menu/i });
      await userEvent.click(button);
      await userEvent.keyboard('{Escape}');

      expect(button).toHaveAttribute('aria-expanded', 'false');
      // Without this, dismissing the panel drops focus to <body> and a keyboard user restarts the
      // tab order from the top of the document.
      expect(button).toHaveFocus();
    });

    it('ignores other keys — only Escape dismisses it', async () => {
      stubNarrowViewport(true);
      stubSignedInUser();

      renderNav();
      const button = screen.getByRole('button', { name: /menu/i });
      await userEvent.click(button);
      await userEvent.keyboard('{ArrowDown}');
      await userEvent.keyboard('{Tab}');

      // Deliberately NOT `{Enter}` or `{Space}`: focus is on the toggle after opening, so those
      // re-activate the button and close the panel — correct native behaviour, and nothing to do with
      // the Escape handler. Asserting them here would have pinned the wrong mechanism.
      //
      // The point of this case is the `else` of the Escape check. A document-level keydown listener
      // that closed on ANY key would make the panel unusable the moment it held a control, and would
      // fight the browser's own link activation.
      expect(button).toHaveAttribute('aria-expanded', 'true');
      // +1 for the persistent leadingEDJE home link in the bar (issue #579), alongside the 5 panel links.
      expect(screen.getAllByRole('link')).toHaveLength(6);
    });

    it('does not come back already open after widening past md and narrowing again', async () => {
      const viewport = stubNarrowViewport(true);
      stubSignedInUser();

      renderNav();
      await userEvent.click(screen.getByRole('button', { name: /menu/i }));
      expect(screen.getByRole('button', { name: /menu/i })).toHaveAttribute(
        'aria-expanded',
        'true',
      );

      // Widen past `md` — the desktop bar. Then narrow again, as a tablet rotation does.
      await act(async () => {
        viewport.set(false);
      });
      await act(async () => {
        viewport.set(true);
      });

      // The panel must be CLOSED. Open state that survives a width round-trip renders a panel nobody
      // asked for, and reports `aria-expanded="true"` on a control the user never pressed.
      const button = screen.getByRole('button', { name: /menu/i });
      expect(button).toHaveAttribute('aria-expanded', 'false');
      expect(screen.queryByRole('link', { name: 'Team Directory' })).not.toBeInTheDocument();
    });

    it('registers no document Escape handler once the viewport is wide', async () => {
      const viewport = stubNarrowViewport(true);
      stubSignedInUser();
      const addSpy = vi.spyOn(document, 'addEventListener');
      const removeSpy = vi.spyOn(document, 'removeEventListener');

      renderNav();
      await userEvent.click(screen.getByRole('button', { name: /menu/i }));
      expect(addSpy.mock.calls.filter(([type]) => type === 'keydown')).toHaveLength(1);

      await act(async () => {
        viewport.set(false);
      });

      // The keydown listener exists to dismiss a panel. At desktop width there is no panel and no
      // button, so a surviving listener is a document-level handler for a control that is not on the
      // page — and it fires `close()`, which focuses a ref that is now null.
      expect(removeSpy.mock.calls.filter(([type]) => type === 'keydown')).toHaveLength(1);

      addSpy.mockRestore();
      removeSpy.mockRestore();
    });

    it('renders NO menu button when the role gate leaves zero links', () => {
      stubNarrowViewport(true);
      mockUseCurrentUser.mockReturnValue({ data: null } as ReturnType<typeof useCurrentUser>);
      mockGetVisibleCompassNavKeys.mockReturnValue([]);

      renderNav();

      // A control that opens an empty panel is worse than no control: it advertises navigation that
      // does not exist. The landmark itself still renders -- that is the unconditional-nav rule the
      // `zero links when unauthenticated` test above pins.
      expect(screen.queryByRole('button', { name: /menu/i })).not.toBeInTheDocument();
      expect(screen.getByRole('navigation', { name: 'Compass navigation' })).toBeInTheDocument();
    });

    it('leaves the desktop bar alone -- no button at or above md', () => {
      stubNarrowViewport(false);
      stubSignedInUser();

      renderNav();

      expect(screen.queryByRole('button', { name: /menu/i })).not.toBeInTheDocument();
      // +1 for the persistent leadingEDJE home link (issue #579), alongside the 5 desktop bar links.
      expect(screen.getAllByRole('link')).toHaveLength(6);
    });

    it('marks the current area with aria-current inside the panel too', async () => {
      stubNarrowViewport(true);
      stubSignedInUser();
      window.history.pushState({}, '', '/compass/reports');

      renderNav();
      await userEvent.click(screen.getByRole('button', { name: /menu/i }));

      expect(screen.getByRole('link', { name: 'Reports' })).toHaveAttribute('aria-current', 'page');
      window.history.pushState({}, '', '/');
    });
  });

  describe('topbar chrome (mockup fidelity, #67)', () => {
    afterEach(() => {
      window.history.pushState({}, '', '/');
    });

    it('renders the leadingEDJE / Compass wordmark', () => {
      renderNav();

      expect(screen.getByText('EDJE')).toBeInTheDocument();
      expect(screen.getByText('Compass')).toBeInTheDocument();
      // "leading" is a bare text node beside the bolded "EDJE" span, not its own element — read the
      // whole logo container's text content rather than looking for a "leading" node.
      const nav = screen.getByRole('navigation', { name: /compass navigation/i });
      expect(nav.textContent).toMatch(/^leadingEDJE\s*Compass/);
    });

    it('links only the leadingEDJE portion back to the LEAP landing page, with a pointer cursor (issue #579)', () => {
      renderNav();

      const homeLink = screen.getByRole('link', { name: 'leadingEDJE' });
      expect(homeLink).toHaveAttribute('href', '/');
      expect(homeLink.className).toMatch(/\bcursor-pointer\b/);

      // "Compass" is a sibling of the link, not inside it — only leadingEDJE is clickable
      // (owner direction: just the leadingEDJE portion, per issue #579).
      expect(homeLink).not.toHaveTextContent('Compass');
      expect(screen.getByText('Compass').closest('a')).toBeNull();
    });

    it("bolds EDJE in the brand green, per the mockup's `.logo b`", () => {
      renderNav();

      const edje = screen.getByText('EDJE');
      expect(edje.className).toMatch(/\bfont-extrabold\b/);
      expect(edje.className).toMatch(/\btext-brand-green\b/);
    });

    it("sizes the wordmark at the mockup's 19px, which is what keeps the green legible", () => {
      // NOT a duplicate of the fidelity assertion above, and not styling pedantry: this size is the
      // difference between a pass and a serious axe violation.
      //
      // Brand green on the brand-gray chrome is 4.33:1 — over WCAG AA's 3:1 for LARGE text, under the
      // 4.5:1 for normal text. axe-core puts the boundary at 14pt bold (18.66px). The mockup's
      // `.logo{font-size:19px}` + `.logo b{font-weight:800}` is 14.25pt bold, so the wordmark is large
      // text and the pairing passes. Tailwind's `text-lg` is 18px = 13.5pt and fails.
      //
      // This shipped as `text-lg`, and `compass-accessibility.critical.spec.ts` caught it in CI with
      // real axe-core. That spec is the true gate; this is the fast one that says WHY, because a
      // one-pixel type change does not look like an accessibility regression in review.
      //
      // A jsdom test cannot measure contrast — no layout, no computed colours — so it pins the input
      // the ratio depends on. Widening this to accept `text-lg` re-opens the violation.
      renderNav();

      const edje = screen.getByText('EDJE');
      const logo = edje.parentElement;

      // Whitespace-delimited rather than `\b`-delimited: `]` and a space are both non-word
      // characters, so there is no word boundary between them and `/\btext-\[19px\]\b/` never matches.
      expect(logo?.className).toMatch(/(?:^|\s)text-\[19px\](?:\s|$)/);
      expect(logo?.className).not.toMatch(/(?:^|\s)text-lg(?:\s|$)/);
    });

    it('gives the active link (matching the current path) the green underline and bold weight', () => {
      window.history.pushState({}, '', '/compass/client-directory');
      renderNav();

      const active = screen.getByRole('link', { name: /clients/i });
      expect(active.className).toMatch(/\bshadow-\[inset_0_-3px_0_var\(--color-brand-green\)\]/);
      expect(active.className).toMatch(/\bfont-semibold\b/);
      expect(active).toHaveAttribute('aria-current', 'page');

      const inactive = screen.getByRole('link', { name: /team directory/i });
      expect(inactive.className).not.toMatch(/\bshadow-\[/);
      expect(inactive).not.toHaveAttribute('aria-current');
    });

    it('treats a nested admin route as keeping Admin active', () => {
      window.history.pushState({}, '', '/compass/admin/edjers/5');
      renderNav();

      expect(screen.getByRole('link', { name: /admin/i })).toHaveAttribute('aria-current', 'page');
    });

    it('renders no active link on an unmatched path', () => {
      renderNav();

      for (const link of screen.getAllByRole('link')) {
        expect(link).not.toHaveAttribute('aria-current');
      }
    });

    it('renders the userchip — initials avatar only, no role label — for a signed-in user (#315)', () => {
      // Owner direction (#315): the role label next to the userchip was a mockup carryover with
      // no identified need, so the topbar shows only the avatar now.
      stubSignedInUser({ displayName: 'Sam Park' });
      mockGetCompassPrivileges.mockReturnValue(['Compass Super Admin']);
      renderNav();

      const avatar = screen.getByRole('img', { name: 'Sam Park' });
      expect(avatar).toHaveTextContent('SP');
      expect(avatar.className).toMatch(/\bbg-brand-green\b/);
      expect(avatar.className).toMatch(/\btext-brand-ink\b/);
      expect(screen.queryByText('Super Admin')).not.toBeInTheDocument();
    });

    it('renders no role label regardless of the role held (#315)', () => {
      stubSignedInUser({ displayName: 'Jamie Lowe' });
      mockGetCompassPrivileges.mockReturnValue([]);
      renderNav();

      expect(screen.getByRole('img', { name: 'Jamie Lowe' })).toHaveTextContent('JL');
      expect(screen.queryByText('EDJEr')).not.toBeInTheDocument();
    });

    // The avatar carries role="img", so an empty accessible name is a SERIOUS axe violation
    // (`role-img-alt`, WCAG 1.1.1) — not a cosmetic blank circle. `/api/me` returns
    // `displayName: ""` whenever the DisplayName claim is absent, which is exactly what
    // DevBypassMiddleware produces: it stamps EdjeId, Email, ClientId and privileges, and no
    // display name. The real sign-in path guards this one level up (SignInService.BuildIdentity
    // falls back to the email "belt-and-braces"), so CI — which mints its session through
    // /auth/stub-login — never renders the empty case and stayed green while the local
    // DevBypass stack failed real-axe. The component must not depend on that upstream guarantee.
    it('still gives the avatar an accessible name when displayName is empty', () => {
      stubSignedInUser({ displayName: '', email: 'ada.lovelace@leadingedje.com' });
      mockGetCompassPrivileges.mockReturnValue(['Compass Super Admin']);
      renderNav();

      const avatar = screen.getByRole('img');
      expect(avatar).toHaveAccessibleName('ada.lovelace@leadingedje.com');
      expect(avatar.textContent).not.toBe('');
    });

    it('prefers displayName over the email once a display name exists', () => {
      stubSignedInUser({ displayName: 'Ada Lovelace', email: 'ada.lovelace@leadingedje.com' });
      renderNav();

      const avatar = screen.getByRole('img', { name: 'Ada Lovelace' });
      expect(avatar).toHaveTextContent('AL');
    });

    it('renders no userchip when unauthenticated', () => {
      renderNav();

      expect(screen.queryByRole('img')).not.toBeInTheDocument();
    });
  });
});
