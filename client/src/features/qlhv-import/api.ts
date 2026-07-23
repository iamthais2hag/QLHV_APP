import type {
  QlhvImportDiagnostics,
  QlhvImportEntityCounts,
  QlhvImportExecuteOutcome,
  QlhvImportExecuteRequest,
  QlhvImportExecuteResult,
  QlhvImportPlan,
  QlhvImportRequest,
  QlhvImportSourceKind,
  QlhvOperationHistoryItem,
  QlhvOperationsRowCounts,
  QlhvOperationsStatus,
  QlhvRefreshBackupRequest,
  QlhvRefreshBackupResult,
} from './types';
import { apiFetch } from '../../api/apiFetch';
import { API_BASE } from '../../api/apiBase';

export async function getQlhvOperationsStatus(
  sourceType: QlhvImportSourceKind,
  signal?: AbortSignal,
): Promise<QlhvOperationsStatus> {
  const query = new URLSearchParams({ sourceType });
  const response = await fetchSafely(
    `${API_BASE}/dong-bo-v2/qlhv/operations/status?${query}`,
    { method: 'GET', headers: { Accept: 'application/json' }, signal },
  );

  if (!response.ok) {
    throw new Error(await getSafeErrorMessage(response, 'Không thể đọc trạng thái vận hành CSĐT.'));
  }

  const payload = await tryReadJson<unknown>(response);
  if (!isOperationsStatus(payload)) {
    throw new Error('Backend không trả trạng thái vận hành hợp lệ.');
  }
  return payload;
}

export async function getQlhvOperationsHistory(
  sourceType: QlhvImportSourceKind,
  signal?: AbortSignal,
): Promise<QlhvOperationHistoryItem[]> {
  const query = new URLSearchParams({ sourceType });
  const response = await fetchSafely(
    `${API_BASE}/dong-bo-v2/qlhv/operations/history?${query}`,
    { method: 'GET', headers: { Accept: 'application/json' }, signal },
  );

  if (!response.ok) {
    throw new Error(await getSafeErrorMessage(response, 'Không thể đọc lịch sử vận hành CSĐT.'));
  }

  const payload = await tryReadJson<unknown>(response);
  if (!Array.isArray(payload) || !payload.every(isOperationHistoryItem)) {
    throw new Error('Backend không trả lịch sử vận hành hợp lệ.');
  }
  return payload;
}

export async function refreshQlhvBackup(
  request: QlhvRefreshBackupRequest,
  signal?: AbortSignal,
): Promise<QlhvRefreshBackupResult> {
  const response = await fetchSafely(
    `${API_BASE}/dong-bo-v2/qlhv/operations/refresh-backup`,
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
  if (!response.ok) {
    throw new Error(await getSafeErrorMessage(
      response,
      getWriteFallback(
        response.status,
        'Nguồn đang có thao tác khác hoặc yêu cầu refresh bị chặn.',
        'Không thể bắt đầu làm mới database BAK.',
      ),
    ));
  }
  if (!isRefreshBackupResult(payload)) {
    throw new Error('Backend không trả kết quả bắt đầu refresh hợp lệ.');
  }
  return payload;
}

export async function getQlhvImportDiagnostics(
  request: QlhvImportRequest,
  signal?: AbortSignal,
): Promise<QlhvImportDiagnostics> {
  const response = await fetchSafely(
    `${API_BASE}/dong-bo-v2/qlhv/import-diagnostics?${buildQuery(request)}`,
    { method: 'GET', headers: { Accept: 'application/json' }, signal },
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
    { method: 'GET', headers: { Accept: 'application/json' }, signal },
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
      getWriteFallback(
        response.status,
        'Yêu cầu đồng bộ đã bị backend chặn.',
        'Không thể thực hiện đồng bộ dữ liệu CSĐT.',
      ),
    ));
  }

  throw new Error('Backend không trả kết quả đồng bộ hợp lệ.');
}

function buildQuery(request: QlhvImportRequest): string {
  return new URLSearchParams({
    sourceProfileCode: request.sourceProfileCode,
    maCSDT: request.maCSDT,
  }).toString();
}

