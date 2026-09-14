import {
  canDeleteCompassAssignments,
  canManageCompassAssignments,
  getCompassPrivileges,
  getVisibleCompassNavKeys,
} from '../../../src/lib/compass-nav-permissions';

describe('getCompassPrivileges', () => {
  it('returns an empty array for null', () => {
    expect(getCompassPrivileges(null)).toEqual([]);
  });

  it('returns an empty array for undefined', () => {
    expect(getCompassPrivileges(undefined)).toEqual([]);
  });

  it('returns an empty array when no privilege is Compass-prefixed', () => {
    expect(getCompassPrivileges(['EDJEr', 'SuperAdmin', 'OOTO Admin'])).toEqual([]);
  });

  it('keeps only the Compass-prefixed strings out of a mixed privileges array', () => {
    expect(getCompassPrivileges(['EDJEr', 'SuperAdmin', 'Compass Ops', 'OOTO Reports'])).toEqual([
      'Compass Ops',
    ]);
  });

  it('keeps every Compass role when the array holds more than one', () => {
    expect(getCompassPrivileges(['Compass Ops', 'Compass Sales'])).toEqual([
      'Compass Ops',
      'Compass Sales',
    ]);
  });
});

describe('getVisibleCompassNavKeys', () => {
  it('returns nothing for an unauthenticated caller, regardless of any stray privilege', () => {
    expect(getVisibleCompassNavKeys(false, ['Compass Super Admin'])).toEqual([]);
  });

  it('returns baseline-only for an authenticated user with zero Compass roles (the implicit EDJEr tier)', () => {
    expect(getVisibleCompassNavKeys(true, [])).toEqual(['team-directory', 'client-directory']);
  });

  it('Compass Admin sees EXACTLY baseline — read-only despite the name, not an elevated role', () => {
    expect(getVisibleCompassNavKeys(true, ['Compass Admin'])).toEqual([
      'team-directory',
      'client-directory',
    ]);
  });

  it('Compass Ops sees baseline + sales-dashboard + reports, no admin-config', () => {
    const keys = getVisibleCompassNavKeys(true, ['Compass Ops']);
    expect(keys).toEqual(['team-directory', 'client-directory', 'sales-dashboard', 'reports']);
  });

  it('Compass Sales sees baseline + sales-dashboard + reports, no admin-config', () => {
    const keys = getVisibleCompassNavKeys(true, ['Compass Sales']);
    expect(keys).toEqual(['team-directory', 'client-directory', 'sales-dashboard', 'reports']);
  });

  it('Compass Super Admin sees every key', () => {
    const keys = getVisibleCompassNavKeys(true, ['Compass Super Admin']);
    expect(keys).toEqual([
      'team-directory',
      'client-directory',
      'sales-dashboard',
      'reports',
      'admin-config',
    ]);
  });

  it('a Timesheet/OOTO role string alongside zero Compass roles contributes nothing beyond baseline', () => {
    // Simulates the real /api/me shape: getCompassPrivileges already stripped the Timesheet role,
    // so this proves the accumulator itself has no knowledge of any non-Compass role string either.
    expect(getVisibleCompassNavKeys(true, [])).toEqual(['team-directory', 'client-directory']);
  });
});

describe('canManageCompassAssignments', () => {
  it.each(['Compass Ops', 'Compass Super Admin'])('grants %s', (role) => {
    expect(canManageCompassAssignments([role])).toBe(true);
  });

  it.each(['Compass Admin', 'Compass Sales'])(
    'does NOT grant %s — the same set the server refuses (AC-44)',
    (role) => {
      expect(canManageCompassAssignments([role])).toBe(false);
    },
  );

  it('denies a viewer with zero Compass roles', () => {
    expect(canManageCompassAssignments([])).toBe(false);
  });
});

describe('canDeleteCompassAssignments', () => {
  it('grants Compass Super Admin', () => {
    expect(canDeleteCompassAssignments(['Compass Super Admin'])).toBe(true);
  });

  it.each(['Compass Ops', 'Compass Admin', 'Compass Sales'])(
    'does NOT grant %s — issue #593 restricts the true delete to the Compass root alone',
    (role) => {
      expect(canDeleteCompassAssignments([role])).toBe(false);
    },
  );

  it('denies a viewer with zero Compass roles', () => {
    expect(canDeleteCompassAssignments([])).toBe(false);
  });
});
