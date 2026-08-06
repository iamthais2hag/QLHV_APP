import { API_BASE } from '../../api/apiBase';
import { apiFetch } from '../../api/apiFetch';
import type {
  RealtimeControlStatus,
  RealtimeIntegrityPreview,
  RealtimeRunRequest,
  RuntimeStatus,
  TimeHealth,
  TimeHealthStatus,
} from './types';

export async function getRealtimeControl(signal?: AbortSignal): Promise<RealtimeControlStatus> {
  return requestRealtime<RealtimeControlStatus>('/system/realtime-control', {
    method: 'GET', headers: { Accept: 'application/json' }, signal,
  });
}

export async function setRealtimeControl(
  enabled: boolean,
  expectedRowVersion: string,
): Promise<RealtimeControlStatus> {
  return requestRealtime<RealtimeControlStatus>(
    `/system/realtime-control/${enabled ? 'enable' : 'disable'}`,
    {
      method: 'POST',
      headers: { Accept: 'application/json', 'Content-Type': 'application/json' },
      body: JSON.stringify({ expectedRowVersion }),
    },
  );
}

export async function runRealtimeOnce(): Promise<RealtimeRunRequest> {
  return requestRealtime<RealtimeRunRequest>('/system/realtime-control/run-once', {
    method: 'POST', headers: { Accept: 'application/json' },
  });
}

export async function previewRealtimeIntegrity(): Promise<RealtimeIntegrityPreview> {
  return requestRealtime<RealtimeIntegrityPreview>('/system/realtime-integrity/preview', {
    method: 'POST', headers: { Accept: 'application/json' },
  });
}

export async function getRuntimeStatus(signal?: AbortSignal): Promise<RuntimeStatus> {
  let response: Response;
  try {
    response = await apiFetch(`${API_BASE}/system/runtime-status`, {
      method: 'GET', headers: { Accept: 'application/json' }, signal,
    });
  } catch (error) {
    if (error instanceof DOMException && error.name === 'AbortError') throw error;
    throw new Error('Không thể kết nối tới dịch vụ kiểm tra trạng thái hệ thống.');
  }
  if (!response.ok) throw new Error('Không thể đọc trạng thái hệ thống.');
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
    || value.timeContractVersion !== '2.0'
    || !Array.isArray(value.messages)) {
    throw new Error('Máy chủ trả trạng thái hệ thống không hợp lệ.');
  }

  return {
    isReady: value.isReady,
    version: sanitizeLabel(value.version, 'Không xác định'),
    environment: sanitizeLabel(value.environment, 'Không xác định'),
    configurationReady: value.configurationReady,
    databaseConnected: value.databaseConnected,
    databaseName: value.databaseName === null
      ? null : sanitizeLabel(value.databaseName, 'Không xác định'),
    authenticationReady: value.authenticationReady,
    requiredSchemaReady: value.requiredSchemaReady,
    backupProfilesReady: value.backupProfilesReady,
    backupStorageReady: value.backupStorageReady,
    fileStorageReady: value.fileStorageReady,
    runtimeStorageReady: value.runtimeStorageReady,
    timeContractVersion: '2.0',
    time: parseTimeHealth(value.time),
    messages: value.messages.map(sanitizeRuntimeMessage)
      .filter((message): message is string => message !== null).slice(0, 30),
  };
}

