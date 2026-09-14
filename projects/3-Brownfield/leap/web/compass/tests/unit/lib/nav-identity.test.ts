import { getInitials, isNavLinkActive } from '../../../src/lib/nav-identity';

describe('getInitials', () => {
  it('takes the first letter of the first and last token, uppercased', () => {
    expect(getInitials('Sam Park')).toBe('SP');
  });

  it('ignores middle names — first and last token only', () => {
    expect(getInitials('Mary Jane Watson')).toBe('MW');
  });

  it('uppercases lowercase input', () => {
    expect(getInitials('sam park')).toBe('SP');
  });

  it('takes the first two letters of a single-token name', () => {
    expect(getInitials('Cher')).toBe('CH');
  });

  it('returns empty string for empty input', () => {
    expect(getInitials('')).toBe('');
  });

  it('returns empty string for whitespace-only input', () => {
    expect(getInitials('   ')).toBe('');
  });

  it('collapses repeated internal whitespace before tokenizing', () => {
    expect(getInitials('Sam   Park')).toBe('SP');
  });

  /**
   * The case that was shipping wrong (owner report 2026-08-18).
   *
   * `CompassNav` falls back to `user.email` whenever the DisplayName claim is blank, and the
   * dev-bypass profiles carry no `DisplayName` at all — so an address is not an edge case here, it is
   * the routine input in every local session. Treated as one whitespace token it took the
   * single-token branch and produced the first TWO letters of the local part: "AV" for
   * avery.quinn@example.com, where the name gives "AQ".
   */
  describe('an email address, which is what a blank display name falls back to', () => {
    it('takes the initial of each side of a dotted local part, not the first two letters', () => {
      expect(getInitials('avery.quinn@example.com')).toBe('AQ');
    });

    it('drops the domain rather than letting it supply the second initial', () => {
      // The failure this rules out is subtle: splitting the WHOLE address on non-letters would make
      // the last token "com" and render "BC".
      expect(getInitials('riley.chen@leadingedje.com')).toBe('RC');
    });

    it('treats an underscored or hyphenated local part the same way', () => {
      expect(getInitials('avery_quinn@example.com')).toBe('AQ');
      expect(getInitials('avery-quinn@example.com')).toBe('AQ');
    });

    it('ignores a middle segment, as it does a middle name', () => {
      expect(getInitials('mary.jane.watson@leadingedje.com')).toBe('MW');
    });

    it('falls back to two letters when the local part has no separator', () => {
      expect(getInitials('jkirk@leadingedje.com')).toBe('JK');
    });

    it('returns empty string for an address with no local part', () => {
      // Nothing produces this label today. It is asserted because the alternative shape —
      // falling through to the whitespace branch — rendered the avatar "@L", which is worse than the
      // blank circle an unset name already gives.
      expect(getInitials('@leadingedje.com')).toBe('');
    });
  });
});

describe('isNavLinkActive', () => {
  it('matches an exact pathname', () => {
    expect(isNavLinkActive('/compass/team-directory', '/compass/team-directory')).toBe(true);
  });

  it('matches with a trailing slash on the current path', () => {
    expect(isNavLinkActive('/compass/team-directory/', '/compass/team-directory')).toBe(true);
  });

  it('matches a nested route under the link target', () => {
    expect(isNavLinkActive('/compass/admin/edjers/5', '/compass/admin')).toBe(true);
  });

  it('marks Team Directory active on the base path, which renders it', () => {
    // router.ts maps '/' to TeamDirectoryRoute, so /compass/ puts the Team Directory on screen. It
    // previously matched no link at all, leaving the app's front door with no highlight and no
    // aria-current for a screen-reader user.
    expect(isNavLinkActive('/compass/', '/compass/team-directory')).toBe(true);
    expect(isNavLinkActive('/compass', '/compass/team-directory')).toBe(true);
  });

  it('does not mark every OTHER link active on the base path', () => {
    // The trap in resolving the index: a naive prefix match would make '/compass' a prefix of every
    // link and light the whole nav up.
    expect(isNavLinkActive('/compass/', '/compass/client-directory')).toBe(false);
    expect(isNavLinkActive('/compass/', '/compass/admin')).toBe(false);
    expect(isNavLinkActive('/compass/', '/compass/sales-dashboard')).toBe(false);
  });

  it('does not match a different destination', () => {
    expect(isNavLinkActive('/compass/client-directory', '/compass/team-directory')).toBe(false);
  });

  it('does not false-positive on a path that merely starts with the same characters', () => {
    expect(isNavLinkActive('/compass/team-directory-extra', '/compass/team-directory')).toBe(false);
  });
});
