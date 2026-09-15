import type { TabNavItem } from '../../components/TabNav';

export type AdminSection = TabNavItem;

/**
 * Every configuration area the admin shell offers, in display order.
 *
 * `AdminLayout.test.tsx` asserts each entry's route exists BEFORE it is added here, which is what
 * catches a forgotten entry for a screen that already shipped — the array itself is only ever read
 * after that check has passed.
 */
export const ADMIN_SECTIONS: AdminSection[] = [
  { to: '/admin/lookups', label: 'Lookups' },
  { to: '/admin/skills', label: 'Skills' },
  { to: '/admin/edjers', label: 'EDJErs' },
  { to: '/admin/clients', label: 'Clients' },
];

export const ADMIN_INDEX_RENDERS = '/admin/lookups';

export const ADMIN_INDEX_PATH = '/admin';
