import { useCallback, useEffect, useMemo, useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import { useAuth } from '../auth/AuthContext';
import { useDataVersionRefresh } from '../data-version/useDataVersionRefresh';
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
  createImportRequest,
  getExecuteDisabledReason,
  getRefreshDisabledReason,
  isOperationBusy,
  isPlanSnapshotCurrent,
  QLHV_IMPORT_SOURCE_KINDS,
  QLHV_IMPORT_SOURCES,
  statusMatchesSource,
} from './logic';
import type {
  QlhvImportDiagnostics,
  QlhvImportDomain,
  QlhvImportDomainResult,
  QlhvImportDomainStatus,
  QlhvImportEntityCounts,
  QlhvImportExecuteResult,
  QlhvImportPlan,
  QlhvImportPhotoCounts,
  QlhvImportSnapshot,
  QlhvImportSourceKind,
  QlhvOperationHistoryItem,
  QlhvOperationType,
  QlhvOperationsStatus,
} from './types';
import AutoSyncPanel from './AutoSyncPanel';
import PhotoProcessingPanel from './PhotoProcessingPanel';

const NUMBER_FORMAT = new Intl.NumberFormat('vi-VN');
const DATE_FORMAT = new Intl.DateTimeFormat('vi-VN', {
  dateStyle: 'short',
  timeStyle: 'medium',
});
const POLL_INTERVAL_MS = 2_500;
const NO_WRITE_PERMISSION_MESSAGE =
  'Chỉ tài khoản Quản trị viên được phép thực hiện đồng bộ.';
const AUTO_SYNC_BUSY_MESSAGE = 'Auto Sync đang chạy; các thao tác ghi tạm thời bị khóa.';
const PARTIAL_SYNC_WARNING =
  'Đợt đồng bộ này sẽ cập nhật Học viên. Khóa học/Giáo viên chưa sẵn sàng sẽ được bỏ qua và không bị xóa.';

