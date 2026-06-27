import type {
  CsdtConnectionProfileDetail,
  CsdtConnectionProfileListItem,
  SaveCsdtConnectionProfileRequest,
  TestCsdtConnectionProfileRequest,
  TestCsdtConnectionProfileResult,
} from './types';

const API_BASE = import.meta.env.VITE_API_BASE_URL ?? '/api';

export async function getCsdtConnectionProfiles(
  signal?: AbortSignal,
): Promise<CsdtConnectionProfileListItem[]> {
  const response = await fetch(`${API_BASE}/csdt-connection-profiles`, {
    method: 'GET',
    headers: { Accept: 'application/json' },
    signal,
  });

  if (!response.ok) {
    throw new Error(await getSafeErrorMessage(response, 'Không thể tải danh sách cấu hình kết nối CSDT.'));
  }

  return (await response.json()) as CsdtConnectionProfileListItem[];
}

export async function getCsdtConnectionProfile(
  profileCode: string,
  signal?: AbortSignal,
): Promise<CsdtConnectionProfileDetail> {
  const response = await fetch(`${API_BASE}/csdt-connection-profiles/${encodeURIComponent(profileCode)}`, {
    method: 'GET',
    headers: { Accept: 'application/json' },
    signal,
  });

  if (!response.ok) {
    throw new Error(await getSafeErrorMessage(response, 'Không thể tải chi tiết cấu hình kết nối CSDT.'));
  }

  return (await response.json()) as CsdtConnectionProfileDetail;
}

export async function saveCsdtConnectionProfile(
  profileCode: string,
  request: SaveCsdtConnectionProfileRequest,
  signal?: AbortSignal,
): Promise<CsdtConnectionProfileDetail> {
  const response = await fetch(`${API_BASE}/csdt-connection-profiles/${encodeURIComponent(profileCode)}`, {
    method: 'PUT',
    headers: {
      Accept: 'application/json',
      'Content-Type': 'application/json',
    },
    body: JSON.stringify(request),
    signal,
  });

  if (!response.ok) {
    throw new Error(await getSafeErrorMessage(response, 'Không thể lưu cấu hình kết nối CSDT.'));
  }

  return (await response.json()) as CsdtConnectionProfileDetail;
}

export async function testCsdtConnectionProfile(
  profileCode: string,
  request?: TestCsdtConnectionProfileRequest,
  signal?: AbortSignal,
): Promise<TestCsdtConnectionProfileResult> {
  const response = await fetch(`${API_BASE}/csdt-connection-profiles/${encodeURIComponent(profileCode)}/test`, {
    method: 'POST',
    headers: {
      Accept: 'application/json',
      'Content-Type': 'application/json',
    },
    body: JSON.stringify(request ?? {}),
    signal,
  });

  if (!response.ok) {
    throw new Error(await getSafeErrorMessage(response, 'Không thể test kết nối CSDT.'));
  }

  return (await response.json()) as TestCsdtConnectionProfileResult;
}

async function getSafeErrorMessage(response: Response, fallback: string): Promise<string> {
  try {
    const payload = (await response.json()) as { message?: string };
    return payload.message ? `${payload.message} (mã ${response.status})` : `${fallback} (mã ${response.status})`;
  } catch {
    return `${fallback} (mã ${response.status})`;
  }
}
