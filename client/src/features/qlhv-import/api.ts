import type {
  QlhvAutoSyncRunResult,
  QlhvAutoSyncSourceResult,
  QlhvAutoSyncStatus,
  QlhvImportDiagnostics,
  QlhvImportDomain,
  QlhvImportDomainResult,
  QlhvImportDomainStatus,
  QlhvImportEntityCounts,
  QlhvImportExecuteOutcome,
  QlhvImportExecuteRequest,
  QlhvImportExecuteResult,
  QlhvImportPlan,
  QlhvImportPhotoCounts,
  QlhvImportRequest,
  QlhvImportSourceKind,
  QlhvOperationHistoryItem,
  QlhvOperationsRowCounts,
  QlhvOperationsStatus,
  QlhvPhotoProcessingItem,
  QlhvPhotoProcessingPage,
  QlhvPhotoProcessingQuery,
  QlhvPhotoProcessingStatus,
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

export async function getQlhvAutoSyncStatus(
  runId?: string | null,
  signal?: AbortSignal,
): Promise<QlhvAutoSyncStatus> {
  const parameters = new URLSearchParams();
  if (runId) {
    parameters.set('runId', runId);
  }
  const query = parameters.toString();
  const response = await fetchSafely(
    `${API_BASE}/dong-bo-v2/qlhv/operations/auto-sync/status${query ? `?${query}` : ''}`,
    { method: 'GET', headers: { Accept: 'application/json' }, signal },
  );
  if (!response.ok) {
    throw new Error(await getSafeErrorMessage(
      response,
      'Không thể đọc trạng thái Auto Sync.',
    ));
  }

  const payload = await tryReadJson<unknown>(response);
  if (!isAutoSyncStatus(payload)) {
    throw new Error('Backend không trả trạng thái Auto Sync hợp lệ.');
  }
  return payload;
}

export async function runQlhvAutoSync(
  signal?: AbortSignal,
): Promise<QlhvAutoSyncRunResult> {
  const response = await fetchSafely(
    `${API_BASE}/dong-bo-v2/qlhv/operations/auto-sync`,
    {
      method: 'POST',
      headers: { Accept: 'application/json' },
      signal,
    },
  );
  const payload = await tryReadJson<unknown>(response);
  if (!isAutoSyncRunResult(payload)) {
    throw new Error(await getSafeErrorMessage(
      response,
      'Backend không trả kết quả Auto Sync hợp lệ.',
    ));
  }
  if (!response.ok || !payload.accepted) {
    throw new Error(payload.message || getWriteFallback(
      response.status,
      'Auto Sync đang chạy hoặc có thao tác cùng nguồn.',
      'Không thể bắt đầu Auto Sync.',
    ));
  }
  return payload;
}

export async function getQlhvPhotoProcessingPage(
  query: QlhvPhotoProcessingQuery,
  signal?: AbortSignal,
): Promise<QlhvPhotoProcessingPage> {
  const parameters = new URLSearchParams({
    page: String(query.page),
    pageSize: String(query.pageSize),
  });
  if (query.sourceProfileCode) {
    parameters.set('sourceProfileCode', query.sourceProfileCode);
  }
  if (query.status) {
    parameters.set('status', query.status);
  }
  if (query.reviewRequired !== undefined) {
    parameters.set('reviewRequired', String(query.reviewRequired));
  }

  const response = await fetchSafely(
    `${API_BASE}/dong-bo-v2/qlhv/photos?${parameters}`,
    { method: 'GET', headers: { Accept: 'application/json' }, signal },
  );
  if (!response.ok) {
    throw new Error(await getSafeErrorMessage(
      response,
      'Không thể tải danh sách xử lý ảnh thẻ.',
    ));
  }
  const payload = await tryReadJson<unknown>(response);
  if (!isPhotoProcessingPage(payload)) {
    throw new Error('Backend không trả danh sách xử lý ảnh hợp lệ.');
  }
  return payload;
}

export async function approveQlhvProcessedPhoto(
  id: number,
  signal?: AbortSignal,
): Promise<QlhvPhotoProcessingItem> {
  return runPhotoAction(id, 'approve', signal);
}

export async function reprocessQlhvPhoto(
  id: number,
  signal?: AbortSignal,
): Promise<QlhvPhotoProcessingItem> {
  return runPhotoAction(id, 'reprocess', signal);
}

export function getQlhvPhotoPreviewUrl(
  id: number,
  kind: 'source' | 'output',
  version?: string | number | null,
): string {
  const query = version === undefined || version === null
    ? ''
    : `?v=${encodeURIComponent(String(version))}`;
  return `${API_BASE}/dong-bo-v2/qlhv/photos/${id}/${kind}-preview${query}`;
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

async function runPhotoAction(
  id: number,
  action: 'approve' | 'reprocess',
  signal?: AbortSignal,
): Promise<QlhvPhotoProcessingItem> {
  const response = await fetchSafely(
    `${API_BASE}/dong-bo-v2/qlhv/photos/${id}/${action}`,
    { method: 'POST', headers: { Accept: 'application/json' }, signal },
  );
  const payload = await tryReadJson<unknown>(response);
  if (!response.ok) {
    throw new Error(await getSafeErrorMessage(
      response,
      getWriteFallback(
        response.status,
        'Ảnh đang được xử lý hoặc trạng thái đã thay đổi.',
        action === 'approve'
          ? 'Không thể chấp nhận ảnh.'
          : 'Không thể yêu cầu xử lý lại ảnh.',
      ),
    ));
  }
  if (!isPhotoProcessingItem(payload)) {
    throw new Error('Backend không trả trạng thái ảnh hợp lệ.');
  }
  return payload;
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
    && (
      value.operationType === 'REFRESH_BACKUP'
      || value.operationType === 'FULL_SYNC'
      || value.operationType === 'AUTO_SYNC'
      || value.operationType === 'PHOTO_PROCESSING'
    )
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
    && isNullableString(value.detailJson)
    && (value.actor === undefined || isNullableString(value.actor));
}

function isAutoSyncStatus(value: unknown): value is QlhvAutoSyncStatus {
  if (!isRecord(value)) {
    return false;
  }
  return typeof value.enabled === 'boolean'
    && typeof value.found === 'boolean'
    && typeof value.runOnServerStartup === 'boolean'
    && typeof value.refreshBackupBeforeSync === 'boolean'
    && isAutoSyncState(value.state)
    && isNullableString(value.runId)
    && isNullableString(value.activeRunId)
    && isNullableString(value.triggerType)
    && isNullableString(value.actor)
    && (value.currentSourceType === null || isSourceKind(value.currentSourceType))
    && isNullableString(value.startedAtUtc)
    && isNullableString(value.completedAtUtc)
    && isNullableString(value.lastSuccessfulSyncUtc)
    && (value.oto === null || isAutoSyncSourceResult(value.oto))
    && (value.moto === null || isAutoSyncSourceResult(value.moto))
    && isNullableString(value.lastError);
}

function isAutoSyncSourceResult(value: unknown): value is QlhvAutoSyncSourceResult {
  return isRecord(value)
    && isSourceKind(value.sourceType)
    && typeof value.status === 'string'
    && isNullableString(value.refreshOperationId)
    && isNullableString(value.syncOperationId)
    && isNullableString(value.startedAtUtc)
    && isNullableString(value.completedAtUtc)
    && isNullableString(value.message);
}

function isAutoSyncRunResult(value: unknown): value is QlhvAutoSyncRunResult {
  return isRecord(value)
    && typeof value.accepted === 'boolean'
    && typeof value.isConflict === 'boolean'
    && typeof value.isUnavailable === 'boolean'
    && isNullableString(value.runId)
    && typeof value.status === 'string'
    && typeof value.message === 'string';
}

function isAutoSyncState(value: unknown): value is QlhvAutoSyncStatus['state'] {
  return value === 'disabled'
    || value === 'not-found'
    || value === 'idle'
    || value === 'queued'
    || value === 'running'
    || value === 'succeeded'
    || value === 'partial-success'
    || value === 'partial-failed'
    || value === 'failed';
}

function isPhotoProcessingPage(value: unknown): value is QlhvPhotoProcessingPage {
  if (!isRecord(value) || !Array.isArray(value.items)) {
    return false;
  }
  return value.items.every(isPhotoProcessingItem)
    && hasFiniteNumbers(value, ['page', 'pageSize', 'totalItems', 'totalPages'])
    && typeof value.engineReady === 'boolean'
    && isNullableString(value.readinessMessage)
    && isRecord(value.counts)
    && hasFiniteNumbers(value.counts, [
      'total',
      'pending',
      'processing',
      'succeeded',
      'reviewRequired',
      'failed',
      'approved',
    ]);
}

function isPhotoProcessingItem(value: unknown): value is QlhvPhotoProcessingItem {
  if (!isRecord(value)) {
    return false;
  }
  return isFiniteNumber(value.id)
    && isSourceProfileCode(value.sourceProfileCode)
    && typeof value.sourceMaDK === 'string'
    && isNullableString(value.studentName)
    && isNullableString(value.maKhoaHoc)
    && isNullableString(value.sourceImagePath)
    && isNullableString(value.outputImagePath)
    && typeof value.sourcePathStatus === 'string'
    && typeof value.sourcePathKind === 'string'
    && isNullableString(value.sourcePreviewUrl)
    && isNullableString(value.outputPreviewUrl)
    && isPhotoProcessingStatus(value.processingStatus)
    && (value.processingConfidence === null || isFiniteNumber(value.processingConfidence))
    && isNullableString(value.processedAtUtc)
    && isNullableString(value.errorMessage)
    && typeof value.reviewRequired === 'boolean'
    && isNullableString(value.approvedAtUtc)
    && isNullableString(value.approvedBy);
}

function isPhotoProcessingStatus(value: unknown): value is QlhvPhotoProcessingStatus {
  return value === 'PENDING'
    || value === 'PROCESSING'
    || value === 'SUCCEEDED'
    || value === 'REVIEW_REQUIRED'
    || value === 'FAILED'
    || value === 'APPROVED';
}

function isExecuteResult(value: unknown): value is QlhvImportExecuteResult {
  if (!isRecord(value)) {
    return false;
  }

  const entityCountsAreValid = ['hocVien', 'khoaHoc', 'giaoVien', 'khoaHocGiaoVien']
    .every((key) => value[key] === undefined || isImportEntityCounts(value[key]));

  return typeof value.executed === 'boolean'
    && typeof value.status === 'string'
    && typeof value.message === 'string'
    && isImportPlan(value.plan)
    && entityCountsAreValid
    && Array.isArray(value.domainResults)
    && value.domainResults.every(isImportDomainResult)
    && (value.photo === undefined || isImportPhotoCounts(value.photo))
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
    && isImportDomainStatus(value.hocVienStatus)
    && isImportDomainStatus(value.khoaHocStatus)
    && isImportDomainStatus(value.giaoVienStatus)
    && isImportDomainStatus(value.relationStatus)
    && typeof value.sourceProfileConstraintExists === 'boolean'
    && typeof value.sourceProfileAllowedByConstraint === 'boolean'
    && isStringArray(value.blockers)
    && isStringArray(value.warnings)
    && isStringArray(value.hocVienBlockers)
    && isStringArray(value.khoaHocBlockers)
    && isStringArray(value.giaoVienBlockers)
    && isStringArray(value.relationBlockers)
    && isStringArray(value.optionalWarnings)
    && isImportDomainArray(value.executableDomains)
    && isImportDomainArray(value.skippedDomains)
    && isImportEntityCounts(value.hocVien)
    && isImportEntityCounts(value.khoaHoc)
    && isImportEntityCounts(value.giaoVien)
    && isImportEntityCounts(value.khoaHocGiaoVien)
    && (value.photo === undefined || isImportPhotoCounts(value.photo))
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
    && isImportDomainStatus(value.hocVienStatus)
    && isImportDomainStatus(value.khoaHocStatus)
    && isImportDomainStatus(value.giaoVienStatus)
    && isImportDomainStatus(value.relationStatus)
    && typeof value.sourceProfileConstraintExists === 'boolean'
    && typeof value.sourceProfileAllowedByConstraint === 'boolean'
    && isStringArray(value.blockers)
    && isStringArray(value.warnings)
    && isStringArray(value.hocVienBlockers)
    && isStringArray(value.khoaHocBlockers)
    && isStringArray(value.giaoVienBlockers)
    && isStringArray(value.relationBlockers)
    && isStringArray(value.optionalWarnings)
    && isImportDomainArray(value.executableDomains)
    && isImportDomainArray(value.skippedDomains)
    && isImportEntityCounts(value.hocVien)
    && isImportEntityCounts(value.khoaHoc)
    && isImportEntityCounts(value.giaoVien)
    && isImportEntityCounts(value.khoaHocGiaoVien)
    && (value.photo === undefined || isImportPhotoCounts(value.photo))
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

function isImportDomainResult(value: unknown): value is QlhvImportDomainResult {
  return isRecord(value)
    && isImportDomain(value.domain)
    && typeof value.status === 'string'
    && isNullableString(value.message)
    && isImportEntityCounts(value.counts);
}

function isImportDomainArray(value: unknown): value is QlhvImportDomain[] {
  return Array.isArray(value) && value.every(isImportDomain);
}

function isImportDomain(value: unknown): value is QlhvImportDomain {
  return value === 'HOC_VIEN'
    || value === 'KHOA_HOC'
    || value === 'GIAO_VIEN'
    || value === 'KHOA_HOC_GIAO_VIEN';
}

function isImportDomainStatus(value: unknown): value is QlhvImportDomainStatus {
  return value === 'EXECUTABLE'
    || value === 'BLOCKED'
    || value === 'SKIPPED_SCHEMA_NOT_READY'
    || value === 'SKIPPED_SOURCE_NOT_READY'
    || value === 'SKIPPED_DEPENDENCY_NOT_READY'
    || value === 'SUCCESS'
    || value === 'FAILED'
    || value === 'NO_OP';
}

function isImportPhotoCounts(value: unknown): value is QlhvImportPhotoCounts {
  return isRecord(value)
    && hasFiniteNumbers(value, [
      'found',
      'missing',
      'pending',
      'toReprocess',
      'reviewRequired',
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
    || value === 'partial-success'
    || value === 'failed';
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return !!value && typeof value === 'object' && !Array.isArray(value);
}
