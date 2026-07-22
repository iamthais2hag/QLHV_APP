import { useEffect, useMemo, useState } from 'react';
import {
  executeQlhvImport,
  getQlhvImportDiagnostics,
  getQlhvImportPlan,
  getQlhvOperationsHistory,
  getQlhvOperationsStatus,
  refreshQlhvBackup,
} from './api';
import {
  buildExecuteRequest,
  buildRefreshRequest,
  canOpenExecute,
  createImportRequest,
  getExecuteDisabledReason,
  getRefreshDisabledReason,
  isOperationBusy,
  isPlanSnapshotCurrent,
  QLHV_IMPORT_CONFIRM_TEXT,
  QLHV_IMPORT_SOURCE_KINDS,
  QLHV_IMPORT_SOURCES,
  QLHV_REFRESH_CONFIRM_TEXT,
  statusMatchesSource,
} from './logic';
import type {
  QlhvImportDiagnostics,
  QlhvImportExecuteResult,
  QlhvImportPlan,
  QlhvImportSnapshot,
  QlhvImportSourceKind,
  QlhvOperationHistoryItem,
  QlhvOperationsStatus,
} from './types';

const NUMBER_FORMAT = new Intl.NumberFormat('vi-VN');
const DATE_FORMAT = new Intl.DateTimeFormat('vi-VN', {
  dateStyle: 'short',
  timeStyle: 'medium',
});
const POLL_INTERVAL_MS = 2_500;

type QlhvImportLastResult = QlhvImportSnapshot<QlhvImportExecuteResult> & {
  outcomeKind: 'executed' | 'blocked';
};

interface SourceViewState {
  status: QlhvOperationsStatus | null;
  history: QlhvOperationHistoryItem[];
  diagnostics: QlhvImportSnapshot<QlhvImportDiagnostics> | null;
  plan: QlhvImportSnapshot<QlhvImportPlan> | null;
  lastResult: QlhvImportLastResult | null;
  statusLoading: boolean;
  historyLoading: boolean;
  diagnosticsLoading: boolean;
  planLoading: boolean;
  refreshing: boolean;
  executing: boolean;
  statusError: string | null;
  historyError: string | null;
  diagnosticsError: string | null;
  planError: string | null;
  operationError: string | null;
  operationNotice: string | null;
  pendingOperationId: string | null;
}

interface ConfirmationModalState {
  sourceKind: QlhvImportSourceKind;
  confirmText: string;
  operationsKey: string;
}

function createEmptySourceState(): SourceViewState {
  return {
    status: null,
    history: [],
    diagnostics: null,
    plan: null,
    lastResult: null,
    statusLoading: true,
    historyLoading: true,
    diagnosticsLoading: false,
    planLoading: false,
    refreshing: false,
    executing: false,
    statusError: null,
    historyError: null,
    diagnosticsError: null,
    planError: null,
    operationError: null,
    operationNotice: null,
    pendingOperationId: null,
  };
}

