import { afterEach, describe, expect, it, vi } from 'vitest';
import {
  buildLoginRedirectUrl,
  installSessionExpiredRedirect,
} from '../../../src/lib/session-redirect';

describe('buildLoginRedirectUrl', () => {
  it('encodes the current path as returnUrl', () => {
    expect(buildLoginRedirectUrl({ pathname: '/compass/team-directory', search: '' })).toBe(
      '/auth/login?returnUrl=%2Fcompass%2Fteam-directory',
    );
  });

  it('preserves and encodes a query string on the requested location', () => {
    expect(
      buildLoginRedirectUrl({ pathname: '/compass/assignments', search: '?clientId=42' }),
    ).toBe('/auth/login?returnUrl=%2Fcompass%2Fassignments%3FclientId%3D42');
  });

  it('falls back to the bare root when the requested path is only the SPA mount', () => {
    expect(buildLoginRedirectUrl({ pathname: '/compass/', search: '' })).toBe(
      '/auth/login?returnUrl=%2Fcompass%2F',
    );
  });
});

describe('installSessionExpiredRedirect', () => {
  /** Captures the listener `installSessionExpiredRedirect` registers so each test can remove it. */
  function captureInstalledListener(): EventListenerOrEventListenerObject {
    const addSpy = vi.spyOn(window, 'addEventListener');
    installSessionExpiredRedirect();
    const call = addSpy.mock.calls.find(([type]) => type === 'session-expired');
    if (!call) {
      throw new Error('installSessionExpiredRedirect did not register a session-expired listener');
    }
    addSpy.mockRestore();
    return call[1];
  }

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('redirects to /auth/login with the current location as returnUrl on session-expired', () => {
    const assignSpy = vi.fn();
    vi.stubGlobal('location', {
      pathname: '/compass/reports',
      search: '?year=2026',
      assign: assignSpy,
    });
    const listener = captureInstalledListener();

    window.dispatchEvent(new CustomEvent('session-expired'));

    expect(assignSpy).toHaveBeenCalledWith(
      '/auth/login?returnUrl=%2Fcompass%2Freports%3Fyear%3D2026',
    );
    window.removeEventListener('session-expired', listener);
  });

  it('does not redirect before a session-expired event fires', () => {
    const assignSpy = vi.fn();
    vi.stubGlobal('location', {
      pathname: '/compass/team-directory',
      search: '',
      assign: assignSpy,
    });
    const listener = captureInstalledListener();

    expect(assignSpy).not.toHaveBeenCalled();
    window.removeEventListener('session-expired', listener);
  });
});
