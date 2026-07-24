import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import {
  getQlhvAutoSyncStatus,
  runQlhvAutoSync,
} from './api';
import type {
  QlhvAutoSyncSourceResult,
  QlhvAutoSyncStatus,
  QlhvOperationHistoryItem,
} from './types';

const POLL_INTERVAL_MS = 2_500;
const IDLE_POLL_INTERVAL_MS = 10_000;

export interface AutoSyncPanelProps {
  isAdmin: boolean;
  operationBlocker: string | null;
  operationHistory: QlhvOperationHistoryItem[];
  reloadToken: number;
  onAccepted: () => void | Promise<void>;
  onBusyChange?: (busy: boolean) => void;
  sessionStartRunId?: string | null;
  sessionStartRequestFailed?: boolean;
}

export default function AutoSyncPanel({
  isAdmin,
  operationBlocker,
  operationHistory,
  reloadToken,
  onAccepted,
  onBusyChange,
  sessionStartRunId = null,
  sessionStartRequestFailed = false,
}: AutoSyncPanelProps) {
  const [status, setStatus] = useState<QlhvAutoSyncStatus | null>(null);
  const [loading, setLoading] = useState(true);
  const [starting, setStarting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [completedSessionRunId, setCompletedSessionRunId] = useState<string | null>(null);
  const [sessionTerminalStatus, setSessionTerminalStatus] =
    useState<QlhvAutoSyncStatus | null>(null);
  const [manualRunId, setManualRunId] = useState<string | null>(null);
  const [statusQueryRunId, setStatusQueryRunId] = useState<string | null>(null);
  const wasRunningRef = useRef(false);
  const startingRef = useRef(false);
  const loadRequestIdRef = useRef(0);
  const notifiedTerminalRunRef = useRef<string | null>(null);
  const trackedSessionRunId = completedSessionRunId === sessionStartRunId
    ? null
    : sessionStartRunId;
  const trackedRunId = manualRunId ?? trackedSessionRunId;

  const load = useCallback(async () => {
    const requestedRunId = trackedRunId;
    const requestId = ++loadRequestIdRef.current;
    setLoading(true);
    try {
      const next = await getQlhvAutoSyncStatus(requestedRunId);
      if (requestId !== loadRequestIdRef.current) {
        return;
      }
      setStatus(next);
      setStatusQueryRunId(requestedRunId);
      setError(null);
    } catch (reason) {
      if (requestId !== loadRequestIdRef.current) {
        return;
      }
      setError(reason instanceof Error
        ? reason.message
        : 'Không thể tải trạng thái Auto Sync.');
    } finally {
      if (requestId === loadRequestIdRef.current) {
        setLoading(false);
      }
    }
  }, [trackedRunId]);

  useEffect(() => {
    void load();
  }, [load, reloadToken]);

  // A response is authoritative only for the runId used by that exact GET.
  // This prevents an old not-found/terminal response from completing a newly
  // accepted manual run before its first exact-run status arrives.
  const trackedStatus = statusQueryRunId === trackedRunId ? status : null;
  const running = trackedStatus?.state === 'queued' || trackedStatus?.state === 'running';

  useEffect(() => {
    if (!trackedRunId && wasRunningRef.current && !running) {
      void onAccepted();
    }
    wasRunningRef.current = running;
  }, [onAccepted, running, trackedRunId]);

  useEffect(() => {
    if (!trackedRunId || !trackedStatus ||
        trackedStatus.state === 'queued' ||
        trackedStatus.state === 'running' ||
        notifiedTerminalRunRef.current === trackedRunId) {
      return;
    }
    if (trackedStatus.found && trackedStatus.runId !== trackedRunId) {
      return;
    }
    if (!trackedStatus.found || trackedStatus.runId === trackedRunId) {
      notifiedTerminalRunRef.current = trackedRunId;
      if (manualRunId === trackedRunId) {
        setManualRunId(null);
      }
      if (trackedSessionRunId === trackedRunId) {
        setSessionTerminalStatus(trackedStatus);
        setCompletedSessionRunId(trackedSessionRunId);
      }
      void onAccepted();
    }
  }, [
    manualRunId,
    onAccepted,
    trackedStatus,
    trackedRunId,
    trackedSessionRunId,
  ]);

  const waitingForTrackedRun = !!trackedRunId
    && trackedStatus?.found !== false
    && (!trackedStatus || trackedStatus.runId !== trackedRunId);
  const shouldPoll = running || waitingForTrackedRun;
  const busy = starting || running || waitingForTrackedRun;

  useEffect(() => {
    onBusyChange?.(busy);
  }, [busy, onBusyChange]);

  useEffect(
    () => () => onBusyChange?.(false),
    [onBusyChange],
  );

  useEffect(() => {
    const timer = window.setInterval(
      () => void load(),
      shouldPoll ? POLL_INTERVAL_MS : IDLE_POLL_INTERVAL_MS,
    );
    return () => window.clearInterval(timer);
  }, [load, shouldPoll]);

  useEffect(() => {
    setSessionTerminalStatus(null);
  }, [sessionStartRunId]);

  useEffect(() => {
    if (completedSessionRunId && completedSessionRunId !== sessionStartRunId) {
      setCompletedSessionRunId(null);
      notifiedTerminalRunRef.current = null;
    }
  }, [completedSessionRunId, sessionStartRunId]);

  useEffect(() => {
    const handleFocus = () => void load();
    window.addEventListener('focus', handleFocus);
    return () => window.removeEventListener('focus', handleFocus);
  }, [load]);

  const disabledReason = getAutoSyncDisabledReason(
    isAdmin,
    trackedStatus,
    operationBlocker,
    loading,
    starting,
  );

  async function handleRun() {
    if (startingRef.current) {
      return;
    }
    if (disabledReason) {
      setError(disabledReason);
      return;
    }
    startingRef.current = true;
    setStarting(true);
    setError(null);
    setNotice(null);
    try {
      const result = await runQlhvAutoSync();
      setNotice(result.message || 'Đã tiếp nhận yêu cầu Auto Sync.');
      if (result.runId) {
        setManualRunId(result.runId);
      } else {
        await Promise.all([load(), onAccepted()]);
      }
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Không thể chạy Auto Sync.');
    } finally {
      startingRef.current = false;
      setStarting(false);
    }
  }

  const autoHistory = useMemo(
    () => operationHistory.filter((row) =>
      row.operationType === 'AUTO_SYNC'
      || row.actor === 'SYSTEM_AUTO_SYNC'
      || row.actor === 'SYSTEM_SESSION_START'
      || row.actor === 'SYSTEM_APP_OPEN'
      || row.detailJson?.includes('SYSTEM_AUTO_SYNC')
      || row.detailJson?.includes('SYSTEM_SESSION_START')
      || row.detailJson?.includes('SYSTEM_APP_OPEN')
      || row.detailJson?.includes('AUTO_SYNC')),
    [operationHistory],
  );
  const displayedSessionStatus = trackedSessionRunId && !manualRunId
    ? trackedStatus
    : sessionTerminalStatus;

  return (
    <section className="panel qlhv-auto-sync" aria-label="Auto Sync">
      <div className="qlhv-import-section-heading">
        <strong>Auto Sync</strong>
        <span>Chạy tuần tự OTO rồi MOTO trên máy chủ</span>
      </div>

      {loading && !trackedStatus && <div className="qlhv-import-empty">Đang tải trạng thái Auto Sync...</div>}
      {sessionStartRequestFailed && (
        <div className="qlhv-session-sync-status is-failed" role="alert">
          Không thể tiếp nhận phiên cập nhật từ icon Desktop. Ứng dụng vẫn mở với dữ liệu
          commit gần nhất; xem lý do khóa bên dưới.
        </div>
      )}
      {sessionStartRunId && displayedSessionStatus && (
        <SessionStartStatus
          expectedRunId={sessionStartRunId}
          status={displayedSessionStatus}
        />
      )}
      {trackedStatus && (
        <>
          <div className="qlhv-auto-sync__summary">
            <AutoSyncFact
              label="Tự chạy khi server khởi động"
              value={trackedStatus.enabled && trackedStatus.runOnServerStartup ? 'Đang bật' : 'Đang tắt'}
              tone={trackedStatus.enabled && trackedStatus.runOnServerStartup ? 'ok' : 'warning'}
            />
            <AutoSyncFact
              label="Trạng thái"
              value={formatAutoSyncState(trackedStatus.state)}
              tone={running
                ? 'busy'
                : trackedStatus.state === 'failed'
                  ? 'failed'
                  : trackedStatus.state === 'partial-success'
                    || trackedStatus.state === 'partial-failed'
                    ? 'warning'
                    : 'ok'}
            />
            <AutoSyncFact label="Nguồn đang xử lý" value={trackedStatus.currentSourceType ?? 'Không có'} />
            <AutoSyncFact label="Bước hiện tại" value={formatAutoSyncStage(trackedStatus.currentStage)} tone={running ? 'busy' : 'default'} />
            <AutoSyncFact label="Actor" value={trackedStatus.actor ?? 'Chưa có'} />
            <AutoSyncFact label="Lần chạy gần nhất" value={formatDate(trackedStatus.startedAtUtc)} />
            <AutoSyncFact label="Sync thành công gần nhất" value={formatDate(trackedStatus.lastSuccessfulSyncUtc)} />
          </div>
          <div className="qlhv-auto-sync__sources">
            <AutoSyncSourceCard title="Ô tô" result={trackedStatus.oto} />
            <AutoSyncSourceCard title="Mô tô" result={trackedStatus.moto} />
          </div>
          {trackedStatus.lastError && <div className="qlhv-import-error" role="alert">{trackedStatus.lastError}</div>}
        </>
      )}

      {notice && <div className="qlhv-import-success" role="status">{notice}</div>}
      {error && <div className="qlhv-import-error" role="alert">{error}</div>}

      <div className="qlhv-auto-sync__actions">
        <button
          type="button"
          className="btn btn--ghost"
          onClick={() => void load()}
          disabled={loading}
        >
          {loading ? 'Đang tải...' : 'Tải lại trạng thái'}
        </button>
        <button
          type="button"
          className="btn btn--primary"
          onClick={() => void handleRun()}
          disabled={disabledReason !== null}
          title={disabledReason ?? undefined}
          aria-busy={starting || running}
        >
          {starting || running ? 'Auto Sync đang chạy...' : 'Chạy Auto Sync ngay'}
        </button>
      </div>
      {disabledReason && (
        <p className="qlhv-auto-sync__disabled-reason" role="status">
          <strong>Chưa thể chạy:</strong> {disabledReason}
        </p>
      )}

      <div className="qlhv-auto-sync__history">
        <strong>Lịch sử Auto Sync hệ thống</strong>
        {autoHistory.length === 0 ? (
          <span>Chưa có bản ghi Auto Sync trong lịch sử hiện tại.</span>
        ) : (
          <ul>
            {autoHistory.slice(0, 6).map((row) => (
              <li key={row.operationId}>
                <span>{formatDate(row.startedAtUtc)}</span>
                <strong>{row.sourceType}</strong>
                <span>{row.status}</span>
                {row.errorMessage && <span className="is-error">{row.errorMessage}</span>}
              </li>
            ))}
          </ul>
        )}
      </div>
    </section>
  );
}

function SessionStartStatus({
  expectedRunId,
  status,
}: {
  expectedRunId: string;
  status: QlhvAutoSyncStatus;
}) {
  if (!status.found) {
    return (
      <div className="qlhv-session-sync-status is-failed" role="alert">
        <strong>Không tìm thấy phiên cập nhật đã yêu cầu.</strong>
        <span> Ứng dụng vẫn dùng dữ liệu commit gần nhất; hãy xem lịch sử vận hành để kiểm tra.</span>
      </div>
    );
  }

  if (status.runId !== expectedRunId) {
    return (
      <div className="qlhv-session-sync-status is-busy" role="status">
        Đang kết nối máy chủ và chờ phiên dữ liệu {expectedRunId.slice(0, 8)}...
      </div>
    );
  }

  if (status.state === 'queued' || status.state === 'running') {
    return (
      <div className="qlhv-session-sync-status is-busy" role="status" aria-live="polite">
        <strong>{formatAutoSyncStage(status.currentStage)}</strong>
        <span> Dữ liệu sẽ tự tải lại khi hoàn tất; máy chủ không bị restart.</span>
      </div>
    );
  }

  if (status.state === 'succeeded') {
    return (
      <div className="qlhv-session-sync-status is-success" role="status">
        <strong>Hoàn tất.</strong>
        <span> Dữ liệu mới đã được tải lại mà không restart máy chủ.</span>
      </div>
    );
  }

  if (status.state === 'partial-success') {
    return (
      <div className="qlhv-session-sync-status is-warning" role="status">
        <strong>Hoàn tất một phần.</strong>
        <span>
          {' '}Dữ liệu đã đồng bộ thành công vẫn được tải lại; module chưa sẵn sàng được bỏ qua và không bị xóa.
        </span>
      </div>
    );
  }

  return (
    <div className="qlhv-session-sync-status is-failed" role="alert">
      <strong>Phiên cập nhật có lỗi.</strong>
      <span>
        {' '}Ứng dụng vẫn dùng dữ liệu commit gần nhất.
        {status.lastSuccessfulSyncUtc
          ? ` Lần thành công gần nhất: ${formatDate(status.lastSuccessfulSyncUtc)}.`
          : ''}
      </span>
    </div>
  );
}

function getAutoSyncDisabledReason(
  isAdmin: boolean,
  status: QlhvAutoSyncStatus | null,
  operationBlocker: string | null,
  loading: boolean,
  starting: boolean,
): string | null {
  if (!isAdmin) return 'Bạn không có quyền thực hiện: cần vai trò Admin.';
  if (loading || !status) return 'Chưa đọc được trạng thái Auto Sync.';
  if (!status.enabled) return 'Auto Sync đang tắt trong cấu hình Production Local.';
  if (status.state === 'queued' || status.state === 'running' || starting) {
    return 'Auto Sync đang chạy; không thể gửi yêu cầu lần hai.';
  }
  if (operationBlocker) return operationBlocker;
  return null;
}

function AutoSyncSourceCard({
  title,
  result,
}: {
  title: string;
  result: QlhvAutoSyncSourceResult | null;
}) {
  return (
    <article>
      <div><strong>{title}</strong><span>{result?.sourceType ?? '—'}</span></div>
      <dl>
        <dt>Trạng thái</dt><dd>{result?.status ?? 'Chưa chạy'}</dd>
        <dt>Bắt đầu</dt><dd>{formatDate(result?.startedAtUtc)}</dd>
        <dt>Hoàn tất</dt><dd>{formatDate(result?.completedAtUtc)}</dd>
      </dl>
      {result?.message && <p>{result.message}</p>}
    </article>
  );
}

function AutoSyncFact({
  label,
  value,
  tone = 'default',
}: {
  label: string;
  value: string;
  tone?: 'default' | 'ok' | 'warning' | 'busy' | 'failed';
}) {
  return <div className={`is-${tone}`}><span>{label}</span><strong>{value}</strong></div>;
}

function formatAutoSyncState(state: QlhvAutoSyncStatus['state']): string {
  const labels: Record<QlhvAutoSyncStatus['state'], string> = {
    'not-found': 'Không tìm thấy',
    disabled: 'Đang tắt',
    idle: 'Sẵn sàng',
    queued: 'Đang chờ',
    running: 'Đang chạy',
    succeeded: 'Thành công',
    'partial-success': 'Thành công một phần',
    'partial-failed': 'Có nguồn thất bại',
    failed: 'Thất bại',
  };
  return labels[state];
}

function formatAutoSyncStage(stage: string | null | undefined): string {
  const labels: Record<string, string> = {
    CONNECTING: 'Đang kết nối máy chủ',
    REFRESH_OTO: 'Đang làm mới dữ liệu Ô tô',
    SYNC_OTO: 'Đang đồng bộ dữ liệu Ô tô',
    REFRESH_MOTO: 'Đang làm mới dữ liệu Mô tô',
    SYNC_MOTO: 'Đang đồng bộ dữ liệu Mô tô',
    LOADING_DATA: 'Đang tải dữ liệu mới',
    COMPLETED: 'Hoàn tất',
    FAILED: 'Cập nhật có lỗi',
  };
  return stage ? labels[stage] ?? stage : 'Chưa có';
}

function formatDate(value: string | null | undefined): string {
  if (!value) return 'Chưa có';
  const date = new Date(value);
  return Number.isNaN(date.getTime())
    ? value
    : date.toLocaleString('vi-VN');
}