function parseTimeHealth(value: unknown): TimeHealth {
  if (!isRecord(value)
    || !isTimeHealthStatus(value.health)
    || typeof value.reasonCode !== 'string'
    || value.reasonCode.trim().length === 0
    || typeof value.writesAllowed !== 'boolean'
    || typeof value.databaseClockAvailable !== 'boolean'
    || !isIsoUtcTimestamp(value.serverUtcNow)
    || !isNullableIsoUtcTimestamp(value.databaseUtcNow)
    || !isNonNegativeNumber(value.databaseUtcQueryMilliseconds)
    || !isNullableNumber(value.clockSkewMilliseconds)
    || !isNonNegativeNumber(value.monotonicQueryMilliseconds)
    || typeof value.timeZone !== 'string'
    || typeof value.displayTimeZone !== 'string'
    || typeof value.windowsTimeServiceState !== 'string'
    || !(typeof value.configuredPeer === 'string' || value.configuredPeer === null)
    || !(typeof value.currentSource === 'string' || value.currentSource === null)
    || !isNullableIsoUtcTimestamp(value.lastSuccessfulSyncUtc)
    || !isNullableInteger(value.lastSyncError)
    || !isNullableNumber(value.phaseOffsetMilliseconds)
    || !isNullableNumber(value.lastSuccessfulSyncAgeSeconds)
    || !isNullableNumber(value.effectivePollIntervalSeconds)
    || !isIsoUtcTimestamp(value.evaluatedAtUtc)
    || !Array.isArray(value.messages)) {
    throw new Error('Máy chủ trả SQL UTC contract 2.0 không hợp lệ.');
  }

  return {
    health: value.health,
    reasonCode: sanitizeLabel(value.reasonCode, 'CONTRACT_SCHEMA_INVALID'),
    writesAllowed: value.writesAllowed,
    databaseClockAvailable: value.databaseClockAvailable,
    serverUtcNow: value.serverUtcNow,
    databaseUtcNow: value.databaseUtcNow,
    databaseUtcQueryMilliseconds: value.databaseUtcQueryMilliseconds,
    clockSkewMilliseconds: value.clockSkewMilliseconds,
    monotonicQueryMilliseconds: value.monotonicQueryMilliseconds,
    timeZone: sanitizeLabel(value.timeZone, 'Không xác định'),
    displayTimeZone: sanitizeLabel(value.displayTimeZone, 'Asia/Ho_Chi_Minh'),
    windowsTimeServiceState: sanitizeLabel(value.windowsTimeServiceState, 'Unknown'),
    configuredPeer: value.configuredPeer,
    currentSource: value.currentSource,
    lastSuccessfulSyncUtc: value.lastSuccessfulSyncUtc,
    lastSyncError: value.lastSyncError,
    phaseOffsetMilliseconds: value.phaseOffsetMilliseconds,
    lastSuccessfulSyncAgeSeconds: value.lastSuccessfulSyncAgeSeconds,
    effectivePollIntervalSeconds: value.effectivePollIntervalSeconds,
    evaluatedAtUtc: value.evaluatedAtUtc,
    messages: value.messages.map(sanitizeRuntimeMessage)
      .filter((message): message is string => message !== null).slice(0, 20),
  };
}

function sanitizeRuntimeMessage(value: unknown): string | null {
  if (typeof value !== 'string') return null;
  const message = value.replace(/\s+/g, ' ').trim();
  if (!message || message.length > 400) return null;
  if (/(password(hash)?|\bpwd\s*=|connection\s*string|user\s*id\s*=|server\s*=|data\s*source\s*=|initial\s*catalog\s*=|cookie|\btoken\s*=|stack\s*trace)/i.test(message)) return null;
  return message;
}

function sanitizeLabel(value: string, fallback: string): string {
  const label = value.replace(/\s+/g, ' ').trim();
  return label && label.length <= 120 ? label : fallback;
}

async function tryReadJson(response: Response): Promise<unknown> {
  try { return await response.json(); } catch { return null; }
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return !!value && typeof value === 'object' && !Array.isArray(value);
}
function isOptionalBoolean(value: unknown): value is boolean | undefined {
  return value === undefined || typeof value === 'boolean';
}
function isTimeHealthStatus(value: unknown): value is TimeHealthStatus {
  return value === 'HEALTHY' || value === 'WARNING' || value === 'BLOCKED';
}
function isNonNegativeNumber(value: unknown): value is number {
  return typeof value === 'number' && Number.isFinite(value) && value >= 0;
}
function isNullableNumber(value: unknown): value is number | null {
  return value === null || (typeof value === 'number' && Number.isFinite(value));
}
function isNullableInteger(value: unknown): value is number | null {
  return value === null || (typeof value === 'number' && Number.isInteger(value));
}
function isIsoUtcTimestamp(value: unknown): value is string {
  return typeof value === 'string'
    && Number.isFinite(Date.parse(value))
    && /(?:Z|\+00:00)$/i.test(value);
}
function isNullableIsoUtcTimestamp(value: unknown): value is string | null {
  return value === null || isIsoUtcTimestamp(value);
}

async function requestRealtime<T>(path: string, init: RequestInit): Promise<T> {
  const response = await apiFetch(`${API_BASE}${path}`, init);
  const body = await tryReadJson(response);
  if (!response.ok) {
    const detail = isRecord(body) && typeof body.detail === 'string'
      ? sanitizeRuntimeMessage(body.detail) : null;
    throw new Error(detail ?? 'Không thể thực hiện thao tác Realtime.');
  }
  if (!isRecord(body)) throw new Error('Máy chủ trả dữ liệu Realtime không hợp lệ.');
  return body as T;
}