export default function QlhvImportPage() {
  const [activeSource, setActiveSource] = useState<QlhvImportSourceKind>('OTO');
  const [sources, setSources] = useState<Record<QlhvImportSourceKind, SourceViewState>>({
    OTO: createEmptySourceState(),
    MOTO: createEmptySourceState(),
  });
  const [refreshModal, setRefreshModal] = useState<ConfirmationModalState | null>(null);
  const [syncModal, setSyncModal] = useState<ConfirmationModalState | null>(null);

  function patchSource(sourceKind: QlhvImportSourceKind, patch: Partial<SourceViewState>) {
    setSources((current) => ({
      ...current,
      [sourceKind]: { ...current[sourceKind], ...patch },
    }));
  }

  useEffect(() => {
    let cancelled = false;

    for (const sourceKind of QLHV_IMPORT_SOURCE_KINDS) {
      void Promise.allSettled([
        getQlhvOperationsStatus(sourceKind).then((status) => {
          if (!cancelled) {
            patchSource(sourceKind, { status, statusLoading: false, statusError: null });
          }
        }),
        getQlhvOperationsHistory(sourceKind).then((history) => {
          if (!cancelled) {
            patchSource(sourceKind, { history, historyLoading: false, historyError: null });
          }
        }),
      ]).then((results) => {
        if (cancelled) {
          return;
        }
        if (results[0].status === 'rejected') {
          patchSource(sourceKind, {
            statusLoading: false,
            statusError: toSafeClientMessage(results[0].reason, 'Không thể đọc trạng thái.'),
          });
        }
        if (results[1].status === 'rejected') {
          patchSource(sourceKind, {
            historyLoading: false,
            historyError: toSafeClientMessage(results[1].reason, 'Không thể đọc lịch sử.'),
          });
        }
      });
    }

    return () => {
      cancelled = true;
    };
  }, []);

  const pollingSignature = QLHV_IMPORT_SOURCE_KINDS
    .map((kind) => {
      const source = sources[kind];
      return [kind, source.status?.state, source.status?.activeOperationId, source.pendingOperationId].join(':');
    })
    .join('|');
  const shouldPoll = QLHV_IMPORT_SOURCE_KINDS.some((kind) =>
    isOperationBusy(sources[kind].status) || !!sources[kind].pendingOperationId,
  );

  useEffect(() => {
    if (!shouldPoll) {
      return undefined;
    }

    let cancelled = false;
    const poll = async () => {
      for (const sourceKind of QLHV_IMPORT_SOURCE_KINDS) {
        const current = sources[sourceKind];
        if (!isOperationBusy(current.status) && !current.pendingOperationId) {
          continue;
        }
        const [statusResult, historyResult] = await Promise.allSettled([
          getQlhvOperationsStatus(sourceKind),
          getQlhvOperationsHistory(sourceKind),
        ]);
        if (cancelled) {
          return;
        }

        if (statusResult.status === 'fulfilled') {
          const status = statusResult.value;
          patchSource(sourceKind, {
            status,
            statusError: null,
            pendingOperationId: current.pendingOperationId && !isOperationBusy(status)
              ? null
              : current.pendingOperationId,
          });
        } else {
          patchSource(sourceKind, {
            statusError: toSafeClientMessage(
              statusResult.reason,
              'Không thể cập nhật trạng thái đang chạy.',
            ),
          });
        }

        if (historyResult.status === 'fulfilled') {
          patchSource(sourceKind, { history: historyResult.value, historyError: null });
        } else {
          patchSource(sourceKind, {
            historyError: toSafeClientMessage(
              historyResult.reason,
              'Không thể cập nhật lịch sử đang chạy.',
            ),
          });
        }
      }
    };

    const timer = window.setInterval(() => void poll(), POLL_INTERVAL_MS);
    return () => {
      cancelled = true;
      window.clearInterval(timer);
    };
  }, [pollingSignature, shouldPoll]);

  async function reloadStatus(sourceKind: QlhvImportSourceKind, includeHistory = false) {
    patchSource(sourceKind, {
      statusLoading: true,
      statusError: null,
      ...(includeHistory ? { historyLoading: true, historyError: null } : {}),
    });
    const [statusResult, historyResult] = await Promise.allSettled([
      getQlhvOperationsStatus(sourceKind),
      includeHistory ? getQlhvOperationsHistory(sourceKind) : Promise.resolve(null),
    ]);
    if (statusResult.status === 'fulfilled') {
      patchSource(sourceKind, {
        status: statusResult.value,
        statusLoading: false,
        statusError: null,
      });
    } else {
      patchSource(sourceKind, {
        statusLoading: false,
        statusError: toSafeClientMessage(statusResult.reason, 'Không thể tải lại trạng thái.'),
      });
    }

    if (includeHistory && historyResult.status === 'fulfilled' && historyResult.value) {
      patchSource(sourceKind, {
        history: historyResult.value,
        historyLoading: false,
        historyError: null,
      });
    } else if (includeHistory && historyResult.status === 'rejected') {
      patchSource(sourceKind, {
        historyLoading: false,
        historyError: toSafeClientMessage(historyResult.reason, 'Không thể tải lại lịch sử.'),
      });
    }
  }

  async function loadDiagnostics(sourceKind: QlhvImportSourceKind) {
    const request = createImportRequest(sourceKind);
    setActiveSource(sourceKind);
    patchSource(sourceKind, { diagnosticsLoading: true, diagnosticsError: null });
    try {
      const data = await getQlhvImportDiagnostics(request);
      patchSource(sourceKind, {
        diagnostics: { request, data },
        diagnosticsLoading: false,
        diagnosticsError: null,
      });
    } catch (error) {
      patchSource(sourceKind, {
        diagnostics: null,
        diagnosticsLoading: false,
        diagnosticsError: toSafeClientMessage(error, 'Không thể chạy chẩn đoán.'),
      });
    }
  }

  async function loadPlan(sourceKind: QlhvImportSourceKind) {
    const request = createImportRequest(sourceKind);
    setActiveSource(sourceKind);
    patchSource(sourceKind, {
      planLoading: true,
      planError: null,
      operationError: null,
      operationNotice: null,
    });
    try {
      const [data, status] = await Promise.all([
        getQlhvImportPlan(request),
        getQlhvOperationsStatus(sourceKind),
      ]);
      patchSource(sourceKind, {
        plan: { request, data },
        status,
        planLoading: false,
        planError: null,
      });
    } catch (error) {
      patchSource(sourceKind, {
        plan: null,
        planLoading: false,
        planError: toSafeClientMessage(error, 'Không thể lập kế hoạch.'),
      });
    }
  }

  function openRefreshConfirmation(sourceKind: QlhvImportSourceKind) {
    const source = sources[sourceKind];
    if (getRefreshDisabledReason(
      source.status,
      sourceKind,
      source.refreshing || source.executing || !!source.pendingOperationId,
    )) {
      return;
    }
    setActiveSource(sourceKind);
    setSyncModal(null);
    setRefreshModal({ sourceKind, confirmText: '', operationsKey: '' });
    patchSource(sourceKind, { operationError: null, operationNotice: null });
  }

  function openSyncConfirmation(sourceKind: QlhvImportSourceKind) {
    const state = sources[sourceKind];
    const request = createImportRequest(sourceKind);
    if (!canOpenExecute(
      state.plan,
      request,
      state.status,
      state.planLoading || state.executing || !!state.pendingOperationId,
    )) {
      return;
    }
    setActiveSource(sourceKind);
    setRefreshModal(null);
    setSyncModal({ sourceKind, confirmText: '', operationsKey: '' });
    patchSource(sourceKind, { operationError: null, operationNotice: null });
  }

  async function handleRefresh() {
    if (!refreshModal) {
      return;
    }
    const { sourceKind, confirmText, operationsKey } = refreshModal;
    const state = sources[sourceKind];
    const body = buildRefreshRequest(sourceKind, confirmText);
    if (!body
      || !operationsKey
      || getRefreshDisabledReason(state.status, sourceKind, state.refreshing || !!state.pendingOperationId)) {
      patchSource(sourceKind, { operationError: 'Chuỗi xác nhận, operations key hoặc trạng thái nguồn không hợp lệ.' });
      return;
    }

    patchSource(sourceKind, { refreshing: true, operationError: null, operationNotice: null });
    try {
      const result = await refreshQlhvBackup(body, operationsKey);
      setRefreshModal(null);
      patchSource(sourceKind, {
        refreshing: false,
        pendingOperationId: result.operationId,
        plan: null,
        diagnostics: null,
        operationNotice: result.message || 'Đã tiếp nhận yêu cầu làm mới BAK. Trạng thái sẽ tự cập nhật.',
      });
      await reloadStatus(sourceKind, true);
    } catch (error) {
      patchSource(sourceKind, {
        refreshing: false,
        operationError: toSafeClientMessage(error, 'Không thể bắt đầu làm mới BAK.'),
      });
    }
  }

  async function handleExecute() {
    if (!syncModal) {
      return;
    }
    const { sourceKind, confirmText, operationsKey } = syncModal;
    const state = sources[sourceKind];
    const request = createImportRequest(sourceKind);
    const body = buildExecuteRequest(
      state.plan,
      request,
      state.status,
      confirmText,
      state.planLoading || state.executing || !!state.pendingOperationId,
    );
    if (!body || !operationsKey || !state.plan) {
      patchSource(sourceKind, { operationError: 'Kế hoạch, snapshot token, chuỗi xác nhận hoặc operations key không hợp lệ.' });
      return;
    }

    patchSource(sourceKind, { executing: true, operationError: null, operationNotice: null });
    try {
      const outcome = await executeQlhvImport(body, operationsKey);
      setSyncModal(null);
      patchSource(sourceKind, {
        executing: false,
        lastResult: { request, data: outcome.result, outcomeKind: outcome.kind },
      });

      if (outcome.kind === 'blocked' || !outcome.result.executed) {
        patchSource(sourceKind, {
          plan: { request, data: outcome.result.plan },
          operationError: outcome.result.message || 'Yêu cầu đồng bộ đã bị backend chặn.',
        });
      } else {
        patchSource(sourceKind, {
          plan: null,
          diagnostics: null,
          operationNotice: outcome.result.message || 'Full sync hoàn tất.',
        });
        await Promise.all([
          reloadStatus(sourceKind, true),
          loadDiagnostics(sourceKind),
          loadPlan(sourceKind),
        ]);
      }
    } catch (error) {
      patchSource(sourceKind, {
        executing: false,
        operationError: toSafeClientMessage(error, 'Không thể thực hiện đồng bộ.'),
      });
    }
  }

  const active = sources[activeSource];
  const activeRequest = useMemo(() => createImportRequest(activeSource), [activeSource]);
  const activeSourceDefinition = QLHV_IMPORT_SOURCES[activeSource];
  const activeBusy = active.planLoading
    || active.executing
    || active.refreshing
    || !!active.pendingOperationId;
  const activeExecuteReason = getExecuteDisabledReason(
    active.plan,
    activeRequest,
    active.status,
    activeBusy,
  );

  return (
    <div className="qlhv-import-page">
      <section className="panel qlhv-import-hero">
        <div>
          <span className="qlhv-import-eyebrow">QLHV_APP · VẬN HÀNH AN TOÀN</span>
          <h2>Đồng bộ dữ liệu CSĐT</h2>
          <p>Làm mới database BAK, kiểm tra snapshot, lập kế hoạch và full sync theo từng phân vùng độc lập.</p>
        </div>
        <span className="qlhv-import-badge">Live → BAK → QLHV_APP</span>
      </section>

      <section className="qlhv-import-source-cards" aria-label="Nguồn dữ liệu CSĐT">
        {QLHV_IMPORT_SOURCE_KINDS.map((sourceKind) => {
          const state = sources[sourceKind];
          const request = createImportRequest(sourceKind);
          return (
            <SourceCard
              key={sourceKind}
              sourceKind={sourceKind}
              state={state}
              selected={activeSource === sourceKind}
              executeReason={getExecuteDisabledReason(
                state.plan,
                request,
                state.status,
                state.planLoading || state.executing || state.refreshing || !!state.pendingOperationId,
              )}
              refreshReason={getRefreshDisabledReason(
                state.status,
                sourceKind,
                state.refreshing || state.executing || !!state.pendingOperationId,
              )}
              onSelect={() => setActiveSource(sourceKind)}
              onReload={() => void reloadStatus(sourceKind, true)}
              onRefresh={() => openRefreshConfirmation(sourceKind)}
              onDiagnostics={() => void loadDiagnostics(sourceKind)}
              onPlan={() => void loadPlan(sourceKind)}
              onSync={() => openSyncConfirmation(sourceKind)}
            />
          );
        })}
      </section>

      <section className="panel qlhv-import-active-source">
        <SectionHeading
          title={`${activeSourceDefinition.label}: ${activeSourceDefinition.liveDatabaseName} → ${activeSourceDefinition.backupDatabaseName}`}
          hint={`CSĐT ${activeSourceDefinition.maCSDT} · phân vùng ${activeSourceDefinition.sourceProfileCode}`}
        />
        <div className="qlhv-import-source-summary">
          Client chỉ gửi loại nguồn <strong>{activeSource}</strong>. Database, mã CSĐT và profile được cố định phía server.
        </div>
        {active.status && <OperationsStatusView status={active.status} />}
        <div className="qlhv-import-full-sync-warning" role="note">
          Full sync chỉ cập nhật đường dẫn và metadata ảnh trong database; không sao chép file <code>.jp2</code> vật lý.
          Học viên không còn trong snapshot sẽ bị xóa mềm, chỉ trong phân vùng {activeSourceDefinition.sourceProfileCode}.
        </div>
        {active.operationNotice && <SuccessBanner message={active.operationNotice} />}
        {active.operationError && <ErrorBanner message={active.operationError} />}
        {active.status?.lastError && <ErrorBanner message={`Lỗi vận hành gần nhất: ${active.status.lastError}`} />}
      </section>

      <div className="qlhv-import-workspace-grid">
        <section className="panel">
          <SectionHeading title="Chẩn đoán an toàn" hint="GET chỉ đọc" />
          {active.diagnosticsError && <ErrorBanner message={active.diagnosticsError} />}
          {active.diagnosticsLoading && <EmptyState text="Đang đọc dữ liệu chẩn đoán..." />}
          {!active.diagnosticsLoading && !active.diagnostics && !active.diagnosticsError && (
            <EmptyState text="Chưa có chẩn đoán cho nguồn này." />
          )}
          {active.diagnostics && <DiagnosticsView diagnostics={active.diagnostics.data} />}
        </section>

        <section className="panel">
          <SectionHeading title="Kế hoạch full sync" hint="Gắn với snapshot token hiện tại" />
          {active.planError && <ErrorBanner message={active.planError} />}
          {active.planLoading && <EmptyState text="Đang lập kế hoạch..." />}
          {!active.planLoading && !active.plan && !active.planError && (
            <EmptyState text="Chưa có kế hoạch. Sau mỗi lần refresh BAK phải lập lại kế hoạch." />
          )}
          {active.plan && (
            <PlanView
              plan={active.plan.data}
              snapshotCurrent={isPlanSnapshotCurrent(active.plan.data, active.status)}
            />
          )}
          <div className="qlhv-import-execute-row">
            <div>
              <strong>Đồng bộ vào QLHV_APP</strong>
              <p>{activeExecuteReason ?? 'Kế hoạch và snapshot token hợp lệ.'}</p>
            </div>
            <button
              type="button"
              className="btn btn--primary"
              onClick={() => openSyncConfirmation(activeSource)}
              disabled={!!activeExecuteReason}
            >
              Đồng bộ vào QLHV_APP
            </button>
          </div>
        </section>
      </div>

      <section className="panel">
        <SectionHeading title={`Lịch sử gần nhất · ${activeSourceDefinition.label}`} hint="Tự cập nhật khi thao tác đang chạy" />
        {active.historyError && <ErrorBanner message={active.historyError} />}
        {active.historyLoading && <EmptyState text="Đang tải lịch sử..." />}
        {!active.historyLoading && active.history.length === 0 && !active.historyError && (
          <EmptyState text="Chưa có lịch sử refresh hoặc full sync cho nguồn này." />
        )}
        {active.history.length > 0 && <HistoryTable rows={active.history.slice(0, 10)} />}
      </section>

      {active.lastResult && (
        <section className="panel">
          <SectionHeading title={`Kết quả full sync gần nhất · ${activeSourceDefinition.label}`} />
          <ResultView snapshot={active.lastResult} />
        </section>
      )}

      {refreshModal && (
        <RefreshModal
          state={refreshModal}
          sourceStatus={sources[refreshModal.sourceKind].status}
          submitting={sources[refreshModal.sourceKind].refreshing}
          error={sources[refreshModal.sourceKind].operationError}
          onChange={setRefreshModal}
          onClose={() => {
            if (!sources[refreshModal.sourceKind].refreshing) setRefreshModal(null);
          }}
          onSubmit={() => void handleRefresh()}
        />
      )}

      {syncModal && sources[syncModal.sourceKind].plan && (
        <SyncModal
          state={syncModal}
          plan={sources[syncModal.sourceKind].plan!.data}
          status={sources[syncModal.sourceKind].status}
          submitting={sources[syncModal.sourceKind].executing}
          error={sources[syncModal.sourceKind].operationError}
          onChange={setSyncModal}
          onClose={() => {
            if (!sources[syncModal.sourceKind].executing) setSyncModal(null);
          }}
          onSubmit={() => void handleExecute()}
        />
      )}
    </div>
  );
}

