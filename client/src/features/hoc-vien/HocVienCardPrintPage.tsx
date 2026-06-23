import { useCallback, useEffect, useRef, useState } from 'react';
import {
  getHocVienHangHocLookups,
  getHocVienKhoaLookups,
  getHocVienPhotoPreviewUrl,
  previewHocVienCardsA4,
  printHocVienCardsA4,
  searchHocVien,
} from './api';
import type {
  HocVienCardPrintPreview,
  HocVienCardPrintPreviewItem,
  HocVienHangHocLookup,
  HocVienKhoaLookup,
  HocVienListItem,
  HocVienMissingPhotoMode,
  HocVienPrintCardsRequest,
  HocVienPrintSortBy,
  HocVienSearchParams,
} from './types';
import { formatGioiTinh, formatNgaySinh } from './utils';

const PAGE_SIZE = 20;
const TITLE_MAX_LENGTH = 100;
const TITLE_STORAGE_KEY = 'qlhv.hoc-vien-card-titles.v1';
const DEFAULT_TITLES = {
  titleLine1: 'SỞ XÂY DỰNG TỈNH GIA LAI',
  titleLine2: 'TRUNG TÂM ĐÀO TẠO LÁI XE THÀNH CÔNG',
};

type Status = 'idle' | 'loading' | 'success' | 'error';

interface CardTitles {
  titleLine1: string;
  titleLine2: string;
}

interface PdfPreviewState {
  blob: Blob;
  fileName: string;
  url: string;
}

function normalizeTitle(value: string, fallback: string): string {
  const normalized = value.trim() || fallback;
  return normalized.slice(0, TITLE_MAX_LENGTH).toLocaleUpperCase('vi-VN');
}

function loadSavedTitles(): CardTitles {
  try {
    const raw = window.localStorage.getItem(TITLE_STORAGE_KEY);
    if (!raw) return DEFAULT_TITLES;
    const parsed = JSON.parse(raw) as Partial<CardTitles>;
    return {
      titleLine1: normalizeTitle(parsed.titleLine1 ?? '', DEFAULT_TITLES.titleLine1),
      titleLine2: normalizeTitle(parsed.titleLine2 ?? '', DEFAULT_TITLES.titleLine2),
    };
  } catch {
    return DEFAULT_TITLES;
  }
}

function formatTrainingRank(hangGplxHoc: string | null, maHangDT: string | null): string {
  let value = (hangGplxHoc || maHangDT || '').trim();
  while (/^(hạng|hang)\s+/i.test(value)) {
    value = value.replace(/^(hạng|hang)\s+/i, '').trimStart();
  }
  return value ? `TẬP LÁI XE HẠNG: ${value.toLocaleUpperCase('vi-VN')}` : '';
}

