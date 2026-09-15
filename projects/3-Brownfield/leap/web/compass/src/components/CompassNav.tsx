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

/** Every Compass nav destination, including the standalone `assignments` link (feature 006). */
const allNavLinks: CompassNavLink[] = [
  { key: 'team-directory', label: 'Team Directory', to: '/compass/team-directory' },
  { key: 'client-directory', label: 'Clients', to: '/compass/client-directory' },
  { key: 'sales-dashboard', label: 'Sales Dashboard', to: '/compass/sales-dashboard' },
  { key: 'reports', label: 'Reports', to: '/compass/reports' },
  { key: 'admin-config', label: 'Admin', to: '/compass/admin' },
];

function NavLink({
  link,
  pathname,
  variant = 'bar',
}: {
  link: CompassNavLink;
  pathname: string;
  variant?: 'bar' | 'panel';
}) {
  const active = isNavLinkActive(pathname, link.to);

  if (variant === 'panel') {
    return (
      <a
        href={link.to}
        aria-current={active ? 'page' : undefined}
        // tabIndex forces a consistent tab order across Chromium, Firefox, and Safari alike.
        tabIndex={0}
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

/** The mockup's `.logo`, rounded to Tailwind's `text-lg` step for consistency with the rest of the chrome. */
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

/** The nav below `md`, implemented as a conditional branch inside {@link CompassNav} rather than a separate mount. */
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
            // 44x44 (h-11 w-11) is the exact floor WCAG 2.2's AA target-size criterion (2.5.8) requires; anything smaller fails.
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
 * The Compass role-gated navigation shell. Renders the `<nav>` landmark only once a user is signed in.
 * Below `md` the links collapse behind a disclosure button, matching the mockup's own `@media(max-width:900px)` breakpoint.
 */
export function CompassNav() {
  const { data: user } = useCurrentUser();
  const isNarrow = useIsNarrowViewport();
  const compassPrivileges = getCompassPrivileges(user?.privileges);
  const visibleKeys = getVisibleCompassNavKeys(user != null, compassPrivileges);
  const visibleLinks = allNavLinks.filter((link) => visibleKeys.includes(link.key));
  const pathname = window.location.pathname;
  const avatarLabel = user ? user.displayName.trim() || user.email : '';
  const avatar = user ? <NavAvatar label={avatarLabel} /> : null;

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
