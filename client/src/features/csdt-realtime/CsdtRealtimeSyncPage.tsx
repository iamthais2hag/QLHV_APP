import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { useAuth } from '../auth/AuthContext';
import {
  getCsdtRealtimeHistory,
  getCsdtRealtimeStreams,
  getCsdtRealtimeTombstones,
  retryCsdtRealtimeStream,
  runCsdtRealtimeBaseline,
  setCsdtRealtimeEnabled,
} from './api';
import {
  CSDT_REALTIME_PRESENTATION,
  CSDT_REALTIME_STREAMS,
  formatDateTime,
  formatNumber,
  formatRealtimeState,
  hasExpectedStreamMapping,
  shouldPollRealtimeFast,
} from './logic';
import RealtimeStreamCard from './RealtimeStreamCard';
import ReverseSyncPanel from './ReverseSyncPanel';
import type {
  CsdtRealtimeActionResult,
  CsdtRealtimeHistoryItem,
  CsdtRealtimeStreamCode,
  CsdtRealtimeStreamStatus,
  CsdtRealtimeTombstone,
} from './types';

const ACTIVE_POLL_INTERVAL_MS = 2_500;
const IDLE_POLL_INTERVAL_MS = 10_000;

type StreamAction = 'enabled' | 'baseline' | 'retry';

