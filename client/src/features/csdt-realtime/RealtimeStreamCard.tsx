import {
  CSDT_REALTIME_PRESENTATION,
  canRetryRealtime,
  formatBaselineStatus,
  formatDateTime,
  formatNumber,
  formatRealtimeState,
  isRealtimeBusy,
  statusTone,
  streamActionDisabledReason,
} from './logic';
import type { CsdtRealtimeStreamStatus } from './types';

interface RealtimeStreamCardProps {
  status: CsdtRealtimeStreamStatus;
  selected: boolean;
  isAdmin: boolean;
  pendingAction: string | null;
  onSelect: () => void;
  onToggleEnabled: () => void;
  onBaseline: () => void;
  onRetry: () => void;
}

export default function RealtimeStreamCard({
  status,
  selected,
  isAdmin,
  pendingAction,
  onSelect,
  onToggleEnabled,
  onBaseline,
  onRetry,
}: RealtimeStreamCardProps) {
  const presentation = CSDT_REALTIME_PRESENTATION[status.streamCode];
  const pending = pendingAction !== null;
  const actionReason = streamActionDisabledReason(status, isAdmin, pending);
  const busy = isRealtimeBusy(status);
  const canRetry = canRetryRealtime(status);

  return (
    <article className={`csdt-realtime-stream ${selected ? 'is-selected' : ''}`}>
      <button
        type="button"
        className="csdt-realtime-stream__select"
        aria-pressed={selected}
        onClick={onSelect}
      >
        <span>
          <strong>{presentation.title}</strong>
          <small>Mã CSĐT {status.maCSDT}</small>
        </span>
        <span className={`csdt-realtime-state is-${statusTone(status.state)}`}>
          {formatRealtimeState(status.state)}
        </span>
      </button>

      <div className="csdt-realtime-direction" aria-label={`Luồng ${presentation.title}`}>
        <code>{status.sourceDatabaseName}</code>
        <span aria-hidden="true">→</span>
        <code>{status.targetDatabaseName}</code>
      </div>

      <dl className="csdt-realtime-facts">
        <div>
          <dt>Stream</dt>
          <dd>{status.enabled ? 'Enabled' : 'Disabled'}</dd>
        </div>
        <div>
          <dt>Baseline</dt>
          <dd>{formatBaselineStatus(status.baselineStatus)}</dd>
        </div>
        <div>
          <dt>Checkpoint</dt>
          <dd>{formatNumber(status.lastSuccessfulVersion)}</dd>
        </div>
        <div>
          <dt>Version nguồn</dt>
          <dd>{formatNumber(status.currentSourceVersion)}</dd>
        </div>
        <div>
          <dt>Min valid</dt>
          <dd>{formatNumber(status.minimumValidVersion)}</dd>
        </div>
        <div>
          <dt>Độ trễ version</dt>
          <dd>{formatNumber(status.lagVersions)}</dd>
        </div>
        <div>
          <dt>Thành công gần nhất</dt>
          <dd>{formatDateTime(status.lastSuccessAtUtc)}</dd>
        </div>
        <div>
          <dt>Lần thử lại</dt>
          <dd>{formatNumber(status.retryCount)}</dd>
        </div>
      </dl>

      <div className="csdt-realtime-counters" aria-label="Thống kê lần chạy gần nhất">
        <span><small>Insert</small><strong>{formatNumber(status.insertedRows)}</strong></span>
        <span><small>Update</small><strong>{formatNumber(status.updatedRows)}</strong></span>
        <span><small>Skip</small><strong>{formatNumber(status.skippedRows)}</strong></span>
        <span className={status.errorRows > 0 ? 'is-error' : ''}>
          <small>Lỗi</small><strong>{formatNumber(status.errorRows)}</strong>
        </span>
      </div>

      <div className="csdt-realtime-review-counts">
        <span>Delete tombstone: <strong>{formatNumber(status.deleteTombstoneCount)}</strong></span>
        <span>Xung đột: <strong>{formatNumber(status.unresolvedConflictCount)}</strong></span>
      </div>

      {status.lastError && (
        <div className="csdt-realtime-error" role="alert">
          <strong>Lỗi gần nhất:</strong> {status.lastError}
        </div>
      )}

      {isAdmin ? (
        <>
          <div className="csdt-realtime-stream__actions">
            <button
              type="button"
              className="btn btn--ghost"
              onClick={onToggleEnabled}
              disabled={actionReason !== null}
              title={actionReason ?? undefined}
              aria-busy={pendingAction === 'enabled'}
            >
              {pendingAction === 'enabled'
                ? 'Đang cập nhật...'
                : status.enabled ? 'Tắt stream' : 'Bật stream'}
            </button>
            <button
              type="button"
              className="btn btn--ghost"
              onClick={onBaseline}
              disabled={actionReason !== null || !status.enabled}
              title={!status.enabled ? 'Phải bật stream trước khi baseline.' : actionReason ?? undefined}
              aria-busy={pendingAction === 'baseline'}
            >
              {pendingAction === 'baseline' ? 'Đang tiếp nhận...' : 'Chạy baseline lại'}
            </button>
            <button
              type="button"
              className="btn btn--primary"
              onClick={onRetry}
              disabled={actionReason !== null || busy || !status.enabled || !canRetry}
              title={!status.enabled
                ? 'Stream đang tắt.'
                : !canRetry
                  ? 'Chỉ thử lại khi stream đang lỗi hoặc chờ retry.'
                  : actionReason ?? undefined}
              aria-busy={pendingAction === 'retry'}
            >
              {pendingAction === 'retry' ? 'Đang tiếp nhận...' : 'Thử lại'}
            </button>
          </div>
          {actionReason && <p className="csdt-realtime-disabled-reason">{actionReason}</p>}
        </>
      ) : (
        <p className="csdt-realtime-readonly-note">
          Bạn đang ở chế độ chỉ xem. Chỉ tài khoản Admin được bật/tắt, baseline hoặc retry.
        </p>
      )}
    </article>
  );
}