function SourceCard({
  sourceKind,
  state,
  selected,
  executeReason,
  refreshReason,
  onSelect,
  onReload,
  onRefresh,
  onDiagnostics,
  onPlan,
  onSync,
}: {
  sourceKind: QlhvImportSourceKind;
  state: SourceViewState;
  selected: boolean;
  executeReason: string | null;
  refreshReason: string | null;
  onSelect: () => void;
  onReload: () => void;
  onRefresh: () => void;
  onDiagnostics: () => void;
  onPlan: () => void;
  onSync: () => void;
}) {
  const source = QLHV_IMPORT_SOURCES[sourceKind];
  const status = state.status;
  const mappingValid = status ? statusMatchesSource(status, sourceKind) : false;
  const busy = state.refreshing
    || state.executing
    || state.planLoading
    || state.diagnosticsLoading
    || !!state.pendingOperationId
    || isOperationBusy(status);
  return (
    <article className={`qlhv-operation-card${selected ? ' is-selected' : ''}`}>
      <button type="button" className="qlhv-operation-card__tab" onClick={onSelect} aria-pressed={selected}>
        <span>
          <strong>{source.label}</strong>
          <small>CSĐT {source.maCSDT}</small>
        </span>
        <StatusBadge status={status} loading={state.statusLoading} />
      </button>
      <div className="qlhv-operation-card__mapping">
        <code>{source.liveDatabaseName}</code>
        <span aria-hidden="true">→</span>
        <code>{source.backupDatabaseName}</code>
        <span aria-hidden="true">→</span>
        <code>{source.sourceProfileCode}</code>
      </div>
      {state.statusError && <ErrorBanner message={state.statusError} />}
      {status && !mappingValid && <ErrorBanner message="Mapping backend không khớp cấu hình cố định." />}
      <div className="qlhv-operation-card__stats">
        <CompactStat label="Live / NguoiLX" value={status ? formatNumber(status.liveRows.nguoiLX) : '—'} />
        <CompactStat label="BAK / NguoiLX" value={status ? formatNumber(status.backupRows.nguoiLX) : '—'} />
        <CompactStat label="QLHV active" value={status ? formatNumber(status.targetActiveRows) : '—'} />
        <CompactStat label="Refresh gần nhất" value={formatDate(status?.backupLastRefreshTimeUtc)} />
      </div>
      <div className="qlhv-operation-card__actions">
        <button type="button" className="btn btn--ghost" onClick={onReload} disabled={state.statusLoading}>
          {state.statusLoading ? 'Đang tải...' : 'Tải trạng thái'}
        </button>
        <button type="button" className="btn btn--ghost" onClick={onRefresh} disabled={!!refreshReason} title={refreshReason ?? undefined}>
          Làm mới dữ liệu BAK
        </button>
        <button type="button" className="btn btn--ghost" onClick={onDiagnostics} disabled={busy}>
          Kiểm tra dữ liệu
        </button>
        <button type="button" className="btn btn--ghost" onClick={onPlan} disabled={busy}>
          Lập kế hoạch
        </button>
        <button type="button" className="btn btn--primary" onClick={onSync} disabled={!!executeReason} title={executeReason ?? undefined}>
          Đồng bộ vào QLHV_APP
        </button>
      </div>
    </article>
  );
}

