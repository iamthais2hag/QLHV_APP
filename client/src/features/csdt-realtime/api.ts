import { apiFetch } from '../../api/apiFetch';
import { API_BASE } from '../../api/apiBase';
import type {
  CsdtRealtimeActionResult,
  CsdtRealtimeBaselineRequest,
  CsdtRealtimeEnableRequest,
  CsdtRealtimeHistoryItem,
  CsdtRealtimeRetryRequest,
  CsdtRealtimeStreamCode,
  CsdtRealtimeStreamsResponse,
  CsdtRealtimeTombstone,
  CsdtRealtimeVehicleType,
  CsdtReverseExecuteRequest,
  CsdtReverseExecuteResult,
  CsdtReversePlan,
} from './types';

const REALTIME_API = `${API_BASE}/dong-bo-v2/csdt-realtime`;

export async function getCsdtRealtimeStreams(
  signal?: AbortSignal,
): Promise<CsdtRealtimeStreamsResponse> {
  const response = await fetchRealtime(`${REALTIME_API}/streams`, {
    method: 'GET',
    headers: { Accept: 'application/json' },
    signal,
  });
  const payload = await readJson<unknown>(response);
  if (!response.ok) {
    throw new Error(await responseMessage(response, payload, 'Không thể đọc trạng thái đồng bộ realtime.'));
  }
  if (!isStreamsResponse(payload)) {
    throw new Error('Máy chủ trả trạng thái đồng bộ realtime không hợp lệ.');
  }
  return payload;
}

export async function getCsdtRealtimeHistory(
  streamCode: CsdtRealtimeStreamCode,
  take = 50,
  signal?: AbortSignal,
): Promise<CsdtRealtimeHistoryItem[]> {
  const query = new URLSearchParams({ take: String(Math.min(Math.max(take, 1), 200)) });
  const response = await fetchRealtime(
    `${REALTIME_API}/streams/${encodeURIComponent(streamCode)}/history?${query}`,
    { method: 'GET', headers: { Accept: 'application/json' }, signal },
  );
  const payload = await readJson<unknown>(response);
  if (!response.ok) {
    throw new Error(await responseMessage(response, payload, 'Không thể đọc lịch sử đồng bộ realtime.'));
  }
  if (!Array.isArray(payload)) {
    throw new Error('Máy chủ trả lịch sử đồng bộ realtime không hợp lệ.');
  }
  return payload as CsdtRealtimeHistoryItem[];
}

export async function getCsdtRealtimeTombstones(
  streamCode: CsdtRealtimeStreamCode,
  take = 50,
  signal?: AbortSignal,
): Promise<CsdtRealtimeTombstone[]> {
  const query = new URLSearchParams({ take: String(Math.min(Math.max(take, 1), 200)) });
  const response = await fetchRealtime(
    `${REALTIME_API}/streams/${encodeURIComponent(streamCode)}/tombstones?${query}`,
    { method: 'GET', headers: { Accept: 'application/json' }, signal },
  );
  const payload = await readJson<unknown>(response);
  if (!response.ok) {
    throw new Error(await responseMessage(response, payload, 'Không thể đọc cảnh báo xóa từ nguồn.'));
  }
  if (!Array.isArray(payload)) {
    throw new Error('Máy chủ trả danh sách cảnh báo xóa không hợp lệ.');
  }
  return payload as CsdtRealtimeTombstone[];
}

export async function setCsdtRealtimeEnabled(
  streamCode: CsdtRealtimeStreamCode,
  request: CsdtRealtimeEnableRequest,
  signal?: AbortSignal,
): Promise<CsdtRealtimeActionResult> {
  return runAction(
    `${REALTIME_API}/streams/${encodeURIComponent(streamCode)}/enabled`,
    'PUT',
    request,
    'Không thể thay đổi trạng thái stream.',
    signal,
  );
}

export async function runCsdtRealtimeBaseline(
  streamCode: CsdtRealtimeStreamCode,
  request: CsdtRealtimeBaselineRequest,
  signal?: AbortSignal,
): Promise<CsdtRealtimeActionResult> {
  return runAction(
    `${REALTIME_API}/streams/${encodeURIComponent(streamCode)}/baseline`,
    'POST',
    request,
    'Không thể yêu cầu chạy baseline.',
    signal,
  );
}

export async function retryCsdtRealtimeStream(
  streamCode: CsdtRealtimeStreamCode,
  request: CsdtRealtimeRetryRequest,
  signal?: AbortSignal,
): Promise<CsdtRealtimeActionResult> {
  return runAction(
    `${REALTIME_API}/streams/${encodeURIComponent(streamCode)}/retry`,
    'POST',
    request,
    'Không thể yêu cầu thử lại stream.',
    signal,
  );
}

export async function getCsdtReversePlan(
  vehicleType: CsdtRealtimeVehicleType,
  maKhoaHoc: string,
  signal?: AbortSignal,
): Promise<CsdtReversePlan> {
  const query = new URLSearchParams({
    vehicleType,
    maKhoaHoc,
  });
  const response = await fetchRealtime(`${REALTIME_API}/reverse-plan?${query}`, {
    method: 'GET',
    headers: { Accept: 'application/json' },
    signal,
  });
  const payload = await readJson<unknown>(response);
  if (!response.ok) {
    throw new Error(await responseMessage(response, payload, 'Không thể lập kế hoạch V1 → V2.'));
  }
  if (!isReversePlan(payload)) {
    throw new Error('Máy chủ trả kế hoạch V1 → V2 không hợp lệ.');
  }
  return payload;
}

