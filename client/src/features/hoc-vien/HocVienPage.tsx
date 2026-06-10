import { useCallback, useEffect, useRef, useState } from 'react';
import { lookupHangHoc, lookupKhoa, searchHocVien } from './api';
import type {
  HocVienHangHocLookupItem,
  HocVienKhoaLookupItem,
  HocVienListItem,
  HocVienSearchParams,
} from './types';
import {
  buildHocVienPhotoUrl,
  exportCurrentRowsToExcel,
  formatGioiTinh,
  formatNgaySinh,
  getHocVienPhotoTitle,
} from './utils';
import CopyButton from './CopyButton';

const PAGE_SIZE = 20;

type Status = 'idle' | 'loading' | 'success' | 'error';

type LookupKind = 'khoa' | 'hangHoc';

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

function buildKhoaLabel(row: HocVienListItem): string {
  if (row.tenKhoa && row.maKhoa) return `${row.tenKhoa} - ${row.maKhoa}`;
  return row.tenKhoa ?? row.maKhoa ?? '';
}

export default function HocVienPage() {
  const [keyword, setKeyword] = useState('');
  const [khoaInput, setKhoaInput] = useState('');
  const [selectedKhoa, setSelectedKhoa] = useState<HocVienKhoaLookupItem | null>(null);
  const [khoaOptions, setKhoaOptions] = useState<HocVienKhoaLookupItem[]>([]);
  const [khoaOpen, setKhoaOpen] = useState(false);
  const [khoaWarning, setKhoaWarning] = useState('');
  const [hangHocInput, setHangHocInput] = useState('');
  const [selectedHangHoc, setSelectedHangHoc] = useState<HocVienHangHocLookupItem | null>(null);
  const [hangHocOptions, setHangHocOptions] = useState<HocVienHangHocLookupItem[]>([]);
  const [hangHocOpen, setHangHocOpen] = useState(false);
  const [hangHocWarning, setHangHocWarning] = useState('');
  const [gioiTinh, setGioiTinh] = useState('');
  const [page, setPage] = useState(1);

  const [status, setStatus] = useState<Status>('idle');
  const [rows, setRows] = useState<HocVienListItem[]>([]);
  const [totalItems, setTotalItems] = useState(0);
  const [totalPages, setTotalPages] = useState(0);
  const [errorMessage, setErrorMessage] = useState('');

  const abortRef = useRef<AbortController | null>(null);

  const buildSearchParams = useCallback(
    (nextPage: number): HocVienSearchParams => ({
      keyword,
      maKhoa: selectedKhoa?.maKhoa,
      maHangDT: selectedHangHoc?.maHangDT,
      gioiTinh,
      page: nextPage,
      pageSize: PAGE_SIZE,
    }),
    [gioiTinh, keyword, selectedHangHoc, selectedKhoa],
  );

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
    const value = khoaInput.trim();
    if (!value || selectedKhoa?.label === khoaInput) {
      setKhoaOptions([]);
      return;
    }

    const controller = new AbortController();
    const timer = window.setTimeout(async () => {
      try {
        const items = await lookupKhoa(value, 20, controller.signal);
        setKhoaOptions(items);
        setKhoaOpen(true);
      } catch {
        if (!controller.signal.aborted) {
          setKhoaOptions([]);
        }
      }
    }, 180);

    return () => {
      window.clearTimeout(timer);
      controller.abort();
    };
  }, [khoaInput, selectedKhoa]);

  useEffect(() => {
    const value = hangHocInput.trim();
    if (!value || selectedHangHoc?.label === hangHocInput) {
      setHangHocOptions([]);
      return;
    }

    const controller = new AbortController();
    const timer = window.setTimeout(async () => {
      try {
        const items = await lookupHangHoc(value, 20, controller.signal);
        setHangHocOptions(items);
        setHangHocOpen(true);
      } catch {
        if (!controller.signal.aborted) {
          setHangHocOptions([]);
        }
      }
    }, 180);

    return () => {
      window.clearTimeout(timer);
      controller.abort();
    };
  }, [hangHocInput, selectedHangHoc]);

  useEffect(() => {
    load(buildSearchParams(page));
    return () => abortRef.current?.abort();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [page]);

  function handleSearch() {
    const nextKhoaWarning =
      khoaInput.trim() && !selectedKhoa ? 'Vui lòng chọn khóa trong danh sách gợi ý.' : '';
    const nextHangHocWarning =
      hangHocInput.trim() && !selectedHangHoc ? 'Vui lòng chọn hạng học trong danh sách gợi ý.' : '';
    setKhoaWarning(nextKhoaWarning);
    setHangHocWarning(nextHangHocWarning);
    if (nextKhoaWarning || nextHangHocWarning) {
      return;
    }

    if (page === 1) {
      load(buildSearchParams(1));
    } else {
      setPage(1);
    }
  }

  function handleReset() {
    setKeyword('');
    setKhoaInput('');
    setSelectedKhoa(null);
    setKhoaOptions([]);
    setKhoaOpen(false);
    setKhoaWarning('');
    setHangHocInput('');
    setSelectedHangHoc(null);
    setHangHocOptions([]);
    setHangHocOpen(false);
    setHangHocWarning('');
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

  function handleLookupBlur(kind: LookupKind) {
    window.setTimeout(() => {
      if (kind === 'khoa') {
        setKhoaOpen(false);
      } else {
        setHangHocOpen(false);
      }
    }, 120);
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
              Khóa
            </label>
            <div className="autocomplete">
              <input
                id="hv-makhoa"
                className="field__input"
                type="text"
                value={khoaInput}
                onChange={(e) => {
                  setKhoaInput(e.target.value);
                  setSelectedKhoa(null);
                  setKhoaWarning('');
                }}
                onFocus={() => setKhoaOpen(khoaOptions.length > 0)}
                onBlur={() => handleLookupBlur('khoa')}
                onKeyDown={(e) => e.key === 'Enter' && handleSearch()}
                placeholder="Nhập tên hoặc mã khóa"
                autoComplete="off"
              />
              {khoaOpen && khoaOptions.length > 0 && (
                <div className="autocomplete__menu">
                  {khoaOptions.map((option) => (
                    <button
                      key={option.maKhoa}
                      type="button"
                      className="autocomplete__option"
                      onMouseDown={(e) => e.preventDefault()}
                      onClick={() => {
                        setSelectedKhoa(option);
                        setKhoaInput(option.label);
                        setKhoaOptions([]);
                        setKhoaOpen(false);
                        setKhoaWarning('');
                      }}
                    >
                      {option.label}
                    </button>
                  ))}
                </div>
              )}
            </div>
            {khoaWarning && <div className="field__hint field__hint--warning">{khoaWarning}</div>}
          </div>
          <div className="field">
            <label className="field__label" htmlFor="hv-hang">
              Hạng học
            </label>
            <div className="autocomplete">
              <input
                id="hv-hang"
                className="field__input"
                type="text"
                value={hangHocInput}
                onChange={(e) => {
                  setHangHocInput(e.target.value);
                  setSelectedHangHoc(null);
                  setHangHocWarning('');
                }}
                onFocus={() => setHangHocOpen(hangHocOptions.length > 0)}
                onBlur={() => handleLookupBlur('hangHoc')}
                onKeyDown={(e) => e.key === 'Enter' && handleSearch()}
                placeholder="Nhập mã hoặc tên hạng"
                autoComplete="off"
              />
              {hangHocOpen && hangHocOptions.length > 0 && (
                <div className="autocomplete__menu">
                  {hangHocOptions.map((option) => (
                    <button
                      key={option.maHangDT}
                      type="button"
                      className="autocomplete__option"
                      onMouseDown={(e) => e.preventDefault()}
                      onClick={() => {
                        setSelectedHangHoc(option);
                        setHangHocInput(option.label);
                        setHangHocOptions([]);
                        setHangHocOpen(false);
                        setHangHocWarning('');
                      }}
                    >
                      {option.label}
                    </button>
                  ))}
                </div>
              )}
            </div>
            {hangHocWarning && <div className="field__hint field__hint--warning">{hangHocWarning}</div>}
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
              onClick={() => load(buildSearchParams(page))}
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
                  <td>{formatGioiTinh(row.gioiTinh)}</td>
                  <td>{row.soCccd ?? ''}</td>
                  <td className="cell-ellipsis cell-address" title={row.diaChiThuongTru ?? ''}>
                    {row.diaChiThuongTru ?? ''}
                  </td>
                  <td title={row.hangGplxHoc ?? ''}>{row.maHangDT ?? ''}</td>
                  <td>{row.soGplxDaCo ?? ''}</td>
                  <td>{row.hangGplxDaCo ?? ''}</td>
                  <td className="cell-ellipsis" title={row.nguoiNhanHoSo ?? ''}>
                    {row.nguoiNhanHoSo ?? ''}
                  </td>
                  <td title={buildKhoaLabel(row)}>
                    {buildKhoaLabel(row)}
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
