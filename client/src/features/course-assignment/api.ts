import { API_BASE } from '../../api/apiBase';
import { apiFetch } from '../../api/apiFetch';
import type {
  AssignmentConfirmRequest,
  AssignmentConfirmResult,
  AssignmentImportConfirmResult,
  AssignmentImportPreview,
  AssignmentPreview,
  AssignmentPreviewRequest,
  AssignmentHistoryItem,
  CourseAssignmentDetail,
  CourseAuditItem,
  CourseDetailQuery,
  CourseListQuery,
  DownloadResult,
  GiaoVienHoSoCommand,
  GiaoVienHoSoHistory,
  GiaoVienHoSoItem,
  GiaoVienSourceItem,
  GroupDefaultsPreviewRequest,
  KhoaHocListItem,
  ListQuery,
  PagedResult,
  TrainingGroup,
  TrainingGroupCommand,
  XeTapSourceItem,
} from './types';

export class AssignmentApiError extends Error {
  readonly status: number;
  readonly code: string | null;
  readonly correlationId: string | null;

  constructor(message: string, status: number, code: string | null, correlationId: string | null) {
    super(message);
    this.name = 'AssignmentApiError';
    this.status = status;
    this.code = code;
    this.correlationId = correlationId;
  }

  get isConcurrencyConflict(): boolean {
    return this.status === 409;
  }
}

export async function searchSourceTeachers(
  query: ListQuery,
  signal?: AbortSignal,
): Promise<PagedResult<GiaoVienSourceItem>> {
  return getJson(`/giao-vien?${toQuery(query)}`, signal);
}

export async function searchDossierReceivers(
  query: ListQuery,
  signal?: AbortSignal,
): Promise<PagedResult<GiaoVienHoSoItem>> {
  return getJson(`/giao-vien-ho-so?${toQuery(query)}`, signal);
}

export async function createDossierReceiver(
  command: GiaoVienHoSoCommand,
): Promise<GiaoVienHoSoItem> {
  return sendJson('/giao-vien-ho-so', 'POST', command);
}

export async function updateDossierReceiver(
  id: number,
  command: GiaoVienHoSoCommand,
): Promise<GiaoVienHoSoItem> {
  return sendJson(`/giao-vien-ho-so/${id}`, 'PUT', command);
}

export async function inactivateDossierReceiver(
  item: GiaoVienHoSoItem,
  reason: string,
): Promise<GiaoVienHoSoItem> {
  return sendJson(`/giao-vien-ho-so/${item.giaoVienHsId}/inactive`, 'POST', {
    rowVersion: item.rowVersion,
    reason,
  });
}

export async function deleteDossierReceiver(
  item: GiaoVienHoSoItem,
  reason: string,
): Promise<void> {
  await sendJson(`/giao-vien-ho-so/${item.giaoVienHsId}`, 'DELETE', {
    rowVersion: item.rowVersion,
    reason,
  }, false);
}

export async function getDossierReceiverHistory(
  id: number,
  signal?: AbortSignal,
): Promise<GiaoVienHoSoHistory> {
  return getJson(`/giao-vien-ho-so/${id}/history`, signal);
}

export async function searchVehicles(
  query: ListQuery,
  signal?: AbortSignal,
): Promise<PagedResult<XeTapSourceItem>> {
  return getJson(`/xe-tap-lai?${toQuery(query)}`, signal);
}

export async function searchCourses(
  query: CourseListQuery,
  signal?: AbortSignal,
): Promise<PagedResult<KhoaHocListItem>> {
  return getJson(`/khoa-hoc?${toQuery(query)}`, signal);
}

export async function getCourseAssignmentDetail(
  courseId: number,
  query: CourseDetailQuery,
  signal?: AbortSignal,
): Promise<CourseAssignmentDetail> {
  const url = `/khoa-hoc/${courseId}/chi-tiet-phan-cong?${toQuery({
    studentKeyword: query.keyword,
    groupId: query.groupId,
    unassignedOnly: query.unassignedOnly,
    page: query.page,
    pageSize: query.pageSize,
  })}`;
  return getJson(url, signal);
}

