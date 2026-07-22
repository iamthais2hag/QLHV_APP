import { API_BASE } from '../../api/apiBase';
import { apiFetch } from '../../api/apiFetch';
import type { RuntimeStatus } from './types';

export async function getRuntimeStatus(signal?: AbortSignal): Promise<RuntimeStatus> {
  let response: Response;
  try {
    response = await apiFetch(`${API_BASE}/system/runtime-status`, {
      method: 'GET',
      headers: { Accept: 'application/json' },
      signal,
    });
  } catch (error) {
    if (error instanceof DOMException && error.name === 'AbortError') {
      throw error;
    }
    throw new Error('Không thể kết nối tới dịch vụ kiểm tra trạng thái hệ thống.');
  }

  if (!response.ok) {
    throw new Error('Không thể đọc trạng thái hệ thống.');
  }

  return parseRuntimeStatus(await tryReadJson(response));
}

export function parseRuntimeStatus(value: unknown): RuntimeStatus {
  if (!isRecord(value)
    || !isOptionalBoolean(value.isReady)
    || typeof value.version !== 'string'
    || typeof value.environment !== 'string'
    || !isOptionalBoolean(value.configurationReady)
    || typeof value.databaseConnected !== 'boolean'
    || !(typeof value.databaseName === 'string' || value.databaseName === null)
    || typeof value.authenticationReady !== 'boolean'
    || typeof value.requiredSchemaReady !== 'boolean'
    || typeof value.backupProfilesReady !== 'boolean'
    || !isOptionalBoolean(value.backupStorageReady)
    || typeof value.fileStorageReady !== 'boolean'
    || !isOptionalBoolean(value.runtimeStorageReady)
    || !Array.isArray(value.messages)) {
    throw new Error('Máy chủ trả trạng thái hệ thống không hợp lệ.');
  }

  const messages = value.messages
    .map(sanitizeRuntimeMessage)
    .filter((message): message is string => message !== null)
    .slice(0, 30);

  return {
    isReady: value.isReady,
    version: sanitizeLabel(value.version, 'Không xác định'),
    environment: sanitizeLabel(value.environment, 'Không xác định'),
    configurationReady: value.configurationReady,
    databaseConnected: value.databaseConnected,
    databaseName: value.databaseName === null
      ? null
      : sanitizeLabel(value.databaseName, 'Không xác định'),
    authenticationReady: value.authenticationReady,
    requiredSchemaReady: value.requiredSchemaReady,
    backupProfilesReady: value.backupProfilesReady,
    backupStorageReady: value.backupStorageReady,
    fileStorageReady: value.fileStorageReady,
    runtimeStorageReady: value.runtimeStorageReady,
    messages,
  };
}

function sanitizeRuntimeMessage(value: unknown): string | null {
  if (typeof value !== 'string') {
    return null;
  }

  const message = value.replace(/\s+/g, ' ').trim();
  if (!message || message.length > 400) {
    return null;
  }

  // The endpoint is designed to return public diagnostics only. Keep a final
  // client-side guard so an accidental backend regression is not rendered.
  if (/(password(hash)?|\bpwd\s*=|connection\s*string|user\s*id\s*=|server\s*=|data\s*source\s*=|initial\s*catalog\s*=|cookie|\btoken\s*=|stack\s*trace)/i.test(message)) {
    return null;
  }

  return message;
}

function sanitizeLabel(value: string, fallback: string): string {
  const label = value.replace(/\s+/g, ' ').trim();
  return label && label.length <= 120 ? label : fallback;
}

async function tryReadJson(response: Response): Promise<unknown> {
  try {
    return await response.json();
  } catch {
    return null;
  }
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return !!value && typeof value === 'object' && !Array.isArray(value);
}

function isOptionalBoolean(value: unknown): value is boolean | undefined {
  return value === undefined || typeof value === 'boolean';
}