const IMPORT_DOMAIN_DEFINITIONS: ReadonlyArray<{
  domain: QlhvImportDomain;
  label: string;
}> = [
  { domain: 'HOC_VIEN', label: 'Học viên' },
  { domain: 'KHOA_HOC', label: 'Khóa học' },
  { domain: 'GIAO_VIEN', label: 'Giáo viên' },
  { domain: 'KHOA_HOC_GIAO_VIEN', label: 'Quan hệ Giáo viên – Khóa học' },
];

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
  const { user } = useAuth();
  const [searchParams] = useSearchParams();
  const isAdmin = user?.role === 'Admin';
  const sessionStartRunId = searchParams.get('sessionStartRunId');
  const sessionStartRequestFailed = searchParams.get('sessionStartState') === 'failed';
  const [activeSource, setActiveSource] = useState<QlhvImportSourceKind>('OTO');
  const [sources, setSources] = useState<Record<QlhvImportSourceKind, SourceViewState>>({
    OTO: createEmptySourceState(),
    MOTO: createEmptySourceState(),
  });
  const [contentReloadToken, setContentReloadToken] = useState(0);
  const [autoSyncBusy, setAutoSyncBusy] = useState(false);
  const handleAutoSyncBusyChange = useCallback((busy: boolean) => {
    setAutoSyncBusy(busy);
  }, []);

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
            status: null,
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
            status: null,
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
      // Fail closed while the current permission/configuration snapshot is unknown.
      status: null,
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
        status: null,
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
    return statusResult.status === 'fulfilled'
      && (!includeHistory || historyResult.status === 'fulfilled');
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
      return true;
    } catch (error) {
      patchSource(sourceKind, {
        diagnostics: null,
        diagnosticsLoading: false,
        diagnosticsError: toSafeClientMessage(error, 'Không thể chạy chẩn đoán.'),
      });
      return false;
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
      return true;
    } catch (error) {
      patchSource(sourceKind, {
        plan: null,
        planLoading: false,
        planError: toSafeClientMessage(error, 'Không thể lập kế hoạch.'),
      });
      return false;
    }
  }

  async function handleRefresh(sourceKind: QlhvImportSourceKind) {
    const state = sources[sourceKind];
    setActiveSource(sourceKind);
    if (!isAdmin) {
      patchSource(sourceKind, { operationError: NO_WRITE_PERMISSION_MESSAGE });
      return;
    }
    if (autoSyncBusy) {
      patchSource(sourceKind, { operationError: AUTO_SYNC_BUSY_MESSAGE });
      return;
    }

    const body = buildRefreshRequest(sourceKind);
    if (getRefreshDisabledReason(
      state.status,
      sourceKind,
      state.refreshing || state.executing || !!state.pendingOperationId,
    )) {
      patchSource(sourceKind, { operationError: 'Trạng thái nguồn không hợp lệ để làm mới BAK.' });
      return;
    }

    patchSource(sourceKind, { refreshing: true, operationError: null, operationNotice: null });
    try {
      const result = await refreshQlhvBackup(body);
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

  async function handleExecute(sourceKind: QlhvImportSourceKind) {
    const state = sources[sourceKind];
    setActiveSource(sourceKind);
    if (!isAdmin) {
      patchSource(sourceKind, { operationError: NO_WRITE_PERMISSION_MESSAGE });
      return;
    }
    if (autoSyncBusy) {
      patchSource(sourceKind, { operationError: AUTO_SYNC_BUSY_MESSAGE });
      return;
    }

    const request = createImportRequest(sourceKind);
    const body = buildExecuteRequest(
      state.plan,
      request,
      state.status,
      state.planLoading || state.executing || state.refreshing || !!state.pendingOperationId,
    );
    if (!body || !state.plan) {
      patchSource(sourceKind, { operationError: 'Kế hoạch hoặc snapshot token không hợp lệ.' });
      return;
    }

    patchSource(sourceKind, { executing: true, operationError: null, operationNotice: null });
    try {
      const outcome = await executeQlhvImport(body);
      patchSource(sourceKind, {
        executing: false,
        lastResult: { request, data: outcome.result, outcomeKind: outcome.kind },
      });

      const failedAfterWrite = outcome.result.executed
        && outcome.result.status.trim().toUpperCase() === 'FAILED';
      if (outcome.kind === 'blocked' || !outcome.result.executed) {
        patchSource(sourceKind, {
          plan: { request, data: outcome.result.plan },
          operationError: outcome.result.message || 'Yêu cầu đồng bộ đã bị backend chặn.',
        });
      } else {
        patchSource(sourceKind, {
          plan: null,
          diagnostics: null,
          operationError: failedAfterWrite
            ? outcome.result.message || 'Học viên đồng bộ thất bại; hãy kiểm tra kết quả từng module.'
            : null,
          operationNotice: failedAfterWrite
            ? null
            : outcome.result.message || 'Full sync hoàn tất.',
        });
        await Promise.all([
          reloadStatus(sourceKind, true),
          loadDiagnostics(sourceKind),
          loadPlan(sourceKind),
        ]);
        await dataVersion.reload();
      }
    } catch (error) {
      patchSource(sourceKind, {
        executing: false,
        operationError: toSafeClientMessage(error, 'Không thể thực hiện đồng bộ.'),
      });
    }
  }

  async function reloadVisibleData() {
    setContentReloadToken((current) => current + 1);
    const statusResults = await Promise.all(
      QLHV_IMPORT_SOURCE_KINDS.map((sourceKind) => reloadStatus(sourceKind, true)),
    );
    const current = sources[activeSource];
    const contentResults = await Promise.all([
      current.diagnostics ? loadDiagnostics(activeSource) : Promise.resolve(),
      current.plan ? loadPlan(activeSource) : Promise.resolve(),
    ]);
    return statusResults.every(Boolean)
      && contentResults.every((result) => result !== false);
  }

  const dataVersion = useDataVersionRefresh({
    resources: [
      'hocVienVersion',
      'khoaHocVersion',
      'giaoVienVersion',
      'photoVersion',
    ],
    onVersionChanged: async () => {
      if (!await reloadVisibleData()) {
        throw new Error('Không thể tải lại đầy đủ dữ liệu đồng bộ theo phiên bản mới.');
      }
    },
  });

  const active = sources[activeSource];
  const activeRequest = useMemo(() => createImportRequest(activeSource), [activeSource]);
  const activeSourceDefinition = QLHV_IMPORT_SOURCES[activeSource];
  const activeBusy = active.planLoading
    || active.executing
    || active.refreshing
    || !!active.pendingOperationId;
  const activeExecuteReason = !isAdmin
    ? NO_WRITE_PERMISSION_MESSAGE
    : autoSyncBusy
      ? AUTO_SYNC_BUSY_MESSAGE
    : getExecuteDisabledReason(
        active.plan,
        activeRequest,
        active.status,
        activeBusy,
      );
  const autoSyncOperationBlocker = getAutoSyncOperationBlocker(sources);
  const combinedHistory = useMemo(
    () => [...sources.OTO.history, ...sources.MOTO.history]
      .filter((row, index, all) =>
        all.findIndex((candidate) => candidate.operationId === row.operationId) === index)
      .sort((left, right) =>
        (right.startedAtUtc ?? '').localeCompare(left.startedAtUtc ?? '')),
    [sources.MOTO.history, sources.OTO.history],
  );

  return (
    <div className="qlhv-import-page">
      <section className="panel qlhv-import-hero">
        <div>
          <span className="qlhv-import-eyebrow">QLHV_APP · VẬN HÀNH AN TOÀN</span>
          <h2>Đồng bộ dữ liệu CSĐT</h2>
          <p>Làm mới database BAK, kiểm tra snapshot, lập kế hoạch và full sync theo từng phân vùng độc lập.</p>
        </div>
        <div className="qlhv-import-hero__actions">
          <span className="qlhv-import-badge">Live → BAK → QLHV_APP</span>
          <button
            type="button"
            className="btn btn--ghost"
            onClick={() => void dataVersion.reload()}
            disabled={dataVersion.checking}
          >
            {dataVersion.checking ? 'Đang tải lại...' : 'Tải lại dữ liệu'}
          </button>
        </div>
      </section>

      {dataVersion.error && <ErrorBanner message={dataVersion.error} />}

      {!isAdmin && (
        <div className="qlhv-import-permission-note" role="status">
          {NO_WRITE_PERMISSION_MESSAGE}. Bạn vẫn có thể xem trạng thái, chẩn đoán, kế hoạch và lịch sử.
        </div>
      )}

      <AutoSyncPanel
        isAdmin={isAdmin}
        operationBlocker={autoSyncOperationBlocker}
        operationHistory={combinedHistory}
        reloadToken={contentReloadToken}
        onBusyChange={handleAutoSyncBusyChange}
        onAccepted={async () => {
          await dataVersion.reload();
        }}
        sessionStartRunId={sessionStartRunId}
        sessionStartRequestFailed={sessionStartRequestFailed}
      />

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
              executeReason={!isAdmin
                ? NO_WRITE_PERMISSION_MESSAGE
                : autoSyncBusy
                  ? AUTO_SYNC_BUSY_MESSAGE
                : getExecuteDisabledReason(
                    state.plan,
                    request,
                    state.status,
                    state.planLoading || state.executing || state.refreshing || !!state.pendingOperationId,
                  )}
              refreshReason={!isAdmin
                ? NO_WRITE_PERMISSION_MESSAGE
                : autoSyncBusy
                  ? AUTO_SYNC_BUSY_MESSAGE
                : getRefreshDisabledReason(
                    state.status,
                    sourceKind,
                    state.refreshing || state.executing || !!state.pendingOperationId,
                  )}
              onSelect={() => setActiveSource(sourceKind)}
              onReload={() => void reloadStatus(sourceKind, true)}
              onRefresh={() => void handleRefresh(sourceKind)}
              onDiagnostics={() => void loadDiagnostics(sourceKind)}
              onPlan={() => void loadPlan(sourceKind)}
              onSync={() => void handleExecute(sourceKind)}
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
          Chỉ module có snapshot hợp lệ mới được cập nhật và xóa mềm trong phân vùng {activeSourceDefinition.sourceProfileCode};
          module tạm bỏ qua sẽ không bị thêm, sửa hoặc xóa.
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
              onClick={() => void handleExecute(activeSource)}
              disabled={!!activeExecuteReason}
              aria-busy={active.executing}
            >
              {active.executing ? 'Đang đồng bộ...' : 'Đồng bộ vào QLHV_APP'}
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

      <PhotoProcessingPanel
        isAdmin={isAdmin}
        photoVersion={dataVersion.version?.photoVersion}
        reloadToken={contentReloadToken}
        writeBlockedReason={autoSyncBusy ? AUTO_SYNC_BUSY_MESSAGE : null}
      />

    </div>
  );
}

function getAutoSyncOperationBlocker(
  sources: Record<QlhvImportSourceKind, SourceViewState>,
): string | null {
  for (const sourceKind of QLHV_IMPORT_SOURCE_KINDS) {
    const current = sources[sourceKind];
    if (!current.status) {
      return `Chưa đọc được trạng thái ${sourceKind}.`;
    }
    if (isOperationBusy(current.status) || current.pendingOperationId) {
      return `${sourceKind} đang có thao tác vận hành.`;
    }
    if (current.status.dryRun) {
      return `${sourceKind}: chế độ DryRun đang bật.`;
    }
    if (!current.status.targetWritesEnabled) {
      return `${sourceKind}: quyền ghi dữ liệu đang tắt.`;
    }
    if (!current.status.writeAuthorized) {
      return `${sourceKind}: tài khoản hiện tại chưa được backend cấp quyền ghi.`;
    }
  }
  return null;
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
        <button type="button" className="btn btn--ghost" onClick={onRefresh} disabled={!!refreshReason} title={refreshReason ?? undefined} aria-busy={state.refreshing}>
          {state.refreshing ? 'Đang làm mới BAK...' : 'Làm mới dữ liệu BAK'}
        </button>
        <button type="button" className="btn btn--ghost" onClick={onDiagnostics} disabled={busy}>
          Kiểm tra dữ liệu
        </button>
        <button type="button" className="btn btn--ghost" onClick={onPlan} disabled={busy}>
          Lập kế hoạch
        </button>
        <button type="button" className="btn btn--primary" onClick={onSync} disabled={!!executeReason} title={executeReason ?? undefined} aria-busy={state.executing}>
          {state.executing ? 'Đang đồng bộ...' : 'Đồng bộ vào QLHV_APP'}
        </button>
      </div>
      {(refreshReason || executeReason) && (
        <div className="qlhv-operation-card__disabled-reasons" role="status">
          {refreshReason && <p><strong>Làm mới BAK:</strong> {refreshReason}</p>}
          {executeReason && <p><strong>Full sync:</strong> {executeReason}</p>}
        </div>
      )}
    </article>
  );
}

