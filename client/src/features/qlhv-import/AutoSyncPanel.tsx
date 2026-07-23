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

export interface AutoSyncPanelProps {
  isAdmin: boolean;
  operationBlocker: string | null;
  operationHistory: QlhvOperationHistoryItem[];
  reloadToken: number;
  onAccepted: () => void | Promise<void>;
  sessionStartRunId?: string | null;
  sessionStartRequestFailed?: boolean;
}

export default function AutoSyncPanel({
  isAdmin,
  operationBlocker,
  operationHistory,
  reloadToken,
  onAccepted,
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
  const wasRunningRef = useRef(false);
  const startingRef = useRef(false);
  const loadInFlightRef = useRef(false);
  const notifiedTerminalRunRef = useRef<string | null>(null);
  const trackedSessionRunId = completedSessionRunId === sessionStartRunId
    ? null
    : sessionStartRunId;

  const load = useCallback(async () => {
    if (loadInFlightRef.current) {
      return;
    }
    loadInFlightRef.current = true;
    setLoading(true);
    try {
      setStatus(await getQlhvAutoSyncStatus(trackedSessionRunId));
      setError(null);
    } catch (reason) {
      setError(reason instanceof Error
        ? reason.message
        : 'Không thể tải trạng thái Auto Sync.');
    } finally {
      loadInFlightRef.current = false;
      setLoading(false);
    }
  }, [trackedSessionRunId]);

  useEffect(() => {
    void load();
  }, [load, reloadToken]);

  const running = status?.state === 'queued' || status?.state === 'running';

  useEffect(() => {
    if (wasRunningRef.current && !running) {
      if (trackedSessionRunId && status?.runId === trackedSessionRunId) {
        notifiedTerminalRunRef.current = trackedSessionRunId;
        setSessionTerminalStatus(status);
        setCompletedSessionRunId(trackedSessionRunId);
      }
      void onAccepted();
    }
    wasRunningRef.current = running;
  }, [onAccepted, running, status?.runId, trackedSessionRunId]);

  useEffect(() => {
    if (!trackedSessionRunId || !status ||
        status.state === 'queued' ||
        status.state === 'running' ||
        notifiedTerminalRunRef.current === trackedSessionRunId) {
      return;
    }
    if (status.found && status.runId !== trackedSessionRunId) {
      return;
    }
    if (!status.found || status.runId === trackedSessionRunId) {
      notifiedTerminalRunRef.current = trackedSessionRunId;
      setSessionTerminalStatus(status);
      setCompletedSessionRunId(trackedSessionRunId);
      void onAccepted();
    }
  }, [onAccepted, status, trackedSessionRunId]);

  const waitingForExpectedSession = !!trackedSessionRunId
    && status?.found !== false
    && (!status || status.runId !== trackedSessionRunId);
  const shouldPoll = running || waitingForExpectedSession;

  useEffect(() => {
    if (!shouldPoll) {
      return undefined;
    }
    const timer = window.setInterval(() => void load(), POLL_INTERVAL_MS);
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
    status,
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
      await Promise.all([load(), onAccepted()]);
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
      || row.detailJson?.includes('SYSTEM_AUTO_SYNC')
      || row.detailJson?.includes('SYSTEM_SESSION_START')
      || row.detailJson?.includes('AUTO_SYNC')),
    [operationHistory],
  );
  const displayedSessionStatus = trackedSessionRunId
    ? status
    : sessionTerminalStatus;

  return (
    <section className="panel qlhv-auto-sync" aria-label="Auto Sync">
      <div className="qlhv-import-section-heading">
        <strong>Auto Sync</strong>
        <span>Chạy tuần tự OTO rồi MOTO trên máy chủ</span>
      </div>

      {loading && !status && <div className="qlhv-import-empty">Đang tải trạng thái Auto Sync...</div>}
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
      {status && (
        <>
          <div className="qlhv-auto-sync__summary">
            <AutoSyncFact
              label="Tự chạy khi server khởi động"
              value={status.enabled && status.runOnServerStartup ? 'Đang bật' : 'Đang tắt'}
              tone={status.enabled && status.runOnServerStartup ? 'ok' : 'warning'}
            />
            <AutoSyncFact label="Trạng thái" value={formatAutoSyncState(status.state)} tone={running ? 'busy' : status.state === 'failed' || status.state === 'partial-failed' ? 'failed' : 'ok'} />
            <AutoSyncFact label="Nguồn đang xử lý" value={status.currentSourceType ?? 'Không có'} />
            <AutoSyncFact label="Bước hiện tại" value={formatAutoSyncStage(status.currentStage)} tone={running ? 'busy' : 'default'} />
            <AutoSyncFact label="Actor" value={status.actor ?? 'Chưa có'} />
            <AutoSyncFact label="Lần chạy gần nhất" value={formatDate(status.startedAtUtc)} />
            <AutoSyncFact label="Sync thành công gần nhất" value={formatDate(status.lastSuccessfulSyncUtc)} />
          </div>
          <div className="qlhv-auto-sync__sources">
            <AutoSyncSourceCard title="Ô tô" result={status.oto} />
            <AutoSyncSourceCard title="Mô tô" result={status.moto} />
          </div>
          {status.lastError && <div className="qlhv-import-error" role="alert">{status.lastError}</div>}
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
    'partial-failed': 'Thành công một phần',
    failed: 'Có lỗi',
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
