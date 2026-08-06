import { useCallback, useEffect, useRef, useState } from 'react';
import { FRONTEND_BUILD_ID } from '../../buildIdentity';
import { getQlhvAutoSyncStatus, runQlhvAutoSync } from './api';
import type { QlhvAutoSyncStatus } from './types';

const UI_REFRESH_INTERVAL_MS = 10_000;

export interface AutoSyncPanelProps {
  isAdmin: boolean;
  operationBlocker: string | null;
  reloadToken: number;
  onAccepted: () => void | Promise<void>;
  onBusyChange?: (busy: boolean) => void;
}

export default function AutoSyncPanel({
  isAdmin,
  operationBlocker,
  reloadToken,
  onAccepted,
  onBusyChange,
}: AutoSyncPanelProps) {
  const [status, setStatus] = useState<QlhvAutoSyncStatus | null>(null);
  const [loading, setLoading] = useState(true);
  const [starting, setStarting] = useState(false);
  const [lastRefresh, setLastRefresh] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const requestId = useRef(0);

  const load = useCallback(async () => {
    const id = ++requestId.current;
    setLoading(true);
    try {
      const next = await getQlhvAutoSyncStatus();
      if (id !== requestId.current) return;
      setStatus(next);
      setLastRefresh(new Date().toISOString());
      setError(null);
    } catch (reason) {
      if (id === requestId.current) {
        setError(reason instanceof Error ? reason.message : 'Không thể đọc trạng thái vận hành.');
      }
    } finally {
      if (id === requestId.current) setLoading(false);
    }
  }, []);

  useEffect(() => { void load(); }, [load, reloadToken]);
  useEffect(() => {
    const timer = window.setInterval(() => void load(), UI_REFRESH_INTERVAL_MS);
    return () => window.clearInterval(timer);
  }, [load]);

  const active = status?.autoSyncRuntime.isRunActive === true;
  useEffect(() => { onBusyChange?.(active || starting); }, [active, onBusyChange, starting]);
  useEffect(() => () => onBusyChange?.(false), [onBusyChange]);

  const disabledReason = !isAdmin
    ? 'Cần quyền Quản trị viên.'
    : loading || !status
      ? 'Chưa có trạng thái backend mới nhất.'
      : !status.configuration.manualRunAllowed
        ? status.configuration.manualRunReason
        : operationBlocker ?? (starting ? 'Đang gửi yêu cầu.' : null);

  async function handleRun() {
    if (disabledReason || starting) return;
    setStarting(true);
    setError(null);
    setNotice(null);
    try {
      const result = await runQlhvAutoSync();
      setNotice(result.message || 'Đã tiếp nhận Auto Sync dự phòng.');
      await Promise.all([load(), onAccepted()]);
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Không thể chạy Auto Sync dự phòng.');
    } finally {
      setStarting(false);
    }
  }

  const frontendMatches = status?.runtime.frontendBuildId === FRONTEND_BUILD_ID;
  const workerDisplay = status
    && !status.realtime.cycleActive
    && status.realtime.profiles.length > 0
    && status.realtime.profiles.every((profile) =>
      !profile.enabled || profile.health === 'HEALTHY_NO_CHANGE')
    ? 'HEALTHY_NO_CHANGE'
    : status?.realtime.overallHealth ?? 'Chưa có';
  const manualRunDisplay = status?.configuration.manualRunDecision
    === 'AUTOSYNC_BLOCKED_BY_REALTIME_PRIMARY_WRITER'
    ? 'Bị khóa bởi Realtime'
    : status?.configuration.manualRunAllowed
      ? 'Sẵn sàng'
      : 'Bị khóa';
  return (
    <section className="panel qlhv-auto-sync" aria-label="Trạng thái đồng bộ dữ liệu CSDT">
      <div className="qlhv-import-section-heading">
        <strong>Vận hành đồng bộ dữ liệu CSDT</strong>
        <span>Realtime là đường chính; Auto Sync Live → BAK → QLHV_APP chỉ là dự phòng.</span>
      </div>

      {status && (
        <>
          <section aria-label="Realtime primary">
            <div className="qlhv-import-section-heading">
              <strong>A. Realtime trực tiếp — đường chính</strong>
              <span>CSDL_OTO / CSDL_MOTO → QLHV_APP</span>
            </div>
            <div className="qlhv-auto-sync__summary">
              <Fact label="Windows Service" value={status.realtime.serviceState === 'RUNNING' ? 'Đang chạy' : status.realtime.serviceState} tone={ok(status.realtime.serviceState === 'RUNNING')} />
              <Fact label="Worker" value={workerDisplay} tone={ok(workerDisplay === 'HEALTHY_NO_CHANGE')} />
              <Fact label="Worker process" value={status.realtime.processState === 'RUNNING' ? 'Đang chạy' : status.realtime.processState} tone={ok(status.realtime.processState === 'RUNNING')} />
              <Fact label="Writer chính" value={status.realtime.writerEnabled ? 'QLHV Realtime' : 'OFF'} tone={ok(status.realtime.writerEnabled)} />
              <Fact label="Mutex" value={status.realtime.mutexHeld ? 'Đang bảo vệ' : 'Đã nhả'} tone={ok(status.realtime.mutexHeld)} />
              <Fact label="Chu kỳ hiện tại" value={status.realtime.cycleActive ? status.realtime.currentProfile ?? 'Đang chạy' : 'Không có'} />
              <Fact label="Heartbeat" value={formatDate(status.realtime.lastHeartbeatUtc)} />
              <Fact label="Lỗi gần nhất" value={status.realtime.lastFailureCode ?? 'Không có'} />
            </div>
            <div className="qlhv-auto-sync__sources">
              {status.realtime.profiles.map((profile) => (
                <article key={profile.profileCode}>
                  <div><strong>{profile.profileCode}</strong><span>{profile.enabled ? 'Bật' : 'Tắt'}</span></div>
                  <dl>
                    <dt>Sức khỏe</dt><dd>{profile.health}</dd>
                    <dt>Checkpoint</dt><dd>{profile.checkpointVersion}</dd>
                    <dt>Chu kỳ gần nhất</dt><dd>{formatDate(profile.lastCycleCompletedAtUtc)}</dd>
                  </dl>
                </article>
              ))}
            </div>
          </section>

          <section aria-label="Auto Sync fallback">
            <div className="qlhv-import-section-heading">
              <strong>B. Auto Sync dự phòng</strong>
              <span>Không cạnh tranh với realtime primary writer.</span>
            </div>
            <div className="qlhv-auto-sync__summary">
              <Fact label="Enabled" value={status.configuration.enabled ? 'ON' : 'OFF'} tone={ok(!status.configuration.enabled)} />
              <Fact label="Polling" value={status.configuration.pollingEnabled ? `ON · ${status.configuration.pollIntervalSeconds}s` : 'OFF'} tone={ok(!status.configuration.pollingEnabled)} />
              <Fact label="RunOnServerStartup" value={status.configuration.runOnStartup ? 'ON' : 'OFF'} tone={ok(!status.configuration.runOnStartup)} />
              <Fact label="Chế độ" value={status.configuration.isFallbackOnly ? 'Chỉ dự phòng' : 'Sai cấu hình'} tone={ok(status.configuration.isFallbackOnly)} />
              <Fact label="Phân loại runtime" value={status.autoSyncRuntime.classification} tone={status.autoSyncRuntime.isRunActive ? 'busy' : 'ok'} />
              <Fact label="Lượt đang chạy" value={status.autoSyncRuntime.isRunActive ? 'Có' : 'Không có'} tone={ok(!status.autoSyncRuntime.isRunActive)} />
              <Fact label="Active run / slot / operation" value={`${status.autoSyncRuntime.isRunActive ? 1 : 0} / ${status.autoSyncRuntime.effectiveActiveSlotCount} / ${status.autoSyncRuntime.activeOperationCount}`} tone={ok(!status.autoSyncRuntime.isRunActive && status.autoSyncRuntime.effectiveActiveSlotCount === 0 && status.autoSyncRuntime.activeOperationCount === 0)} />
              <Fact label="Chạy thủ công" value={manualRunDisplay} tone={ok(manualRunDisplay === 'Bị khóa bởi Realtime')} />
              <Fact label="Active RunId" value={status.autoSyncRuntime.activeRunId?.slice(0, 8) ?? 'Không có'} />
              <Fact label="Nguồn / bước" value={[status.autoSyncRuntime.source, status.autoSyncRuntime.step].filter(Boolean).join(' / ') || 'Không có'} />
            </div>
            {!status.configuration.manualRunAllowed && (
              <p className="qlhv-auto-sync__disabled-reason" role="status">
                <strong>{status.configuration.manualRunDecision}:</strong> {status.configuration.manualRunReason}
              </p>
            )}
          </section>

          <section aria-label="UI refresh">
            <div className="qlhv-import-readonly-summary">
              <span>UI refresh: ON · {UI_REFRESH_INTERVAL_MS / 1000}s · GET chỉ đọc</span>
              <span>Lần làm mới: {formatDate(lastRefresh)}</span>
              <span className={frontendMatches ? 'is-ok' : undefined}>Frontend: {FRONTEND_BUILD_ID}</span>
              <span>API: {short(status.runtime.apiBuildId)}</span>
              <span>Worker: {short(status.runtime.workerBuildId)}</span>
              <span>Instance: {short(status.runtime.instanceId)}</span>
              <span>Môi trường: {status.runtime.environment}</span>
            </div>
          </section>

          <div className="qlhv-auto-sync__history">
            <strong>Lịch sử Auto Sync dự phòng</strong>
            <ul>
              {status.history.slice(0, 6).map((row) => (
                <li key={row.runId}>
                  <span>{formatDate(row.createdAtUtc)}</span>
                  <strong>{row.runId.slice(0, 8)}</strong>
                  <span>{row.isStale ? 'Lịch sử stale — không hoạt động' : row.status}</span>
                  <span>{row.classification}</span>
                </li>
              ))}
            </ul>
          </div>
        </>
      )}

      {loading && !status && <div className="qlhv-import-empty">Đang đọc trạng thái vận hành...</div>}
      {notice && <div className="qlhv-import-success" role="status">{notice}</div>}
      {error && <div className="qlhv-import-error" role="alert">{error}</div>}
      <div className="qlhv-auto-sync__actions">
        <button type="button" className="btn btn--ghost" onClick={() => void load()} disabled={loading}>
          {loading ? 'Đang làm mới...' : 'Làm mới trạng thái'}
        </button>
        <button type="button" className="btn btn--primary" onClick={() => void handleRun()} disabled={disabledReason !== null} title={disabledReason ?? undefined}>
          {starting ? 'Đang gửi...' : 'Chạy Auto Sync dự phòng'}
        </button>
      </div>
      {disabledReason && <p className="qlhv-auto-sync__disabled-reason"><strong>Không thể chạy:</strong> {disabledReason}</p>}
    </section>
  );
}

function Fact({ label, value, tone = 'default' }: { label: string; value: string; tone?: 'default' | 'ok' | 'warning' | 'busy' | 'failed' }) {
  return <div className={`is-${tone}`}><span>{label}</span><strong>{value}</strong></div>;
}

function ok(value: boolean): 'ok' | 'warning' { return value ? 'ok' : 'warning'; }
function formatDate(value: string | null | undefined): string {
  if (!value) return 'Chưa có';
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString('vi-VN');
}
function short(value: string): string { return !value || value === 'unknown' ? 'unknown' : value.slice(0, 16); }
