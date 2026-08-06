import { useRef, useState } from 'react';
import {
  executeCsdtReversePlan,
  getCsdtReversePlan,
} from './api';
import {
  formatDateTime,
  formatNumber,
  reverseExecuteDisabledReason,
} from './logic';
import type {
  CsdtRealtimeVehicleType,
  CsdtReversePlan,
} from './types';

interface ReverseSyncPanelProps {
  isAdmin: boolean;
  onOperationAccepted: () => void | Promise<void>;
}

export default function ReverseSyncPanel({
  isAdmin,
  onOperationAccepted,
}: ReverseSyncPanelProps) {
  const [vehicleType, setVehicleType] = useState<CsdtRealtimeVehicleType>('OTO');
  const [maKhoaHoc, setMaKhoaHoc] = useState('');
  const [plan, setPlan] = useState<CsdtReversePlan | null>(null);
  const [loadingPlan, setLoadingPlan] = useState(false);
  const [executing, setExecuting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const operationRef = useRef(false);
  const executeReason = reverseExecuteDisabledReason(plan, isAdmin, executing || loadingPlan);

  function invalidatePlan() {
    setPlan(null);
    setNotice(null);
    setError(null);
  }

  async function handlePlan() {
    if (operationRef.current) {
      return;
    }
    operationRef.current = true;
    setLoadingPlan(true);
    setError(null);
    setNotice(null);
    try {
      const next = await getCsdtReversePlan(vehicleType, maKhoaHoc);
      const normalizedCourse = maKhoaHoc.length === 0 ? null : maKhoaHoc;
      if (next.vehicleType !== vehicleType || next.maKhoaHoc !== normalizedCourse) {
        throw new Error('Kế hoạch máy chủ không khớp bộ lọc hiện tại.');
      }
      setPlan(next);
    } catch (reason) {
      setPlan(null);
      setError(reason instanceof Error ? reason.message : 'Không thể lập kế hoạch V1 → V2.');
    } finally {
      operationRef.current = false;
      setLoadingPlan(false);
    }
  }

  async function handleExecute() {
    if (operationRef.current || executeReason || !plan) {
      return;
    }
    operationRef.current = true;
    setExecuting(true);
    setError(null);
    setNotice(null);
    try {
      const result = await executeCsdtReversePlan({
        vehicleType,
        maKhoaHoc: maKhoaHoc.length === 0 ? null : maKhoaHoc,
        expectedPlanToken: plan.planToken,
      });
      setNotice(result.message || 'Máy chủ đã tiếp nhận kế hoạch V1 → V2.');
      setPlan(null);
      await onOperationAccepted();
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Không thể thực thi kế hoạch V1 → V2.');
    } finally {
      operationRef.current = false;
      setExecuting(false);
    }
  }

  return (
    <section className="panel csdt-reverse-sync" aria-label="Đồng bộ thủ công V1 sang V2">
      <div className="csdt-realtime-section-heading">
        <div>
          <h3>Đồng bộ thủ công V1 → V2</h3>
          <p>Chỉ lập kế hoạch và ghi các dòng an toàn. Không tự đổi mã và không tự xóa V2.</p>
        </div>
        <span className="csdt-realtime-readonly-chip">Plan read-only</span>
      </div>

      <div className="csdt-reverse-sync__filters">
        <label className="field">
          <span className="field__label">Loại phương tiện</span>
          <select
            className="field__input"
            value={vehicleType}
            onChange={(event) => {
              setVehicleType(event.target.value as CsdtRealtimeVehicleType);
              invalidatePlan();
            }}
            disabled={loadingPlan || executing}
          >
            <option value="OTO">Ô tô</option>
            <option value="MOTO">Mô tô</option>
          </select>
        </label>
        <label className="field">
          <span className="field__label">Mã khóa học (không bắt buộc)</span>
          <input
            className="field__input"
            value={maKhoaHoc}
            onChange={(event) => {
              setMaKhoaHoc(event.target.value);
              invalidatePlan();
            }}
            placeholder="Ví dụ: 66029K260001"
            disabled={loadingPlan || executing}
          />
        </label>
        <button
          type="button"
          className="btn btn--ghost"
          onClick={() => void handlePlan()}
          disabled={loadingPlan || executing}
          aria-busy={loadingPlan}
        >
          {loadingPlan ? 'Đang lập kế hoạch...' : 'Lập kế hoạch'}
        </button>
      </div>

      {error && <div className="csdt-realtime-error" role="alert">{error}</div>}
      {notice && <div className="csdt-realtime-success" role="status">{notice}</div>}

      {!plan && !loadingPlan && (
        <div className="csdt-realtime-empty">
          Chưa có kế hoạch. Employee và Viewer có thể xem plan; chỉ Admin được thực thi.
        </div>
      )}

      {plan && (
        <div className="csdt-reverse-plan">
          <div className="csdt-realtime-direction">
            <code>{plan.sourceDatabaseName}</code>
            <span aria-hidden="true">→</span>
            <code>{plan.targetDatabaseName}</code>
          </div>
          <div className="csdt-reverse-plan__meta">
            <span>Tạo lúc: <strong>{formatDateTime(plan.generatedAtUtc)}</strong></span>
            <span>Hết hạn: <strong>{formatDateTime(plan.expiresAtUtc)}</strong></span>
            <span>Token: <code title={plan.planToken}>{shortToken(plan.planToken)}</code></span>
          </div>
          <div className="csdt-realtime-counters csdt-reverse-plan__counters">
            <span><small>Nguồn V1</small><strong>{formatNumber(plan.sourceRows)}</strong></span>
            <span><small>Insert an toàn</small><strong>{formatNumber(plan.safeInsertRows)}</strong></span>
            <span><small>Update an toàn</small><strong>{formatNumber(plan.safeUpdateRows)}</strong></span>
            <span><small>Skip</small><strong>{formatNumber(plan.skippedRows)}</strong></span>
            <span className={plan.v1OnlyRequiresReview > 0 ? 'is-warning' : ''}>
              <small>V1-only cần duyệt</small><strong>{formatNumber(plan.v1OnlyRequiresReview)}</strong>
            </span>
            <span className={plan.identityChanged > 0 ? 'is-error' : ''}>
              <small>Identity thay đổi</small><strong>{formatNumber(plan.identityChanged)}</strong>
            </span>
            <span className={plan.conflictRequiresReview > 0 ? 'is-warning' : ''}>
              <small>Xung đột cần duyệt</small><strong>{formatNumber(plan.conflictRequiresReview)}</strong>
            </span>
          </div>

          {plan.domains.length > 0 && (
            <div className="table-wrap">
              <table className="table csdt-realtime-table">
                <thead>
                  <tr>
                    <th>Domain</th>
                    <th>Nguồn</th>
                    <th>Insert</th>
                    <th>Update</th>
                    <th>Skip</th>
                    <th>Cần duyệt</th>
                  </tr>
                </thead>
                <tbody>
                  {plan.domains.map((domain) => (
                    <tr key={domain.domain}>
                      <td>{domain.domain}</td>
                      <td>{formatNumber(domain.sourceRows)}</td>
                      <td>{formatNumber(domain.safeInsertRows)}</td>
                      <td>{formatNumber(domain.safeUpdateRows)}</td>
                      <td>{formatNumber(domain.skippedRows)}</td>
                      <td>{formatNumber(domain.reviewRows)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}

          <MessageList title="Điểm chặn" items={plan.blockers} tone="error" />
          <MessageList title="Cảnh báo" items={plan.warnings} tone="warning" />

          <div className="csdt-reverse-plan__execute">
            <div>
              <strong>{plan.executable ? 'Kế hoạch có thể thực thi' : 'Kế hoạch chưa thể thực thi'}</strong>
              <p>MaCSDT, MaKH và MaDK là identity bất biến; máy chủ sẽ kiểm tra lại token trước khi ghi.</p>
            </div>
            {isAdmin ? (
              <button
                type="button"
                className="btn btn--primary"
                onClick={() => void handleExecute()}
                disabled={executeReason !== null}
                title={executeReason ?? undefined}
                aria-busy={executing}
              >
                {executing ? 'Đang tiếp nhận...' : 'Thực thi V1 → V2'}
              </button>
            ) : (
              <span className="csdt-realtime-readonly-chip">Chỉ Admin được thực thi</span>
            )}
          </div>
          {isAdmin && executeReason && (
            <p className="csdt-realtime-disabled-reason">{executeReason}</p>
          )}
        </div>
      )}
    </section>
  );
}

function MessageList({
  title,
  items,
  tone,
}: {
  title: string;
  items: string[];
  tone: 'error' | 'warning';
}) {
  if (items.length === 0) {
    return null;
  }
  return (
    <div className={`csdt-realtime-message-list is-${tone}`}>
      <strong>{title}</strong>
      <ul>
        {items.map((item, index) => <li key={`${index}-${item}`}>{item}</li>)}
      </ul>
    </div>
  );
}

function shortToken(value: string): string {
  return value.length <= 18 ? value : `${value.slice(0, 8)}…${value.slice(-8)}`;
}