function RefreshModal({
  state,
  sourceStatus,
  submitting,
  error,
  onChange,
  onClose,
  onSubmit,
}: {
  state: ConfirmationModalState;
  sourceStatus: QlhvOperationsStatus | null;
  submitting: boolean;
  error: string | null;
  onChange: (value: ConfirmationModalState) => void;
  onClose: () => void;
  onSubmit: () => void;
}) {
  const source = QLHV_IMPORT_SOURCES[state.sourceKind];
  const valid = !!state.operationsKey
    && state.confirmText === QLHV_REFRESH_CONFIRM_TEXT
    && !submitting
    && !getRefreshDisabledReason(sourceStatus, state.sourceKind, submitting);
  return (
    <ModalShell title="Làm mới database BAK" id="qlhv-refresh-title" onSubmit={onSubmit}>
      <h3 id="qlhv-refresh-title" className="qlhv-import-modal__title">
        {source.liveDatabaseName} → {source.backupDatabaseName}
      </h3>
      <div className="qlhv-import-full-sync-warning" role="alert">
        Database BAK hiện tại sẽ được backup dự phòng trước khi restore snapshot live mới.
        Kế hoạch full sync cũ sẽ bị vô hiệu.
      </div>
      <div className="qlhv-import-confirm-summary">
        <SummaryRow label="Loại nguồn cố định" value={state.sourceKind} />
        <SummaryRow label="Database live" value={source.liveDatabaseName} />
        <SummaryRow label="Database BAK" value={source.backupDatabaseName} />
        <SummaryRow label="Mã CSĐT" value={source.maCSDT} />
      </div>
      {error && <ErrorBanner message={error} />}
      <OperationsKeyField
        value={state.operationsKey}
        disabled={submitting}
        onChange={(operationsKey) => onChange({ ...state, operationsKey })}
      />
      <ConfirmationField
        expected={QLHV_REFRESH_CONFIRM_TEXT}
        value={state.confirmText}
        disabled={submitting}
        onChange={(confirmText) => onChange({ ...state, confirmText })}
      />
      <ModalActions
        submitting={submitting}
        submitLabel="Xác nhận làm mới BAK"
        valid={valid}
        onClose={onClose}
      />
    </ModalShell>
  );
}

