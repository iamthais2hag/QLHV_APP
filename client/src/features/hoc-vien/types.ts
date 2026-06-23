/** Một dòng học viên trong danh sách (chỉ đọc). */
export interface HocVienListItem {
  hocVienId: number;
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
  maHangDT?: string;
  hangGplx?: string;
  gioiTinh?: string;
  page: number;
  pageSize: number;
}

export type HocVienExportParams = Omit<HocVienSearchParams, 'page' | 'pageSize'>;

export type HocVienPrintMode = 'selected' | 'single' | 'course' | 'teacherInCourse';
export type HocVienMissingPhotoMode = 'placeholder' | 'skip';
export type HocVienPrintSortBy = 'current' | 'hoTen' | 'maDK';

export interface HocVienPrintCardsRequest {
  mode: HocVienPrintMode;
  hocVienId?: number;
  hocVienIds?: number[];
  maKhoa?: string;
  giaoVienId?: number;
  missingPhotoMode?: HocVienMissingPhotoMode;
  sortBy?: HocVienPrintSortBy;
  titleLine1?: string;
  titleLine2?: string;
}

export interface HocVienCardPrintPreviewItem {
  hocVienId: number;
  maDangKy: string;
  hoVaTen: string;
  maKhoa: string | null;
  tenKhoa: string | null;
  maHangDT: string | null;
  hangGplxHoc: string | null;
  hasPhoto: boolean;
  photoStatus: string;
}

export interface HocVienCardPrintPreview {
  totalStudents: number;
  totalPages: number;
  cardsPerPage: number;
  layoutName: string;
  missingPhotoCount: number;
  organizationLine1: string;
  organizationLine2: string;
  cardTitle: string;
  items: HocVienCardPrintPreviewItem[];
}

export interface HocVienPhotoAuditParams {
  keyword?: string;
  maKhoa?: string;
  maHangDT?: string;
  page?: number;
  pageSize?: number;
  validateDecode?: boolean;
  onlyMissing?: boolean;
  onlyInvalid?: boolean;
}

export interface HocVienPhotoAuditItem {
  hocVienId: number;
  maDangKy: string;
  hoVaTen: string;
  maKhoa: string | null;
  tenKhoa: string | null;
  maHangDT: string | null;
  hangGplxHoc: string | null;
  expectedRelativePath: string;
  hasPhoto: boolean;
  photoStatus: string;
  message: string;
}

export interface HocVienPhotoAuditResult {
  totalItems: number;
  totalHasPhoto: number;
  totalMissingPhoto: number;
  totalInvalidPhoto: number;
  page: number;
  pageSize: number;
  totalPages: number;
  items: HocVienPhotoAuditItem[];
}

export interface HocVienKhoaLookup {
  maKhoa: string;
  tenKhoa: string | null;
  label: string;
}

export interface HocVienHangHocLookup {
  maHangDT: string;
  tenHangDT: string | null;
  hangGplxHoc: string | null;
  label: string;
}

/** Kết quả phân trang chung. */
export interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalItems: number;
  totalPages: number;
}