export default function CsdtRealtimeSyncPage() {
  const { user } = useAuth();
  const isAdmin = user?.role === 'Admin';
  const [streams, setStreams] = useState<CsdtRealtimeStreamStatus[]>([]);
  const [selectedStreamCode, setSelectedStreamCode] =
    useState<CsdtRealtimeStreamCode>('OTO_V2_TO_V1');
  const [observedAtUtc, setObservedAtUtc] = useState<string | null>(null);
  const [initialLoading, setInitialLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [statusError, setStatusError] = useState<string | null>(null);
  const [history, setHistory] = useState<CsdtRealtimeHistoryItem[]>([]);
  const [tombstones, setTombstones] = useState<CsdtRealtimeTombstone[]>([]);
  const [detailsLoading, setDetailsLoading] = useState(false);
  const [detailsError, setDetailsError] = useState<string | null>(null);
  const [pendingActions, setPendingActions] =
    useState<Partial<Record<CsdtRealtimeStreamCode, StreamAction>>>({});
  const [notice, setNotice] = useState<string | null>(null);
  const [reloadToken, setReloadToken] = useState(0);
  const statusRequestIdRef = useRef(0);
  const detailsRequestIdRef = useRef(0);
  const actionKeysRef = useRef(new Set<string>());

  const selectedStatus = streams.find(
    (stream) => stream.streamCode === selectedStreamCode,
  ) ?? null;

  const refreshNow = useCallback(() => {
    setReloadToken((current) => current + 1);
  }, []);

  useEffect(() => {
    let stopped = false;
    let timer: number | undefined;
    let controller: AbortController | null = null;

    async function poll() {
      const requestId = ++statusRequestIdRef.current;
      controller?.abort();
      controller = new AbortController();
      setRefreshing(true);
      try {
        const response = await getCsdtRealtimeStreams(controller.signal);
        if (stopped || requestId !== statusRequestIdRef.current) {
          return;
        }
        setStreams(response.streams);
        setObservedAtUtc(response.observedAtUtc);
        setStatusError(null);
        setInitialLoading(false);
        timer = window.setTimeout(
          () => void poll(),
          shouldPollRealtimeFast(response.streams)
            ? ACTIVE_POLL_INTERVAL_MS
            : IDLE_POLL_INTERVAL_MS,
        );
      } catch (reason) {
        if (stopped || (reason instanceof DOMException && reason.name === 'AbortError')) {
          return;
        }
        setStatusError(
          reason instanceof Error
            ? reason.message
            : 'Không thể tải trạng thái đồng bộ realtime.',
        );
        setInitialLoading(false);
        timer = window.setTimeout(() => void poll(), IDLE_POLL_INTERVAL_MS);
      } finally {
        if (!stopped && requestId === statusRequestIdRef.current) {
          setRefreshing(false);
        }
      }
    }

    void poll();
    const handleFocus = () => {
      if (timer !== undefined) {
        window.clearTimeout(timer);
      }
      void poll();
    };
    window.addEventListener('focus', handleFocus);

    return () => {
      stopped = true;
      controller?.abort();
      if (timer !== undefined) {
        window.clearTimeout(timer);
      }
      window.removeEventListener('focus', handleFocus);
    };
  }, [reloadToken]);

  useEffect(() => {
    const requestId = ++detailsRequestIdRef.current;
    const controller = new AbortController();
    setHistory([]);
    setTombstones([]);
    setDetailsLoading(true);
    setDetailsError(null);
    Promise.all([
      getCsdtRealtimeHistory(selectedStreamCode, 50, controller.signal),
      getCsdtRealtimeTombstones(selectedStreamCode, 50, controller.signal),
    ])
      .then(([nextHistory, nextTombstones]) => {
        if (requestId !== detailsRequestIdRef.current) {
          return;
        }
        setHistory(nextHistory);
        setTombstones(nextTombstones);
      })
      .catch((reason) => {
        if (requestId !== detailsRequestIdRef.current
          || (reason instanceof DOMException && reason.name === 'AbortError')) {
          return;
        }
        setHistory([]);
        setTombstones([]);
        setDetailsError(
          reason instanceof Error
            ? reason.message
            : 'Không thể tải lịch sử stream.',
        );
      })
      .finally(() => {
        if (requestId === detailsRequestIdRef.current) {
          setDetailsLoading(false);
        }
      });
    return () => controller.abort();
  }, [reloadToken, selectedStreamCode, selectedStatus?.lastCompletedAtUtc]);

  const statusByCode = useMemo(
    () => new Map(streams.map((stream) => [stream.streamCode, stream])),
    [streams],
  );

  async function runStreamAction(
    streamCode: CsdtRealtimeStreamCode,
    action: StreamAction,
  ) {
    const status = statusByCode.get(streamCode);
    const actionKey = `${streamCode}:${action}`;
    if (!status || !isAdmin || actionKeysRef.current.has(actionKey)) {
      return;
    }
    actionKeysRef.current.add(actionKey);
    setPendingActions((current) => ({ ...current, [streamCode]: action }));
    setStatusError(null);
    setNotice(null);
    try {
      let result: CsdtRealtimeActionResult;
      if (action === 'enabled') {
        result = await setCsdtRealtimeEnabled(streamCode, {
          enabled: !status.enabled,
          expectedStateToken: status.stateToken,
        });
      } else if (action === 'baseline') {
        result = await runCsdtRealtimeBaseline(streamCode, {
          expectedStateToken: status.stateToken,
        });
      } else {
        result = await retryCsdtRealtimeStream(streamCode, {
          expectedStateToken: status.stateToken,
        });
      }
      setNotice(result.message || 'Máy chủ đã tiếp nhận thao tác.');
      refreshNow();
    } catch (reason) {
      setStatusError(
        reason instanceof Error ? reason.message : 'Không thể thực hiện thao tác stream.',
      );
    } finally {
      actionKeysRef.current.delete(actionKey);
      setPendingActions((current) => {
        const next = { ...current };
        delete next[streamCode];
        return next;
      });
    }
  }

  return (
    <section className="csdt-realtime-page">
      <header className="panel csdt-realtime-hero">
        <div>
          <span className="csdt-realtime-eyebrow">SQL Server Change Tracking · QLHV Worker</span>
          <h2>Đồng bộ dữ liệu CSĐT V1 ↔ V2</h2>
          <p>
            V2 tự động cập nhật V1 gần realtime. Chiều V1 → V2 luôn cần plan read-only
            và quyền Admin trước khi ghi.
          </p>
        </div>
        <div className="csdt-realtime-hero__actions">
          <span>
            Quan sát lúc <strong>{formatDateTime(observedAtUtc)}</strong>
          </span>
          <button
            type="button"
            className="btn btn--ghost"
            onClick={refreshNow}
            disabled={refreshing}
            aria-busy={refreshing}
          >
            {refreshing ? 'Đang tải...' : 'Tải lại trạng thái'}
          </button>
        </div>
      </header>

      {!isAdmin && (
        <div className="csdt-realtime-readonly-banner" role="status">
          Bạn đang ở chế độ chỉ xem. Có thể xem trạng thái, lịch sử và plan V1 → V2;
          các thao tác ghi chỉ dành cho Admin.
        </div>
      )}
      {statusError && <div className="csdt-realtime-error" role="alert">{statusError}</div>}
      {notice && <div className="csdt-realtime-success" role="status">{notice}</div>}

      {initialLoading ? (
        <div className="panel csdt-realtime-empty" aria-busy="true">
          Đang tải hai stream Ô tô và Mô tô...
        </div>
      ) : (
        <div className="csdt-realtime-stream-grid">
          {CSDT_REALTIME_STREAMS.map((streamCode) => {
            const status = statusByCode.get(streamCode);
            if (!status) {
              const presentation = CSDT_REALTIME_PRESENTATION[streamCode];
              return (
                <article className="panel csdt-realtime-stream is-missing" key={streamCode}>
                  <strong>{presentation.title}</strong>
                  <p>Máy chủ chưa trả trạng thái stream {streamCode}.</p>
                </article>
              );
            }
            return (
              <RealtimeStreamCard
                key={streamCode}
                status={status}
                selected={selectedStreamCode === streamCode}
                isAdmin={isAdmin}
                pendingAction={pendingActions[streamCode] ?? null}
                onSelect={() => setSelectedStreamCode(streamCode)}
                onToggleEnabled={() => void runStreamAction(streamCode, 'enabled')}
                onBaseline={() => void runStreamAction(streamCode, 'baseline')}
                onRetry={() => void runStreamAction(streamCode, 'retry')}
              />
            );
          })}
        </div>
      )}

      {selectedStatus && (
        <>
          {!hasExpectedStreamMapping(selectedStatus) && (
            <div className="csdt-realtime-error" role="alert">
              Mapping stream không khớp allowlist cố định. Mọi thao tác ghi đã bị khóa.
            </div>
          )}
          <StreamDetails
            status={selectedStatus}
            history={history}
            tombstones={tombstones}
            loading={detailsLoading}
            error={detailsError}
            onReload={refreshNow}
          />
        </>
      )}

      <ReverseSyncPanel isAdmin={isAdmin} onOperationAccepted={refreshNow} />
    </section>
  );
}

function StreamDetails({
  status,
  history,
  tombstones,
  loading,
  error,
  onReload,
}: {
  status: CsdtRealtimeStreamStatus;
  history: CsdtRealtimeHistoryItem[];
  tombstones: CsdtRealtimeTombstone[];
  loading: boolean;
  error: string | null;
  onReload: () => void;
}) {
  const presentation = CSDT_REALTIME_PRESENTATION[status.streamCode];
  return (
    <section className="panel csdt-realtime-details">
      <div className="csdt-realtime-section-heading">
        <div>
          <h3>Chi tiết stream {presentation.title}</h3>
          <p>{status.streamCode} · {formatRealtimeState(status.state)}</p>
        </div>
        <button
          type="button"
          className="btn btn--ghost"
          onClick={onReload}
          disabled={loading}
        >
          Tải lại chi tiết
        </button>
      </div>

      {error && <div className="csdt-realtime-error" role="alert">{error}</div>}
      {loading && history.length === 0 && (
        <div className="csdt-realtime-empty">Đang tải domain và lịch sử...</div>
      )}

      {status.domains.length > 0 && (
        <>
          <h4>Thống kê theo domain</h4>
          <div className="table-wrap">
            <table className="table csdt-realtime-table">
              <thead>
                <tr>
                  <th>Domain</th>
                  <th>Trạng thái</th>
                  <th>Nguồn</th>
                  <th>Đích</th>
                  <th>Insert</th>
                  <th>Update</th>
                  <th>Skip</th>
                  <th>Lỗi</th>
                </tr>
              </thead>
              <tbody>
                {status.domains.map((domain) => (
                  <tr key={domain.domain}>
                    <td>{domain.domain}</td>
                    <td>{formatRealtimeState(domain.state)}</td>
                    <td>{formatNumber(domain.sourceRows)}</td>
                    <td>{formatNumber(domain.targetRows)}</td>
                    <td>{formatNumber(domain.insertedRows)}</td>
                    <td>{formatNumber(domain.updatedRows)}</td>
                    <td>{formatNumber(domain.skippedRows)}</td>
                    <td title={domain.lastError ?? undefined}>{formatNumber(domain.errorRows)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </>
      )}

      <h4>Lịch sử gần nhất</h4>
      {history.length === 0 && !loading ? (
        <div className="csdt-realtime-empty">Chưa có lần chạy nào.</div>
      ) : (
        <div className="table-wrap">
          <table className="table csdt-realtime-table">
            <thead>
              <tr>
                <th>Bắt đầu</th>
                <th>Loại</th>
                <th>Trạng thái</th>
                <th>Domain</th>
                <th>Version</th>
                <th>Insert</th>
                <th>Update</th>
                <th>Skip</th>
                <th>Lỗi</th>
              </tr>
            </thead>
            <tbody>
              {history.map((row) => (
                <tr key={row.runId}>
                  <td>{formatDateTime(row.startedAtUtc)}</td>
                  <td>{row.runType}</td>
                  <td>{formatRealtimeState(row.status)}</td>
                  <td>
                    {row.domains.length === 0
                      ? '-'
                      : row.domains
                          .map((domain) =>
                            `${domain.domain}: ${formatRealtimeState(domain.state)} (#${domain.attemptCount})`)
                          .join('; ')}
                  </td>
                  <td>{formatNumber(row.fromVersion)} → {formatNumber(row.toVersion)}</td>
                  <td>{formatNumber(row.insertedRows)}</td>
                  <td>{formatNumber(row.updatedRows)}</td>
                  <td>{formatNumber(row.skippedRows)}</td>
                  <td title={row.errorMessage ?? undefined}>{formatNumber(row.errorRows)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      <h4>Delete tombstone</h4>
      <p className="csdt-realtime-help">
        Delete từ V2 chỉ được ghi nhận để Admin xem xét; hệ thống không tự xóa dữ liệu V1.
      </p>
      {tombstones.length === 0 && !loading ? (
        <div className="csdt-realtime-empty">Không có tombstone trong danh sách gần nhất.</div>
      ) : (
        <div className="table-wrap">
          <table className="table csdt-realtime-table">
            <thead>
              <tr>
                <th>Phát hiện</th>
                <th>Domain</th>
                <th>Khóa nguồn</th>
                <th>Version</th>
                <th>Trạng thái</th>
              </tr>
            </thead>
            <tbody>
              {tombstones.map((row) => (
                <tr key={row.id}>
                  <td>{formatDateTime(row.detectedAtUtc)}</td>
                  <td>{row.domain}</td>
                  <td><code>{row.sourceKey}</code></td>
                  <td>{formatNumber(row.changeVersion)}</td>
                  <td title={row.message ?? undefined}>{row.status}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </section>
  );
}