function formatPhotoStatus(status: string): string {
  switch (status) {
    case 'HasPhoto': return 'Có ảnh';
    case 'Missing': return 'Thiếu ảnh';
    case 'Invalid': return 'Ảnh lỗi';
    case 'Unsupported': return 'Không hỗ trợ';
    case 'UnsafePath': return 'Đường dẫn không an toàn';
    default: return status;
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

function CardPhoto({ item }: { item: HocVienCardPrintPreviewItem }) {
  const [failed, setFailed] = useState(!item.hasPhoto);

  useEffect(() => setFailed(!item.hasPhoto), [item.hocVienId, item.hasPhoto]);

  if (failed) {
    return (
      <span className="card-preview__photo-placeholder">
        <strong>Ảnh màu</strong><span>3 cm x 4 cm</span><span>chưa có ảnh</span>
      </span>
    );
  }

  return (
    <img
      className="card-preview__photo-image"
      src={getHocVienPhotoPreviewUrl(item.hocVienId)}
      alt={`Ảnh thẻ ${item.hoVaTen}`}
      onError={() => setFailed(true)}
    />
  );
}

function CardSheetPreview({ preview }: { preview: HocVienCardPrintPreview }) {
  const slots = Array.from(
    { length: 12 },
    (_, index) => preview.items.slice(0, 12)[index] ?? null,
  );

  return (
    <section className="card-sheet-preview" aria-label="Xem trước trang A4 đầu tiên">
      <div className="card-sheet-preview__toolbar">
        <strong>Xem trước trang A4 đầu tiên</strong>
        <span>Tỷ lệ mô phỏng, khi in chọn Actual size / 100%</span>
      </div>
      <div className="card-sheet-preview__viewport">
        <div className="card-sheet-preview__page">
          <div className="card-sheet-preview__grid">
            {slots.map((item, index) => item ? (
              <article className="card-preview" key={item.hocVienId}>
                <header className="card-preview__header">
                  <div>{preview.organizationLine1}</div>
                  <div>{preview.organizationLine2}</div>
                </header>
                <div className="card-preview__body">
                  <div className="card-preview__photo"><CardPhoto item={item} /></div>
                  <div className="card-preview__content">
                    <div className="card-preview__title">{preview.cardTitle}</div>
                    <div className="card-preview__name" title={item.hoVaTen}>
                      {item.hoVaTen.toLocaleUpperCase('vi-VN')}
                    </div>
                    <div className="card-preview__rank">
                      {formatTrainingRank(item.hangGplxHoc, item.maHangDT)}
                    </div>
                  </div>
                </div>
              </article>
            ) : <div className="card-preview card-preview--empty" key={`empty-${index}`} />)}
          </div>
        </div>
      </div>
    </section>
  );
}

function ResultPhoto({ row }: { row: HocVienListItem }) {
  const [failed, setFailed] = useState(false);
  useEffect(() => setFailed(false), [row.hocVienId]);
  return failed ? (
    <span className="hocvien-photo hocvien-photo--placeholder">Ảnh</span>
  ) : (
    <span className="hocvien-photo">
      <img
        src={getHocVienPhotoPreviewUrl(row.hocVienId)}
        alt={`Ảnh ${row.hoVaTen}`}
        onError={() => setFailed(true)}
      />
    </span>
  );
}

export default function HocVienCardPrintPage() {
  const initialTitles = loadSavedTitles();
  const [titleInputs, setTitleInputs] = useState<CardTitles>(initialTitles);
  const [savedTitles, setSavedTitles] = useState<CardTitles>(initialTitles);
  const [settingsMessage, setSettingsMessage] = useState('');

  const [keyword, setKeyword] = useState('');
  const [khoaInput, setKhoaInput] = useState('');
  const [selectedKhoa, setSelectedKhoa] = useState<HocVienKhoaLookup | null>(null);
  const [khoaSuggestions, setKhoaSuggestions] = useState<HocVienKhoaLookup[]>([]);
  const [showKhoaSuggestions, setShowKhoaSuggestions] = useState(false);
  const [khoaLookupStatus, setKhoaLookupStatus] = useState<Status>('idle');
  const [khoaWarning, setKhoaWarning] = useState('');

  const [hangHocInput, setHangHocInput] = useState('');
  const [selectedHangHoc, setSelectedHangHoc] = useState<HocVienHangHocLookup | null>(null);
  const [hangHocSuggestions, setHangHocSuggestions] = useState<HocVienHangHocLookup[]>([]);
  const [showHangHocSuggestions, setShowHangHocSuggestions] = useState(false);
  const [hangLookupStatus, setHangLookupStatus] = useState<Status>('idle');
  const [hangWarning, setHangWarning] = useState('');

  const [gioiTinh, setGioiTinh] = useState('');
  const [page, setPage] = useState(1);
  const [rows, setRows] = useState<HocVienListItem[]>([]);
  const [totalItems, setTotalItems] = useState(0);
  const [totalPages, setTotalPages] = useState(0);
  const [listStatus, setListStatus] = useState<Status>('idle');
  const [listError, setListError] = useState('');
  const [selectedIds, setSelectedIds] = useState<Set<number>>(() => new Set());

  const [printRequest, setPrintRequest] = useState<HocVienPrintCardsRequest | null>(null);
  const [printPreview, setPrintPreview] = useState<HocVienCardPrintPreview | null>(null);
  const [printStatus, setPrintStatus] = useState<Status>('idle');
  const [printError, setPrintError] = useState('');
  const [missingPhotoMode, setMissingPhotoMode] = useState<HocVienMissingPhotoMode>('placeholder');
  const [sortBy, setSortBy] = useState<HocVienPrintSortBy>('current');
  const [pdfPreview, setPdfPreview] = useState<PdfPreviewState | null>(null);
  const [pdfStatus, setPdfStatus] = useState<Status>('idle');
  const [pdfError, setPdfError] = useState('');
  const [isDownloading, setIsDownloading] = useState(false);

  const listAbortRef = useRef<AbortController | null>(null);
  const pdfAbortRef = useRef<AbortController | null>(null);
  const pdfUrlRef = useRef<string | null>(null);

  const currentFilters = useCallback(() => ({
    keyword: keyword.trim() || undefined,
    maKhoa: selectedKhoa?.maKhoa,
    maHangDT: selectedHangHoc?.maHangDT,
    gioiTinh: gioiTinh || undefined,
  }), [keyword, selectedKhoa, selectedHangHoc, gioiTinh]);

  const loadRows = useCallback(async (params: HocVienSearchParams) => {
    listAbortRef.current?.abort();
    const controller = new AbortController();
    listAbortRef.current = controller;
    setListStatus('loading');
    setListError('');
    try {
      const result = await searchHocVien(params, controller.signal);
      setRows(result.items);
      setTotalItems(result.totalItems);
      setTotalPages(result.totalPages);
      setListStatus('success');
    } catch (error) {
      if (controller.signal.aborted) return;
      setRows([]);
      setTotalItems(0);
      setTotalPages(0);
      setListStatus('error');
      setListError(error instanceof Error ? error.message : 'Không thể tải danh sách học viên.');
    }
  }, []);

  useEffect(() => {
    loadRows({ ...currentFilters(), page, pageSize: PAGE_SIZE });
    return () => listAbortRef.current?.abort();
    // Filters are applied explicitly by the search button; page is the automatic trigger.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [page]);

  useEffect(() => () => {
    pdfAbortRef.current?.abort();
    if (pdfUrlRef.current) URL.revokeObjectURL(pdfUrlRef.current);
  }, []);

  useEffect(() => {
    const value = khoaInput.trim();
    if (!value || selectedKhoa?.label === value) {
      setKhoaSuggestions([]);
      setShowKhoaSuggestions(false);
      setKhoaLookupStatus('idle');
      return;
    }
    const controller = new AbortController();
    setKhoaLookupStatus('loading');
    const timer = window.setTimeout(async () => {
      try {
        const result = await getHocVienKhoaLookups(
          value, 20, selectedHangHoc?.maHangDT, controller.signal,
        );
        setKhoaSuggestions(result);
        setShowKhoaSuggestions(true);
        setKhoaLookupStatus('success');
      } catch {
        if (!controller.signal.aborted) {
          setKhoaSuggestions([]);
          setKhoaLookupStatus('error');
        }
      }
    }, 200);
    return () => { window.clearTimeout(timer); controller.abort(); };
  }, [khoaInput, selectedKhoa, selectedHangHoc]);

  useEffect(() => {
    const value = hangHocInput.trim();
    if (!value || selectedHangHoc?.label === value) {
      setHangHocSuggestions([]);
      setShowHangHocSuggestions(false);
      setHangLookupStatus('idle');
      return;
    }
    const controller = new AbortController();
    setHangLookupStatus('loading');
    const timer = window.setTimeout(async () => {
      try {
        const result = await getHocVienHangHocLookups(value, 20, controller.signal);
        setHangHocSuggestions(result);
        setShowHangHocSuggestions(true);
        setHangLookupStatus('success');
      } catch {
        if (!controller.signal.aborted) {
          setHangHocSuggestions([]);
          setHangLookupStatus('error');
        }
      }
    }, 200);
    return () => { window.clearTimeout(timer); controller.abort(); };
  }, [hangHocInput, selectedHangHoc]);

  function clearKhoa() {
    setKhoaInput('');
    setSelectedKhoa(null);
    setKhoaSuggestions([]);
    setShowKhoaSuggestions(false);
    setKhoaWarning('');
  }

  function validateLookups(): boolean {
    const invalidKhoa = Boolean(khoaInput.trim() && !selectedKhoa);
    const invalidHang = Boolean(hangHocInput.trim() && !selectedHangHoc);
    setKhoaWarning(invalidKhoa ? 'Vui lòng chọn Khóa trong danh sách gợi ý' : '');
    setHangWarning(invalidHang ? 'Vui lòng chọn Hạng học trong danh sách gợi ý' : '');
    return !invalidKhoa && !invalidHang;
  }

  function handleSearch() {
    if (!validateLookups()) return;
    setSelectedIds(new Set());
    if (page === 1) loadRows({ ...currentFilters(), page: 1, pageSize: PAGE_SIZE });
    else setPage(1);
  }

  function handleReset() {
    setKeyword('');
    clearKhoa();
    setHangHocInput('');
    setSelectedHangHoc(null);
    setHangHocSuggestions([]);
    setShowHangHocSuggestions(false);
    setHangWarning('');
    setGioiTinh('');
    setSelectedIds(new Set());
    if (page === 1) loadRows({ page: 1, pageSize: PAGE_SIZE });
    else setPage(1);
  }

  function handleSaveSettings() {
    const normalized = {
      titleLine1: normalizeTitle(titleInputs.titleLine1, DEFAULT_TITLES.titleLine1),
      titleLine2: normalizeTitle(titleInputs.titleLine2, DEFAULT_TITLES.titleLine2),
    };
    window.localStorage.setItem(TITLE_STORAGE_KEY, JSON.stringify(normalized));
    setTitleInputs(normalized);
    setSavedTitles(normalized);
    setSettingsMessage('Đã lưu thiết lập trên trình duyệt này.');
  }

  function withTitles(request: HocVienPrintCardsRequest): HocVienPrintCardsRequest {
    return { ...request, ...savedTitles };
  }

  function openPrint(request: HocVienPrintCardsRequest) {
    const titledRequest = withTitles(request);
    setPrintRequest(titledRequest);
    setMissingPhotoMode('placeholder');
    setSortBy('current');
    void loadPrintPreview(titledRequest, 'placeholder', 'current');
  }

  async function loadPrintPreview(
    request: HocVienPrintCardsRequest,
    photoMode = missingPhotoMode,
    nextSort = sortBy,
  ) {
    clearPdfPreview();
    setPrintStatus('loading');
    setPrintError('');
    try {
      const result = await previewHocVienCardsA4({
        ...request, missingPhotoMode: photoMode, sortBy: nextSort,
      });
      setPrintPreview(result);
      setPrintStatus('success');
    } catch (error) {
      setPrintPreview(null);
      setPrintStatus('error');
      setPrintError(error instanceof Error ? error.message : 'Không thể tạo bản xem trước.');
    }
  }

  function clearPdfPreview() {
    pdfAbortRef.current?.abort();
    pdfAbortRef.current = null;
    if (pdfUrlRef.current) {
      URL.revokeObjectURL(pdfUrlRef.current);
      pdfUrlRef.current = null;
    }
    setPdfPreview(null);
    setPdfStatus('idle');
    setPdfError('');
  }

  async function handlePreviewPdf() {
    if (!printRequest) return;
    clearPdfPreview();
    const controller = new AbortController();
    pdfAbortRef.current = controller;
    setPdfStatus('loading');
    try {
      const result = await printHocVienCardsA4({
        ...printRequest, missingPhotoMode, sortBy,
      }, controller.signal);
      if (controller.signal.aborted) return;
      const url = URL.createObjectURL(result.blob);
      pdfUrlRef.current = url;
      setPdfPreview({ ...result, url });
      setPdfStatus('success');
    } catch (error) {
      if (controller.signal.aborted) return;
      setPdfStatus('error');
      setPdfError(error instanceof Error ? error.message : 'Không thể tạo bản xem trước PDF.');
    } finally {
      if (pdfAbortRef.current === controller) pdfAbortRef.current = null;
    }
  }

  async function handleDownloadPdf() {
    if (!printRequest) return;
    if (pdfPreview) {
      downloadBlob(pdfPreview.blob, pdfPreview.fileName);
      return;
    }
    setIsDownloading(true);
    setPrintError('');
    try {
      const result = await printHocVienCardsA4({
        ...printRequest, missingPhotoMode, sortBy,
      });
      downloadBlob(result.blob, result.fileName);
    } catch (error) {
      setPrintError(error instanceof Error ? error.message : 'Không thể tải PDF.');
    } finally {
      setIsDownloading(false);
    }
  }

  function closePrint() {
    clearPdfPreview();
    setPrintRequest(null);
    setPrintPreview(null);
    setPrintStatus('idle');
    setPrintError('');
  }

  function toggleSelection(id: number, checked: boolean) {
    setSelectedIds((current) => {
      const next = new Set(current);
      if (checked) next.add(id); else next.delete(id);
      return next;
    });
  }

  const pageIds = rows.map((row) => row.hocVienId).filter((id) => id > 0);
  const allPageSelected = pageIds.length > 0 && pageIds.every((id) => selectedIds.has(id));
  const startIndex = (page - 1) * PAGE_SIZE;

  return (
    <div className="card-print-page">
      <section className="panel card-title-settings">
        <div className="card-title-settings__heading">
          <div>
            <strong>Thiết lập tiêu đề thẻ</strong>
            <p>Lưu trên trình duyệt hiện tại, không ghi vào cơ sở dữ liệu.</p>
          </div>
          <button type="button" className="btn btn--primary" onClick={handleSaveSettings}>
            Lưu thiết lập
          </button>
        </div>
        <div className="card-title-settings__fields">
          <label className="field">
            <span className="field__label">Tiêu đề 1 - Cơ quan quản lý cấp trên trực tiếp</span>
            <input
              className="field__input"
              maxLength={TITLE_MAX_LENGTH}
              value={titleInputs.titleLine1}
              onChange={(event) => {
                setTitleInputs((current) => ({ ...current, titleLine1: event.target.value }));
                setSettingsMessage('');
              }}
            />
          </label>
          <label className="field">
            <span className="field__label">Tiêu đề 2 - Tên cơ sở đào tạo</span>
            <input
              className="field__input"
              maxLength={TITLE_MAX_LENGTH}
              value={titleInputs.titleLine2}
              onChange={(event) => {
                setTitleInputs((current) => ({ ...current, titleLine2: event.target.value }));
                setSettingsMessage('');
              }}
            />
          </label>
        </div>
        {settingsMessage && <div className="card-title-settings__message">{settingsMessage}</div>}
      </section>

      <div className="toolbar">
        <div className="toolbar__row">
          <div className="field" style={{ flexBasis: 250 }}>
            <label className="field__label" htmlFor="print-keyword">Tìm kiếm</label>
            <input
              id="print-keyword" className="field__input" value={keyword}
              onChange={(event) => setKeyword(event.target.value)}
              onKeyDown={(event) => event.key === 'Enter' && handleSearch()}
              placeholder="Họ tên, mã đăng ký, số CCCD..."
            />
          </div>

          <div className="field field--autocomplete">
            <label className="field__label" htmlFor="print-khoa">Khóa</label>
            <input
              id="print-khoa" className="field__input" value={khoaInput}
              onChange={(event) => {
                setKhoaInput(event.target.value); setSelectedKhoa(null); setKhoaWarning('');
              }}
              onFocus={() => khoaSuggestions.length > 0 && setShowKhoaSuggestions(true)}
              onBlur={() => window.setTimeout(() => setShowKhoaSuggestions(false), 120)}
              placeholder="Tên khóa hoặc mã khóa"
              autoComplete="off"
            />
            {khoaLookupStatus === 'loading' && <span className="field__hint">Đang tìm...</span>}
            {khoaLookupStatus === 'error' && <span className="field__warning">Không tải được gợi ý Khóa.</span>}
            {showKhoaSuggestions && (
              <div className="autocomplete-list">
                {khoaSuggestions.length === 0 ? <div className="autocomplete-list__empty">Không có gợi ý</div> :
                  khoaSuggestions.map((option) => (
                    <button type="button" key={option.maKhoa} onMouseDown={(event) => event.preventDefault()}
                      onClick={() => {
                        setSelectedKhoa(option); setKhoaInput(option.label);
                        setKhoaSuggestions([]); setShowKhoaSuggestions(false); setKhoaWarning('');
                      }}>{option.label}</button>
                  ))}
              </div>
            )}
            {khoaWarning && <div className="field__warning">{khoaWarning}</div>}
          </div>

          <div className="field field--autocomplete">
            <label className="field__label" htmlFor="print-hang">Hạng học</label>
            <input
              id="print-hang" className="field__input" value={hangHocInput}
              onChange={(event) => {
                setHangHocInput(event.target.value); setSelectedHangHoc(null);
                clearKhoa(); setHangWarning('');
              }}
              onFocus={() => hangHocSuggestions.length > 0 && setShowHangHocSuggestions(true)}
              onBlur={() => window.setTimeout(() => setShowHangHocSuggestions(false), 120)}
              placeholder="AM, A1M..." autoComplete="off"
            />
            {hangLookupStatus === 'loading' && <span className="field__hint">Đang tìm...</span>}
            {hangLookupStatus === 'error' && <span className="field__warning">Không tải được gợi ý Hạng học.</span>}
            {showHangHocSuggestions && (
              <div className="autocomplete-list">
                {hangHocSuggestions.length === 0 ? <div className="autocomplete-list__empty">Không có gợi ý</div> :
                  hangHocSuggestions.map((option) => (
                    <button type="button" key={option.maHangDT} onMouseDown={(event) => event.preventDefault()}
                      onClick={() => {
                        setSelectedHangHoc(option); setHangHocInput(option.label); clearKhoa();
                        setHangHocSuggestions([]); setShowHangHocSuggestions(false); setHangWarning('');
                      }}>{option.label}</button>
                  ))}
              </div>
            )}
            {hangWarning && <div className="field__warning">{hangWarning}</div>}
          </div>

          <div className="field">
            <label className="field__label" htmlFor="print-gender">Giới tính</label>
            <select id="print-gender" className="field__input" value={gioiTinh}
              onChange={(event) => setGioiTinh(event.target.value)}>
              <option value="">Tất cả</option><option value="Nam">Nam</option><option value="Nữ">Nữ</option>
            </select>
          </div>

          <div className="toolbar__actions">
            <button type="button" className="btn btn--primary" onClick={handleSearch}>Tìm kiếm</button>
            <button type="button" className="btn btn--ghost" onClick={handleReset}>Làm mới</button>
            <button type="button" className="btn btn--ghost"
              disabled={selectedIds.size === 0}
              onClick={() => openPrint({ mode: 'selected', hocVienIds: Array.from(selectedIds) })}>
              In đã chọn ({selectedIds.size})
            </button>
            <button type="button" className="btn btn--ghost" disabled={!selectedKhoa}
              title={selectedKhoa?.label ?? 'Chọn Khóa để in toàn khóa'}
              onClick={() => selectedKhoa && openPrint({ mode: 'course', maKhoa: selectedKhoa.maKhoa })}>
              In toàn khóa
            </button>
          </div>
        </div>
      </div>

      <div className="table-wrap card-print-results">
        {listStatus === 'loading' && <div className="state"><div className="spinner" /><div>Đang tải dữ liệu...</div></div>}
        {listStatus === 'error' && <div className="state state--error">{listError}</div>}
        {listStatus === 'success' && rows.length === 0 && <div className="state">Không tìm thấy học viên phù hợp.</div>}
        {listStatus === 'success' && rows.length > 0 && (
          <table className="table table--card-print-results">
            <thead><tr>
              <th><input type="checkbox" aria-label="Chọn tất cả trên trang" checked={allPageSelected}
                onChange={(event) => {
                  const checked = event.target.checked;
                  setSelectedIds((current) => {
                    const next = new Set(current);
                    pageIds.forEach((id) => checked ? next.add(id) : next.delete(id));
                    return next;
                  });
                }} /></th>
              <th>STT</th><th>Ảnh</th><th>Họ và tên</th><th>Mã ĐK</th>
              <th>Hạng học</th><th>Khóa</th><th>Ngày sinh</th><th>Giới tính</th><th>Thao tác</th>
            </tr></thead>
            <tbody>{rows.map((row, index) => {
              const khoaLabel = row.tenKhoa && row.maKhoa
                ? `${row.tenKhoa} - ${row.maKhoa}` : row.maKhoa ?? row.tenKhoa ?? '';
              return <tr key={row.hocVienId}>
                <td><input type="checkbox" aria-label={`Chọn ${row.hoVaTen}`}
                  checked={selectedIds.has(row.hocVienId)}
                  onChange={(event) => toggleSelection(row.hocVienId, event.target.checked)} /></td>
                <td>{startIndex + index + 1}</td><td><ResultPhoto row={row} /></td>
                <td className="cell-ellipsis" title={row.hoVaTen}>{row.hoVaTen}</td>
                <td className="cell-ellipsis" title={row.maDangKy}>{row.maDangKy}</td>
                <td title={row.maHangDT ? `Mã hạng học: ${row.maHangDT}` : ''}>{row.hangGplxHoc ?? ''}</td>
                <td className="cell-ellipsis" title={khoaLabel}>{khoaLabel}</td>
                <td>{formatNgaySinh(row.ngaySinh)}</td><td>{formatGioiTinh(row.gioiTinh)}</td>
                <td><button type="button" className="btn btn--ghost btn--sm"
                  onClick={() => openPrint({ mode: 'single', hocVienId: row.hocVienId })}>In lẻ</button></td>
              </tr>;
            })}</tbody>
          </table>
        )}
      </div>

      {listStatus === 'success' && totalItems > 0 && <div className="pager">
        <span>Tổng số: {totalItems.toLocaleString('vi-VN')} học viên · Trang {page}/{Math.max(totalPages, 1)}</span>
        <div className="pager__controls">
          <button type="button" className="btn btn--ghost btn--sm" disabled={page <= 1}
            onClick={() => setPage((current) => Math.max(1, current - 1))}>Trang trước</button>
          <button type="button" className="btn btn--ghost btn--sm" disabled={page >= totalPages}
            onClick={() => setPage((current) => current + 1)}>Trang sau</button>
        </div>
      </div>}

      {printRequest && <div className="print-modal" role="dialog" aria-modal="true"
        aria-label="In thẻ học viên" onClick={closePrint}>
        <div className="print-modal__dialog" onClick={(event) => event.stopPropagation()}>
          <div className="print-modal__header"><div><strong>In thẻ học viên</strong>
            <p>A4 ngang · 3 cột × 4 dòng · 12 thẻ/trang · 85mm × 50mm</p></div>
            <button type="button" className="photo-modal__close" aria-label="Đóng" onClick={closePrint}>x</button>
          </div>
          <div className="print-modal__options">
            <label>Ảnh thiếu<select className="field__input" value={missingPhotoMode}
              onChange={(event) => {
                const value = event.target.value as HocVienMissingPhotoMode;
                setMissingPhotoMode(value); void loadPrintPreview(printRequest, value, sortBy);
              }}><option value="placeholder">Vẫn in ảnh placeholder</option><option value="skip">Bỏ qua học viên thiếu ảnh</option></select></label>
            <label>Sắp xếp<select className="field__input" value={sortBy}
              onChange={(event) => {
                const value = event.target.value as HocVienPrintSortBy;
                setSortBy(value); void loadPrintPreview(printRequest, missingPhotoMode, value);
              }}><option value="current">Thứ tự hiện tại</option><option value="hoTen">Theo họ tên</option><option value="maDK">Theo mã đăng ký</option></select></label>
          </div>
          {printStatus === 'loading' && <div className="state"><div className="spinner" /><div>Đang tạo bản xem trước...</div></div>}
          {printStatus === 'error' && <div className="state state--error">{printError}</div>}
          {printStatus === 'success' && printPreview && <>
            <div className="print-modal__summary">
              <span>Tổng sẽ in: {printPreview.totalStudents.toLocaleString('vi-VN')}</span>
              <span>Số trang PDF: {printPreview.totalPages.toLocaleString('vi-VN')}</span>
              <span>Thẻ/trang: {printPreview.cardsPerPage}</span><span>Layout: {printPreview.layoutName}</span>
              {printPreview.missingPhotoCount > 0 && <strong>Cảnh báo: {printPreview.missingPhotoCount} học viên thiếu ảnh</strong>}
            </div>
            <CardSheetPreview preview={printPreview} />
            {pdfStatus === 'loading' && <section className="pdf-preview-panel"><div className="pdf-preview-panel__status"><div className="spinner" /><div>Đang tạo PDF để xem trước...</div></div></section>}
            {pdfStatus === 'error' && <div className="pdf-preview-panel__error" role="alert">{pdfError}</div>}
            {pdfStatus === 'success' && pdfPreview && <section className="pdf-preview-panel" aria-label="Bản xem trước PDF">
              <div className="pdf-preview-panel__header"><div><strong>Bản xem trước PDF</strong><span>{pdfPreview.fileName}</span></div>
                <div className="pdf-preview-panel__actions">
                  <a className="btn btn--ghost btn--sm" href={pdfPreview.url} target="_blank" rel="noopener noreferrer">Mở PDF trong tab mới</a>
                  <button type="button" className="btn btn--ghost btn--sm" onClick={clearPdfPreview}>Đóng bản xem trước</button>
                </div></div>
              <iframe className="pdf-preview-panel__frame" src={`${pdfPreview.url}#toolbar=1&navpanes=0&view=FitH`}
                title={`Bản xem trước ${pdfPreview.fileName}`} />
            </section>}
            <div className="print-modal__table-wrap"><table className="table table--print-preview"><thead><tr>
              <th>Mã ĐK</th><th>Họ tên</th><th>Khóa</th><th>Hạng học</th><th>Trạng thái ảnh</th>
            </tr></thead><tbody>{printPreview.items.slice(0, 100).map((item) => <tr key={item.hocVienId}>
              <td>{item.maDangKy}</td><td>{item.hoVaTen}</td><td>{item.tenKhoa ?? item.maKhoa ?? ''}</td>
              <td>{item.hangGplxHoc ?? item.maHangDT ?? ''}</td><td>{formatPhotoStatus(item.photoStatus)}</td>
            </tr>)}</tbody></table></div>
          </>}
          <div className="print-modal__actions">
            <button type="button" className="btn btn--ghost" onClick={() => void loadPrintPreview(printRequest)}
              disabled={printStatus === 'loading' || pdfStatus === 'loading'}>Làm mới xem trước</button>
            <button type="button" className="btn btn--ghost" onClick={() => void handlePreviewPdf()}
              disabled={printStatus !== 'success' || pdfStatus === 'loading'}>
              {pdfStatus === 'loading' ? 'Đang tải PDF...' : pdfPreview ? 'Tạo lại PDF' : 'Xem trước PDF'}
            </button>
            <button type="button" className="btn btn--primary" onClick={() => void handleDownloadPdf()}
              disabled={printStatus !== 'success' || isDownloading || pdfStatus === 'loading'}>
              {isDownloading ? 'Đang tạo PDF...' : 'Tải PDF'}
            </button>
            <button type="button" className="btn btn--ghost" onClick={closePrint}>Hủy</button>
          </div>
        </div>
      </div>}
    </div>
  );
}
