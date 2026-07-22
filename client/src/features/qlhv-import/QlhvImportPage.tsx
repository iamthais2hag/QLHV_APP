import { useMemo, useRef, useState } from 'react';
import {
  executeQlhvImport,
  getQlhvImportDiagnostics,
  getQlhvImportPlan,
} from './api';
import {
  buildExecuteRequest,
  canOpenExecute,
  createImportRequest,
  createRequestKey,
  getExecuteDisabledReason,
  isSourceKind,
  QLHV_IMPORT_CONFIRM_TEXT,
  QLHV_IMPORT_SOURCES,
} from './logic';
import type {
  QlhvImportDiagnostics,
  QlhvImportExecuteResult,
  QlhvImportFormState,
  QlhvImportPlan,
  QlhvImportRequest,
  QlhvImportSnapshot,
  QlhvImportSourceKind,
} from './types';

const NUMBER_FORMAT = new Intl.NumberFormat('vi-VN');

type QlhvImportLastResult = QlhvImportSnapshot<QlhvImportExecuteResult> & {
  outcomeKind: 'executed' | 'blocked';
};

export default function QlhvImportPage() {
  const [form, setForm] = useState<QlhvImportFormState>({
    sourceKind: 'OTO',
    maKhoaHocInput: '',
  });
  const [diagnostics, setDiagnostics] = useState<QlhvImportSnapshot<QlhvImportDiagnostics> | null>(null);
  const [plan, setPlan] = useState<QlhvImportSnapshot<QlhvImportPlan> | null>(null);
  const [lastResult, setLastResult] = useState<QlhvImportLastResult | null>(null);
  const [diagnosticsLoading, setDiagnosticsLoading] = useState(false);
  const [planLoading, setPlanLoading] = useState(false);
  const [executing, setExecuting] = useState(false);
  const [diagnosticsError, setDiagnosticsError] = useState<string | null>(null);
  const [planError, setPlanError] = useState<string | null>(null);
  const [executeError, setExecuteError] = useState<string | null>(null);
  const [confirmOpen, setConfirmOpen] = useState(false);
  const [confirmText, setConfirmText] = useState('');
  const requestGenerationRef = useRef(0);
  const diagnosticsRunRef = useRef(0);
  const planRunRef = useRef(0);

  const request = useMemo(() => createImportRequest(form), [form]);
  const requestKey = createRequestKey(request);
  const currentRequestKeyRef = useRef(requestKey);
  currentRequestKeyRef.current = requestKey;

  const source = QLHV_IMPORT_SOURCES[form.sourceKind];
  const busyForExecute = planLoading || executing;
  const canExecute = canOpenExecute(plan, request, busyForExecute);
  const executeDisabledReason = getExecuteDisabledReason(plan, request, busyForExecute);
  const executeRequest = buildExecuteRequest(plan, request, confirmText, busyForExecute);

  function invalidateRequestData() {
    requestGenerationRef.current += 1;
    diagnosticsRunRef.current += 1;
    planRunRef.current += 1;
    setDiagnostics(null);
    setPlan(null);
    setDiagnosticsLoading(false);
    setPlanLoading(false);
    setDiagnosticsError(null);
    setPlanError(null);
    setExecuteError(null);
    setConfirmOpen(false);
    setConfirmText('');
  }

  function handleSourceChange(value: string) {
    if (!isSourceKind(value)) {
      return;
    }
    invalidateRequestData();
    setForm((current) => ({ ...current, sourceKind: value }));
  }

  function handleCourseChange(value: string) {
    invalidateRequestData();
    setForm((current) => ({ ...current, maKhoaHocInput: value }));
  }

  async function loadDiagnostics(snapshot: QlhvImportRequest = request) {
    const generation = requestGenerationRef.current;
    const run = ++diagnosticsRunRef.current;
    const snapshotKey = createRequestKey(snapshot);
    setDiagnosticsLoading(true);
    setDiagnosticsError(null);

    try {
      const data = await getQlhvImportDiagnostics(snapshot);
      if (isLatestRequest(generation, run, diagnosticsRunRef, snapshotKey)) {
        setDiagnostics({ request: snapshot, data });
      }
    } catch (error) {
      if (isLatestRequest(generation, run, diagnosticsRunRef, snapshotKey)) {
        setDiagnostics(null);
        setDiagnosticsError(toSafeClientMessage(error, 'Không thể chạy chẩn đoán.'));
      }
    } finally {
      if (isLatestRequest(generation, run, diagnosticsRunRef, snapshotKey)) {
        setDiagnosticsLoading(false);
      }
    }
  }

  async function loadPlan(snapshot: QlhvImportRequest = request) {
    const generation = requestGenerationRef.current;
    const run = ++planRunRef.current;
    const snapshotKey = createRequestKey(snapshot);
    setPlanLoading(true);
    setPlanError(null);
    setConfirmOpen(false);
    setConfirmText('');

    try {
      const data = await getQlhvImportPlan(snapshot);
      if (isLatestRequest(generation, run, planRunRef, snapshotKey)) {
        setPlan({ request: snapshot, data });
      }
    } catch (error) {
      if (isLatestRequest(generation, run, planRunRef, snapshotKey)) {
        setPlan(null);
        setPlanError(toSafeClientMessage(error, 'Không thể lập kế hoạch.'));
      }
    } finally {
      if (isLatestRequest(generation, run, planRunRef, snapshotKey)) {
        setPlanLoading(false);
      }
    }
  }

  function isLatestRequest(
    generation: number,
    run: number,
    runRef: { current: number },
    snapshotKey: string,
  ) {
    return generation === requestGenerationRef.current
      && run === runRef.current
      && snapshotKey === currentRequestKeyRef.current;
  }

  function openConfirmation() {
    if (!canOpenExecute(plan, request, busyForExecute)) {
      return;
    }
    setExecuteError(null);
    setConfirmText('');
    setConfirmOpen(true);
  }

  function closeConfirmation() {
    if (executing) {
      return;
    }
    setConfirmOpen(false);
    setConfirmText('');
  }

  async function handleExecute() {
    const body = buildExecuteRequest(plan, request, confirmText, busyForExecute);
    if (!body || !plan) {
      setExecuteError('Kế hoạch hoặc chuỗi xác nhận không còn hợp lệ.');
      return;
    }

    const snapshot = plan.request;
    const snapshotKey = createRequestKey(snapshot);
    const generation = requestGenerationRef.current;
    setExecuting(true);
    setExecuteError(null);

    try {
      const outcome = await executeQlhvImport(body);
      if (generation !== requestGenerationRef.current || snapshotKey !== currentRequestKeyRef.current) {
        return;
      }

      setLastResult({ request: snapshot, data: outcome.result, outcomeKind: outcome.kind });
      setConfirmOpen(false);
      setConfirmText('');

      if (outcome.kind === 'blocked' || !outcome.result.executed) {
        setPlan({ request: snapshot, data: outcome.result.plan });
        setExecuteError(outcome.result.message || 'Yêu cầu nhập dữ liệu đã bị backend chặn.');
        return;
      }

      setPlan(null);
      await Promise.all([loadDiagnostics(snapshot), loadPlan(snapshot)]);
    } catch (error) {
      if (generation === requestGenerationRef.current && snapshotKey === currentRequestKeyRef.current) {
        setExecuteError(toSafeClientMessage(error, 'Không thể thực hiện nhập dữ liệu.'));
      }
    } finally {
      if (generation === requestGenerationRef.current && snapshotKey === currentRequestKeyRef.current) {
        setExecuting(false);
      }
    }
  }

  return (
    <div className="qlhv-import-page">
      <section className="panel qlhv-import-hero">
        <div>
          <span className="qlhv-import-eyebrow">QLHV_APP</span>
          <h2>Đồng bộ toàn bộ dữ liệu CSĐT</h2>
          <p>Chẩn đoán snapshot BAK và lập kế hoạch chỉ đọc trước khi ghi vào QLHV_APP.</p>
        </div>
        <span className="qlhv-import-badge">Full snapshot có bảo vệ</span>
      </section>

      <section className="panel qlhv-import-source-section">
        <SectionHeading
          title="1. Chọn nguồn dữ liệu"
          hint="Chẩn đoán và kế hoạch chỉ chạy khi bấm nút"
        />
        <div className="qlhv-import-source-grid">
          <label className="field">
            <span className="field__label">Loại dữ liệu</span>
            <select
              className="field__input"
              value={form.sourceKind}
              onChange={(event) => handleSourceChange(event.target.value)}
              disabled={executing}
            >
              {(Object.keys(QLHV_IMPORT_SOURCES) as QlhvImportSourceKind[]).map((kind) => (
                <option key={kind} value={kind}>
                  {QLHV_IMPORT_SOURCES[kind].label} — {QLHV_IMPORT_SOURCES[kind].sourceProfileCode}
                  {' '}— mã CSĐT {QLHV_IMPORT_SOURCES[kind].maCSDT}
                </option>
              ))}
            </select>
          </label>

          <label className="field">
            <span className="field__label">Phân vùng đích (SourceProfileCode)</span>
            <input className="field__input" value={source.sourceProfileCode} readOnly />
          </label>

          <label className="field">
            <span className="field__label">Database snapshot nguồn</span>
            <input className="field__input" value={source.sourceDatabaseName} readOnly />
          </label>

          <label className="field">
            <span className="field__label">Mã CSĐT</span>
            <input className="field__input" value={source.maCSDT} readOnly />
          </label>

          <label className="field">
            <span className="field__label">Mã khóa học (không bắt buộc)</span>
            <input
              className="field__input"
              value={form.maKhoaHocInput}
              onChange={(event) => handleCourseChange(event.target.value)}
              placeholder="Để trống để xem toàn bộ trung tâm"
              disabled={executing}
            />
          </label>
        </div>

        <div className="qlhv-import-source-summary">
          <strong>Luồng cố định:</strong>{' '}
          {form.sourceKind} → {source.sourceDatabaseName} → QLHV_APP / phân vùng {source.sourceProfileCode}
          {' '}/ CSĐT {source.maCSDT}
          {request.maKhoaHoc ? ` / khóa ${request.maKhoaHoc}` : ' / tất cả khóa'}
        </div>

        <div className="qlhv-import-full-sync-warning" role="alert">
          <strong>Đây là đồng bộ toàn bộ snapshot.</strong>{' '}
          Học viên không còn trong {source.sourceDatabaseName} sẽ bị xóa mềm,
          chỉ trong phân vùng {source.sourceProfileCode}. Snapshot rỗng sẽ bị chặn.
        </div>

        <div className="qlhv-import-actions">
          <button
            type="button"
            className="btn btn--ghost"
            onClick={() => void loadDiagnostics()}
            disabled={diagnosticsLoading || executing}
          >
            {diagnosticsLoading ? 'Đang kiểm tra...' : 'Kiểm tra dữ liệu'}
          </button>
          <button
            type="button"
            className="btn btn--primary"
            onClick={() => void loadPlan()}
            disabled={planLoading || executing}
          >
            {planLoading ? 'Đang lập kế hoạch...' : 'Lập kế hoạch full sync'}
          </button>
        </div>
      </section>

      <section className="panel">
        <SectionHeading title="2. Chẩn đoán an toàn" hint="GET chỉ đọc" />
        {diagnosticsError && <ErrorBanner message={diagnosticsError} />}
        {diagnosticsLoading && <EmptyState text="Đang đọc dữ liệu chẩn đoán..." />}
        {!diagnosticsLoading && !diagnostics && !diagnosticsError && (
          <EmptyState text="Chưa có chẩn đoán. Bấm “Kiểm tra dữ liệu” để kiểm tra nguồn và đích." />
        )}
        {diagnostics && (
          <DiagnosticsView diagnostics={diagnostics.data} />
        )}
      </section>

      <section className="panel">
        <SectionHeading title="3. Kế hoạch full sync" hint="GET chỉ đọc, không tự chạy" />
        {planError && <ErrorBanner message={planError} />}
        {planLoading && <EmptyState text="Đang lập kế hoạch nhập dữ liệu..." />}
        {!planLoading && !plan && !planError && (
          <EmptyState text="Chưa có kế hoạch cho biểu mẫu hiện tại." />
        )}
        {plan && <PlanView plan={plan.data} />}

        <div className="qlhv-import-execute-row">
          <div>
            <strong>Thực hiện có xác nhận</strong>
            <p>{executeDisabledReason ?? 'Kế hoạch hợp lệ. Có thể mở hộp xác nhận.'}</p>
          </div>
          <button
            type="button"
            className="btn btn--primary"
            onClick={openConfirmation}
            disabled={!canExecute}
          >
            Thực hiện full sync
          </button>
        </div>
        {executeError && <ErrorBanner message={executeError} />}
      </section>

      <section className="panel">
        <SectionHeading title="4. Kết quả gần nhất" />
        {!lastResult && <EmptyState text="Chưa có kết quả thực hiện trong phiên làm việc này." />}
        {lastResult && <ResultView snapshot={lastResult} />}
      </section>

      {confirmOpen && plan && (
        <div className="qlhv-import-modal" role="presentation">
          <form
            className="qlhv-import-modal__dialog"
            role="dialog"
            aria-modal="true"
            aria-labelledby="qlhv-import-confirm-title"
            onSubmit={(event) => {
              event.preventDefault();
              void handleExecute();
            }}
          >
            <SectionHeading title="Xác nhận full sync" />
            <h3 id="qlhv-import-confirm-title" className="qlhv-import-modal__title">
              Kiểm tra snapshot và phân vùng lần cuối trước khi ghi
            </h3>
            <div className="qlhv-import-confirm-summary">
              <SummaryRow label="Database snapshot nguồn" value={plan.data.sourceDatabaseName} />
              <SummaryRow label="Phân vùng đích" value={plan.request.sourceProfileCode} />
              <SummaryRow label="Mã CSĐT" value={plan.request.maCSDT} />
              <SummaryRow label="Mã khóa học" value={plan.request.maKhoaHoc ?? 'Tất cả khóa'} />
              <SummaryRow label="Dự kiến thêm" value={formatNumber(plan.data.plannedInsertHocVienRows)} />
              <SummaryRow label="Dự kiến cập nhật" value={formatNumber(plan.data.plannedUpdateHocVienRows)} />
              <SummaryRow label="Dự kiến khôi phục" value={formatNumber(plan.data.plannedReactivateHocVienRows)} />
              <SummaryRow label="Dự kiến xóa mềm" value={formatNumber(plan.data.plannedSoftDeleteHocVienRows)} />
            </div>
            <div className="qlhv-import-full-sync-warning" role="alert">
              Full sync sẽ xóa mềm các dòng không còn trong snapshot,
              chỉ trong phân vùng <strong>{plan.request.sourceProfileCode}</strong>.
            </div>
            <p className="qlhv-import-confirm-instruction">
              Nhập chính xác <code>{QLHV_IMPORT_CONFIRM_TEXT}</code> để mở khóa nút xác nhận cuối.
            </p>
            <label className="field">
              <span className="field__label">Chuỗi xác nhận</span>
              <input
                className="field__input"
                value={confirmText}
                onChange={(event) => setConfirmText(event.target.value)}
                autoComplete="off"
                spellCheck={false}
                autoFocus
                disabled={executing}
              />
            </label>
            <div className="qlhv-import-modal__actions">
              <button
                type="button"
                className="btn btn--ghost"
                onClick={closeConfirmation}
                disabled={executing}
              >
                Hủy
              </button>
              <button
                type="submit"
                className="btn btn--primary"
                disabled={!executeRequest}
              >
                {executing ? 'Đang thực hiện...' : 'Xác nhận và full sync'}
              </button>
            </div>
          </form>
        </div>
      )}
    </div>
  );
}

