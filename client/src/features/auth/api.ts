import { apiFetch } from '../../api/apiFetch';
import { API_BASE } from '../../api/apiBase';
import type {
  AuthenticatedUser,
  ChangePasswordRequest,
  LoginRequest,
} from './types';

export type LoginFailureKind = 'invalid-credentials' | 'locked' | 'runtime-unavailable' | 'unexpected';

export class LoginRequestError extends Error {
  constructor(
    message: string,
    public readonly kind: LoginFailureKind,
    public readonly status: number | null,
    public readonly correlationId: string | null = null,
  ) {
    super(message);
    this.name = 'LoginRequestError';
  }
}

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
  let response: Response;
  try {
    response = await apiFetch(`${API_BASE}/auth/login`, {
      method: 'POST',
      headers: {
        Accept: 'application/json',
        'Content-Type': 'application/json',
      },
      body: JSON.stringify(request),
    });
  } catch {
    throw new LoginRequestError(
      'Hệ thống chưa sẵn sàng. Vui lòng liên hệ quản trị viên.',
      'runtime-unavailable',
      null,
    );
  }

  if (!response.ok) {
    const payload = await tryReadJson<{ correlationId?: unknown; traceId?: unknown }>(response);
    const correlationId = parseCorrelationId(payload?.correlationId ?? payload?.traceId);

    if (response.status === 401) {
      throw new LoginRequestError(
        'Tên đăng nhập hoặc mật khẩu không đúng.',
        'invalid-credentials',
        response.status,
        correlationId,
      );
    }
    if (response.status === 423) {
      throw new LoginRequestError(
        'Tài khoản tạm thời bị khóa. Vui lòng thử lại sau hoặc liên hệ quản trị viên.',
        'locked',
        response.status,
        correlationId,
      );
    }
    if (response.status === 503) {
      throw new LoginRequestError(
        'Hệ thống chưa sẵn sàng. Vui lòng liên hệ quản trị viên.',
        'runtime-unavailable',
        response.status,
        correlationId,
      );
    }

    throw new LoginRequestError(
      'Không thể đăng nhập. Vui lòng liên hệ quản trị viên.',
      'unexpected',
      response.status,
      correlationId,
    );
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

export async function changePassword(request: ChangePasswordRequest): Promise<void> {
  const response = await apiFetch(`${API_BASE}/auth/change-password`, {
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
      'Không thể đổi mật khẩu. Vui lòng kiểm tra mật khẩu hiện tại và thử lại.',
    ));
  }
}

function parseUser(value: unknown): AuthenticatedUser {
  if (!isRecord(value)
    || typeof value.id !== 'number'
    || !Number.isFinite(value.id)
    || typeof value.username !== 'string'
    || typeof value.displayName !== 'string'
    || (value.role !== 'Admin' && value.role !== 'Employee' && value.role !== 'Viewer')
    || typeof value.mustChangePassword !== 'boolean') {
    throw new Error('Máy chủ trả thông tin tài khoản không hợp lệ.');
  }

  return {
    id: value.id,
    username: value.username,
    displayName: value.displayName,
    role: value.role,
    mustChangePassword: value.mustChangePassword,
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

function parseCorrelationId(value: unknown): string | null {
  if (typeof value !== 'string') {
    return null;
  }

  const correlationId = value.trim();
  return correlationId.length > 0
    && correlationId.length <= 100
    && /^[a-zA-Z0-9._:-]+$/.test(correlationId)
    ? correlationId
    : null;
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
