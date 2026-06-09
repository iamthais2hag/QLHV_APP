import type { HocVienListItem } from './types';

/** Định dạng ngày sinh dạng dd/MM/yyyy từ chuỗi ISO (yyyy-MM-dd). */
export function formatNgaySinh(value: string | null): string {
  if (!value) return '';
  const parts = value.split('-');
  if (parts.length !== 3) return value;
  const [y, m, d] = parts;
  return `${d}/${m}/${y}`;
}

/** Cột hiển thị của bảng học viên (theo thứ tự). */
export const HOC_VIEN_COLUMNS: { key: keyof HocVienListItem; header: string }[] = [
  { key: 'maDangKy', header: 'Mã đăng ký' },
  { key: 'hoVaTen', header: 'Họ và tên' },
  { key: 'ngaySinh', header: 'Ngày sinh' },
  { key: 'gioiTinh', header: 'Giới tính' },
  { key: 'soCccd', header: 'Số CCCD' },
  { key: 'diaChiThuongTru', header: 'Địa chỉ thường trú' },
  { key: 'soGplxDaCo', header: 'Số GPLX đã có' },
  { key: 'hangGplxDaCo', header: 'Hạng GPLX đã có' },
  { key: 'nguoiNhanHoSo', header: 'Người nhận hồ sơ' },
  { key: 'tenKhoa', header: 'Tên khóa' },
  { key: 'maKhoa', header: 'Mã khóa' },
];

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
        const value = c.key === 'ngaySinh' ? formatNgaySinh(row.ngaySinh) : row[c.key];
        return `<td>${escape(value)}</td>`;
      }).join('');
      return `<tr><td>${index + 1}</td>${cells}</tr>`;
    })
    .join('');

  const html = `<html><head><meta charset="utf-8" /></head><body>` +
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
