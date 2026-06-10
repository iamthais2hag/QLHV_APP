import { useCallback, useEffect, useRef, useState } from 'react';
import {
  exportHocVienExcel,
  getHocVienHangHocLookups,
  getHocVienKhoaLookups,
  searchHocVien,
} from './api';
import type {
  HocVienExportParams,
  HocVienHangHocLookup,
  HocVienKhoaLookup,
  HocVienListItem,
  HocVienSearchParams,
} from './types';
import {
  buildHocVienPhotoUrl,
  formatGioiTinh,
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
  const [khoaInput, setKhoaInput] = useState('');
  const [selectedKhoa, setSelectedKhoa] = useState<HocVienKhoaLookup | null>(null);
  const [khoaSuggestions, setKhoaSuggestions] = useState<HocVienKhoaLookup[]>([]);
  const [showKhoaSuggestions, setShowKhoaSuggestions] = useState(false);
  const [isKhoaLookupLoading, setIsKhoaLookupLoading] = useState(false);
  const [khoaWarning, setKhoaWarning] = useState('');
  const [hangHocInput, setHangHocInput] = useState('');
  const [selectedHangHoc, setSelectedHangHoc] = useState<HocVienHangHocLookup | null>(null);
  const [hangHocSuggestions, setHangHocSuggestions] = useState<HocVienHangHocLookup[]>([]);
  const [showHangHocSuggestions, setShowHangHocSuggestions] = useState(false);
  const [isHangHocLookupLoading, setIsHangHocLookupLoading] = useState(false);
  const [hangHocWarning, setHangHocWarning] = useState('');
  const [gioiTinh, setGioiTinh] = useState('');
  const [page, setPage] = useState(1);

  const [status, setStatus] = useState<Status>('idle');
  const [rows, setRows] = useState<HocVienListItem[]>([]);
  const [totalItems, setTotalItems] = useState(0);
  const [totalPages, setTotalPages] = useState(0);
  const [errorMessage, setErrorMessage] = useState('');
  const [isExporting, setIsExporting] = useState(false);
  const [exportErrorMessage, setExportErrorMessage] = useState('');

  const abortRef = useRef<AbortController | null>(null);

  const currentFilters = useCallback(
    (): HocVienExportParams => ({
      keyword,
      maKhoa: selectedKhoa?.maKhoa,
      maHangDT: selectedHangHoc?.maHangDT,
      gioiTinh,
    }),
    [keyword, selectedKhoa, selectedHangHoc, gioiTinh],
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
    const lookupKeyword = khoaInput.trim();
    if (!lookupKeyword || selectedKhoa?.label === lookupKeyword) {
      setKhoaSuggestions([]);
      setShowKhoaSuggestions(false);
      setIsKhoaLookupLoading(false);
      return;
    }

    const controller = new AbortController();
    setIsKhoaLookupLoading(true);
    const timer = window.setTimeout(async () => {
      try {
        const result = await getHocVienKhoaLookups(lookupKeyword, 20, controller.signal);
        setKhoaSuggestions(result);
        setShowKhoaSuggestions(true);
      } catch {
        if (!controller.signal.aborted) {
          setKhoaSuggestions([]);
        }
      } finally {
        if (!controller.signal.aborted) {
          setIsKhoaLookupLoading(false);
        }
      }
    }, 200);

    return () => {
      window.clearTimeout(timer);
      controller.abort();
    };
  }, [khoaInput, selectedKhoa]);

  useEffect(() => {
    const lookupKeyword = hangHocInput.trim();
    if (!lookupKeyword || selectedHangHoc?.label === lookupKeyword) {
      setHangHocSuggestions([]);
      setShowHangHocSuggestions(false);
      setIsHangHocLookupLoading(false);
      return;
    }

    const controller = new AbortController();
    setIsHangHocLookupLoading(true);
    const timer = window.setTimeout(async () => {
      try {
        const result = await getHocVienHangHocLookups(lookupKeyword, 20, controller.signal);
        setHangHocSuggestions(result);
        setShowHangHocSuggestions(true);
      } catch {
        if (!controller.signal.aborted) {
          setHangHocSuggestions([]);
        }
      } finally {
        if (!controller.signal.aborted) {
          setIsHangHocLookupLoading(false);
        }
      }
    }, 200);

    return () => {
      window.clearTimeout(timer);
      controller.abort();
    };
  }, [hangHocInput, selectedHangHoc]);

  useEffect(() => {
    load({ ...currentFilters(), page, pageSize: PAGE_SIZE });
    return () => abortRef.current?.abort();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [page]);

  function validateSelectedLookups(): boolean {
    const hasInvalidKhoa = khoaInput.trim().length > 0 && !selectedKhoa;
    const hasInvalidHangHoc = hangHocInput.trim().length > 0 && !selectedHangHoc;

    setKhoaWarning(hasInvalidKhoa ? 'Vui lòng chọn Khóa trong danh sách gợi ý' : '');
    setHangHocWarning(hasInvalidHangHoc ? 'Vui lòng chọn Hạng học trong danh sách gợi ý' : '');
    return !hasInvalidKhoa && !hasInvalidHangHoc;
  }

  function handleKhoaInputChange(value: string) {
    setKhoaInput(value);
    setKhoaWarning('');
    if (!selectedKhoa || value !== selectedKhoa.label) {
      setSelectedKhoa(null);
    }
    setShowKhoaSuggestions(true);
  }

  function handleHangHocInputChange(value: string) {
    setHangHocInput(value);
    setHangHocWarning('');
    if (!selectedHangHoc || value !== selectedHangHoc.label) {
      setSelectedHangHoc(null);
    }
    setShowHangHocSuggestions(true);
  }

  function selectKhoa(option: HocVienKhoaLookup) {
    setSelectedKhoa(option);
    setKhoaInput(option.label);
    setKhoaWarning('');
    setKhoaSuggestions([]);
    setShowKhoaSuggestions(false);
  }

  function selectHangHoc(option: HocVienHangHocLookup) {
    setSelectedHangHoc(option);
    setHangHocInput(option.label);
    setHangHocWarning('');
    setHangHocSuggestions([]);
    setShowHangHocSuggestions(false);
  }

  function handleSearch() {
    setExportErrorMessage('');
    if (!validateSelectedLookups()) return;

    if (page === 1) {
      load({ ...currentFilters(), page: 1, pageSize: PAGE_SIZE });
    } else {
      setPage(1);
    }
  }

  function handleReset() {
    setKeyword('');
    setKhoaInput('');
    setSelectedKhoa(null);
    setKhoaSuggestions([]);
    setKhoaWarning('');
    setHangHocInput('');
    setSelectedHangHoc(null);
    setHangHocSuggestions([]);
    setHangHocWarning('');
    setGioiTinh('');
    setExportErrorMessage('');
    if (page === 1) {
      load({ page: 1, pageSize: PAGE_SIZE });
    } else {
      setPage(1);
    }
  }

  async function handleExport() {
    setExportErrorMessage('');
    if (!validateSelectedLookups()) return;

    setIsExporting(true);
    try {
      const result = await exportHocVienExcel(currentFilters());
      const url = URL.createObjectURL(result.blob);
      const link = document.createElement('a');
      link.href = url;
      link.download = result.fileName;
      document.body.appendChild(link);
      link.click();
      document.body.removeChild(link);
      URL.revokeObjectURL(url);
    } catch (err) {
      setExportErrorMessage(
        err instanceof Error ? err.message : 'Không thể xuất Excel. Vui lòng thử lại.',
      );
    } finally {
      setIsExporting(false);
    }
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
          <div className="field field--autocomplete">
            <label className="field__label" htmlFor="hv-khoa">
              Khóa
            </label>
            <input
              id="hv-khoa"
              className="field__input"
              type="text"
              value={khoaInput}
              onChange={(e) => handleKhoaInputChange(e.target.value)}
              onFocus={() => khoaSuggestions.length > 0 && setShowKhoaSuggestions(true)}
              onBlur={() => window.setTimeout(() => setShowKhoaSuggestions(false), 120)}
              onKeyDown={(e) => e.key === 'Enter' && handleSearch()}
              placeholder="AK01, 66016K26A..."
              autoComplete="off"
            />
            {showKhoaSuggestions && (
              <div className="autocomplete-menu">
                {isKhoaLookupLoading && <div className="autocomplete-empty">Đang tìm...</div>}
                {!isKhoaLookupLoading && khoaSuggestions.length === 0 && (
                  <div className="autocomplete-empty">Không có gợi ý</div>
                )}
                {!isKhoaLookupLoading &&
                  khoaSuggestions.map((option) => (
                    <button
                      key={option.maKhoa}
                      type="button"
                      className="autocomplete-option"
                      onMouseDown={(e) => e.preventDefault()}
                      onClick={() => selectKhoa(option)}
                    >
                      {option.label}
                    </button>
                  ))}
              </div>
            )}
            {khoaWarning && <div className="field__warning">{khoaWarning}</div>}
          </div>
          <div className="field field--autocomplete">
            <label className="field__label" htmlFor="hv-hang">
              Hạng học
            </label>
            <input
              id="hv-hang"
              className="field__input"
              type="text"
              value={hangHocInput}
              onChange={(e) => handleHangHocInputChange(e.target.value)}
              onFocus={() => hangHocSuggestions.length > 0 && setShowHangHocSuggestions(true)}
              onBlur={() => window.setTimeout(() => setShowHangHocSuggestions(false), 120)}
              onKeyDown={(e) => e.key === 'Enter' && handleSearch()}
              placeholder="AM, A1M..."
              autoComplete="off"
            />
            {showHangHocSuggestions && (
              <div className="autocomplete-menu">
                {isHangHocLookupLoading && <div className="autocomplete-empty">Đang tìm...</div>}
                {!isHangHocLookupLoading && hangHocSuggestions.length === 0 && (
                  <div className="autocomplete-empty">Không có gợi ý</div>
                )}
                {!isHangHocLookupLoading &&
                  hangHocSuggestions.map((option) => (
                    <button
                      key={option.maHangDT}
                      type="button"
                      className="autocomplete-option"
                      onMouseDown={(e) => e.preventDefault()}
                      onClick={() => selectHangHoc(option)}
                    >
                      {option.label}
                    </button>
                  ))}
              </div>
            )}
            {hangHocWarning && <div className="field__warning">{hangHocWarning}</div>}
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
              disabled={totalItems === 0 || isExporting}
            >
              {isExporting ? 'Đang xuất...' : 'Xuất Excel'}
            </button>
          </div>
        </div>
        {exportErrorMessage && <div className="toolbar__error">{exportErrorMessage}</div>}
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
              onClick={() => load({ ...currentFilters(), page, pageSize: PAGE_SIZE })}
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
                  <td title={row.tenKhoa ? `${row.tenKhoa} - ${row.maKhoa ?? ''}` : row.maKhoa ?? ''}>
                    {row.tenKhoa && row.maKhoa
                      ? `${row.tenKhoa} - ${row.maKhoa}`
                      : row.maKhoa ?? row.tenKhoa ?? ''}
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