function DiagnosticsView({ diagnostics }: { diagnostics: QlhvImportDiagnostics }) {
  const metrics: MetricItem[] = [
    ['Học viên nguồn', diagnostics.sourceHocVienRows, diagnostics.sourceHocVienRows > 0 ? 'ok' : 'blocked'],
    ['MaDK nguồn phân biệt', diagnostics.sourceDistinctMaDkRows],
    ['MaDK nguồn trùng', diagnostics.duplicateSourceMaDkRows, diagnostics.duplicateSourceMaDkRows > 0 ? 'blocked' : 'ok'],
    ['Học viên hiện có trong app', diagnostics.currentAppHocVienRows],
    ['Dòng theo profile nguồn', diagnostics.targetRowsForSourceProfile],
    ['Khớp đúng định danh', diagnostics.targetExactIdentityMatches],
    ['MaDK cũng có ở profile khác', diagnostics.targetMaDkConflictsOtherProfiles, diagnostics.targetMaDkConflictsOtherProfiles > 0 ? 'warning' : 'ok'],
    ['Dòng đang xóa mềm sẽ khôi phục', diagnostics.softDeletedIdentityConflicts, diagnostics.softDeletedIdentityConflicts > 0 ? 'warning' : 'ok'],
    ['Có constraint profile nguồn', diagnostics.sourceProfileConstraintExists ? 'Có' : 'Không', diagnostics.sourceProfileConstraintExists ? 'ok' : 'blocked'],
    ['Profile được constraint cho phép', diagnostics.sourceProfileAllowedByConstraint ? 'Có' : 'Không', diagnostics.sourceProfileAllowedByConstraint ? 'ok' : 'blocked'],
    ['Dự kiến thêm', diagnostics.plannedInsertHocVienRows],
    ['Dự kiến cập nhật', diagnostics.plannedUpdateHocVienRows],
    ['Dự kiến khôi phục', diagnostics.plannedReactivateHocVienRows],
    ['Dự kiến xóa mềm', diagnostics.plannedSoftDeleteHocVienRows, diagnostics.plannedSoftDeleteHocVienRows > 0 ? 'warning' : 'ok'],
    ['Dự kiến bỏ qua', diagnostics.plannedSkipHocVienRows],
    ['Có thể thực hiện', diagnostics.executable ? 'Có' : 'Không', diagnostics.executable ? 'ok' : 'blocked'],
  ];

  return (
    <>
      <ReadOnlySummary
        isReadOnly={diagnostics.isReadOnly}
        sourceDatabaseName={diagnostics.sourceDatabaseName}
        profile={diagnostics.sourceProfileCode}
        maCSDT={diagnostics.maCSDT}
        maKhoaHoc={diagnostics.maKhoaHoc}
      />
      <MetricGrid items={metrics} />
      <IssueList title="Điểm chặn chẩn đoán" items={diagnostics.blockers} variant="blocker" />
      <IssueList title="Cảnh báo chẩn đoán" items={diagnostics.warnings} variant="warning" />
    </>
  );
}