function getWriteFallback(status: number, conflictFallback: string, fallback: string): string {
  if (status === 401) {
    return 'Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại.';
  }
  if (status === 403) {
    return 'Bạn không có quyền thực hiện.';
  }
  return status === 409 ? conflictFallback : fallback;
}

async function fetchSafely(input: RequestInfo | URL, init: RequestInit): Promise<Response> {
  try {
    return await apiFetch(input, init);
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

function isRefreshBackupResult(value: unknown): value is QlhvRefreshBackupResult {
  return isRecord(value)
    && typeof value.operationId === 'string'
    && isSourceKind(value.sourceType)
    && typeof value.status === 'string'
    && typeof value.message === 'string';
}

function isOperationsStatus(value: unknown): value is QlhvOperationsStatus {
  if (!isRecord(value) || !isSourceKind(value.sourceType)) {
    return false;
  }

  return typeof value.liveDatabaseName === 'string'
    && typeof value.backupDatabaseName === 'string'
    && typeof value.maCSDT === 'string'
    && isSourceProfileCode(value.sourceProfileCode)
    && isOperationState(value.state)
    && isNullableString(value.activeOperationId)
    && isNullableString(value.backupLastRefreshTimeUtc)
    && isNullableString(value.backupSnapshotToken)
    && isOperationsRowCounts(value.liveRows)
    && isOperationsRowCounts(value.backupRows)
    && isFiniteNumber(value.targetActiveRows)
    && isNullableString(value.lastSyncTimeUtc)
    && isNullableString(value.lastError)
    && typeof value.dryRun === 'boolean'
    && typeof value.targetWritesEnabled === 'boolean'
    && typeof value.currentUserRole === 'string'
    && typeof value.writeAuthorized === 'boolean'
    && isStringArray(value.refreshBlockers)
    && isStringArray(value.syncBlockers)
    && typeof value.canRefresh === 'boolean'
    && typeof value.canSync === 'boolean';
}

function isOperationsRowCounts(value: unknown): value is QlhvOperationsRowCounts {
  return isRecord(value)
    && isFiniteNumber(value.nguoiLX)
    && isFiniteNumber(value.nguoiLXHoSo)
    && isFiniteNumber(value.khoaHoc);
}

function isOperationHistoryItem(value: unknown): value is QlhvOperationHistoryItem {
  return isRecord(value)
    && typeof value.operationId === 'string'
    && isSourceKind(value.sourceType)
    && (value.operationType === 'REFRESH_BACKUP' || value.operationType === 'FULL_SYNC')
    && typeof value.status === 'string'
    && isNullableString(value.startedAtUtc)
    && isNullableString(value.completedAtUtc)
    && hasFiniteNumbers(value, [
      'sourceRows',
      'insertedRows',
      'updatedRows',
      'reactivatedRows',
      'softDeletedRows',
      'skippedRows',
    ])
    && isNullableString(value.snapshotToken)
    && isNullableString(value.errorMessage)
    && isNullableString(value.detailJson);
}

function isExecuteResult(value: unknown): value is QlhvImportExecuteResult {
  if (!isRecord(value)) {
    return false;
  }

  const entityCountsAreValid = ['hocVien', 'khoaHoc', 'giaoVien']
    .every((key) => value[key] === undefined || isImportEntityCounts(value[key]));

  return typeof value.executed === 'boolean'
    && typeof value.status === 'string'
    && typeof value.message === 'string'
    && isImportPlan(value.plan)
    && entityCountsAreValid
    && hasFiniteNumbers(value, [
      'insertedHocVienRows',
      'updatedHocVienRows',
      'reactivatedHocVienRows',
      'softDeletedHocVienRows',
      'skippedHocVienRows',
    ]);
}

function isImportPlan(value: unknown): value is QlhvImportPlan {
  if (!isRecord(value) || !hasImportIdentity(value)) {
    return false;
  }

  return typeof value.isReadOnly === 'boolean'
    && typeof value.backupSnapshotToken === 'string'
    && typeof value.generatedAtUtc === 'string'
    && typeof value.executable === 'boolean'
    && typeof value.sourceProfileConstraintExists === 'boolean'
    && typeof value.sourceProfileAllowedByConstraint === 'boolean'
    && isStringArray(value.blockers)
    && isStringArray(value.warnings)
    && isImportEntityCounts(value.hocVien)
    && isImportEntityCounts(value.khoaHoc)
    && isImportEntityCounts(value.giaoVien)
    && isFiniteNumber(value.duplicateSourceKeys)
    && isFiniteNumber(value.relationConflicts)
    && hasFiniteNumbers(value, [
      'sourceHocVienRows',
      'sourceDistinctMaDkRows',
      'duplicateSourceMaDkRows',
      'sourceKhoaHocRows',
      'currentAppHocVienRows',
      'currentAppKhoaHocRows',
      'targetRowsForSourceProfile',
      'targetExactIdentityMatches',
      'targetMaDkConflictsOtherProfiles',
      'softDeletedIdentityConflicts',
      'plannedInsertHocVienRows',
      'plannedUpdateHocVienRows',
      'plannedReactivateHocVienRows',
      'plannedSoftDeleteHocVienRows',
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
    && typeof value.executable === 'boolean'
    && typeof value.sourceProfileConstraintExists === 'boolean'
    && typeof value.sourceProfileAllowedByConstraint === 'boolean'
    && isStringArray(value.blockers)
    && isStringArray(value.warnings)
    && isImportEntityCounts(value.hocVien)
    && isImportEntityCounts(value.khoaHoc)
    && isImportEntityCounts(value.giaoVien)
    && isFiniteNumber(value.duplicateSourceKeys)
    && isFiniteNumber(value.relationConflicts)
    && hasFiniteNumbers(value, [
      'sourceHocVienRows',
      'sourceDistinctMaDkRows',
      'duplicateSourceMaDkRows',
      'currentAppHocVienRows',
      'targetRowsForSourceProfile',
      'targetExactIdentityMatches',
      'targetMaDkConflictsOtherProfiles',
      'softDeletedIdentityConflicts',
      'plannedInsertHocVienRows',
      'plannedUpdateHocVienRows',
      'plannedReactivateHocVienRows',
      'plannedSoftDeleteHocVienRows',
      'plannedSkipHocVienRows',
      'plannedUpsertHocVienRows',
    ]);
}

function isImportEntityCounts(value: unknown): value is QlhvImportEntityCounts {
  return isRecord(value)
    && hasFiniteNumbers(value, [
      'sourceRows',
      'insert',
      'update',
      'reactivate',
      'softDelete',
      'skip',
      'duplicateSourceKeys',
    ]);
}

function hasImportIdentity(value: Record<string, unknown>): boolean {
  return typeof value.sourceDatabaseName === 'string'
    && typeof value.sourceProfileCode === 'string'
    && typeof value.maCSDT === 'string'
    && (value.maKhoaHoc === null || typeof value.maKhoaHoc === 'string');
}

function hasFiniteNumbers(value: Record<string, unknown>, keys: string[]): boolean {
  return keys.every((key) => isFiniteNumber(value[key]));
}

function isFiniteNumber(value: unknown): value is number {
  return typeof value === 'number' && Number.isFinite(value);
}

function isNullableString(value: unknown): value is string | null {
  return value === null || typeof value === 'string';
}

function isStringArray(value: unknown): value is string[] {
  return Array.isArray(value) && value.every((item) => typeof item === 'string');
}

function isSourceKind(value: unknown): value is QlhvImportSourceKind {
  return value === 'OTO' || value === 'MOTO';
}

function isSourceProfileCode(value: unknown): value is 'CSDT_OTO' | 'CSDT_MOTO' {
  return value === 'CSDT_OTO' || value === 'CSDT_MOTO';
}

function isOperationState(value: unknown): value is QlhvOperationsStatus['state'] {
  return value === 'idle'
    || value === 'refreshing'
    || value === 'syncing'
    || value === 'succeeded'
    || value === 'failed';
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return !!value && typeof value === 'object' && !Array.isArray(value);
}
