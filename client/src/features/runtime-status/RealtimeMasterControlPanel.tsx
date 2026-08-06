import { useCallback, useEffect, useState } from 'react';
import { useAuth } from '../auth/AuthContext';
import {
  getRealtimeControl,
  previewRealtimeIntegrity,
  runRealtimeOnce,
  setRealtimeControl,
} from './api';
import type { RealtimeControlStatus, RealtimeIntegrityPreview } from './types';

export default function RealtimeMasterControlPanel() {
  const { user } = useAuth();
  const isAdmin = user?.role === 'Admin';
  const [status, setStatus] = useState<RealtimeControlStatus | null>(null);
  const [integrity, setIntegrity] = useState<RealtimeIntegrityPreview | null>(null);
  const [busy, setBusy] = useState(false);
  const [confirmEnable, setConfirmEnable] = useState(false);
  const [message, setMessage] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const refresh = useCallback(async (signal?: AbortSignal) => {
    try {
      setError(null);
      setStatus(await getRealtimeControl(signal));
    } catch (loadError) {
      if (!(loadError instanceof DOMException && loadError.name === 'AbortError')) {
        setError(asMessage(loadError));
      }
    }
  }, []);

  useEffect(() => {
    const controller = new AbortController();
    void refresh(controller.signal);
    return () => controller.abort();
  }, [refresh]);

  const change = async (enabled: boolean) => {
    if (!status || !isAdmin) return;
    setBusy(true);
    setError(null);
    setMessage(null);
    try {
      const next = await setRealtimeControl(enabled, status.rowVersion);
      setStatus(next);
      setMessage(enabled
        ? 'Realtime đã ON. Worker sẽ xử lý thay đổi sau checkpoint hiện tại.'
        : 'Realtime đã OFF. Worker chỉ duy trì heartbeat.');
    } catch (changeError) {
      setError(asMessage(changeError));
      await refresh();
    } finally {
      setBusy(false);
      setConfirmEnable(false);
    }
  };

  const runOnce = async () => {
    if (!isAdmin || status?.state !== 'ON') return;
    setBusy(true);
    setError(null);
    setMessage(null);
    try {
      const request = await runRealtimeOnce();
      setMessage(`Đã tiếp nhận yêu cầu chạy một lần (${request.status}). Công tắc không bị thay đổi.`);
      await refresh();
    } catch (runError) {
      setError(asMessage(runError));
    } finally {
      setBusy(false);
    }
  };

  const previewIntegrity = async () => {
    if (!isAdmin) return;
    setBusy(true);
    setError(null);
    setMessage(null);
    try {
      const result = await previewRealtimeIntegrity();
      setIntegrity(result);
      setMessage('Kiểm tra toàn vẹn đã hoàn tất ở chế độ chỉ đọc.');
    } catch (previewError) {
      setError(asMessage(previewError));
    } finally {
      setBusy(false);
    }
  };

  return (
    <section className="panel realtime-master" aria-label="Công tắc Realtime tổng">
      <div className="realtime-master__header">
        <div>
          <span className="runtime-status-eyebrow">Điều khiển theo sự kiện</span>
          <h3>Realtime tổng</h3>
          <p>OFF chỉ giữ heartbeat. ON mới đọc Change Tracking và xử lý event.</p>
        </div>
        <span className={`realtime-master__state is-${(status?.state ?? 'unknown').toLowerCase()}`}>
          {status?.state ?? 'ĐANG ĐỌC'}
        </span>
      </div>

      {error && <div className="runtime-status-error" role="alert">{error}</div>}
      {message && <div className="realtime-master__message" role="status">{message}</div>}

      <div className="realtime-master__switch" role="group" aria-label="Bật hoặc tắt Realtime">
        <button type="button" className={status?.state === 'OFF' ? 'is-selected' : ''} disabled={!isAdmin || busy || !status} onClick={() => void change(false)}>OFF</button>
        <button type="button" className={status?.state === 'ON' ? 'is-selected' : ''} disabled={!isAdmin || busy || !status || status.state === 'BLOCKED'} onClick={() => setConfirmEnable(true)}>ON</button>
      </div>

      {!isAdmin && <p className="realtime-master__note">Chỉ Admin được thay đổi công tắc hoặc chạy thao tác.</p>}
      {status?.state === 'BLOCKED' && (
        <p className="realtime-master__blocker">Blocker: <code>{status.blockerReason ?? status.reason ?? 'Không xác định'}</code></p>
      )}

      {status && (
        <dl className="realtime-master__facts">
          <div><dt>Worker</dt><dd>{status.workerRunning ? 'Running' : 'Stopped'} · {status.workerStatus}</dd></div>
          <div><dt>Cycle outcome</dt><dd>{status.cycleOutcome ?? 'Chưa có'}</dd></div>
          <div><dt>Heartbeat</dt><dd>{formatTime(status.lastHeartbeatUtc)}</dd></div>
          <div><dt>Cycle thành công gần nhất</dt><dd>{formatTime(status.lastSuccessfulCycleUtc)}</dd></div>
          {status.profiles.map((profile) => (
            <div key={profile.sourceProfileCode}>
              <dt>{profile.sourceProfileCode}</dt>
              <dd>Checkpoint {profile.checkpointVersion} · CT {profile.currentVersion} · Backlog {profile.backlogVersions}</dd>
            </div>
          ))}
        </dl>
      )}

      <div className="realtime-master__actions">
        <button type="button" className="btn btn--primary" disabled={!isAdmin || busy || status?.state !== 'ON'} onClick={() => void runOnce()}>Đồng bộ ngay một lần</button>
        <button type="button" className="btn btn--secondary" disabled={!isAdmin || busy} onClick={() => void previewIntegrity()}>Kiểm tra toàn vẹn dữ liệu</button>
        <button type="button" className="btn btn--secondary" disabled={busy} onClick={() => void refresh()}>Làm mới</button>
      </div>

      {integrity && (
        <div className="realtime-integrity" aria-label="Kết quả kiểm tra toàn vẹn chỉ đọc">
          <strong>Toàn vẹn: {integrity.status}</strong>
          <span>Chỉ đọc · {formatTime(integrity.observedAtUtc)}</span>
          {integrity.profiles.map((profile) => (
            <p key={profile.sourceProfileCode}>{profile.sourceProfileCode}: {profile.status} · insert {profile.plannedInsertRows} · update {profile.plannedUpdateRows} · target-only {profile.targetOnlyRows} · review {profile.manualReviewRows}</p>
          ))}
        </div>
      )}

      {confirmEnable && (
        <div className="realtime-master__modal" role="presentation">
          <div role="dialog" aria-modal="true" aria-labelledby="realtime-enable-title" className="panel">
            <h3 id="realtime-enable-title">Bật Realtime?</h3>
            <p>Bật Realtime sẽ xử lý toàn bộ thay đổi sau checkpoint hiện tại.</p>
            <div className="realtime-master__actions">
              <button type="button" className="btn btn--secondary" onClick={() => setConfirmEnable(false)}>Hủy</button>
              <button type="button" className="btn btn--primary" onClick={() => void change(true)}>Xác nhận bật ON</button>
            </div>
          </div>
        </div>
      )}
    </section>
  );
}

function asMessage(error: unknown): string {
  return error instanceof Error ? error.message : 'Không thể thực hiện thao tác Realtime.';
}

function formatTime(value: string | null): string {
  if (!value) return 'Chưa có';
  return new Intl.DateTimeFormat('vi-VN', {
    dateStyle: 'short',
    timeStyle: 'medium',
    timeZone: 'Asia/Ho_Chi_Minh',
  }).format(new Date(value));
}
