import { useCallback, useEffect, useRef, useState } from 'react';
import { Link } from 'react-router-dom';
import {
  auditHocVienPhotos,
  exportHocVienExcel,
  getHocVienHangHocLookups,
  getHocVienPhotoPreviewUrl,
  getHocVienKhoaLookups,
  searchHocVien,
} from './api';
import type {
  HocVienExportParams,
  HocVienHangHocLookup,
  HocVienKhoaLookup,
  HocVienListItem,
  HocVienPhotoAuditResult,
  HocVienSearchParams,
} from './types';
import {
  formatGioiTinh,
  formatNgaySinh,
} from './utils';
import CopyButton from './CopyButton';

const PAGE_SIZE = 20;

type Status = 'idle' | 'loading' | 'success' | 'error';

function formatPhotoStatus(status: string): string {
  switch (status) {
    case 'HasPhoto':
      return 'Co anh';
    case 'Missing':
      return 'Thieu anh';
    case 'Invalid':
      return 'Anh loi';
    case 'Unsupported':
      return 'Khong ho tro';
    case 'UnsafePath':
      return 'Duong dan khong an toan';
    default:
      return status;
  }
}

function HocVienPhoto({
  hocVienId,
  title,
  onPreview,
}: {
  hocVienId: number;
  title: string;
  onPreview: (url: string, title: string) => void;
}) {
  const [failed, setFailed] = useState(false);
  const url = getHocVienPhotoPreviewUrl(hocVienId);

  useEffect(() => {
    setFailed(false);
  }, [hocVienId]);

  if (!failed) {
    return (
      <button
        type="button"
        className="hocvien-photo-button"
        title={title}
        onClick={() => onPreview(url, title)}
      >
        <span className="hocvien-photo">
        <img src={url} alt="Ảnh thẻ" onError={() => setFailed(true)} />
        </span>
      </button>
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
  const [khoaLookupError, setKhoaLookupError] = useState('');

  const [hangHocInput, setHangHocInput] = useState('');
  const [selectedHangHoc, setSelectedHangHoc] = useState<HocVienHangHocLookup | null>(null);
  const [hangHocSuggestions, setHangHocSuggestions] = useState<HocVienHangHocLookup[]>([]);
  const [showHangHocSuggestions, setShowHangHocSuggestions] = useState(false);
  const [isHangHocLookupLoading, setIsHangHocLookupLoading] = useState(false);
  const [hangHocWarning, setHangHocWarning] = useState('');
  const [hangHocLookupError, setHangHocLookupError] = useState('');

  const [gioiTinh, setGioiTinh] = useState('');
  const [page, setPage] = useState(1);

  const [status, setStatus] = useState<Status>('idle');
  const [rows, setRows] = useState<HocVienListItem[]>([]);
  const [totalItems, setTotalItems] = useState(0);
  const [totalPages, setTotalPages] = useState(0);
  const [errorMessage, setErrorMessage] = useState('');
  const [isExporting, setIsExporting] = useState(false);
  const [exportErrorMessage, setExportErrorMessage] = useState('');
  const [photoPreview, setPhotoPreview] = useState<{ url: string; title: string } | null>(null);
  const [showPhotoAudit, setShowPhotoAudit] = useState(false);
  const [photoAudit, setPhotoAudit] = useState<HocVienPhotoAuditResult | null>(null);
  const [photoAuditStatus, setPhotoAuditStatus] = useState<Status>('idle');
  const [photoAuditErrorMessage, setPhotoAuditErrorMessage] = useState('');
  const [auditPage, setAuditPage] = useState(1);
  const [auditValidateDecode, setAuditValidateDecode] = useState(false);
  const [auditOnlyMissing, setAuditOnlyMissing] = useState(false);
  const [auditOnlyInvalid, setAuditOnlyInvalid] = useState(false);
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

  const loadPhotoAudit = useCallback(
    async (
      options?: Partial<{
        page: number;
        validateDecode: boolean;
        onlyMissing: boolean;
        onlyInvalid: boolean;
      }>,
    ) => {
      const nextPage = options?.page ?? auditPage;
      const nextValidateDecode = options?.validateDecode ?? auditValidateDecode;
      const nextOnlyMissing = options?.onlyMissing ?? auditOnlyMissing;
      const nextOnlyInvalid = options?.onlyInvalid ?? auditOnlyInvalid;

      setPhotoAuditStatus('loading');
      setPhotoAuditErrorMessage('');
      try {
        const result = await auditHocVienPhotos({
          ...currentFilters(),
          page: nextPage,
          pageSize: PAGE_SIZE,
          validateDecode: nextValidateDecode,
          onlyMissing: nextOnlyMissing,
          onlyInvalid: nextOnlyInvalid,
        });
        setPhotoAudit(result);
        setAuditPage(result.page);
        setAuditValidateDecode(nextValidateDecode);
        setAuditOnlyMissing(nextOnlyMissing);
        setAuditOnlyInvalid(nextOnlyInvalid);
        setPhotoAuditStatus('success');
      } catch (err) {
        setPhotoAudit(null);
        setPhotoAuditErrorMessage(
          err instanceof Error ? err.message : 'Khong the doi soat anh hoc vien.',
        );
        setPhotoAuditStatus('error');
      }
    },
    [
      auditOnlyInvalid,
      auditOnlyMissing,
      auditPage,
      auditValidateDecode,
      currentFilters,
    ],
  );

  useEffect(() => {
    load({ ...currentFilters(), page, pageSize: PAGE_SIZE });
    return () => abortRef.current?.abort();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [page]);

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
    setKhoaLookupError('');

    const timer = window.setTimeout(async () => {
      try {
        const result = await getHocVienKhoaLookups(
          lookupKeyword,
          20,
          selectedHangHoc?.maHangDT,
          controller.signal,
        );
        setKhoaSuggestions(result);
        setShowKhoaSuggestions(true);
      } catch {
        if (!controller.signal.aborted) {
          setKhoaSuggestions([]);
          setKhoaLookupError('Không tải được gợi ý Khóa.');
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
  }, [khoaInput, selectedKhoa, selectedHangHoc]);

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
    setHangHocLookupError('');

    const timer = window.setTimeout(async () => {
      try {
        const result = await getHocVienHangHocLookups(lookupKeyword, 20, controller.signal);
        setHangHocSuggestions(result);
        setShowHangHocSuggestions(true);
      } catch {
        if (!controller.signal.aborted) {
          setHangHocSuggestions([]);
          setHangHocLookupError('Không tải được gợi ý Hạng học.');
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

  function validateSelectedLookups(): boolean {
    const hasInvalidKhoa = khoaInput.trim().length > 0 && !selectedKhoa;
    const hasInvalidHangHoc = hangHocInput.trim().length > 0 && !selectedHangHoc;

    setKhoaWarning(hasInvalidKhoa ? 'Vui lòng chọn Khóa trong danh sách gợi ý' : '');
    setHangHocWarning(hasInvalidHangHoc ? 'Vui lòng chọn Hạng học trong danh sách gợi ý' : '');

    return !hasInvalidKhoa && !hasInvalidHangHoc;
  }

  function handleKhoaInputChange(value: string) {
    setKhoaInput(value);
    setSelectedKhoa(null);
    setKhoaWarning('');
    setKhoaLookupError('');
  }

  function handleHangHocInputChange(value: string) {
    setHangHocInput(value);
    setSelectedHangHoc(null);
    clearKhoaSelection();
    setHangHocWarning('');
    setHangHocLookupError('');
  }

  function clearKhoaSelection() {
    setKhoaInput('');
    setSelectedKhoa(null);
    setKhoaSuggestions([]);
    setShowKhoaSuggestions(false);
    setKhoaWarning('');
    setKhoaLookupError('');
  }

  function selectKhoa(option: HocVienKhoaLookup) {
    setSelectedKhoa(option);
    setKhoaInput(option.label);
    setKhoaSuggestions([]);
    setShowKhoaSuggestions(false);
    setKhoaWarning('');
    setKhoaLookupError('');
  }

  function selectHangHoc(option: HocVienHangHocLookup) {
    setSelectedHangHoc(option);
    setHangHocInput(option.label);
    clearKhoaSelection();
    setHangHocSuggestions([]);
    setShowHangHocSuggestions(false);
    setHangHocWarning('');
    setHangHocLookupError('');
  }

  function handleSearch() {
    setExportErrorMessage('');
    if (!validateSelectedLookups()) return;

    if (showPhotoAudit) {
      setAuditPage(1);
      loadPhotoAudit({ page: 1 });
      return;
    }

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
    setShowKhoaSuggestions(false);
    setKhoaWarning('');
    setKhoaLookupError('');
    setHangHocInput('');
    setSelectedHangHoc(null);
    setHangHocSuggestions([]);
    setShowHangHocSuggestions(false);
    setHangHocWarning('');
    setHangHocLookupError('');
    setGioiTinh('');
    setExportErrorMessage('');
    setShowPhotoAudit(false);
    setPhotoAudit(null);
    setPhotoAuditStatus('idle');
    setPhotoAuditErrorMessage('');
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
      downloadBlob(result.blob, result.fileName);
    } catch (err) {
      setExportErrorMessage(
        err instanceof Error ? err.message : 'Không thể xuất Excel. Vui lòng thử lại.',
      );
    } finally {
      setIsExporting(false);
    }
  }

  function downloadBlob(blob: Blob, fileName: string) {
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    link.download = fileName;
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
    URL.revokeObjectURL(url);
  }

  const startIndex = (page - 1) * PAGE_SIZE;

  return (
    <div>
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
                      onMouseDown={(event) => event.preventDefault()}
                      onClick={() => selectKhoa(option)}
                      title={option.label}
                    >
                      {option.label}
                    </button>
                  ))}
              </div>
            )}
            {(khoaWarning || khoaLookupError) && (
              <div className="field__warning">{khoaWarning || khoaLookupError}</div>
            )}
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
                      onMouseDown={(event) => event.preventDefault()}
                      onClick={() => selectHangHoc(option)}
                      title={option.label}
                    >
                      {option.label}
                    </button>
                  ))}
              </div>
            )}
            {(hangHocWarning || hangHocLookupError) && (
              <div className="field__warning">{hangHocWarning || hangHocLookupError}</div>
            )}
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
            <button
              type="button"
              className="btn btn--ghost"
              onClick={() => {
                if (showPhotoAudit) {
                  setShowPhotoAudit(false);
                  return;
                }

                setShowPhotoAudit(true);
                loadPhotoAudit({ page: 1, validateDecode: false });
              }}
            >
              {showPhotoAudit ? 'Danh sach hoc vien' : 'Doi soat anh'}
            </button>
            {showPhotoAudit && (
              <button
                type="button"
                className="btn btn--ghost"
                onClick={() => loadPhotoAudit({ page: 1, validateDecode: true })}
                disabled={photoAuditStatus === 'loading'}
              >
                Kiem tra decode anh
              </button>
            )}
            <Link className="btn btn--ghost" to="/in-the-hoc-vien">
              Sang màn In thẻ học viên
            </Link>
          </div>
        </div>
        {exportErrorMessage && <div className="toolbar__error">{exportErrorMessage}</div>}
      </div>

      <div className="table-wrap">
        {showPhotoAudit ? (
          <>
            <div className="audit-panel">
              <div className="audit-panel__summary">
                <strong>Doi soat anh hoc vien</strong>
                {photoAudit && (
                  <span>
                    Tong: {photoAudit.totalItems.toLocaleString('vi-VN')} · Co anh:{' '}
                    {photoAudit.totalHasPhoto.toLocaleString('vi-VN')} · Thieu:{' '}
                    {photoAudit.totalMissingPhoto.toLocaleString('vi-VN')} · Loi:{' '}
                    {photoAudit.totalInvalidPhoto.toLocaleString('vi-VN')}
                  </span>
                )}
              </div>
              <div className="audit-panel__filters">
                <label>
                  <input
                    type="checkbox"
                    checked={auditOnlyMissing}
                    onChange={(event) => {
                      const value = event.target.checked;
                      loadPhotoAudit({ page: 1, onlyMissing: value });
                    }}
                  />{' '}
                  Chi xem thieu anh
                </label>
                <label>
                  <input
                    type="checkbox"
                    checked={auditOnlyInvalid}
                    onChange={(event) => {
                      const value = event.target.checked;
                      loadPhotoAudit({ page: 1, onlyInvalid: value });
                    }}
                  />{' '}
                  Chi xem anh loi/khong ho tro
                </label>
                <span>Decode: {auditValidateDecode ? 'da kiem tra' : 'chua kiem tra'}</span>
                <span>TODO: xuat danh sach thieu anh ra Excel.</span>
              </div>
            </div>

            {photoAuditStatus === 'loading' && (
              <div className="state">
                <div className="spinner" aria-hidden="true" />
                <div>Dang doi soat anh...</div>
              </div>
            )}

            {photoAuditStatus === 'error' && (
              <div className="state state--error">
                <div>{photoAuditErrorMessage}</div>
                <button
                  type="button"
                  className="btn btn--ghost btn--sm"
                  style={{ marginTop: 12 }}
                  onClick={() => loadPhotoAudit()}
                >
                  Thu lai
                </button>
              </div>
            )}

            {photoAuditStatus === 'success' && photoAudit?.items.length === 0 && (
              <div className="state">Khong co hoc vien phu hop voi dieu kien doi soat.</div>
            )}

            {photoAuditStatus === 'success' && photoAudit && photoAudit.items.length > 0 && (
              <table className="table table--photo-audit">
                <thead>
                  <tr>
                    <th>Ma DK</th>
                    <th>Ho ten</th>
                    <th>Khoa</th>
                    <th>Hang hoc</th>
                    <th>Duong dan du kien</th>
                    <th>Trang thai anh</th>
                    <th>Ghi chu</th>
                  </tr>
                </thead>
                <tbody>
                  {photoAudit.items.map((item) => {
                    const khoaLabel =
                      item.tenKhoa && item.maKhoa
                        ? `${item.tenKhoa} - ${item.maKhoa}`
                        : item.maKhoa ?? item.tenKhoa ?? '';
                    const hangLabel =
                      item.maHangDT && item.hangGplxHoc
                        ? `${item.maHangDT} - ${item.hangGplxHoc}`
                        : item.maHangDT ?? item.hangGplxHoc ?? '';

                    return (
                      <tr key={item.hocVienId}>
                        <td className="cell-ellipsis" title={item.maDangKy}>
                          {item.maDangKy}
                        </td>
                        <td className="cell-ellipsis cell-name" title={item.hoVaTen}>
                          {item.hoVaTen}
                        </td>
                        <td className="cell-ellipsis" title={khoaLabel}>
                          {khoaLabel}
                        </td>
                        <td className="cell-ellipsis" title={hangLabel}>
                          {hangLabel}
                        </td>
                        <td className="cell-ellipsis" title={item.expectedRelativePath}>
                          {item.expectedRelativePath}
                        </td>
                        <td>
                          <span className={`photo-status photo-status--${item.photoStatus.toLowerCase()}`}>
                            {formatPhotoStatus(item.photoStatus)}
                          </span>
                        </td>
                        <td className="cell-ellipsis" title={item.message}>
                          {item.message}
                        </td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            )}
          </>
        ) : (
          <>
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
                <th>
                  Giới
                  <br />
                  tính
                </th>
                <th>Số CCCD</th>
                <th>Địa chỉ</th>
                <th>Hạng học</th>
                <th>Số GPLX đã có</th>
                <th>
                  Hạng GPLX
                  <br />
                  đã có
                </th>
                <th>Người nhận hồ sơ</th>
                <th>Khóa</th>
              </tr>
            </thead>
            <tbody>
              {rows.map((row, index) => {
                const khoaLabel =
                  row.tenKhoa && row.maKhoa
                    ? `${row.tenKhoa} - ${row.maKhoa}`
                    : row.maKhoa ?? row.tenKhoa ?? '';

                return (
                  <tr key={row.hocVienId || row.maDangKy || index}>
                    <td>{startIndex + index + 1}</td>
                    <td>
                      <HocVienPhoto
                        hocVienId={row.hocVienId}
                        title={row.hoVaTen}
                        onPreview={(url, title) =>
                          setPhotoPreview({ url, title })
                        }
                      />
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
                    <td title={row.maHangDT ? `Mã hạng học: ${row.maHangDT}` : ''}>
                      {row.hangGplxHoc ?? ''}
                    </td>
                    <td>{row.soGplxDaCo ?? ''}</td>
                    <td>{row.hangGplxDaCo ?? ''}</td>
                    <td className="cell-ellipsis" title={row.nguoiNhanHoSo ?? ''}>
                      {row.nguoiNhanHoSo ?? ''}
                    </td>
                    <td className="cell-ellipsis" title={khoaLabel}>
                      {khoaLabel}
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        )}
          </>
        )}
      </div>

      {!showPhotoAudit && status === 'success' && totalItems > 0 && (
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

      {showPhotoAudit && photoAuditStatus === 'success' && photoAudit && photoAudit.totalItems > 0 && (
        <div className="pager">
          <span>
            Tong so: {photoAudit.totalItems.toLocaleString('vi-VN')} hoc vien · Trang {photoAudit.page}/
            {Math.max(photoAudit.totalPages, 1)}
          </span>
          <div className="pager__controls">
            <button
              type="button"
              className="btn btn--ghost btn--sm"
              disabled={photoAudit.page <= 1}
              onClick={() => loadPhotoAudit({ page: Math.max(1, photoAudit.page - 1) })}
            >
              Trang truoc
            </button>
            <button
              type="button"
              className="btn btn--ghost btn--sm"
              disabled={photoAudit.page >= photoAudit.totalPages}
              onClick={() => loadPhotoAudit({ page: photoAudit.page + 1 })}
            >
              Trang sau
            </button>
          </div>
        </div>
      )}

      {photoPreview && (
        <div
          className="photo-modal"
          role="dialog"
          aria-modal="true"
          aria-label={photoPreview.title}
          onClick={() => setPhotoPreview(null)}
        >
          <div className="photo-modal__dialog" onClick={(event) => event.stopPropagation()}>
            <button
              type="button"
              className="photo-modal__close"
              aria-label="Dong anh"
              onClick={() => setPhotoPreview(null)}
            >
              x
            </button>
            <img src={photoPreview.url} alt={photoPreview.title} />
          </div>
        </div>
      )}

    </div>
  );
}
