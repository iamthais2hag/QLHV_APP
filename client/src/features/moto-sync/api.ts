import type {
  MotoCenterTransferExecuteRequest,
  MotoCenterTransferExecuteResult,
  MotoCenterTransferPlan,
  MotoCenterTransferPlanRequest,
  MotoCenterTransferRunHistoryQuery,
  MotoCenterTransferRunHistoryDetail,
  MotoCenterTransferRunHistoryListItem,
  MotoSyncExecuteRequest,
  MotoSyncExecuteResult,
  MotoSyncKhoaHocOption,
  MotoSyncKhoaHocOptionsQuery,
  MotoSyncPlan,
  MotoSyncPlanRequest,
  MotoSyncRunHistoryDetail,
  MotoSyncRunHistoryListItem,
  MotoTargetDonViGTVTOptionsQuery,
  MotoTargetDonViGTVTOptionsResult,
} from './types';
import { apiFetch } from '../../api/apiFetch';
import { API_BASE } from '../../api/apiBase';

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

  const response = await apiFetch(`${API_BASE}/dong-bo-v2/moto/sync-plan?${query.toString()}`, {
    method: 'GET',
    headers: { Accept: 'application/json' },
    signal,
  });

  if (!response.ok) {
    throw new Error(await getSafeErrorMessage(response, 'Không thể lập kế hoạch đồng bộ Moto TEST.'));
  }

  return (await response.json()) as MotoSyncPlan;
}

export async function getMotoSyncKhoaHocOptions(
  request: MotoSyncKhoaHocOptionsQuery,
  signal?: AbortSignal,
): Promise<MotoSyncKhoaHocOption[]> {
  const query = new URLSearchParams();
  query.set('direction', request.direction);
  query.set('sourceProfileCode', request.sourceProfileCode);
  query.set('targetProfileCode', request.targetProfileCode);
  if (request.search?.trim()) {
    query.set('search', request.search.trim());
  }
  query.set('take', String(request.take ?? 50));

  const response = await apiFetch(`${API_BASE}/dong-bo-v2/moto/khoa-hoc-options?${query.toString()}`, {
    method: 'GET',
    headers: { Accept: 'application/json' },
    signal,
  });

  if (!response.ok) {
    throw new Error(await getSafeErrorMessage(response, 'Không thể tải danh sách khóa học Moto TEST.'));
  }

  return (await response.json()) as MotoSyncKhoaHocOption[];
}

export async function getMotoTargetDonViGTVTOptions(
  request: MotoTargetDonViGTVTOptionsQuery,
  signal?: AbortSignal,
): Promise<MotoTargetDonViGTVTOptionsResult> {
  const query = new URLSearchParams();
  query.set('targetProfileCode', request.targetProfileCode);
  if (request.search?.trim()) {
    query.set('search', request.search.trim());
  }
  query.set('take', String(request.take ?? 20));

  const response = await apiFetch(`${API_BASE}/dong-bo-v2/moto/target-don-vi-gtvt-options?${query.toString()}`, {
    method: 'GET',
    headers: { Accept: 'application/json' },
    signal,
  });

  if (!response.ok) {
    throw new Error(await getSafeErrorMessage(response, 'Khong the tai danh muc DM_DonViGTVT target Moto TEST.'));
  }

  return (await response.json()) as MotoTargetDonViGTVTOptionsResult;
}

export async function getMotoCenterTransferPlan(
  request: MotoCenterTransferPlanRequest,
  signal?: AbortSignal,
): Promise<MotoCenterTransferPlan> {
  const query = new URLSearchParams();
  query.set('sourceProfileCode', request.sourceProfileCode);
  query.set('targetProfileCode', request.targetProfileCode);
  query.set('maKhoaHocCu', request.maKhoaHocCu);
  query.set('maCSDTCu', request.maCSDTCu);
  query.set('maCSDTMoi', request.maCSDTMoi);
  query.set('maSoGTVTMoi', request.maSoGTVTMoi);

  const response = await apiFetch(`${API_BASE}/dong-bo-v2/moto/center-transfer-plan?${query.toString()}`, {
    method: 'GET',
    headers: { Accept: 'application/json' },
    signal,
  });

  if (!response.ok) {
    throw new Error(await getSafeErrorMessage(response, 'Không thể lập kế hoạch chuyển MaCSDT Moto TEST.'));
  }

  return (await response.json()) as MotoCenterTransferPlan;
}

