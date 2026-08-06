import { useCallback, useEffect, useState } from 'react';
import { confirmCourseCompletion, getCourseCompletionStatus, previewCourseCompletion } from './api';
import type { CourseCompletionPreview, CourseCompletionStatus } from './types';

interface Props {
  courseId: number;
  sourceProfileCode: string;
  canPreview: boolean;
  canComplete: boolean;
}

export default function CourseCompletionPanel({ courseId, sourceProfileCode, canPreview, canComplete }: Props) {
  const [status, setStatus] = useState<CourseCompletionStatus | null>(null);
  const [preview, setPreview] = useState<CourseCompletionPreview | null>(null);
  const [businessDate, setBusinessDate] = useState('');
  const [reason, setReason] = useState('');
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const previewProfile = sourceProfileCode.trim() || status?.sourceProfileCode?.trim() || '';

  const loadStatus = useCallback(async (signal?: AbortSignal) => {
    setBusy(true);
    setError(null);
    try { setStatus(await getCourseCompletionStatus(courseId, signal)); }
    catch (cause) {
      if (!(cause instanceof DOMException && cause.name === 'AbortError'))
        setError(cause instanceof Error ? cause.message : 'Không thể tải trạng thái hoàn thành.');
    } finally { if (!signal?.aborted) setBusy(false); }
  }, [courseId]);

  useEffect(() => {
    const controller = new AbortController();
    void loadStatus(controller.signal);
    return () => controller.abort();
  }, [loadStatus]);

  async function runPreview() {
    setBusy(true); setError(null); setMessage(null);
    try {
      if (!previewProfile) throw new Error('Không xác định được profile nguồn của khóa học.');
      setPreview(await previewCourseCompletion(courseId, previewProfile));
    }
    catch (cause) { setError(cause instanceof Error ? cause.message : 'Không thể kiểm tra điều kiện.'); }
    finally { setBusy(false); }
  }

  async function confirm() {
    if (!preview?.canConfirm || !businessDate || !reason.trim()) return;
    setBusy(true); setError(null); setMessage(null);
    try {
      const result = await confirmCourseCompletion(courseId, preview.previewToken, businessDate, reason.trim());
      setMessage(result.resultCode === 'NO_CHANGE'
        ? 'Mốc hoàn thành đã tồn tại và snapshot không thay đổi.'
        : 'Đã ghi nhận mốc hoàn thành khóa học trong QLHV_APP.');
      setPreview(null); setReason('');
      await loadStatus();
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : 'Không thể xác nhận hoàn thành.');
      setPreview(null);
      await loadStatus();
    } finally { setBusy(false); }
  }

  return (
    <section className="panel assignment-stack" aria-label="Hoàn thành khóa học">
      <header className="assignment-section-heading">
        <div>
          <span className="assignment-eyebrow">7 · Hoàn thành khóa học</span>
          <h3>Mốc chốt kết quả đào tạo</h3>
          <p>Thao tác này chỉ ghi nhận mốc hoàn thành trong QLHV_APP. Không sửa dữ liệu CSDT, Báo cáo II, sát hạch hoặc GPLX.</p>
        </div>
        <strong>{statusLabel(status?.status)}</strong>
      </header>

      {error && <div className="page-message page-message--error">{error}</div>}
      {message && <div className="page-message page-message--success">{message}</div>}
      {status?.status === 'COMPLETED' && <CompletedFacts status={status} />}
      {status?.status === 'CORRECTION_REQUIRED' && (
        <div className="page-message page-message--warning">
          <strong>Cần quy trình correction riêng.</strong>
          <p>Nguồn đã thay đổi sau khi chốt. V1 không tự sửa marker và không hỗ trợ mở lại khóa.</p>
          {status.drift && <p>Thêm {status.drift.addedLearners}; thiếu {status.drift.missingLearners}; thay đổi {status.drift.changedLearners}; đổi trạng thái/kết quả {status.drift.statusOrResultChanges}.</p>}
        </div>
      )}
      {status?.status === 'NOT_COMPLETED' && !preview && (
        canPreview
          ? <button type="button" className="btn btn--primary" disabled={busy || !previewProfile} onClick={() => void runPreview()}>{busy ? 'Đang kiểm tra...' : 'Kiểm tra điều kiện'}</button>
          : <p>Bạn có quyền xem trạng thái nhưng không có quyền preview hoặc xác nhận.</p>
      )}
      {preview && (
        <div className="assignment-stack">
          <dl className="assignment-facts">
            <Fact label="Profile" value={preview.sourceProfileCode} />
            <Fact label="Mã khóa" value={preview.sourceCourseKey} />
            <Fact label="Tổng học viên" value={String(preview.learnerCount)} />
            <Fact label="Đạt (09)" value={String(preview.passedCount)} />
            <Fact label="Không đạt (10)" value={String(preview.failedCount)} />
            <Fact label="Downstream (11–19)" value={String(preview.downstreamCount)} />
          </dl>
          <ReasonList title="Điều kiện chặn" values={preview.blockers} empty="Không có điều kiện chặn." />
          <ReasonList title="Cảnh báo" values={preview.warnings} empty="Không có cảnh báo." />
          <label className="field"><span className="field__label">Ngày hoàn thành nghiệp vụ</span>
            <input className="field__input" type="date" value={businessDate} onChange={(event) => setBusinessDate(event.target.value)} />
          </label>
          <label className="field"><span className="field__label">Lý do xác nhận</span>
            <textarea className="field__input" value={reason} maxLength={500} onChange={(event) => setReason(event.target.value)} />
          </label>
          <div className="assignment-row-actions">
            <button type="button" className="btn btn--ghost" disabled={busy} onClick={() => setPreview(null)}>Hủy preview</button>
            {canComplete && <button type="button" className="btn btn--primary" disabled={busy || !preview.canConfirm || !businessDate || !reason.trim()} onClick={() => void confirm()}>Xác nhận hoàn thành</button>}
          </div>
        </div>
      )}
    </section>
  );
}

function CompletedFacts({ status }: { status: CourseCompletionStatus }) {
  return <dl className="assignment-facts">
    <Fact label="Ngày nghiệp vụ" value={status.completionBusinessDate ?? '—'} />
    <Fact label="Người xác nhận" value={status.completedBy ?? '—'} />
    <Fact label="Thời điểm SQL UTC" value={status.completedAtUtc ?? '—'} />
    <Fact label="Số học viên" value={String(status.learnerCount ?? 0)} />
    <Fact label="Contract" value={status.contractVersion ?? '—'} />
    <Fact label="Snapshot" value="Khớp nguồn hiện tại" />
  </dl>;
}

function ReasonList({ title, values, empty }: { title: string; values: string[]; empty: string }) {
  return <div><strong>{title}</strong>{values.length === 0 ? <p>{empty}</p> : <ul>{values.map((value) => <li key={value}>{value}</li>)}</ul>}</div>;
}

function Fact({ label, value }: { label: string; value: string }) { return <div><dt>{label}</dt><dd>{value}</dd></div>; }
function statusLabel(status: CourseCompletionStatus['status'] | undefined): string {
  if (status === 'COMPLETED') return 'ĐÃ HOÀN THÀNH';
  if (status === 'CORRECTION_REQUIRED') return 'CẦN CORRECTION';
  return 'CHƯA HOÀN THÀNH';
}