function SyncModal({
  state,
  plan,
  status,
  submitting,
  error,
  onChange,
  onClose,
  onSubmit,
}: {
  state: ConfirmationModalState;
  plan: QlhvImportPlan;
  status: QlhvOperationsStatus | null;
  submitting: boolean;
  error: string | null;
  onChange: (value: ConfirmationModalState) => void;
  onClose: () => void;
  onSubmit: () => void;
}) {
  const source = QLHV_IMPORT_SOURCES[state.sourceKind];
  const request = createImportRequest(state.sourceKind);
  const validRequest = buildExecuteRequest(
    { request, data: plan },
    request,
    status,
    state.confirmText,
    submitting,
  );
  const valid = !!state.operationsKey && !!validRequest;
  return (
    <ModalShell title="Xác nhận full sync" id="qlhv-sync-title" onSubmit={onSubmit}>
      <h3 id="qlhv-sync-title" className="qlhv-import-modal__title">
        {source.backupDatabaseName} → QLHV_APP / {source.sourceProfileCode}
      </h3>
      <div className="qlhv-import-confirm-summary">
        <SummaryRow label="Snapshot token" value={shortToken(plan.backupSnapshotToken)} />
        <SummaryRow label="Kế hoạch tạo lúc" value={formatDate(plan.generatedAtUtc)} />
        <SummaryRow label="Dự kiến thêm" value={formatNumber(plan.plannedInsertHocVienRows)} />
        <SummaryRow label="Dự kiến cập nhật" value={formatNumber(plan.plannedUpdateHocVienRows)} />
        <SummaryRow label="Dự kiến khôi phục" value={formatNumber(plan.plannedReactivateHocVienRows)} />
        <SummaryRow label="Dự kiến xóa mềm" value={formatNumber(plan.plannedSoftDeleteHocVienRows)} />
        <SummaryRow label="Dự kiến bỏ qua" value={formatNumber(plan.plannedSkipHocVienRows)} />
      </div>
      <div className="qlhv-import-full-sync-warning" role="alert">
        Backend sẽ đọc lại snapshot token và khóa thao tác theo nguồn trước khi ghi.
        Chuỗi xác nhận cũ được giữ để tương thích API hiện tại.
      </div>
      {error && <ErrorBanner message={error} />}
      <OperationsKeyField
        value={state.operationsKey}
        disabled={submitting}
        onChange={(operationsKey) => onChange({ ...state, operationsKey })}
      />
      <ConfirmationField
        expected={QLHV_IMPORT_CONFIRM_TEXT}
        value={state.confirmText}
        disabled={submitting}
        onChange={(confirmText) => onChange({ ...state, confirmText })}
      />
      <ModalActions
        submitting={submitting}
        submitLabel="Xác nhận và full sync"
        valid={valid}
        onClose={onClose}
      />
    </ModalShell>
  );
}

