export type CompassNavKey =
  'team-directory' | 'client-directory' | 'sales-dashboard' | 'reports' | 'admin-config';

/** Visible only to Compass Super Admin, per the FR-004 tier table in the deleted docs/ folder. */
const BASELINE_KEYS: CompassNavKey[] = ['team-directory', 'client-directory'];

const SALES_ROLES = ['Compass Sales', 'Compass Ops', 'Compass Super Admin'];
const SUPER_ADMIN_ROLES = ['Compass Super Admin'];
const OPS_ROLES = ['Compass Ops', 'Compass Super Admin'];

export function getCompassPrivileges(privileges: string[] | null | undefined): string[] {
  return (privileges ?? []).filter((privilege) => privilege.startsWith('Compass '));
}

export function getVisibleCompassNavKeys(
  isAuthenticated: boolean,
  compassPrivileges: string[],
): CompassNavKey[] {
  if (!isAuthenticated) {
    return [];
  }

  const keys: CompassNavKey[] = [...BASELINE_KEYS];
  if (compassPrivileges.some((p) => SALES_ROLES.includes(p))) {
    keys.push('sales-dashboard', 'reports');
  }
  if (compassPrivileges.some((p) => SUPER_ADMIN_ROLES.includes(p))) {
    keys.push('admin-config');
  }
  return keys;
}

/** Whether a viewer may create/edit assignments — this check is the sole authorization boundary, matching `RolePolicy.CompassOps` (contract §1). */
export function canManageCompassAssignments(compassPrivileges: string[]): boolean {
  return compassPrivileges.some((p) => OPS_ROLES.includes(p));
}

export function canDeleteCompassAssignments(compassPrivileges: string[]): boolean {
  return compassPrivileges.some((p) => SUPER_ADMIN_ROLES.includes(p));
}
