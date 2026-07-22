import type {
  QlhvImportExecuteRequest,
  QlhvImportPlan,
  QlhvImportRequest,
  QlhvImportSnapshot,
  QlhvImportSourceKind,
  QlhvOperationsStatus,
  QlhvRefreshBackupRequest,
} from './types';

export const QLHV_IMPORT_SOURCE_KINDS: readonly QlhvImportSourceKind[] = ['OTO', 'MOTO'];

export const QLHV_IMPORT_SOURCES = {
  OTO: {
    label: 'Ô tô',
    liveDatabaseName: 'CSDL_OTO',
    backupDatabaseName: 'CSDL_OTO_BAK',
    sourceProfileCode: 'CSDT_OTO',
    maCSDT: '66029',
  },
  MOTO: {
    label: 'Mô tô',
    liveDatabaseName: 'CSDL_MOTO',
    backupDatabaseName: 'CSDL_MOTO_BAK',
    sourceProfileCode: 'CSDT_MOTO',
    maCSDT: '66030',
  },
} as const;

export function createImportRequest(sourceKind: QlhvImportSourceKind): QlhvImportRequest {
  const source = QLHV_IMPORT_SOURCES[sourceKind];
  return {
    sourceProfileCode: source.sourceProfileCode,
    maCSDT: source.maCSDT,
    maKhoaHoc: null,
  };
}

export function createRequestKey(request: QlhvImportRequest): string {
  return JSON.stringify([request.sourceProfileCode, request.maCSDT]);
}

export function requestsEqual(left: QlhvImportRequest, right: QlhvImportRequest): boolean {
  return createRequestKey(left) === createRequestKey(right);
}

export function planMatchesRequest(plan: QlhvImportPlan, request: QlhvImportRequest): boolean {
  return plan.sourceProfileCode === request.sourceProfileCode
    && plan.maCSDT === request.maCSDT
    && plan.maKhoaHoc === null
    && sourceDatabaseMatchesRequest(plan.sourceDatabaseName, request);
}

export function sourceDatabaseMatchesRequest(
  sourceDatabaseName: string,
  request: QlhvImportRequest,
): boolean {
  const expectedDatabaseName = getExpectedSourceDatabaseName(request.sourceProfileCode);
  return expectedDatabaseName !== null && sourceDatabaseName === expectedDatabaseName;
}

export function statusMatchesSource(
  status: QlhvOperationsStatus,
  sourceKind: QlhvImportSourceKind,
): boolean {
  const source = QLHV_IMPORT_SOURCES[sourceKind];
  return status.sourceType === sourceKind
    && status.liveDatabaseName === source.liveDatabaseName
    && status.backupDatabaseName === source.backupDatabaseName
    && status.sourceProfileCode === source.sourceProfileCode
    && status.maCSDT === source.maCSDT;
}

export function isOperationBusy(status: QlhvOperationsStatus | null | undefined): boolean {
  return status?.state === 'refreshing' || status?.state === 'syncing' || !!status?.activeOperationId;
}

export function isPlanSnapshotCurrent(
  plan: QlhvImportPlan,
  status: QlhvOperationsStatus | null | undefined,
): boolean {
  return !!status
    && !!plan.backupSnapshotToken
    && !!status.backupSnapshotToken
    && plan.backupSnapshotToken === status.backupSnapshotToken;
}

export function canOpenExecute(
  plan: QlhvImportSnapshot<QlhvImportPlan> | null,
  currentRequest: QlhvImportRequest,
  status: QlhvOperationsStatus | null | undefined,
  busy: boolean,
): boolean {
  return !!plan
    && !!status
    && requestsEqual(plan.request, currentRequest)
    && planMatchesRequest(plan.data, currentRequest)
    && statusMatchesRequest(status, currentRequest)
    && isPlanSnapshotCurrent(plan.data, status)
    && plan.data.executable
    && plan.data.blockers.length === 0
    && plan.data.sourceHocVienRows > 0
    && status.canSync
    && !isOperationBusy(status)
    && !busy;
}

