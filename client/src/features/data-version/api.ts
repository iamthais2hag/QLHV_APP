import { API_BASE } from '../../api/apiBase';
import { apiFetch } from '../../api/apiFetch';
import type { DataVersionValue, SystemDataVersion } from './types';

export async function getSystemDataVersion(signal?: AbortSignal): Promise<SystemDataVersion> {
  let response: Response;
  try {
    response = await apiFetch(`${API_BASE}/system/data-version`, {
      method: 'GET',
      headers: { Accept: 'application/json' },
      signal,
    });
  } catch (error) {
    if (error instanceof DOMException && error.name === 'AbortError') {
      throw error;
    }
    throw new Error('Không thể kết nối để kiểm tra phiên bản dữ liệu.');
  }

  if (!response.ok) {
    throw new Error(`Không thể kiểm tra phiên bản dữ liệu (mã ${response.status}).`);
  }

  const payload = await tryReadJson(response);
  if (!isSystemDataVersion(payload)) {
    throw new Error('Backend không trả phiên bản dữ liệu hợp lệ.');
  }
  return payload;
}

async function tryReadJson(response: Response): Promise<unknown> {
  try {
    return await response.json();
  } catch {
    return null;
  }
}

function isSystemDataVersion(value: unknown): value is SystemDataVersion {
  if (!value || typeof value !== 'object' || Array.isArray(value)) {
    return false;
  }

  const item = value as Record<string, unknown>;
  return isVersionValue(item.hocVienVersion)
    && isVersionValue(item.khoaHocVersion)
    && isVersionValue(item.giaoVienVersion)
    && isVersionValue(item.photoVersion)
    && (item.lastSuccessfulSyncUtc === null || typeof item.lastSuccessfulSyncUtc === 'string');
}

function isVersionValue(value: unknown): value is DataVersionValue {
  return typeof value === 'string'
    || (typeof value === 'number' && Number.isFinite(value));
}