export async function executeCsdtReversePlan(
  request: CsdtReverseExecuteRequest,
  signal?: AbortSignal,
): Promise<CsdtReverseExecuteResult> {
  const response = await fetchRealtime(`${REALTIME_API}/reverse-execute`, {
    method: 'POST',
    headers: {
      Accept: 'application/json',
      'Content-Type': 'application/json',
    },
    body: JSON.stringify(request),
    signal,
  });
  const payload = await readJson<unknown>(response);
  if (!response.ok) {
    throw new Error(await responseMessage(response, payload, 'Không thể thực thi kế hoạch V1 → V2.'));
  }
  if (!isActionResult(payload)) {
    throw new Error('Máy chủ trả kết quả thực thi V1 → V2 không hợp lệ.');
  }
  return payload as unknown as CsdtReverseExecuteResult;
}

async function runAction(
  url: string,
  method: 'POST' | 'PUT',
  request: CsdtRealtimeEnableRequest | CsdtRealtimeBaselineRequest | CsdtRealtimeRetryRequest,
  fallback: string,
  signal?: AbortSignal,
): Promise<CsdtRealtimeActionResult> {
  const response = await fetchRealtime(url, {
    method,
    headers: {
      Accept: 'application/json',
      'Content-Type': 'application/json',
    },
    body: JSON.stringify(request),
    signal,
  });
  const payload = await readJson<unknown>(response);
  if (!response.ok) {
    throw new Error(await responseMessage(response, payload, fallback));
  }
  if (!isActionResult(payload)) {
    throw new Error('Máy chủ trả kết quả thao tác realtime không hợp lệ.');
  }
  return payload;
}

async function fetchRealtime(input: RequestInfo | URL, init: RequestInit): Promise<Response> {
  try {
    return await apiFetch(input, {
      ...init,
      cache: 'no-store',
      credentials: 'include',
    });
  } catch (error) {
    if (error instanceof DOMException && error.name === 'AbortError') {
      throw error;
    }
    throw new Error('Không thể kết nối tới máy chủ. Vui lòng kiểm tra kết nối và thử lại.');
  }
}

async function responseMessage(
  response: Response,
  payload: unknown,
  fallback: string,
): Promise<string> {
  if (isRecord(payload)) {
    const message = readString(payload.message)
      ?? readString(payload.detail)
      ?? readString(payload.title);
    if (message) {
      return message;
    }
  }
  if (response.status === 403) {
    return 'Bạn không có quyền thực hiện thao tác này.';
  }
  if (response.status === 409) {
    return 'Trạng thái đã thay đổi hoặc stream đang có thao tác khác. Hãy tải lại.';
  }
  return `${fallback} (mã ${response.status})`;
}

async function readJson<T>(response: Response): Promise<T | null> {
  try {
    return await response.clone().json() as T;
  } catch {
    return null;
  }
}

function isStreamsResponse(value: unknown): value is CsdtRealtimeStreamsResponse {
  return isRecord(value)
    && typeof value.observedAtUtc === 'string'
    && Array.isArray(value.streams)
    && value.streams.every(isStreamStatus);
}

function isStreamStatus(value: unknown): boolean {
  return isRecord(value)
    && isStreamCode(value.streamCode)
    && (value.vehicleType === 'OTO' || value.vehicleType === 'MOTO')
    && typeof value.sourceProfileCode === 'string'
    && typeof value.targetProfileCode === 'string'
    && typeof value.sourceDatabaseName === 'string'
    && typeof value.targetDatabaseName === 'string'
    && typeof value.maCSDT === 'string'
    && typeof value.enabled === 'boolean'
    && typeof value.state === 'string'
    && typeof value.baselineStatus === 'string'
    && typeof value.writeAuthorized === 'boolean'
    && typeof value.stateToken === 'string'
    && Array.isArray(value.actionBlockers)
    && Array.isArray(value.domains);
}

function isReversePlan(value: unknown): value is CsdtReversePlan {
  return isRecord(value)
    && value.isReadOnly === true
    && (value.vehicleType === 'OTO' || value.vehicleType === 'MOTO')
    && value.direction === 'V1_TO_V2'
    && typeof value.planToken === 'string'
    && typeof value.executable === 'boolean'
    && Array.isArray(value.blockers)
    && Array.isArray(value.warnings)
    && Array.isArray(value.domains);
}

function isActionResult(value: unknown): value is CsdtRealtimeActionResult {
  return isRecord(value)
    && typeof value.accepted === 'boolean'
    && typeof value.joinedExisting === 'boolean'
    && (value.runId === null || typeof value.runId === 'string')
    && typeof value.status === 'string'
    && typeof value.message === 'string';
}

function isStreamCode(value: unknown): value is CsdtRealtimeStreamCode {
  return value === 'OTO_V2_TO_V1' || value === 'MOTO_V2_TO_V1';
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function readString(value: unknown): string | null {
  return typeof value === 'string' && value.trim() ? value : null;
}
