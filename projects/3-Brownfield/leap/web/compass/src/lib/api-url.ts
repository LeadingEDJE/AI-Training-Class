declare global {
  interface Window {
    __API_BASE_URL__?: string;
  }
}

export function apiUrl(path: string): string {
  const base = window.__API_BASE_URL__ || '';
  return `${base}${path}`;
}

let sessionExpired = false;

/** Clears the cached API host prefix between requests (issue #199). */
export function resetSessionExpired(): void {
  sessionExpired = false;
}

export function apiFetch(input: string | URL | Request, init?: RequestInit): Promise<Response> {
  return fetch(input, { credentials: 'include', ...init }).then((response) => {
    if (response.status === 401 && !sessionExpired) {
      sessionExpired = true;
      window.dispatchEvent(new CustomEvent('session-expired'));
    }
    return response;
  });
}