export async function createTrainingGroup(
  courseId: number,
  command: TrainingGroupCommand,
): Promise<TrainingGroup> {
  return sendJson(`/khoa-hoc/${courseId}/nhom-dao-tao`, 'POST', command);
}

export async function updateTrainingGroup(
  courseId: number,
  groupId: number,
  command: TrainingGroupCommand,
): Promise<TrainingGroup> {
  return sendJson(`/khoa-hoc/${courseId}/nhom-dao-tao/${groupId}`, 'PUT', command);
}

export async function inactivateTrainingGroup(
  courseId: number,
  group: TrainingGroup,
  reason: string,
): Promise<TrainingGroup> {
  return sendJson(
    `/khoa-hoc/${courseId}/nhom-dao-tao/${group.groupId}/inactive`,
    'POST',
    { rowVersion: group.rowVersion, reason },
  );
}

export async function previewGroupDefaults(
  groupId: number,
  request: GroupDefaultsPreviewRequest,
): Promise<AssignmentPreview> {
  return sendJson(`/nhom-dao-tao/${groupId}/defaults/preview`, 'POST', request);
}

export async function confirmGroupDefaults(
  groupId: number,
  request: AssignmentConfirmRequest,
): Promise<AssignmentConfirmResult> {
  return sendJson(`/nhom-dao-tao/${groupId}/defaults/confirm`, 'POST', request);
}

export async function previewAssignment(
  request: AssignmentPreviewRequest,
): Promise<AssignmentPreview> {
  return sendJson('/phan-cong/preview', 'POST', request);
}

export async function confirmAssignment(
  request: AssignmentConfirmRequest,
): Promise<AssignmentConfirmResult> {
  return sendJson('/phan-cong/confirm', 'POST', request);
}

export async function getStudentAssignmentHistory(
  hocVienId: number,
  signal?: AbortSignal,
): Promise<AssignmentHistoryItem[]> {
  return getJson(`/hoc-vien/${hocVienId}/phan-cong/history`, signal);
}

export async function getCourseAssignmentHistory(
  courseId: number,
  page: number,
  pageSize: number,
  signal?: AbortSignal,
): Promise<PagedResult<CourseAuditItem>> {
  return getJson(
    `/khoa-hoc/${courseId}/phan-cong/history?${toQuery({ page, pageSize })}`,
    signal,
  );
}

export async function exportCourseAssignments(courseId: number): Promise<DownloadResult> {
  return download(`/khoa-hoc/${courseId}/phan-cong/export`, 'PhanCongHocVien.xlsx');
}

export async function downloadAssignmentImportTemplate(
  courseId: number,
): Promise<DownloadResult> {
  return download(
    `/khoa-hoc/${courseId}/phan-cong/import/template`,
    'MauNhapPhanCongHocVien.xlsx',
  );
}

export async function previewAssignmentImport(
  courseId: number,
  sourceProfileCode: string,
  file: File,
): Promise<AssignmentImportPreview> {
  const form = new FormData();
  form.set('file', file);
  form.set('sourceProfileCode', sourceProfileCode);
  const response = await apiFetch(
    `${API_BASE}/khoa-hoc/${courseId}/phan-cong/import/preview`,
    {
      method: 'POST',
      headers: { Accept: 'application/json' },
      body: form,
    },
  );
  return readResponse<AssignmentImportPreview>(response, 'Không thể kiểm tra tệp Excel.');
}

export async function confirmAssignmentImport(
  courseId: number,
  request: AssignmentConfirmRequest,
): Promise<AssignmentImportConfirmResult> {
  return sendJson(
    `/khoa-hoc/${courseId}/phan-cong/import/confirm`,
    'POST',
    request,
  );
}