function DiagnosticsView({ diagnostics }: { diagnostics: QlhvImportDiagnostics }) {
  const hocVienExecutable = isImportDomainExecutable(diagnostics, 'HOC_VIEN');
  const metrics: MetricItem[] = [
    ['QLHV hiện có', diagnostics.currentAppHocVienRows],
    ['Khớp định danh', diagnostics.targetExactIdentityMatches],
    ['Profile khác cùng MaDK', diagnostics.targetMaDkConflictsOtherProfiles, diagnostics.targetMaDkConflictsOtherProfiles > 0 ? 'warning' : 'ok'],
    ['Khóa Học viên trùng', diagnostics.hocVien.duplicateSourceKeys, diagnostics.hocVien.duplicateSourceKeys > 0 ? 'blocked' : 'ok'],
    ['Xung đột quan hệ', diagnostics.relationConflicts, diagnostics.relationConflicts > 0 ? 'warning' : 'ok'],
    ['Học viên sẵn sàng', hocVienExecutable ? 'Có' : 'Không', hocVienExecutable ? 'ok' : 'blocked'],
  ];
  return (
    <>
      <ReadOnlySummary sourceDatabaseName={diagnostics.sourceDatabaseName} profile={diagnostics.sourceProfileCode} maCSDT={diagnostics.maCSDT} />
      <MetricGrid items={metrics} />
      <DomainReadinessGrid readiness={diagnostics} />
      <EntityCountsGrid
        hocVien={diagnostics.hocVien}
        khoaHoc={diagnostics.khoaHoc}
        giaoVien={diagnostics.giaoVien}
        khoaHocGiaoVien={diagnostics.khoaHocGiaoVien}
        mode="diagnostics"
      />
      <ImportPhotoCounts counts={diagnostics.photo} />
      <IssueList title="Điểm chặn toàn cục" items={diagnostics.blockers} variant="blocker" />
      <IssueList title="Điểm chặn Học viên" items={diagnostics.hocVienBlockers} variant="blocker" />
      <OptionalDomainIssues readiness={diagnostics} />
      <IssueList title="Cảnh báo module tùy chọn" items={diagnostics.optionalWarnings} variant="warning" />
      <IssueList title="Cảnh báo chẩn đoán" items={diagnostics.warnings} variant="warning" />
    </>
  );
}

