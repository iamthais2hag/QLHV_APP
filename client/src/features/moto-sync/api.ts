import type {
  MotoSyncExecuteRequest,
  MotoSyncExecuteResult,
  MotoSyncPlan,
  MotoSyncPlanRequest,
} from './types';

const API_BASE = import.meta.env.VITE_API_BASE_URL ?? '/api';

export async function getMotoSyncPlan(
  request: MotoSyncPlanRequest,
  signal?: AbortSignal,
): Promise<MotoSyncPlan> {
  const query = new URLSearchParams();
  query.set('direction', request.direction);
  query.set('sourceProfileCode', request.sourceProfileCode);
  query.set('targetProfileCode', request.targetProfileCode);
  query.set('maKhoaHoc', request.maKhoaHoc);
  query.set('allowDirtyData', request.allowDirtyData ? 'true' : 'false');

  const response = await fetch(`${API_BASE}/dong-bo-v2/moto/sync-plan?${query.toString()}`, {
    method: 'GET',
    headers: { Accept: 'application/json' },
    signal,
  });

  if (!response.ok) {
    throw new Error(await getSafeErrorMessage(response, 'Không thể lập kế hoạch đồng bộ Moto TEST.'));
  }

  return (await response.json()) as MotoSyncPlan;
}

export async function executeMotoSyncTest(
  request: MotoSyncExecuteRequest,
  signal?: AbortSignal,
): Promise<MotoSyncExecuteResult> {
  const response = await fetch(`${API_BASE}/dong-bo-v2/moto/sync-test`, {
    method: 'POST',
    headers: {
      Accept: 'application/json',
      'Content-Type': 'application/json',
    },
    body: JSON.stringify(request),
    signal,
  });

  const payload = await tryReadJson<MotoSyncExecuteResult>(response);
  if (!response.ok && payload) {
    return payload;
  }

  if (!response.ok) {
    throw new Error(await getSafeErrorMessage(response, 'Không thể thực thi đồng bộ Moto TEST.'));
  }

  if (!payload) {
    throw new Error('Backend không trả kết quả đồng bộ Moto TEST hợp lệ.');
  }

  return payload;
}

async function getSafeErrorMessage(response: Response, fallback: string): Promise<string> {
  const payload = await tryReadJson<{ message?: string }>(response);
  return payload?.message ? `${payload.message} (mã ${response.status})` : `${fallback} (mã ${response.status})`;
}

async function tryReadJson<T>(response: Response): Promise<T | null> {
  try {
    return (await response.clone().json()) as T;
  } catch {
    return null;
  }
}