function PlanView({ plan }: { plan: QlhvImportPlan }) {
  const metrics: MetricItem[] = [
    ['Học viên nguồn', plan.sourceHocVienRows, plan.sourceHocVienRows > 0 ? 'ok' : 'blocked'],
    ['MaDK nguồn phân biệt', plan.sourceDistinctMaDkRows],
    ['MaDK nguồn trùng', plan.duplicateSourceMaDkRows, plan.duplicateSourceMaDkRows > 0 ? 'blocked' : 'ok'],
    ['Khóa học nguồn', plan.sourceKhoaHocRows],
    ['Học viên hiện có trong app', plan.currentAppHocVienRows],
    ['Khóa học hiện có trong app', plan.currentAppKhoaHocRows],
    ['Dòng theo phân vùng', plan.targetRowsForSourceProfile],
    ['Khớp đúng định danh', plan.targetExactIdentityMatches],
    ['Dự kiến thêm học viên', plan.plannedInsertHocVienRows],
    ['Dự kiến cập nhật học viên', plan.plannedUpdateHocVienRows],
    ['Dự kiến khôi phục học viên', plan.plannedReactivateHocVienRows],
    ['Dự kiến xóa mềm học viên', plan.plannedSoftDeleteHocVienRows, plan.plannedSoftDeleteHocVienRows > 0 ? 'warning' : 'ok'],
    ['Dự kiến bỏ qua học viên', plan.plannedSkipHocVienRows],
    ['Tổng upsert học viên', plan.plannedUpsertHocVienRows],
    ['Dự kiến upsert khóa học', plan.plannedUpsertKhoaHocRows],
    ['Có thể thực hiện', plan.executable ? 'Có' : 'Không', plan.executable ? 'ok' : 'blocked'],
  ];

  return (
    <>
      <ReadOnlySummary
        isReadOnly={plan.isReadOnly}
        sourceDatabaseName={plan.sourceDatabaseName}
        profile={plan.sourceProfileCode}
        maCSDT={plan.maCSDT}
        maKhoaHoc={plan.maKhoaHoc}
      />
      <MetricGrid items={metrics} />
      <IssueList title="Điểm chặn kế hoạch" items={plan.blockers} variant="blocker" />
      <IssueList title="Cảnh báo kế hoạch" items={plan.warnings} variant="warning" />
    </>
  );
}

