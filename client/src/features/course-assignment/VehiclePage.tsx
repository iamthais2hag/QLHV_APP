import {
  useCallback,
  useEffect,
  useState,
  type FormEvent,
} from 'react';
import { searchVehicles } from './api';
import type { PagedResult, XeTapSourceItem } from './types';
import {
  EmptyState,
  PageMessage,
  Pager,
  StatusBadge,
} from './ui';

const PAGE_SIZE = 25;

export default function VehiclePage() {
  const [keywordDraft, setKeywordDraft] = useState('');
  const [keyword, setKeyword] = useState('');
  const [sourceProfileCode, setSourceProfileCode] = useState('');
  const [trangThai, setTrangThai] = useState('');
  const [page, setPage] = useState(1);
  const [result, setResult] = useState<PagedResult<XeTapSourceItem> | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async (signal?: AbortSignal) => {
    setLoading(true);
    setError(null);
    try {
      setResult(await searchVehicles({
        keyword,
        sourceProfileCode,
        trangThai,
        page,
        pageSize: PAGE_SIZE,
      }, signal));
    } catch (loadError) {
      if (!(loadError instanceof DOMException && loadError.name === 'AbortError')) {
        setError(loadError instanceof Error ? loadError.message : 'Không thể tải danh sách xe tập lái.');
      }
    } finally {
      if (!signal?.aborted) setLoading(false);
    }
  }, [keyword, page, sourceProfileCode, trangThai]);

  useEffect(() => {
    const controller = new AbortController();
    void load(controller.signal);
    return () => controller.abort();
  }, [load]);

  function submitSearch(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setPage(1);
    setKeyword(keywordDraft.trim());
  }

  return (
    <div className="assignment-page">
      <section className="panel assignment-hero">
        <div>
          <span className="assignment-eyebrow">Danh mục tích hợp</span>
          <h2>Xe tập lái</h2>
          <p>
            Danh mục được đồng bộ realtime từ CSDL_OTO/CSDL_MOTO.
            Định danh nghiệp vụ là nguồn + biển số; toàn bộ trường nguồn chỉ đọc.
          </p>
        </div>
        <span className="assignment-readonly-chip">App_XeTap · chỉ đọc nguồn</span>
      </section>

      <form className="toolbar" onSubmit={submitSearch}>
        <div className="toolbar__row">
          <label className="field">
            <span className="field__label">Biển số / định danh nguồn / số khung</span>
            <input
              className="field__input"
              value={keywordDraft}
              onChange={(event) => setKeywordDraft(event.target.value)}
              placeholder="Ví dụ: 51A12345..."
              maxLength={120}
            />
          </label>
          <label className="field">
            <span className="field__label">Nguồn</span>
            <select
              className="field__input"
              value={sourceProfileCode}
              onChange={(event) => {
                setSourceProfileCode(event.target.value);
                setPage(1);
              }}
            >
              <option value="">Tất cả OTO/MOTO</option>
              <option value="CSDT_OTO">OTO</option>
              <option value="CSDT_MOTO">MOTO</option>
            </select>
          </label>
          <label className="field">
            <span className="field__label">Trạng thái</span>
            <select
              className="field__input"
              value={trangThai}
              onChange={(event) => {
                setTrangThai(event.target.value);
                setPage(1);
              }}
            >
              <option value="">Tất cả trạng thái</option>
              <option value="ACTIVE">Hoạt động</option>
              <option value="INACTIVE">Ngừng hoạt động</option>
              <option value="MANUAL_REVIEW">Cần kiểm tra</option>
            </select>
          </label>
          <div className="toolbar__actions">
            <button type="submit" className="btn btn--primary" disabled={loading}>Tìm kiếm</button>
            <button type="button" className="btn btn--ghost" onClick={() => void load()} disabled={loading}>
              {loading ? 'Đang tải...' : 'Tải lại'}
            </button>
          </div>
        </div>
      </form>

      {error && <PageMessage kind="error">{error}</PageMessage>}
      {!error && result?.totalItems === 0 && (
        <PageMessage kind="info">
          Nguồn hiện chưa có xe phù hợp bộ lọc. Danh sách sẽ cập nhật khi realtime nhận bản ghi nguồn;
          giao diện không tự tạo xe từ nhập tay hoặc Excel.
        </PageMessage>
      )}

      {loading && !result && <div className="panel"><EmptyState>Đang tải danh sách xe...</EmptyState></div>}
      {result && result.items.length === 0 && <div className="panel"><EmptyState>Không có xe phù hợp.</EmptyState></div>}
      {!!result?.items.length && (
        <>
          <div className="table-wrap">
            <table className="table assignment-table assignment-table--vehicle">
              <thead>
                <tr>
                  <th>Nguồn</th>
                  <th>Biển số / định danh</th>
                  <th>Số khung</th>
                  <th>Số máy</th>
                  <th>Hãng / loại xe</th>
                  <th>Hạng đào tạo</th>
                  <th>Đang sử dụng</th>
                  <th>Trạng thái</th>
                  <th>Quyền sở hữu</th>
                </tr>
              </thead>
              <tbody>
                {result.items.map((item) => (
                  <tr key={`${item.sourceProfileCode}:${item.xeTapId}:${item.bienSoXe}`}>
                    <td><span className="assignment-source">{item.sourceProfileCode}</span></td>
                    <td>
                      <strong>{item.bienSoXe}</strong>
                      <small className="assignment-cell-note">
                        Định danh: {item.sourceProfileCode}/{item.bienSoXe}
                      </small>
                    </td>
                    <td>{item.soKhung || '—'}</td>
                    <td>{item.soMay || '—'}</td>
                    <td>
                      {item.hangXe || item.loaiXe || '—'}
                      {item.hangXe && item.loaiXe && (
                        <small className="assignment-cell-note">{item.loaiXe}</small>
                      )}
                    </td>
                    <td>{item.hangDaoTao || '—'}</td>
                    <td>
                      <span className="assignment-usage">
                        <span><strong>{item.courseUsageCount}</strong> khóa</span>
                        <span><strong>{item.groupUsageCount}</strong> nhóm</span>
                        <span><strong>{item.studentUsageCount}</strong> học viên</span>
                      </span>
                    </td>
                    <td>
                      <StatusBadge
                        active={item.isActive}
                        manualReview={item.isManualReview}
                        label={item.trangThai}
                      />
                    </td>
                    <td><span className="assignment-readonly-chip">Chỉ đọc nguồn</span></td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          <Pager result={result} onPage={setPage} disabled={loading} />
        </>
      )}
    </div>
  );
}