function PlanView({ plan, snapshotCurrent }: { plan: QlhvImportPlan; snapshotCurrent: boolean }) {
  const hocVienExecutable = isImportDomainExecutable(plan, 'HOC_VIEN');
  const hasSkippedOptionalDomains = plan.skippedDomains.some((domain) => domain !== 'HOC_VIEN');
  const metrics: MetricItem[] = [
    ['Khóa Học viên trùng', plan.hocVien.duplicateSourceKeys, plan.hocVien.duplicateSourceKeys > 0 ? 'blocked' : 'ok'],
    ['Xung đột quan hệ', plan.relationConflicts, plan.relationConflicts > 0 ? 'warning' : 'ok'],
    ['Token hiện hành', snapshotCurrent ? 'Có' : 'Không', snapshotCurrent ? 'ok' : 'blocked'],
    ['Học viên sẵn sàng', hocVienExecutable ? 'Có' : 'Không', hocVienExecutable ? 'ok' : 'blocked'],
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
      <DomainReadinessGrid readiness={plan} />
      <EntityCountsGrid
        hocVien={plan.hocVien}
        khoaHoc={plan.khoaHoc}
        giaoVien={plan.giaoVien}
        khoaHocGiaoVien={plan.khoaHocGiaoVien}
      />
      <ImportPhotoCounts counts={plan.photo} />
      <IssueList title="Điểm chặn toàn cục" items={plan.blockers} variant="blocker" />
      <IssueList title="Điểm chặn Học viên" items={plan.hocVienBlockers} variant="blocker" />
      <OptionalDomainIssues readiness={plan} />
      <IssueList title="Cảnh báo module tùy chọn" items={plan.optionalWarnings} variant="warning" />
      <IssueList title="Cảnh báo kế hoạch" items={plan.warnings} variant="warning" />
      {hocVienExecutable && hasSkippedOptionalDomains && (
        <div className="qlhv-import-full-sync-warning qlhv-import-partial-warning" role="status">
          {PARTIAL_SYNC_WARNING}
        </div>
      )}
    </>
  );
}