function ResultView({ snapshot }: { snapshot: QlhvImportLastResult }) {
  const result = snapshot.data;
  const successful = snapshot.outcomeKind === 'executed' && result.executed;
  return (
    <div className={`qlhv-import-result qlhv-import-result--${successful ? 'success' : 'blocked'}`}>
      <div className="qlhv-import-result__heading">
        <strong>{successful ? 'Full sync thành công' : 'Yêu cầu không được thực hiện'}</strong>
        <span>{result.status || (successful ? 'Executed' : 'Blocked')}</span>
      </div>
      <p>{result.message}</p>
      <div className="qlhv-import-result-grid">
        <SummaryRow label="Database snapshot nguồn" value={result.plan.sourceDatabaseName} />
        <SummaryRow label="Phân vùng đích" value={snapshot.request.sourceProfileCode} />
        <SummaryRow label="Mã CSĐT" value={snapshot.request.maCSDT} />
        <SummaryRow label="Mã khóa học" value={snapshot.request.maKhoaHoc ?? 'Tất cả khóa'} />
        <SummaryRow label="Đã thêm học viên" value={formatNumber(result.insertedHocVienRows)} />
        <SummaryRow label="Đã cập nhật học viên" value={formatNumber(result.updatedHocVienRows)} />
        <SummaryRow label="Đã khôi phục học viên" value={formatNumber(result.reactivatedHocVienRows)} />
        <SummaryRow label="Đã xóa mềm học viên" value={formatNumber(result.softDeletedHocVienRows)} />
        <SummaryRow label="Đã bỏ qua học viên" value={formatNumber(result.skippedHocVienRows)} />
      </div>
      {!successful && (
        <IssueList title="Điểm chặn từ backend" items={result.plan.blockers} variant="blocker" />
      )}
    </div>
  );
}