function ModalShell({
  title,
  id,
  onSubmit,
  children,
}: {
  title: string;
  id: string;
  onSubmit: () => void;
  children: React.ReactNode;
}) {
  return (
    <div className="qlhv-import-modal" role="presentation">
      <form
        className="qlhv-import-modal__dialog"
        role="dialog"
        aria-modal="true"
        aria-labelledby={id}
        onSubmit={(event) => {
          event.preventDefault();
          onSubmit();
        }}
      >
        <SectionHeading title={title} />
        {children}
      </form>
    </div>
  );
}

function OperationsKeyField({
  value,
  disabled,
  onChange,
}: {
  value: string;
  disabled: boolean;
  onChange: (value: string) => void;
}) {
  return (
    <label className="field">
      <span className="field__label">Operations key</span>
      <input
        type="password"
        className="field__input"
        value={value}
        onChange={(event) => onChange(event.target.value)}
        autoComplete="off"
        spellCheck={false}
        disabled={disabled}
      />
      <small className="field__hint">Key chỉ được giữ trong bộ nhớ của modal này, không lưu trên trình duyệt.</small>
    </label>
  );
}

function ConfirmationField({
  expected,
  value,
  disabled,
  onChange,
}: {
  expected: string;
  value: string;
  disabled: boolean;
  onChange: (value: string) => void;
}) {
  return (
    <label className="field">
      <span className="field__label">Nhập chính xác <code>{expected}</code></span>
      <input
        className="field__input"
        value={value}
        onChange={(event) => onChange(event.target.value)}
        autoComplete="off"
        spellCheck={false}
        autoFocus
        disabled={disabled}
      />
    </label>
  );
}

function ModalActions({
  submitting,
  submitLabel,
  valid,
  onClose,
}: {
  submitting: boolean;
  submitLabel: string;
  valid: boolean;
  onClose: () => void;
}) {
  return (
    <div className="qlhv-import-modal__actions">
      <button type="button" className="btn btn--ghost" onClick={onClose} disabled={submitting}>Hủy</button>
      <button type="submit" className="btn btn--primary" disabled={!valid}>
        {submitting ? 'Đang gửi yêu cầu...' : submitLabel}
      </button>
    </div>
  );
}

