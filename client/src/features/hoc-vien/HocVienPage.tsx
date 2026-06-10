import { useCallback, useEffect, useRef, useState } from 'react';
import { searchHocVien } from './api';
import type { HocVienListItem, HocVienSearchParams } from './types';
import {
  buildHocVienPhotoUrl,
  exportCurrentRowsToExcel,
  formatNgaySinh,
  getHocVienPhotoTitle,
} from './utils';
import CopyButton from './CopyButton';

const PAGE_SIZE = 20;

type Status = 'idle' | 'loading' | 'success' | 'error';

function HocVienPhoto({ path }: { path: string | null }) {
  const [failed, setFailed] = useState(false);
  const url = buildHocVienPhotoUrl(path);
  const title = getHocVienPhotoTitle(path);

  if (url && !failed) {
    return (
      <span className="hocvien-photo" title={title}>
        <img src={url} alt="Ảnh thẻ" onError={() => setFailed(true)} />
      </span>
    );
  }

  return (
    <span className="hocvien-photo hocvien-photo--placeholder" title={title}>
      Ảnh
    </span>
  );
}

export default function HocVienPage() {
  const [keyword, setKeyword] = useState('');
  const [maKhoa, setMaKhoa] = useState('');
  const [hangGplx, setHangGplx] = useState('');
  const [gioiTinh, setGioiTinh] = useState('');
  const [page, setPage] = useState(1);

  const [status, setStatus] = useState<Status>('idle');
  const [rows, setRows] = useState<HocVienListItem[]>([]);
  const [totalItems, setTotalItems] = useState(0);
  const [totalPages, setTotalPages] = useState(0);
  const [errorMessage, setErrorMessage] = useState('');

  const abortRef = useRef<AbortController | null>(null);

  const load = useCallback(
    async (params: HocVienSearchParams) => {
      abortRef.current?.abort();
      const controller = new AbortController();
      abortRef.current = controller;

      setStatus('loading');
      setErrorMessage('');
      try {
        const result = await searchHocVien(params, controller.signal);
        setRows(result.items);
        setTotalItems(result.totalItems);
        setTotalPages(result.totalPages);
        setStatus('success');
      } catch (err) {
        if (controller.signal.aborted) return;
        setRows([]);
        setTotalItems(0);
        setTotalPages(0);
        setErrorMessage(
          err instanceof Error ? err.message : 'Không thể tải dữ liệu. Vui lòng thử lại.',
        );
        setStatus('error');
      }
    },
    [],
  );

  useEffect(() => {
    load({ keyword, maKhoa, hangGplx, gioiTinh, page, pageSize: PAGE_SIZE });
    return () => abortRef.current?.abort();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [page]);

  function handleSearch() {
    if (page === 1) {
      load({ keyword, maKhoa, hangGplx, gioiTinh, page: 1, pageSize: PAGE_SIZE });
    } else {
      setPage(1);
    }
  }

  function handleReset() {
    setKeyword('');
    setMaKhoa('');
    setHangGplx('');
    setGioiTinh('');
    if (page === 1) {
      load({ page: 1, pageSize: PAGE_SIZE });
    } else {
      setPage(1);
    }
  }

  function handleExport() {
    exportCurrentRowsToExcel(rows, 'danh-sach-hoc-vien.xls');
  }

  const startIndex = (page - 1) * PAGE_SIZE;

  return (
    <div>
      <div className="page__head">
        <h2 className="page__title">Học viên</h2>
        <p className="page__subtitle">Tra cứu danh sách và hồ sơ học viên.</p>
      </div>

      {/* Tìm kiếm + bộ lọc */}
      <div className="toolbar">
        <div className="toolbar__row">
          <div className="field" style={{ flexBasis: 260 }}>
            <label className="field__label" htmlFor="hv-keyword">
              Tìm kiếm
            </label>
            <input
              id="hv-keyword"
              className="field__input"
              type="text"
              value={keyword}
              onChange={(e) => setKeyword(e.target.value)}
              onKeyDown={(e) => e.key === 'Enter' && handleSearch()}
              placeholder="Họ tên, mã đăng ký, số CCCD..."
            />
          </div>
          <div className="field">
            <label className="field__label" htmlFor="hv-makhoa">
              Mã khóa
            </label>
            <input
              id="hv-makhoa"
              className="field__input"
              type="text"
              value={maKhoa}
              onChange={(e) => setMaKhoa(e.target.value)}
              onKeyDown={(e) => e.key === 'Enter' && handleSearch()}
            />
          </div>
          <div className="field">
            <label className="field__label" htmlFor="hv-hang">
              Hạng GPLX
            </label>
            <input
              id="hv-hang"
              className="field__input"
              type="text"
              value={hangGplx}
              onChange={(e) => setHangGplx(e.target.value)}
              onKeyDown={(e) => e.key === 'Enter' && handleSearch()}
            />
          </div>
          <div className="field">
            <label className="field__label" htmlFor="hv-gioitinh">
              Giới tính
            </label>
            <select
              id="hv-gioitinh"
              className="field__input"
              value={gioiTinh}
              onChange={(e) => setGioiTinh(e.target.value)}
            >
              <option value="">Tất cả</option>
              <option value="Nam">Nam</option>
              <option value="Nữ">Nữ</option>
            </select>
          </div>

          <div className="toolbar__actions">
            <button type="button" className="btn btn--primary" onClick={handleSearch}>
              Tìm kiếm
            </button>
            <button type="button" className="btn btn--ghost" onClick={handleReset}>
              Làm mới
            </button>
            <button
              type="button"
              className="btn btn--ghost"
              onClick={handleExport}
              disabled={rows.length === 0}
            >
              Xuất Excel
            </button>
          </div>
        </div>
      </div>

      {/* Bảng dữ liệu + các trạng thái */}
      <div className="table-wrap">
        {status === 'loading' && (
          <div className="state">
            <div className="spinner" aria-hidden="true" />
            <div>Đang tải dữ liệu...</div>
          </div>
        )}

        {status === 'error' && (
          <div className="state state--error">
            <div>{errorMessage}</div>
            <button
              type="button"
              className="btn btn--ghost btn--sm"
              style={{ marginTop: 12 }}
              onClick={() =>
                load({ keyword, maKhoa, hangGplx, gioiTinh, page, pageSize: PAGE_SIZE })
              }
            >
              Thử lại
            </button>
          </div>
        )}

        {status === 'success' && rows.length === 0 && (
          <div className="state">Không tìm thấy học viên phù hợp với điều kiện tra cứu.</div>
        )}

        {status === 'success' && rows.length > 0 && (
          <table className="table table--hoc-vien">
            <colgroup>
              <col className="col-stt" />
              <col className="col-photo" />
              <col className="col-madk" />
              <col className="col-name" />
              <col className="col-date" />
              <col className="col-gender" />
              <col className="col-cccd" />
              <col className="col-address" />
              <col className="col-hang-hoc" />
              <col className="col-gplx-number" />
              <col className="col-gplx-class" />
              <col className="col-receiver" />
              <col className="col-course" />
            </colgroup>
            <thead>
              <tr>
                <th>STT</th>
                <th>Ảnh</th>
                <th>Mã ĐK</th>
                <th>Họ và tên</th>
                <th>Ngày sinh</th>
                <th>Giới tính</th>
                <th>Số CCCD</th>
                <th>Địa chỉ</th>
                <th>Hạng học</th>
                <th>Số GPLX đã có</th>
                <th>Hạng GPLX đã có</th>
                <th>Người nhận hồ sơ</th>
                <th>Khóa</th>
              </tr>
            </thead>
            <tbody>
              {rows.map((row, index) => (
                <tr key={row.maDangKy || index}>
                  <td>{startIndex + index + 1}</td>
                  <td>
                    <HocVienPhoto path={row.anhRelativePath} />
                  </td>
                  <td>
                    <CopyButton value={row.maDangKy} title={row.maDangKy} />
                  </td>
                  <td className="cell-ellipsis cell-name" title={row.hoVaTen}>
                    {row.hoVaTen}
                  </td>
                  <td>{formatNgaySinh(row.ngaySinh)}</td>
                  <td>{row.gioiTinh ?? ''}</td>
                  <td>{row.soCccd ?? ''}</td>
                  <td className="cell-ellipsis cell-address" title={row.diaChiThuongTru ?? ''}>
                    {row.diaChiThuongTru ?? ''}
                  </td>
                  <td>{row.hangGplxHoc ?? ''}</td>
                  <td>{row.soGplxDaCo ?? ''}</td>
                  <td>{row.hangGplxDaCo ?? ''}</td>
                  <td className="cell-ellipsis" title={row.nguoiNhanHoSo ?? ''}>
                    {row.nguoiNhanHoSo ?? ''}
                  </td>
                  <td title={row.tenKhoa ? `${row.maKhoa ?? ''} - ${row.tenKhoa}` : row.maKhoa ?? ''}>
                    {row.maKhoa ?? ''}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>

      {/* Phân trang */}
      {status === 'success' && totalItems > 0 && (
        <div className="pager">
          <span>
            Tổng số: {totalItems.toLocaleString('vi-VN')} học viên · Trang {page}/
            {Math.max(totalPages, 1)}
          </span>
          <div className="pager__controls">
            <button
              type="button"
              className="btn btn--ghost btn--sm"
              disabled={page <= 1}
              onClick={() => setPage((p) => Math.max(1, p - 1))}
            >
              Trang trước
            </button>
            <button
              type="button"
              className="btn btn--ghost btn--sm"
              disabled={page >= totalPages}
              onClick={() => setPage((p) => p + 1)}
            >
              Trang sau
            </button>
          </div>
        </div>
      )}
    </div>
  );
}