export async function executeMotoCenterTransferTest(
  request: MotoCenterTransferExecuteRequest,
  signal?: AbortSignal,
): Promise<MotoCenterTransferExecuteResult> {
  const response = await apiFetch(`${API_BASE}/dong-bo-v2/moto/center-transfer-test`, {
    method: 'POST',
    headers: {
      Accept: 'application/json',
      'Content-Type': 'application/json',
    },
    body: JSON.stringify(request),
    signal,
  });

  const payload = await tryReadJson<MotoCenterTransferExecuteResult>(response);
  if (!response.ok && payload) {
    return payload;
  }

  if (!response.ok) {
    throw new Error(await getSafeErrorMessage(response, 'Không thể thực thi chuyển MaCSDT Moto TEST.'));
  }

  if (!payload) {
    throw new Error('Backend không trả kết quả chuyển MaCSDT Moto TEST hợp lệ.');
  }

  return payload;
}

export async function getMotoCenterTransferRunHistory(
  request: MotoCenterTransferRunHistoryQuery | number = 50,
  signal?: AbortSignal,
): Promise<MotoCenterTransferRunHistoryListItem[]> {
  const normalizedRequest = typeof request === 'number' ? { take: request } : request;
  const query = new URLSearchParams();
  query.set('take', String(normalizedRequest.take ?? 50));
  if (normalizedRequest.maKhoaHoc?.trim()) {
    query.set('maKhoaHoc', normalizedRequest.maKhoaHoc.trim());
  }
  if (normalizedRequest.maCSDT?.trim()) {
    query.set('maCSDT', normalizedRequest.maCSDT.trim());
  }
  if (normalizedRequest.status?.trim()) {
    query.set('status', normalizedRequest.status.trim());
  }
  if (typeof normalizedRequest.executed === 'boolean') {
    query.set('executed', normalizedRequest.executed ? 'true' : 'false');
  }

  const response = await apiFetch(`${API_BASE}/dong-bo-v2/moto/center-transfer-runs?${query.toString()}`, {
    method: 'GET',
    headers: { Accept: 'application/json' },
    signal,
  });

  if (!response.ok) {
    throw new Error(await getSafeErrorMessage(response, 'KhÃ´ng thá»ƒ táº£i lá»‹ch sá»­ chuyá»ƒn MaCSDT Moto TEST.'));
  }

  return (await response.json()) as MotoCenterTransferRunHistoryListItem[];
}

export async function getMotoCenterTransferRunHistoryDetail(
  id: number,
  signal?: AbortSignal,
): Promise<MotoCenterTransferRunHistoryDetail> {
  const response = await apiFetch(`${API_BASE}/dong-bo-v2/moto/center-transfer-runs/${id}`, {
    method: 'GET',
    headers: { Accept: 'application/json' },
    signal,
  });

  if (!response.ok) {
    throw new Error(await getSafeErrorMessage(response, 'KhÃ´ng thá»ƒ táº£i chi tiáº¿t lá»‹ch sá»­ chuyá»ƒn MaCSDT Moto TEST.'));
  }

  return (await response.json()) as MotoCenterTransferRunHistoryDetail;
}

export async function executeMotoSyncTest(
  request: MotoSyncExecuteRequest,
  signal?: AbortSignal,
): Promise<MotoSyncExecuteResult> {
  const response = await apiFetch(`${API_BASE}/dong-bo-v2/moto/sync-test`, {
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

export async function getMotoSyncRunHistory(
  take = 50,
  signal?: AbortSignal,
): Promise<MotoSyncRunHistoryListItem[]> {
  const query = new URLSearchParams();
  query.set('take', String(take));

  const response = await apiFetch(`${API_BASE}/dong-bo-v2/moto/sync-runs?${query.toString()}`, {
    method: 'GET',
    headers: { Accept: 'application/json' },
    signal,
  });

  if (!response.ok) {
    throw new Error(await getSafeErrorMessage(response, 'Không thể tải lịch sử đồng bộ Moto TEST.'));
  }

  return (await response.json()) as MotoSyncRunHistoryListItem[];
}

export async function getMotoSyncRunHistoryDetail(
  id: number,
  signal?: AbortSignal,
): Promise<MotoSyncRunHistoryDetail> {
  const response = await apiFetch(`${API_BASE}/dong-bo-v2/moto/sync-runs/${id}`, {
    method: 'GET',
    headers: { Accept: 'application/json' },
    signal,
  });

  if (!response.ok) {
    throw new Error(await getSafeErrorMessage(response, 'Không thể tải chi tiết lịch sử đồng bộ Moto TEST.'));
  }

  return (await response.json()) as MotoSyncRunHistoryDetail;
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