function DiagnosticsView({ diagnostics }: { diagnostics: QlhvImportDiagnostics }) {
  const metrics: MetricItem[] = [
    ['Học viên nguồn', diagnostics.sourceHocVienRows, diagnostics.sourceHocVienRows > 0 ? 'ok' : 'blocked'],
    ['MaDK phân biệt', diagnostics.sourceDistinctMaDkRows],
    ['MaDK trùng', diagnostics.duplicateSourceMaDkRows, diagnostics.duplicateSourceMaDkRows > 0 ? 'blocked' : 'ok'],
    ['QLHV hiện có', diagnostics.currentAppHocVienRows],
    ['Khớp định danh', diagnostics.targetExactIdentityMatches],
    ['Profile khác cùng MaDK', diagnostics.targetMaDkConflictsOtherProfiles, diagnostics.targetMaDkConflictsOtherProfiles > 0 ? 'warning' : 'ok'],
    ['Sẽ khôi phục', diagnostics.plannedReactivateHocVienRows],
    ['Sẽ xóa mềm', diagnostics.plannedSoftDeleteHocVienRows, diagnostics.plannedSoftDeleteHocVienRows > 0 ? 'warning' : 'ok'],
    ['Có thể thực hiện', diagnostics.executable ? 'Có' : 'Không', diagnostics.executable ? 'ok' : 'blocked'],
  ];
  return (
    <>
      <ReadOnlySummary sourceDatabaseName={diagnostics.sourceDatabaseName} profile={diagnostics.sourceProfileCode} maCSDT={diagnostics.maCSDT} />
      <MetricGrid items={metrics} />
      <IssueList title="Điểm chặn chẩn đoán" items={diagnostics.blockers} variant="blocker" />
      <IssueList title="Cảnh báo chẩn đoán" items={diagnostics.warnings} variant="warning" />
    </>
  );
}

function PlanView({ plan, snapshotCurrent }: { plan: QlhvImportPlan; snapshotCurrent: boolean }) {
  const metrics: MetricItem[] = [
    ['Học viên nguồn', plan.sourceHocVienRows, plan.sourceHocVienRows > 0 ? 'ok' : 'blocked'],
    ['Khóa học nguồn', plan.sourceKhoaHocRows],
    ['Thêm', plan.plannedInsertHocVienRows],
    ['Cập nhật', plan.plannedUpdateHocVienRows],
    ['Khôi phục', plan.plannedReactivateHocVienRows],
    ['Xóa mềm', plan.plannedSoftDeleteHocVienRows, plan.plannedSoftDeleteHocVienRows > 0 ? 'warning' : 'ok'],
    ['Bỏ qua', plan.plannedSkipHocVienRows],
    ['Token hiện hành', snapshotCurrent ? 'Có' : 'Không', snapshotCurrent ? 'ok' : 'blocked'],
    ['Có thể thực hiện', plan.executable ? 'Có' : 'Không', plan.executable ? 'ok' : 'blocked'],
  ];
  return (
    <>
      <ReadOnlySummary sourceDatabaseName={plan.sourceDatabaseName} profile={plan.sourceProfileCode} maCSDT={plan.maCSDT} />
      <div className={`qlhv-import-token ${snapshotCurrent ? 'is-current' : 'is-stale'}`}>
        <span>Snapshot token</span>
        <code title={plan.backupSnapshotToken}>{shortToken(plan.backupSnapshotToken)}</code>
        <strong>{snapshotCurrent ? 'Hiện hành' : 'Đã stale — phải lập lại plan'}</strong>
      </div>
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
        <span>{result.status}</span>
      </div>
      <p>{result.message}</p>
      <div className="qlhv-import-result-grid">
        <SummaryRow label="Database snapshot" value={result.plan.sourceDatabaseName} />
        <SummaryRow label="Đã thêm" value={formatNumber(result.insertedHocVienRows)} />
        <SummaryRow label="Đã cập nhật" value={formatNumber(result.updatedHocVienRows)} />
        <SummaryRow label="Đã khôi phục" value={formatNumber(result.reactivatedHocVienRows)} />
        <SummaryRow label="Đã xóa mềm" value={formatNumber(result.softDeletedHocVienRows)} />
        <SummaryRow label="Đã bỏ qua" value={formatNumber(result.skippedHocVienRows)} />
      </div>
      {!successful && <IssueList title="Điểm chặn từ backend" items={result.plan.blockers} variant="blocker" />}
    </div>
  );
}