type ImportDomainReadiness = Pick<
  QlhvImportPlan,
  | 'executable'
  | 'hocVienStatus'
  | 'khoaHocStatus'
  | 'giaoVienStatus'
  | 'relationStatus'
  | 'hocVienBlockers'
  | 'khoaHocBlockers'
  | 'giaoVienBlockers'
  | 'relationBlockers'
  | 'executableDomains'
  | 'skippedDomains'
>;

function DomainReadinessGrid({ readiness }: { readiness: ImportDomainReadiness }) {
  return (
    <div className="qlhv-import-domain-readiness" aria-label="Trạng thái từng nhóm dữ liệu">
      {IMPORT_DOMAIN_DEFINITIONS.map(({ domain, label }) => {
        const executable = isImportDomainExecutable(readiness, domain);
        const skipped = readiness.skippedDomains.includes(domain);
        const blockers = getImportDomainBlockers(readiness, domain);
        const domainStatus = getImportDomainStatus(readiness, domain);
        const tone = executable ? 'ok' : skipped || domain !== 'HOC_VIEN' ? 'warning' : 'blocked';
        const status = formatDomainReadinessStatus(domainStatus);
        return (
          <article key={domain} className={`qlhv-import-domain-readiness__item is-${tone}`}>
            <div>
              <strong>{label}</strong>
              <span>{status}</span>
            </div>
            {!executable && (
              <p>{blockers[0] ?? 'Backend chưa trả lý do cụ thể cho nhóm dữ liệu này.'}</p>
            )}
          </article>
        );
      })}
    </div>
  );
}

