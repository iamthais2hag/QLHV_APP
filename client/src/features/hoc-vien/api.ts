import type { HocVienListItem, HocVienSearchParams, PagedResult } from './types';

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
