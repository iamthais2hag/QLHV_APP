export const AUTH_SESSION_EXPIRED_EVENT = 'qlhv:auth-session-expired';

export async function apiFetch(
  input: RequestInfo | URL,
  init: RequestInit = {},
): Promise<Response> {
  const response = await fetch(input, {
    ...init,
    cache: init.cache ?? 'no-store',
    credentials: 'include',
  });

  if (response.status === 401 && !isLoginRequest(input) && typeof window !== 'undefined') {
    window.dispatchEvent(new Event(AUTH_SESSION_EXPIRED_EVENT));
  }

  return response;
}

function isLoginRequest(input: RequestInfo | URL): boolean {
  const url = typeof input === 'string'
    ? input
    : input instanceof URL
      ? input.href
      : input.url;

  return url.includes('/api/auth/login');
}