function ReadOnlySummary({
  isReadOnly,
  sourceDatabaseName,
  profile,
  maCSDT,
  maKhoaHoc,
}: {
  isReadOnly: boolean;
  sourceDatabaseName: string;
  profile: string;
  maCSDT: string;
  maKhoaHoc: string | null;
}) {
  return (
    <div className="qlhv-import-readonly-summary">
      <span className={isReadOnly ? 'is-ok' : 'is-blocked'}>
        Chế độ chỉ đọc: {isReadOnly ? 'Có' : 'Không'}
      </span>
      <span>DB nguồn: {sourceDatabaseName}</span>
      <span>{profile}</span>
      <span>CSĐT {maCSDT}</span>
      <span>{maKhoaHoc ? `Khóa ${maKhoaHoc}` : 'Tất cả khóa'}</span>
    </div>
  );
}

type MetricTone = 'default' | 'ok' | 'warning' | 'blocked';
type MetricItem = [label: string, value: string | number, tone?: MetricTone];

function MetricGrid({ items }: { items: MetricItem[] }) {
  return (
    <div className="qlhv-import-metrics">
      {items.map(([label, value, tone = 'default']) => (
        <div key={label} className={`qlhv-import-metric is-${tone}`}>
          <span>{label}</span>
          <strong>{typeof value === 'number' ? formatNumber(value) : value}</strong>
        </div>
      ))}
    </div>
  );
}

