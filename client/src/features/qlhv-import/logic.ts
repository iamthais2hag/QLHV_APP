import type {
  QlhvImportExecuteRequest,
  QlhvImportFormState,
  QlhvImportPlan,
  QlhvImportRequest,
  QlhvImportSnapshot,
  QlhvImportSourceKind,
} from './types';

export const QLHV_IMPORT_CONFIRM_TEXT = 'IMPORT QLHV CSĐT';

export const QLHV_IMPORT_SOURCES = {
  OTO: {
    label: 'Ô tô',
    sourceProfileCode: 'CSDT_OTO',
    maCSDT: '66029',
    sourceDatabaseName: 'CSDL_OTO_BAK',
  },
  MOTO: {
    label: 'Mô tô',
    sourceProfileCode: 'CSDT_MOTO',
    maCSDT: '66030',
    sourceDatabaseName: 'CSDL_MOTO_BAK',
  },
} as const;

export function createImportRequest(form: QlhvImportFormState): QlhvImportRequest {
  const source = QLHV_IMPORT_SOURCES[form.sourceKind];
  const maKhoaHoc = form.maKhoaHocInput.trim();

  return {
    sourceProfileCode: source.sourceProfileCode,
    maCSDT: source.maCSDT,
    maKhoaHoc: maKhoaHoc || null,
  };
}

export function createRequestKey(request: QlhvImportRequest): string {
  return JSON.stringify([
    request.sourceProfileCode,
    request.maCSDT,
    request.maKhoaHoc ?? '',
  ]);
}

export function requestsEqual(left: QlhvImportRequest, right: QlhvImportRequest): boolean {
  return createRequestKey(left) === createRequestKey(right);
}

export function planMatchesRequest(plan: QlhvImportPlan, request: QlhvImportRequest): boolean {
  return plan.sourceProfileCode === request.sourceProfileCode
    && plan.maCSDT === request.maCSDT
    && (plan.maKhoaHoc ?? '') === (request.maKhoaHoc ?? '')
    && sourceDatabaseMatchesRequest(plan.sourceDatabaseName, request);
}

export function sourceDatabaseMatchesRequest(
  sourceDatabaseName: string,
  request: QlhvImportRequest,
): boolean {
  const expectedDatabaseName = getExpectedSourceDatabaseName(request.sourceProfileCode);
  return expectedDatabaseName !== null && sourceDatabaseName === expectedDatabaseName;
}

export function canOpenExecute(
  plan: QlhvImportSnapshot<QlhvImportPlan> | null,
  currentRequest: QlhvImportRequest,
  busy: boolean,
): boolean {
  return !!plan
    && requestsEqual(plan.request, currentRequest)
    && planMatchesRequest(plan.data, currentRequest)
    && plan.data.executable
    && plan.data.blockers.length === 0
    && plan.data.sourceHocVienRows > 0
    && !busy;
}

export function buildExecuteRequest(
  plan: QlhvImportSnapshot<QlhvImportPlan> | null,
  currentRequest: QlhvImportRequest,
  confirmText: string,
  busy: boolean,
): QlhvImportExecuteRequest | null {
  if (!plan || !canOpenExecute(plan, currentRequest, busy) || confirmText !== QLHV_IMPORT_CONFIRM_TEXT) {
    return null;
  }

  return {
    ...plan.request,
    confirmText,
  };
}

export function getExecuteDisabledReason(
  plan: QlhvImportSnapshot<QlhvImportPlan> | null,
  currentRequest: QlhvImportRequest,
  busy: boolean,
): string | null {
  if (!plan) {
    return 'Cần lập kế hoạch cho lựa chọn hiện tại trước khi thực hiện.';
  }

  if (!requestsEqual(plan.request, currentRequest)) {
    return 'Biểu mẫu đã thay đổi. Vui lòng lập lại kế hoạch.';
  }

  if (!sourceDatabaseMatchesRequest(plan.data.sourceDatabaseName, currentRequest)) {
    const expectedDatabaseName = getExpectedSourceDatabaseName(currentRequest.sourceProfileCode);
    return `Database nguồn không khớp. Kế hoạch phải đọc từ ${expectedDatabaseName ?? 'database BAK đã cấu hình'}.`;
  }

  if (!planMatchesRequest(plan.data, currentRequest)) {
    return 'Kế hoạch backend trả về không khớp biểu mẫu hiện tại.';
  }

  if (plan.data.sourceHocVienRows <= 0) {
    return 'Snapshot nguồn không có học viên; không được phép thực hiện hoặc xóa mềm phân vùng.';
  }

  if (plan.data.blockers.length > 0 || !plan.data.executable) {
    return 'Kế hoạch đang có điểm chặn và không thể thực hiện.';
  }

  if (busy) {
    return 'Đang xử lý yêu cầu khác.';
  }

  return null;
}

export function isSourceKind(value: string): value is QlhvImportSourceKind {
  return value === 'OTO' || value === 'MOTO';
}

function getExpectedSourceDatabaseName(sourceProfileCode: string): string | null {
  const source = Object.values(QLHV_IMPORT_SOURCES)
    .find((candidate) => candidate.sourceProfileCode === sourceProfileCode);
  return source?.sourceDatabaseName ?? null;
}
