import type {
  HocVienExportParams,
  HocVienHangHocLookup,
  HocVienKhoaLookup,
  HocVienListItem,
  HocVienSearchParams,
  PagedResult,
} from './types';

const API_BASE = import.meta.env.VITE_API_BASE_URL ?? '/api';

export interface HocVienExportResult {
  blob: Blob;
  fileName: string;
}

/**
 * Gọi API tra cứu học viên (chỉ đọc).
 * Endpoint backend: GET /api/hoc-vien
 */
export async function searchHocVien(
  params: HocVienSearchParams,
  signal?: AbortSignal,
): Promise<PagedResult<HocVienListItem>> {
  const query = buildFilterQuery(params);
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

/**
 * Tải Excel từ backend theo toàn bộ kết quả đang lọc.
 * Không gửi page/pageSize để tránh chỉ xuất trang hiện tại.
 */
export async function exportHocVienExcel(
  params: HocVienExportParams,
  signal?: AbortSignal,
): Promise<HocVienExportResult> {
  const query = buildFilterQuery(params);
  const queryString = query.toString();
  const response = await fetch(
    `${API_BASE}/hoc-vien/export-excel${queryString ? `?${queryString}` : ''}`,
    {
      method: 'GET',
      headers: {
        Accept: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
      },
      signal,
    },
  );

  if (!response.ok) {
    throw new Error(`Không thể xuất Excel (mã ${response.status}).`);
  }

  return {
    blob: await response.blob(),
    fileName: getFileNameFromDisposition(response.headers.get('Content-Disposition')),
  };
}

export async function getHocVienKhoaLookups(
  keyword: string,
  limit = 20,
  maHangDT?: string | null,
  signal?: AbortSignal,
): Promise<HocVienKhoaLookup[]> {
  const query = new URLSearchParams();
  if (keyword) query.set('keyword', keyword);
  if (maHangDT) query.set('maHangDT', maHangDT);
  query.set('limit', String(limit));

  const response = await fetch(`${API_BASE}/hoc-vien/lookups/khoa?${query.toString()}`, {
    method: 'GET',
    headers: { Accept: 'application/json' },
    signal,
  });

  if (!response.ok) {
    throw new Error(`Không thể tải gợi ý Khóa (mã ${response.status}).`);
  }

  return (await response.json()) as HocVienKhoaLookup[];
}

export async function getHocVienHangHocLookups(
  keyword: string,
  limit = 20,
  signal?: AbortSignal,
): Promise<HocVienHangHocLookup[]> {
  const query = new URLSearchParams();
  if (keyword) query.set('keyword', keyword);
  query.set('limit', String(limit));

  const response = await fetch(`${API_BASE}/hoc-vien/lookups/hang-hoc?${query.toString()}`, {
    method: 'GET',
    headers: { Accept: 'application/json' },
    signal,
  });

  if (!response.ok) {
    throw new Error(`Không thể tải gợi ý Hạng học (mã ${response.status}).`);
  }

  return (await response.json()) as HocVienHangHocLookup[];
}

function buildFilterQuery(params: HocVienExportParams): URLSearchParams {
  const query = new URLSearchParams();
  if (params.keyword) query.set('keyword', params.keyword);
  if (params.maKhoa) query.set('maKhoa', params.maKhoa);
  if (params.maHangDT) query.set('maHangDT', params.maHangDT);
  if (params.hangGplx) query.set('hangGplx', params.hangGplx);
  if (params.gioiTinh) query.set('gioiTinh', params.gioiTinh);
  return query;
}

function getFileNameFromDisposition(contentDisposition: string | null): string {
  const fallback = `HocVien_${formatDownloadTimestamp()}.xlsx`;
  if (!contentDisposition) return fallback;

  const utf8Match = /filename\*=UTF-8''([^;]+)/i.exec(contentDisposition);
  if (utf8Match?.[1]) {
    try {
      return decodeURIComponent(utf8Match[1]);
    } catch {
      return fallback;
    }
  }

  const asciiMatch = /filename="?([^";]+)"?/i.exec(contentDisposition);
  return asciiMatch?.[1] ?? fallback;
}

function formatDownloadTimestamp(): string {
  const value = new Date();
  const pad = (n: number) => String(n).padStart(2, '0');
  return (
    `${value.getFullYear()}${pad(value.getMonth() + 1)}${pad(value.getDate())}_` +
    `${pad(value.getHours())}${pad(value.getMinutes())}`
  );
}
