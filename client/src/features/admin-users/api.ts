import { API_BASE } from '../../api/apiBase';
import { apiFetch } from '../../api/apiFetch';
import type { AppUserRole } from '../auth/types';
import type {
  CreateManagedUserRequest,
  ManagedUser,
  ResetManagedUserPasswordRequest,
  UpdateManagedUserRequest,
} from './types';

export async function getManagedUsers(signal?: AbortSignal): Promise<ManagedUser[]> {
  const response = await apiFetch(`${API_BASE}/admin/users`, {
    method: 'GET',
    headers: { Accept: 'application/json' },
    signal,
  });
  if (!response.ok) {
    throw new Error(await getSafeErrorMessage(response, 'Không thể tải danh sách tài khoản.'));
  }

  const payload = await tryReadJson<unknown>(response);
  if (!Array.isArray(payload) || !payload.every(isManagedUser)) {
    throw new Error('Máy chủ trả danh sách tài khoản không hợp lệ.');
  }
  return payload;
}

export async function createManagedUser(request: CreateManagedUserRequest): Promise<ManagedUser> {
  return sendForUser(`${API_BASE}/admin/users`, 'POST', request);
}

export async function updateManagedUser(
  id: number,
  request: UpdateManagedUserRequest,
): Promise<ManagedUser> {
  return sendForUser(`${API_BASE}/admin/users/${id}`, 'PUT', request);
}

export async function resetManagedUserPassword(
  id: number,
  request: ResetManagedUserPasswordRequest,
): Promise<void> {
  const response = await apiFetch(`${API_BASE}/admin/users/${id}/reset-password`, {
    method: 'POST',
    headers: {
      Accept: 'application/json',
      'Content-Type': 'application/json',
    },
    body: JSON.stringify(request),
  });
  if (!response.ok) {
    throw new Error(await getSafeErrorMessage(response, 'Không thể đặt lại mật khẩu.'));
  }
}

async function sendForUser(
  url: string,
  method: 'POST' | 'PUT',
  request: CreateManagedUserRequest | UpdateManagedUserRequest,
): Promise<ManagedUser> {
  const response = await apiFetch(url, {
    method,
    headers: {
      Accept: 'application/json',
      'Content-Type': 'application/json',
    },
    body: JSON.stringify(request),
  });
  if (!response.ok) {
    throw new Error(await getSafeErrorMessage(
      response,
      method === 'POST' ? 'Không thể tạo tài khoản.' : 'Không thể cập nhật tài khoản.',
    ));
  }

  const payload = await tryReadJson<unknown>(response);
  if (!isManagedUser(payload)) {
    throw new Error('Máy chủ trả thông tin tài khoản không hợp lệ.');
  }
  return payload;
}

function isManagedUser(value: unknown): value is ManagedUser {
  if (!isRecord(value)) {
    return false;
  }

  return typeof value.id === 'number'
    && Number.isFinite(value.id)
    && typeof value.username === 'string'
    && typeof value.displayName === 'string'
    && isRole(value.role)
    && typeof value.isActive === 'boolean'
    && typeof value.mustChangePassword === 'boolean'
    && isNullableString(value.lastLoginAtUtc)
    && typeof value.createdAtUtc === 'string'
    && isNullableString(value.createdBy);
}

function isRole(value: unknown): value is AppUserRole {
  return value === 'Admin' || value === 'Employee' || value === 'Viewer';
}

async function getSafeErrorMessage(response: Response, fallback: string): Promise<string> {
  const payload = await tryReadJson<{ message?: unknown; detail?: unknown; title?: unknown }>(response);
  const value = typeof payload?.message === 'string'
    ? payload.message
    : typeof payload?.detail === 'string'
      ? payload.detail
    : typeof payload?.title === 'string'
      ? payload.title
      : fallback;
  const message = value.replace(/\s+/g, ' ').trim();
  const safeMessage = !message
    || message.length > 400
    || /\bat\s+\S+\s*\(|stack trace|password(hash)?/i.test(message)
    ? fallback
    : message;
  return `${safeMessage} (mã ${response.status})`;
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

function isNullableString(value: unknown): value is string | null {
  return value === null || typeof value === 'string';
}
