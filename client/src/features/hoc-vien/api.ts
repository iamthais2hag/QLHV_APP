import type {
  HocVienHangHocLookupItem,
  HocVienKhoaLookupItem,
  HocVienListItem,
  HocVienSearchParams,
  PagedResult,
} from './types';

const API_BASE = import.meta.env.VITE_API_BASE_URL ?? '/api';

/**
 * Gọi API tra cứu học viên (chỉ đọc).
 * Endpoint backend: GET /api/hoc-vien
 */
export async function searchHocVien(
  params: HocVienSearchParams,
  signal?: AbortSignal,
): Promise<PagedResult<HocVienListItem>> {
  const query = new URLSearchParams();
  if (params.keyword) query.set('keyword', params.keyword);
  if (params.maKhoa) query.set('maKhoa', params.maKhoa);
  if (params.hangGplx) query.set('hangGplx', params.hangGplx);
  if (params.maHangDT) query.set('maHangDT', params.maHangDT);
  if (params.gioiTinh) query.set('gioiTinh', params.gioiTinh);
  query.set('page', String(params.page));
  query.set('pageSize', String(params.pageSize));

  const response = await fetch(`${API_BASE}/hoc-vien?${query.toString()}`, {
    method: 'GET',
    headers: { Accept: 'application/json' },
    signal,
  });

  if (!response.ok) {
    throw new Error(`Yêu cầu không thành công (mã ${response.status}).`);
  }

  return (await response.json()) as PagedResult<HocVienListItem>;
}

export async function lookupKhoa(
  keyword: string,
  limit = 20,
  signal?: AbortSignal,
): Promise<HocVienKhoaLookupItem[]> {
  const query = new URLSearchParams();
  if (keyword) query.set('keyword', keyword);
  query.set('limit', String(limit));

  return fetchLookup<HocVienKhoaLookupItem>(`/hoc-vien/lookups/khoa?${query.toString()}`, signal);
}

export async function lookupHangHoc(
  keyword: string,
  limit = 20,
  signal?: AbortSignal,
): Promise<HocVienHangHocLookupItem[]> {
  const query = new URLSearchParams();
  if (keyword) query.set('keyword', keyword);
  query.set('limit', String(limit));

  return fetchLookup<HocVienHangHocLookupItem>(`/hoc-vien/lookups/hang-hoc?${query.toString()}`, signal);
}

async function fetchLookup<T>(path: string, signal?: AbortSignal): Promise<T[]> {
  const response = await fetch(`${API_BASE}${path}`, {
    method: 'GET',
    headers: { Accept: 'application/json' },
    signal,
  });

  if (!response.ok) {
    throw new Error(`Yêu cầu không thành công (mã ${response.status}).`);
  }

  return (await response.json()) as T[];
}
