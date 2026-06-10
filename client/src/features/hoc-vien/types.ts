/** Một dòng học viên trong danh sách (chỉ đọc). */
export interface HocVienListItem {
  maDangKy: string;
  hoVaTen: string;
  ngaySinh: string | null;
  gioiTinh: string | null;
  soCccd: string | null;
  diaChiThuongTru: string | null;
  anhRelativePath: string | null;
  hangGplxHoc: string | null;
  maHangDT: string | null;
  soGplxDaCo: string | null;
  hangGplxDaCo: string | null;
  nguoiNhanHoSo: string | null;
  tenKhoa: string | null;
  maKhoa: string | null;
}

/** Tham số tìm kiếm học viên. */
export interface HocVienSearchParams {
  keyword?: string;
  maKhoa?: string;
  hangGplx?: string;
  maHangDT?: string;
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

export interface HocVienKhoaLookupItem {
  maKhoa: string;
  tenKhoa: string | null;
  label: string;
}

export interface HocVienHangHocLookupItem {
  maHangDT: string;
  tenHangDT: string | null;
  label: string;
}
