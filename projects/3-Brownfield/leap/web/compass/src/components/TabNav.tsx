import { Link, useRouterState } from '@tanstack/react-router';
import type { LinkProps } from '@tanstack/react-router';
import { resolveActivePath } from '../lib/nav-identity';

type RoutePath = LinkProps['to'];

/** One destination in a {@link TabNav}. */
export interface TabNavItem {
  /** A router path, including the `/compass` prefix — this component prepends nothing. */
  to: RoutePath;
  label: string;
}

interface TabNavProps {
  label: string;
  items: TabNavItem[];
  indexPath: string;
  indexRenders: string;
  className?: string;
}

/** The tab strip driven by local `useState`, matching the ARIA tab pattern with `role="tab"` panels. */
export function TabNav({ label, items, indexPath, indexRenders, className = '' }: TabNavProps) {
  const pathname = useRouterState({ select: (state) => state.location.pathname });

  const resolved = resolveActivePath(pathname, indexPath, indexRenders);

  return (
    // Rendered even when `items` is empty, so the layout does not shift as destinations are added —
    // the same reasoning CompassNav applies to rendering its own <nav> with zero links.
    <nav
      aria-label={label}
      className={`mb-4 flex flex-wrap gap-0.5 border-b-2 border-brand-taupe/40 ${className}`}
    >
      {items.map((item) => {
        const isActive = resolved === item.to;

        return (
          <Link
            key={item.to}
            to={item.to}
            aria-current={isActive ? 'page' : undefined}
            className={`-mb-0.5 border-b-[3px] px-[18px] py-[9px] text-[13px] ${
              isActive
                ? 'border-brand-green font-semibold text-brand-text'
                : 'border-transparent text-brand-gray-muted hover:text-brand-text'
            }`}
          >
            {item.label}
          </Link>
        );
      })}
    </nav>
  );
}
