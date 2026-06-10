/** Một dòng học viên trong danh sách (chỉ đọc). */
export interface HocVienListItem {
  maDK: string;
  hoTen: string;
  ngaySinh: string | null;
  gioiTinh: string | null;
  soCCCD: string | null;
  diaChiThuongTru: string | null;
  soGPLXDaCo: string | null;
  hangGPLXDaCo: string | null;
  nguoiNhanHoSo: string | null;
  tenKhoa: string | null;
  maKhoa: string | null;
  sourceOfTruth: string | null;
  lastSyncStatus: string | null;
}

/** Tham số tìm kiếm học viên. */
export interface HocVienSearchParams {
  keyword?: string;
  maKhoa?: string;
  hangGplx?: string;
  gioiTinh?: string;
  page: number;
  pageSize: number;
}

/** Kết quả phân trang chung. */
export interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalItems: number;
  totalPages: number;
}