function HistoryTable({ rows }: { rows: QlhvOperationHistoryItem[] }) {
  return (
    <div className="qlhv-import-history-wrap">
      <table className="table qlhv-import-history-table">
        <thead>
          <tr>
            <th>Thời gian</th>
            <th>Thao tác</th>
            <th>Trạng thái</th>
            <th>Nguồn</th>
            <th>Thêm</th>
            <th>Cập nhật</th>
            <th>Khôi phục</th>
            <th>Xóa mềm</th>
            <th>Bỏ qua</th>
            <th>Thông tin</th>
          </tr>
        </thead>
        <tbody>
          {rows.map((row) => (
            <tr key={row.operationId}>
              <td>{formatDate(row.startedAtUtc)}</td>
              <td>{row.operationType === 'REFRESH_BACKUP' ? 'Refresh BAK' : 'Full sync'}</td>
              <td><span className={`qlhv-history-status is-${historyTone(row.status)}`}>{row.status}</span></td>
              <td>{formatNumber(row.sourceRows)}</td>
              <td>{formatNumber(row.insertedRows)}</td>
              <td>{formatNumber(row.updatedRows)}</td>
              <td>{formatNumber(row.reactivatedRows)}</td>
              <td>{formatNumber(row.softDeletedRows)}</td>
              <td>{formatNumber(row.skippedRows)}</td>
              <td>{row.errorMessage ?? (row.snapshotToken ? `Token ${shortToken(row.snapshotToken)}` : '—')}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function StatusBadge({ status, loading }: { status: QlhvOperationsStatus | null; loading: boolean }) {
  if (loading && !status) return <span className="qlhv-operation-state is-loading">Đang tải</span>;
  if (!status) return <span className="qlhv-operation-state is-failed">Chưa có trạng thái</span>;
  const labels: Record<QlhvOperationsStatus['state'], string> = {
    idle: 'Sẵn sàng',
    refreshing: 'Đang refresh BAK',
    syncing: 'Đang full sync',
    succeeded: 'Thành công',
    failed: 'Có lỗi',
  };
  return <span className={`qlhv-operation-state is-${status.state}`}>{labels[status.state]}</span>;
}

function OperationsStatusView({ status }: { status: QlhvOperationsStatus }) {
  const metrics: MetricItem[] = [
    ['Live · NguoiLX', status.liveRows.nguoiLX],
    ['Live · NguoiLX_HoSo', status.liveRows.nguoiLXHoSo],
    ['Live · KhoaHoc', status.liveRows.khoaHoc],
    ['BAK · NguoiLX', status.backupRows.nguoiLX],
    ['BAK · NguoiLX_HoSo', status.backupRows.nguoiLXHoSo],
    ['BAK · KhoaHoc', status.backupRows.khoaHoc],
    ['QLHV_APP active', status.targetActiveRows],
    ['Có thể refresh', status.canRefresh ? 'Có' : 'Không', status.canRefresh ? 'ok' : 'blocked'],
    ['Có thể sync', status.canSync ? 'Có' : 'Không', status.canSync ? 'ok' : 'blocked'],
  ];
  return (
    <div className="qlhv-operations-status-detail">
      <div className="qlhv-import-readonly-summary">
        <span>Refresh gần nhất: {formatDate(status.backupLastRefreshTimeUtc)}</span>
        <span>Sync gần nhất: {formatDate(status.lastSyncTimeUtc)}</span>
        <span>Token: {shortToken(status.backupSnapshotToken ?? '')}</span>
      </div>
      <MetricGrid items={metrics} />
    </div>
  );
}

function CompactStat({ label, value }: { label: string; value: string }) {
  return <div><span>{label}</span><strong>{value}</strong></div>;
}

function ReadOnlySummary({ sourceDatabaseName, profile, maCSDT }: { sourceDatabaseName: string; profile: string; maCSDT: string }) {
  return (
    <div className="qlhv-import-readonly-summary">
      <span className="is-ok">GET chỉ đọc</span>
      <span>DB nguồn: {sourceDatabaseName}</span>
      <span>{profile}</span>
      <span>CSĐT {maCSDT}</span>
      <span>Toàn bộ khóa</span>
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

function IssueList({ title, items, variant }: { title: string; items: string[]; variant: 'blocker' | 'warning' }) {
  if (items.length === 0) return null;
  return (
    <div className={`qlhv-import-issues qlhv-import-issues--${variant}`} role={variant === 'blocker' ? 'alert' : 'status'}>
      <strong>{title}</strong>
      <ul>{items.map((item, index) => <li key={`${index}-${item}`}>{item}</li>)}</ul>
    </div>
  );
}

function SectionHeading({ title, hint }: { title: string; hint?: string }) {
  return <div className="qlhv-import-section-heading"><strong>{title}</strong>{hint && <span>{hint}</span>}</div>;
}

function EmptyState({ text }: { text: string }) {
  return <div className="qlhv-import-empty">{text}</div>;
}

function ErrorBanner({ message }: { message: string }) {
  return <div className="qlhv-import-error" role="alert">{message}</div>;
}

function SuccessBanner({ message }: { message: string }) {
  return <div className="qlhv-import-success" role="status">{message}</div>;
}

function SummaryRow({ label, value }: { label: string; value: string }) {
  return <div className="qlhv-import-summary-row"><span>{label}</span><strong>{value}</strong></div>;
}

function formatNumber(value: number): string {
  return NUMBER_FORMAT.format(value);
}

function formatDate(value: string | null | undefined): string {
  if (!value) return 'Chưa có';
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? value : DATE_FORMAT.format(date);
}

function shortToken(value: string): string {
  if (!value) return 'Chưa có';
  return value.length <= 24 ? value : `${value.slice(0, 12)}…${value.slice(-8)}`;
}

function historyTone(status: string): 'ok' | 'busy' | 'failed' {
  const normalized = status.toLowerCase();
  if (normalized.includes('fail') || normalized.includes('error') || normalized.includes('lỗi')) return 'failed';
  if (normalized.includes('queue') || normalized.includes('running') || normalized.includes('đang')) return 'busy';
  return 'ok';
}

function toSafeClientMessage(error: unknown, fallback: string): string {
  if (error instanceof Error && error.message && error.name !== 'AbortError') return error.message;
  return fallback;
}
