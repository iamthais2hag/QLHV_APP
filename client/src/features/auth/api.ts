import { apiFetch } from '../../api/apiFetch';
import { API_BASE } from '../../api/apiBase';
import type { AuthenticatedUser, LoginRequest } from './types';

export async function getCurrentUser(signal?: AbortSignal): Promise<AuthenticatedUser | null> {
  const response = await apiFetch(`${API_BASE}/auth/me`, {
    method: 'GET',
    headers: { Accept: 'application/json' },
    signal,
  });

  if (response.status === 401) {
    return null;
  }
  if (!response.ok) {
    throw new Error(await getSafeErrorMessage(response, 'Không thể kiểm tra phiên đăng nhập.'));
  }

  return parseUser(await tryReadJson<unknown>(response));
}

export async function login(request: LoginRequest): Promise<AuthenticatedUser> {
  const response = await apiFetch(`${API_BASE}/auth/login`, {
    method: 'POST',
    headers: {
      Accept: 'application/json',
      'Content-Type': 'application/json',
    },
    body: JSON.stringify(request),
  });

  if (!response.ok) {
    throw new Error(await getSafeErrorMessage(
      response,
      response.status === 401
        ? 'Tên đăng nhập hoặc mật khẩu không đúng.'
        : 'Không thể đăng nhập.',
    ));
  }

  return parseUser(await tryReadJson<unknown>(response));
}

export async function logout(): Promise<void> {
  const response = await apiFetch(`${API_BASE}/auth/logout`, {
    method: 'POST',
    headers: { Accept: 'application/json' },
  });

  if (!response.ok && response.status !== 401) {
    throw new Error(await getSafeErrorMessage(response, 'Không thể đăng xuất.'));
  }
}

function parseUser(value: unknown): AuthenticatedUser {
  if (!isRecord(value)
    || typeof value.id !== 'number'
    || !Number.isFinite(value.id)
    || typeof value.username !== 'string'
    || typeof value.displayName !== 'string'
    || (value.role !== 'Admin' && value.role !== 'Viewer')) {
    throw new Error('Máy chủ trả thông tin tài khoản không hợp lệ.');
  }

  return {
    id: value.id,
    username: value.username,
    displayName: value.displayName,
    role: value.role,
  };
}

async function getSafeErrorMessage(response: Response, fallback: string): Promise<string> {
  const payload = await tryReadJson<{ message?: unknown; title?: unknown }>(response);
  const message = sanitizePublicMessage(payload?.message) ?? sanitizePublicMessage(payload?.title);
  return `${message ?? fallback} (mã ${response.status})`;
}

function sanitizePublicMessage(value: unknown): string | null {
  if (typeof value !== 'string') {
    return null;
  }

  const message = value.replace(/\s+/g, ' ').trim();
  if (!message || message.length > 400 || /\bat\s+\S+\s*\(|stack trace/i.test(message)) {
    return null;
  }
  return message;
}

async function tryReadJson<T>(response: Response): Promise<T | null> {
  try {
    return (await response.clone().json()) as T;
  } catch {
    return null;
  }
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return !!value && typeof value === 'object' && !Array.isArray(value);
}