function OptionalDomainIssues({ readiness }: { readiness: ImportDomainReadiness }) {
  return (
    <>
      {IMPORT_DOMAIN_DEFINITIONS
        .filter(({ domain }) => domain !== 'HOC_VIEN')
        .map(({ domain, label }) => (
          <IssueList
            key={domain}
            title={`${label}: tạm bỏ qua`}
            items={getImportDomainBlockers(readiness, domain)}
            variant="warning"
          />
        ))}
    </>
  );
}

function isImportDomainExecutable(
  readiness: ImportDomainReadiness,
  domain: QlhvImportDomain,
): boolean {
  return readiness.executableDomains.includes(domain)
    && (domain !== 'HOC_VIEN' || readiness.executable);
}

function getImportDomainBlockers(
  readiness: ImportDomainReadiness,
  domain: QlhvImportDomain,
): string[] {
  switch (domain) {
    case 'HOC_VIEN':
      return readiness.hocVienBlockers;
    case 'KHOA_HOC':
      return readiness.khoaHocBlockers;
    case 'GIAO_VIEN':
      return readiness.giaoVienBlockers;
    case 'KHOA_HOC_GIAO_VIEN':
      return readiness.relationBlockers;
  }
}

function getImportDomainStatus(
  readiness: ImportDomainReadiness,
  domain: QlhvImportDomain,
): QlhvImportDomainStatus {
  switch (domain) {
    case 'HOC_VIEN':
      return readiness.hocVienStatus;
    case 'KHOA_HOC':
      return readiness.khoaHocStatus;
    case 'GIAO_VIEN':
      return readiness.giaoVienStatus;
    case 'KHOA_HOC_GIAO_VIEN':
      return readiness.relationStatus;
  }
}

function formatDomainReadinessStatus(status: QlhvImportDomainStatus): string {
  switch (status) {
    case 'EXECUTABLE':
      return 'Sẵn sàng đồng bộ';
    case 'BLOCKED':
      return 'Bị chặn';
    case 'SKIPPED_SCHEMA_NOT_READY':
      return 'Tạm bỏ qua – schema chưa sẵn sàng';
    case 'SKIPPED_SOURCE_NOT_READY':
      return 'Tạm bỏ qua – nguồn chưa sẵn sàng';
    case 'SKIPPED_DEPENDENCY_NOT_READY':
      return 'Tạm bỏ qua – phụ thuộc chưa sẵn sàng';
    case 'SUCCESS':
      return 'Thành công';
    case 'FAILED':
      return 'Thất bại';
    case 'NO_OP':
      return 'Không có thay đổi';
  }
}

function EntityCountsGrid({
  hocVien,
  khoaHoc,
  giaoVien,
  khoaHocGiaoVien,
  mode = 'plan',
}: {
  hocVien: QlhvImportEntityCounts | null;
  khoaHoc: QlhvImportEntityCounts | null;
  giaoVien: QlhvImportEntityCounts | null;
  khoaHocGiaoVien: QlhvImportEntityCounts | null;
  mode?: 'diagnostics' | 'plan' | 'result';
}) {
  return (
    <div className="qlhv-import-entity-groups">
      <EntityCountsSection title="Học viên" counts={hocVien} mode={mode} />
      <EntityCountsSection title="Khóa học" counts={khoaHoc} mode={mode} />
      <EntityCountsSection title="Giáo viên" counts={giaoVien} mode={mode} />
      <EntityCountsSection title="Quan hệ Giáo viên – Khóa học" counts={khoaHocGiaoVien} mode={mode} />
    </div>
  );
}

