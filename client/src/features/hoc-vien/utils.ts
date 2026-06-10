import type { HocVienListItem } from './types';

/** Định dạng ngày sinh dạng dd/MM/yyyy từ chuỗi ISO (yyyy-MM-dd). */
export function formatNgaySinh(value: string | null): string {
  if (!value) return '';
  const parts = value.split('-');
  if (parts.length !== 3) return value;
  const [y, m, d] = parts;
  return `${d}/${m}/${y}`;
}

export function formatGioiTinh(value: string | null): string {
  const normalized = value?.trim();
  if (!normalized) return '';
  if (normalized.toUpperCase() === 'M' || normalized.toLocaleLowerCase('vi-VN') === 'nam') {
    return 'Nam';
  }

  if (
    normalized.toUpperCase() === 'F' ||
    normalized.toLocaleLowerCase('vi-VN') === 'nữ' ||
    normalized.toLocaleLowerCase('vi-VN') === 'nu'
  ) {
    return 'Nữ';
  }

  return normalized;
}

export function buildHocVienPhotoUrl(value: string | null): string | null {
  const path = normalizeRelativePhotoPath(value);
  const baseUrl = import.meta.env.VITE_HOC_VIEN_PHOTO_BASE_URL;
  if (!path || !baseUrl) {
    return null;
  }

  const base = baseUrl.endsWith('/') ? baseUrl : `${baseUrl}/`;
  const encodedPath = path.split('/').map(encodeURIComponent).join('/');
  return `${base}${encodedPath}`;
}

export function getHocVienPhotoTitle(value: string | null): string {
  const path = normalizeRelativePhotoPath(value);
  return path ? `Ảnh: ${path}` : 'Chưa có ảnh thẻ';
}

function normalizeRelativePhotoPath(value: string | null): string | null {
  if (!value) return null;
  const trimmed = value.trim().replace(/\\/g, '/');
  if (!trimmed || trimmed.startsWith('/') || trimmed.includes(':') || trimmed.includes('..')) {
    return null;
  }

  return trimmed;
}

/** Cột hiển thị của bảng học viên (theo thứ tự). */
export const HOC_VIEN_COLUMNS: { key: keyof HocVienListItem; header: string }[] = [
  { key: 'maDangKy', header: 'Mã đăng ký' },
  { key: 'hoVaTen', header: 'Họ và tên' },
  { key: 'ngaySinh', header: 'Ngày sinh' },
  { key: 'gioiTinh', header: 'Giới tính' },
  { key: 'soCccd', header: 'Số CCCD' },
  { key: 'diaChiThuongTru', header: 'Địa chỉ thường trú' },
  { key: 'hangGplxHoc', header: 'Hạng học' },
  { key: 'soGplxDaCo', header: 'Số GPLX đã có' },
  { key: 'hangGplxDaCo', header: 'Hạng GPLX đã có' },
  { key: 'nguoiNhanHoSo', header: 'Người nhận hồ sơ' },
  { key: 'maKhoa', header: 'Khóa' },
];

const EXCEL_TEXT_COLUMNS = new Set<keyof HocVienListItem>([
  'maDangKy',
  'soCccd',
  'soGplxDaCo',
]);

/**
 * Xuất các dòng hiện có ra file Excel (xử lý cục bộ phía trình duyệt, không gọi backend).
 * Dùng định dạng bảng HTML mà Excel mở được.
 */
export function exportCurrentRowsToExcel(rows: HocVienListItem[], fileName: string): void {
  const head = ['STT', ...HOC_VIEN_COLUMNS.map((c) => c.header)];
  const escape = (v: unknown) =>
    String(v ?? '')
      .replace(/&/g, '&amp;')
      .replace(/</g, '&lt;')
      .replace(/>/g, '&gt;');

  const headerHtml = head.map((h) => `<th>${escape(h)}</th>`).join('');
  const bodyHtml = rows
    .map((row, index) => {
      const cells = HOC_VIEN_COLUMNS.map((c) => {
        const value =
          c.key === 'ngaySinh'
            ? formatNgaySinh(row.ngaySinh)
            : c.key === 'gioiTinh'
              ? formatGioiTinh(row.gioiTinh)
              : row[c.key];
        const textClass = EXCEL_TEXT_COLUMNS.has(c.key) ? ' class="excel-text"' : '';
        return `<td${textClass}>${escape(value)}</td>`;
      }).join('');
      return `<tr><td>${index + 1}</td>${cells}</tr>`;
    })
    .join('');

  const html = `<html><head><meta charset="utf-8" />` +
    `<style>.excel-text{mso-number-format:"\\@";}</style></head><body>` +
    `<table border="1"><thead><tr>${headerHtml}</tr></thead><tbody>${bodyHtml}</tbody></table>` +
    `</body></html>`;

  const blob = new Blob(['\uFEFF', html], { type: 'application/vnd.ms-excel;charset=utf-8' });
  const url = URL.createObjectURL(blob);
  const link = document.createElement('a');
  link.href = url;
  link.download = fileName;
  document.body.appendChild(link);
  link.click();
  document.body.removeChild(link);
  URL.revokeObjectURL(url);
}
