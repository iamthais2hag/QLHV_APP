import type {
  QlhvImportDiagnostics,
  QlhvImportExecuteOutcome,
  QlhvImportExecuteRequest,
  QlhvImportExecuteResult,
  QlhvImportPlan,
  QlhvImportRequest,
} from './types';

const API_BASE = import.meta.env.VITE_API_BASE_URL ?? '/api';

export async function getQlhvImportDiagnostics(
  request: QlhvImportRequest,
  signal?: AbortSignal,
): Promise<QlhvImportDiagnostics> {
  const response = await fetchSafely(
    `${API_BASE}/dong-bo-v2/qlhv/import-diagnostics?${buildQuery(request)}`,
    {
      method: 'GET',
      headers: { Accept: 'application/json' },
      signal,
    },
  );

  if (!response.ok) {
    throw new Error(await getSafeErrorMessage(response, 'Không thể chạy chẩn đoán nhập dữ liệu CSĐT.'));
  }

  const payload = await tryReadJson<unknown>(response);
  if (!isImportDiagnostics(payload)) {
    throw new Error('Backend không trả kết quả chẩn đoán hợp lệ.');
  }
  return payload;
}

export async function getQlhvImportPlan(
  request: QlhvImportRequest,
  signal?: AbortSignal,
): Promise<QlhvImportPlan> {
  const response = await fetchSafely(
    `${API_BASE}/dong-bo-v2/qlhv/import-plan?${buildQuery(request)}`,
    {
      method: 'GET',
      headers: { Accept: 'application/json' },
      signal,
    },
  );

  if (!response.ok) {
    throw new Error(await getSafeErrorMessage(response, 'Không thể lập kế hoạch nhập dữ liệu CSĐT.'));
  }

  const payload = await tryReadJson<unknown>(response);
  if (!isImportPlan(payload)) {
    throw new Error('Backend không trả kế hoạch hợp lệ.');
  }
  return payload;
}

export async function executeQlhvImport(
  request: QlhvImportExecuteRequest,
  signal?: AbortSignal,
): Promise<QlhvImportExecuteOutcome> {
  const response = await fetchSafely(
    `${API_BASE}/dong-bo-v2/qlhv/import-execute`,
    {
      method: 'POST',
      headers: {
        Accept: 'application/json',
        'Content-Type': 'application/json',
      },
      body: JSON.stringify(request),
      signal,
    },
  );

  const payload = await tryReadJson<unknown>(response);
  if (isExecuteResult(payload) && (response.ok || response.status === 409)) {
    return {
      kind: response.ok && payload.executed ? 'executed' : 'blocked',
      httpStatus: response.status,
      result: payload,
    };
  }

  if (!response.ok) {
    throw new Error(await getSafeErrorMessage(
      response,
      response.status === 409
        ? 'Yêu cầu nhập dữ liệu đã bị backend chặn.'
        : 'Không thể thực hiện nhập dữ liệu CSĐT.',
    ));
  }

  throw new Error('Backend không trả kết quả nhập dữ liệu hợp lệ.');
}

function buildQuery(request: QlhvImportRequest): string {
  const query = new URLSearchParams();
  query.set('sourceProfileCode', request.sourceProfileCode);
  query.set('maCSDT', request.maCSDT);
  if (request.maKhoaHoc) {
    query.set('maKhoaHoc', request.maKhoaHoc);
  }
  return query.toString();
}

async function fetchSafely(input: RequestInfo | URL, init: RequestInit): Promise<Response> {
  try {
    return await fetch(input, init);
  } catch (error) {
    if (error instanceof DOMException && error.name === 'AbortError') {
      throw error;
    }
    throw new Error('Không thể kết nối tới máy chủ. Vui lòng kiểm tra kết nối và thử lại.');
  }
}

async function getSafeErrorMessage(response: Response, fallback: string): Promise<string> {
  const payload = await tryReadJson<{ message?: unknown; title?: unknown }>(response);
  const publicMessage = sanitizePublicMessage(payload?.message) ?? sanitizePublicMessage(payload?.title);
  return `${publicMessage ?? fallback} (mã ${response.status})`;
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

function isExecuteResult(value: unknown): value is QlhvImportExecuteResult {
  if (!isRecord(value)) {
    return false;
  }

  return typeof value.executed === 'boolean'
    && typeof value.status === 'string'
    && typeof value.message === 'string'
    && isImportPlan(value.plan)
    && hasFiniteNumbers(value, [
      'insertedHocVienRows',
      'updatedHocVienRows',
      'skippedHocVienRows',
    ]);
}

function isImportPlan(value: unknown): value is QlhvImportPlan {
  if (!isRecord(value) || !hasImportIdentity(value)) {
    return false;
  }

  return typeof value.isReadOnly === 'boolean'
    && typeof value.executable === 'boolean'
    && isStringArray(value.blockers)
    && isStringArray(value.warnings)
    && hasFiniteNumbers(value, [
      'sourceHocVienRows',
      'sourceKhoaHocRows',
      'currentAppHocVienRows',
      'currentAppKhoaHocRows',
      'plannedInsertHocVienRows',
      'plannedUpdateHocVienRows',
      'plannedSkipHocVienRows',
      'plannedUpsertHocVienRows',
      'plannedUpsertKhoaHocRows',
    ]);
}

function isImportDiagnostics(value: unknown): value is QlhvImportDiagnostics {
  if (!isRecord(value) || !hasImportIdentity(value)) {
    return false;
  }

  return typeof value.isReadOnly === 'boolean'
    && typeof value.sourceProfileConstraintExists === 'boolean'
    && typeof value.sourceProfileAllowedByConstraint === 'boolean'
    && isStringArray(value.blockers)
    && isStringArray(value.warnings)
    && hasFiniteNumbers(value, [
      'sourceHocVienRows',
      'sourceDistinctMaDkRows',
      'duplicateSourceMaDkRows',
      'currentAppHocVienRows',
      'targetRowsForSourceProfile',
      'targetExactIdentityMatches',
      'targetMaDkConflictsOtherProfiles',
      'softDeletedIdentityConflicts',
    ]);
}

function hasImportIdentity(value: Record<string, unknown>): boolean {
  return typeof value.sourceProfileCode === 'string'
    && typeof value.maCSDT === 'string'
    && (value.maKhoaHoc === null || typeof value.maKhoaHoc === 'string');
}

function hasFiniteNumbers(value: Record<string, unknown>, keys: string[]): boolean {
  return keys.every((key) => typeof value[key] === 'number' && Number.isFinite(value[key]));
}

function isStringArray(value: unknown): value is string[] {
  return Array.isArray(value) && value.every((item) => typeof item === 'string');
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return !!value && typeof value === 'object' && !Array.isArray(value);
}