function EntityCountsSection({
  title,
  counts,
  mode,
}: {
  title: string;
  counts: QlhvImportEntityCounts | null;
  mode: 'diagnostics' | 'plan' | 'result';
}) {
  const resultMode = mode === 'result';
  const contextLabel = mode === 'diagnostics' ? 'Chẩn đoán' : mode === 'result' ? 'Kết quả' : 'Kế hoạch';
  return (
    <section className="qlhv-import-entity-group" aria-label={`${contextLabel} ${title}`}>
      <h3>{title}</h3>
      {!counts ? (
        <div className="qlhv-import-entity-group__unavailable">
          Backend chưa trả số liệu kết quả chi tiết cho nhóm này.
        </div>
      ) : (
        <MetricGrid items={[
          ['Nguồn', counts.sourceRows, counts.sourceRows > 0 ? 'ok' : 'default'],
          [resultMode ? 'Đã thêm' : 'Thêm', counts.insert],
          [resultMode ? 'Đã cập nhật' : 'Cập nhật', counts.update],
          [resultMode ? 'Đã khôi phục' : 'Khôi phục', counts.reactivate],
          [resultMode ? 'Đã xóa mềm' : 'Xóa mềm', counts.softDelete, counts.softDelete > 0 ? 'warning' : 'default'],
          [resultMode ? 'Đã bỏ qua' : 'Bỏ qua', counts.skip],
          ['Khóa nguồn trùng', counts.duplicateSourceKeys, counts.duplicateSourceKeys > 0 ? 'blocked' : 'ok'],
        ]} />
      )}
    </section>
  );
}

function ResultView({ snapshot }: { snapshot: QlhvImportLastResult }) {
  const result = snapshot.data;
  const normalizedStatus = result.status.trim().toUpperCase();
  const successful = snapshot.outcomeKind === 'executed'
    && result.executed
    && normalizedStatus !== 'FAILED';
  const partial = successful && normalizedStatus === 'PARTIAL_SUCCESS';
  const noOp = successful && normalizedStatus === 'NO_OP';
  const resultTone = partial ? 'partial' : successful ? 'success' : 'blocked';
  const resultTitle = partial
    ? 'Đồng bộ hoàn tất một phần'
    : noOp
      ? 'Không có thay đổi cần đồng bộ'
      : successful
        ? 'Full sync thành công'
        : 'Yêu cầu không được thực hiện';
  const hocVien = result.hocVien ?? {
    sourceRows: result.plan.hocVien.sourceRows,
    insert: result.insertedHocVienRows,
    update: result.updatedHocVienRows,
    reactivate: result.reactivatedHocVienRows,
    softDelete: result.softDeletedHocVienRows,
    skip: result.skippedHocVienRows,
    duplicateSourceKeys: result.plan.hocVien.duplicateSourceKeys,
  };
  return (
    <div className={`qlhv-import-result qlhv-import-result--${resultTone}`}>
      <div className="qlhv-import-result__heading">
        <strong>{resultTitle}</strong>
        <span>{result.status}</span>
      </div>
      <p>{result.message}</p>
      {partial && (
        <div className="qlhv-import-full-sync-warning qlhv-import-partial-warning" role="status">
          {PARTIAL_SYNC_WARNING}
        </div>
      )}
      <div className="qlhv-import-result-grid">
        <SummaryRow label="Database snapshot" value={result.plan.sourceDatabaseName} />
        <SummaryRow label="Snapshot token" value={shortToken(result.plan.backupSnapshotToken)} />
      </div>
      <EntityCountsGrid
        hocVien={hocVien}
        khoaHoc={result.khoaHoc ?? null}
        giaoVien={result.giaoVien ?? null}
        khoaHocGiaoVien={result.khoaHocGiaoVien ?? null}
        mode="result"
      />
      <DomainResultsGrid results={result.domainResults} />
      <ImportPhotoCounts counts={result.photo ?? result.plan.photo} />
      {!successful && (
        <>
          <IssueList title="Điểm chặn toàn cục từ backend" items={result.plan.blockers} variant="blocker" />
          <IssueList title="Điểm chặn Học viên từ backend" items={result.plan.hocVienBlockers} variant="blocker" />
        </>
      )}
    </div>
  );
}

