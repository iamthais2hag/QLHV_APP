import {
  useCallback,
  useEffect,
  useState,
} from 'react';
import { getCourseAssignmentHistory } from '../api';
import type {
  CourseAuditItem,
  PagedResult,
} from '../types';
import {
  EmptyState,
  formatDateTime,
  PageMessage,
  Pager,
} from '../ui';

const PAGE_SIZE = 30;

export default function CourseHistoryPanel({ courseId }: { courseId: number }) {
  const [page, setPage] = useState(1);
  const [result, setResult] = useState<PagedResult<CourseAuditItem> | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async (signal?: AbortSignal) => {
    setLoading(true);
    setError(null);
    try {
      setResult(await getCourseAssignmentHistory(courseId, page, PAGE_SIZE, signal));
    } catch (failure) {
      if (!(failure instanceof DOMException && failure.name === 'AbortError')) {
        setError(failure instanceof Error ? failure.message : 'Không thể tải lịch sử khóa học.');
      }
    } finally {
      if (!signal?.aborted) setLoading(false);
    }
  }, [courseId, page]);

  useEffect(() => {
    const controller = new AbortController();
    void load(controller.signal);
    return () => controller.abort();
  }, [load]);

  return (
    <section className="assignment-stack">
      <section className="panel assignment-section-heading">
        <div>
          <span className="assignment-eyebrow">6 · Lịch sử</span>
          <h3>Audit phân công và mặc định nhóm</h3>
          <p>Full-snapshot history; NO_CHANGE không tạo bản ghi mới.</p>
        </div>
        <button type="button" className="btn btn--ghost" onClick={() => void load()} disabled={loading}>
          {loading ? 'Đang tải...' : 'Tải lại'}
        </button>
      </section>
      {error && <PageMessage kind="error">{error}</PageMessage>}
      {loading && !result && <div className="panel"><EmptyState>Đang tải lịch sử...</EmptyState></div>}
      {result?.items.length === 0 && <div className="panel"><EmptyState>Khóa học chưa có sự kiện phân công.</EmptyState></div>}
      {!!result?.items.length && (
        <>
          <div className="table-wrap">
            <table className="table assignment-table">
              <thead>
                <tr>
                  <th>Thời gian</th>
                  <th>Đối tượng</th>
                  <th>Hành động</th>
                  <th>Người thực hiện</th>
                  <th>Lý do</th>
                </tr>
              </thead>
              <tbody>
                {result.items.map((item, index) => (
                  <tr key={`${item.occurredAtUtc}:${item.action}:${index}`}>
                    <td>{formatDateTime(item.occurredAtUtc)}</td>
                    <td>{item.entityLabel || '—'}</td>
                    <td><span className="assignment-source">{item.action}</span></td>
                    <td>{item.actor || 'Hệ thống'}</td>
                    <td>{item.reason || '—'}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          <Pager result={result} onPage={setPage} disabled={loading} />
        </>
      )}
    </section>
  );
}