export function buildExecuteRequest(
  plan: QlhvImportSnapshot<QlhvImportPlan> | null,
  currentRequest: QlhvImportRequest,
  status: QlhvOperationsStatus | null | undefined,
  busy: boolean,
): QlhvImportExecuteRequest | null {
  if (!plan
    || !canOpenExecute(plan, currentRequest, status, busy)) {
    return null;
  }

  return {
    ...plan.request,
    expectedSnapshotToken: plan.data.backupSnapshotToken,
  };
}

export function buildRefreshRequest(
  sourceKind: QlhvImportSourceKind,
): QlhvRefreshBackupRequest {
  return { sourceType: sourceKind };
}

export function getExecuteDisabledReason(
  plan: QlhvImportSnapshot<QlhvImportPlan> | null,
  currentRequest: QlhvImportRequest,
  status: QlhvOperationsStatus | null | undefined,
  busy: boolean,
): string | null {
  if (!status) {
    return 'Chưa đọc được trạng thái nguồn hiện tại.';
  }
  if (!statusMatchesRequest(status, currentRequest)) {
    return 'Mapping trạng thái backend không khớp nguồn cố định; thao tác đã bị khóa.';
  }
  if (isOperationBusy(status)) {
    return 'Nguồn này đang refresh hoặc đồng bộ. Vui lòng chờ thao tác hiện tại hoàn tất.';
  }
  if (!status.canSync) {
    return 'Backend hiện không cho phép đồng bộ nguồn này.';
  }
  if (!plan) {
    return 'Cần lập kế hoạch mới trước khi đồng bộ.';
  }
  if (!requestsEqual(plan.request, currentRequest) || !planMatchesRequest(plan.data, currentRequest)) {
    return 'Kế hoạch không khớp nguồn hiện tại. Vui lòng lập lại kế hoạch.';
  }
  if (!isPlanSnapshotCurrent(plan.data, status)) {
    return 'Snapshot BAK đã thay đổi hoặc chưa có token. Kế hoạch cũ đã hết hiệu lực.';
  }
  if (plan.data.sourceHocVienRows <= 0) {
    return 'Snapshot nguồn rỗng; không được phép đồng bộ hoặc xóa mềm phân vùng.';
  }
  if (plan.data.blockers.length > 0 || !plan.data.executable) {
    return 'Kế hoạch có điểm chặn và không thể thực hiện.';
  }
  if (busy) {
    return 'Đang xử lý yêu cầu khác cho nguồn này.';
  }
  return null;
}

export function getRefreshDisabledReason(
  status: QlhvOperationsStatus | null | undefined,
  sourceKind: QlhvImportSourceKind,
  busy: boolean,
): string | null {
  if (!status) {
    return 'Chưa đọc được trạng thái nguồn hiện tại.';
  }
  if (!statusMatchesSource(status, sourceKind)) {
    return 'Mapping trạng thái backend không khớp nguồn cố định.';
  }
  if (isOperationBusy(status) || busy) {
    return 'Nguồn này đang có thao tác vận hành.';
  }
  if (!status.canRefresh) {
    return 'Backend hiện không cho phép làm mới BAK.';
  }
  return null;
}

function statusMatchesRequest(status: QlhvOperationsStatus, request: QlhvImportRequest): boolean {
  const sourceKind = request.sourceProfileCode === 'CSDT_OTO' ? 'OTO' : 'MOTO';
  return statusMatchesSource(status, sourceKind)
    && status.sourceProfileCode === request.sourceProfileCode
    && status.maCSDT === request.maCSDT;
}

function getExpectedSourceDatabaseName(sourceProfileCode: string): string | null {
  const source = Object.values(QLHV_IMPORT_SOURCES)
    .find((candidate) => candidate.sourceProfileCode === sourceProfileCode);
  return source?.backupDatabaseName ?? null;
}