function DomainResultsGrid({ results }: { results: QlhvImportDomainResult[] }) {
  if (results.length === 0) {
    return null;
  }
  return (
    <section className="qlhv-import-domain-results" aria-label="Kết quả từng nhóm dữ liệu">
      <h3>Kết quả từng nhóm dữ liệu</h3>
      <div className="qlhv-import-domain-readiness">
        {results.map((result) => {
          const normalizedStatus = result.status.trim().toUpperCase();
          const tone = normalizedStatus === 'FAILED'
            ? 'blocked'
            : normalizedStatus.startsWith('SKIPPED') || normalizedStatus === 'PARTIAL_SUCCESS'
              ? 'warning'
              : 'ok';
          const label = IMPORT_DOMAIN_DEFINITIONS
            .find((candidate) => candidate.domain === result.domain)?.label ?? result.domain;
          return (
            <article key={result.domain} className={`qlhv-import-domain-readiness__item is-${tone}`}>
              <div>
                <strong>{label}</strong>
                <span>{formatDomainResultStatus(result.status)}</span>
              </div>
              {result.message && <p>{result.message}</p>}
            </article>
          );
        })}
      </div>
    </section>
  );
}

function ImportPhotoCounts({ counts }: { counts?: QlhvImportPhotoCounts }) {
  return (
    <section className="qlhv-import-entity-group" aria-label="Ảnh thẻ">
      <h3>Ảnh thẻ</h3>
      {!counts ? (
        <div className="qlhv-import-entity-group__unavailable">
          Backend chưa trả kế hoạch xử lý ảnh.
        </div>
      ) : (
        <MetricGrid items={[
          ['Tìm thấy', counts.found, 'ok'],
          ['Thiếu', counts.missing, counts.missing > 0 ? 'warning' : 'ok'],
          ['Đang chờ', counts.pending],
          ['Cần xử lý lại', counts.toReprocess, counts.toReprocess > 0 ? 'warning' : 'default'],
          ['Cần kiểm tra', counts.reviewRequired, counts.reviewRequired > 0 ? 'warning' : 'ok'],
        ]} />
      )}
    </section>
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
            <th>Actor</th>
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
              <td>{formatOperationType(row.operationType)}</td>
              <td>
                {row.actor
                  ?? (row.detailJson?.includes('SYSTEM_AUTO_SYNC')
                    ? 'SYSTEM_AUTO_SYNC'
                    : '—')}
              </td>
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
    'partial-success': 'Thành công một phần',
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
    ['Vai trò hiện tại', status.currentUserRole || 'Chưa xác định', status.currentUserRole === 'Admin' ? 'ok' : 'blocked'],
    ['Quyền Admin ghi', status.writeAuthorized ? 'Có' : 'Không', status.writeAuthorized ? 'ok' : 'blocked'],
    ['DryRun', status.dryRun ? 'Đang bật' : 'Đã tắt', status.dryRun ? 'blocked' : 'ok'],
    ['Quyền ghi dữ liệu', status.targetWritesEnabled ? 'Đang bật' : 'Đang tắt', status.targetWritesEnabled ? 'ok' : 'blocked'],
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
      <IssueList title="Lý do khóa làm mới BAK" items={status.refreshBlockers} variant="blocker" />
      <IssueList title="Lý do khóa full sync" items={status.syncBlockers} variant="blocker" />
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

function historyTone(status: string): 'ok' | 'warning' | 'busy' | 'failed' {
  const normalized = status.toLowerCase();
  if (normalized.includes('partial') || normalized.includes('skip') || normalized.includes('một phần')) return 'warning';
  if (normalized.includes('fail') || normalized.includes('error') || normalized.includes('lỗi')) return 'failed';
  if (normalized.includes('queue') || normalized.includes('running') || normalized.includes('đang')) return 'busy';
  return 'ok';
}

function formatDomainResultStatus(status: string): string {
  const normalized = status.trim().toUpperCase();
  if (normalized === 'SUCCESS') return 'Thành công';
  if (normalized === 'NO_OP') return 'Không có thay đổi';
  if (normalized === 'FAILED') return 'Thất bại';
  if (normalized.startsWith('SKIPPED')) return 'Tạm bỏ qua';
  return status;
}

function formatOperationType(type: QlhvOperationType): string {
  const labels: Record<QlhvOperationType, string> = {
    REFRESH_BACKUP: 'Refresh BAK',
    FULL_SYNC: 'Full sync',
    AUTO_SYNC: 'Auto Sync',
    PHOTO_PROCESSING: 'Xử lý ảnh',
  };
  return labels[type];
}

function toSafeClientMessage(error: unknown, fallback: string): string {
  if (error instanceof Error && error.message && error.name !== 'AbortError') return error.message;
  return fallback;
}