function IssueList({
  title,
  items,
  variant,
}: {
  title: string;
  items: string[];
  variant: 'blocker' | 'warning';
}) {
  if (items.length === 0) {
    return null;
  }

  return (
    <div
      className={`qlhv-import-issues qlhv-import-issues--${variant}`}
      role={variant === 'blocker' ? 'alert' : 'status'}
    >
      <strong>{title}</strong>
      <ul>
        {items.map((item, index) => <li key={`${index}-${item}`}>{item}</li>)}
      </ul>
    </div>
  );
}

function SectionHeading({ title, hint }: { title: string; hint?: string }) {
  return (
    <div className="qlhv-import-section-heading">
      <strong>{title}</strong>
      {hint && <span>{hint}</span>}
    </div>
  );
}

function EmptyState({ text }: { text: string }) {
  return <div className="qlhv-import-empty">{text}</div>;
}

function ErrorBanner({ message }: { message: string }) {
  return (
    <div className="qlhv-import-error" role="alert">
      {message}
    </div>
  );
}

function SummaryRow({ label, value }: { label: string; value: string }) {
  return (
    <div className="qlhv-import-summary-row">
      <span>{label}</span>
      <strong>{value}</strong>
    </div>
  );
}

function formatNumber(value: number): string {
  return NUMBER_FORMAT.format(value);
}

function toSafeClientMessage(error: unknown, fallback: string): string {
  if (error instanceof Error && error.message && error.name !== 'AbortError') {
    return error.message;
  }
  return fallback;
}