export async function downloadAssignmentImportResult(
  courseId: number,
  sessionId: number,
): Promise<DownloadResult> {
  return download(
    `/khoa-hoc/${courseId}/phan-cong/import/${sessionId}/result`,
    'KetQuaNhapPhanCong.xlsx',
  );
}

async function getJson<T>(path: string, signal?: AbortSignal): Promise<T> {
  const response = await apiFetch(`${API_BASE}${path}`, {
    method: 'GET',
    headers: { Accept: 'application/json' },
    signal,
  });
  return readResponse<T>(response, 'Không thể tải dữ liệu.');
}

async function sendJson<T>(
  path: string,
  method: 'POST' | 'PUT' | 'DELETE',
  body: unknown,
  expectBody = true,
): Promise<T> {
  const response = await apiFetch(`${API_BASE}${path}`, {
    method,
    headers: {
      Accept: 'application/json',
      'Content-Type': 'application/json',
    },
    body: JSON.stringify(body),
  });

  if (!expectBody && response.ok) {
    return undefined as T;
  }
  return readResponse<T>(response, 'Không thể lưu thay đổi.');
}

async function download(path: string, fallbackName: string): Promise<DownloadResult> {
  const response = await apiFetch(`${API_BASE}${path}`, {
    method: 'GET',
    headers: {
      Accept: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
    },
  });
  if (!response.ok) {
    throw await createApiError(response, 'Không thể tải tệp Excel.');
  }
  return {
    blob: await response.blob(),
    fileName: readFileName(response.headers.get('Content-Disposition'), fallbackName),
  };
}

async function readResponse<T>(response: Response, fallback: string): Promise<T> {
  if (!response.ok) {
    throw await createApiError(response, fallback);
  }
  if (response.status === 204) {
    return undefined as T;
  }
  return (await response.json()) as T;
}

async function createApiError(response: Response, fallback: string): Promise<AssignmentApiError> {
  let payload: Record<string, unknown> | null = null;
  try {
    const candidate = await response.clone().json() as unknown;
    payload = isRecord(candidate) ? candidate : null;
  } catch {
    payload = null;
  }
  const rawMessage = firstString(payload, ['message', 'detail', 'title']) ?? fallback;
  const message = sanitizeMessage(rawMessage, fallback);
  return new AssignmentApiError(
    response.status === 409
      ? `${message} Dữ liệu đã thay đổi; hãy tải lại trước khi tiếp tục.`
      : `${message} (mã ${response.status}).`,
    response.status,
    firstString(payload, ['code', 'errorCode']),
    firstString(payload, ['correlationId', 'traceId']),
  );
}

function toQuery<T extends object>(values: T): string {
  const query = new URLSearchParams();
  for (const [key, value] of Object.entries(values as Record<string, unknown>)) {
    if (value === undefined || value === null || value === '') continue;
    query.set(key, String(value));
  }
  return query.toString();
}

function firstString(
  value: Record<string, unknown> | null,
  keys: string[],
): string | null {
  if (!value) return null;
  for (const key of keys) {
    if (typeof value[key] === 'string' && value[key]) return value[key];
  }
  return null;
}

function sanitizeMessage(value: string, fallback: string): string {
  const message = value.replace(/\s+/g, ' ').trim();
  return !message
    || message.length > 400
    || /\bat\s+\S+\s*\(|stack trace|password(hash)?|connection string/i.test(message)
    ? fallback
    : message;
}

function readFileName(disposition: string | null, fallback: string): string {
  if (!disposition) return fallback;
  const utf8Match = /filename\*=UTF-8''([^;]+)/i.exec(disposition);
  if (utf8Match?.[1]) {
    try {
      return decodeURIComponent(utf8Match[1]);
    } catch {
      return fallback;
    }
  }
  return /filename="?([^";]+)"?/i.exec(disposition)?.[1] ?? fallback;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return !!value && typeof value === 'object' && !Array.isArray(value);
}
