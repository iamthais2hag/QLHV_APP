import { useMemo, useState } from 'react';
import { executeMotoSyncTest, getMotoSyncPlan } from './api';
import type {
  MotoSyncDirection,
  MotoSyncExecuteResult,
  MotoSyncMode,
  MotoSyncPlan,
} from './types';

const INSERT_ONLY_CONFIRM = 'SYNC TEST DATABASE';
const INSERT_AND_UPDATE_CONFIRM = 'SYNC TEST DATABASE UPDATE';

export default function MotoSyncPage() {
  const [direction, setDirection] = useState<MotoSyncDirection>('V1_TO_V2');
  const [maKhoaHoc, setMaKhoaHoc] = useState('');
  const [syncMode, setSyncMode] = useState<MotoSyncMode>('INSERT_ONLY');
  const [confirmText, setConfirmText] = useState('');
  const [plan, setPlan] = useState<MotoSyncPlan | null>(null);
  const [result, setResult] = useState<MotoSyncExecuteResult | null>(null);
  const [loadingPlan, setLoadingPlan] = useState(false);
  const [executing, setExecuting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const profiles = useMemo(() => getProfiles(direction), [direction]);
  const requiredConfirm = syncMode === 'INSERT_AND_UPDATE' ? INSERT_AND_UPDATE_CONFIRM : INSERT_ONLY_CONFIRM;
  const trimmedMaKhoaHoc = maKhoaHoc.trim();
  const planIsCurrent =
    !!plan &&
    plan.direction === direction &&
    plan.sourceProfileCode === profiles.sourceProfileCode &&
    plan.targetProfileCode === profiles.targetProfileCode &&
    (plan.maKhoaHoc ?? '') === trimmedMaKhoaHoc;
  const canExecute =
    planIsCurrent &&
    !!plan &&
    plan.executable &&
    plan.blockers.length === 0 &&
    plan.errors.length === 0 &&
    confirmText === requiredConfirm &&
    !executing &&
    !loadingPlan;

  function invalidatePlan() {
    setPlan(null);
    setResult(null);
    setError(null);
    setConfirmText('');
  }

  function handleDirectionChange(value: MotoSyncDirection) {
    setDirection(value);
    invalidatePlan();
  }

  function handleMaKhoaHocChange(value: string) {
    setMaKhoaHoc(value);
    invalidatePlan();
  }

  function handleSyncModeChange(value: MotoSyncMode) {
    setSyncMode(value);
    invalidatePlan();
  }

  async function handlePlan() {
    if (!trimmedMaKhoaHoc) {
      setError('Vui lòng nhập Mã khóa học trước khi lập kế hoạch.');
      return;
    }

    setLoadingPlan(true);
    setError(null);
    setResult(null);
    try {
      const nextPlan = await getMotoSyncPlan({
        direction,
        sourceProfileCode: profiles.sourceProfileCode,
        targetProfileCode: profiles.targetProfileCode,
        maKhoaHoc: trimmedMaKhoaHoc,
        allowDirtyData: false,
      });
      setPlan(nextPlan);
    } catch (err) {
      setPlan(null);
      setError(err instanceof Error ? err.message : 'Không thể lập kế hoạch đồng bộ Moto TEST.');
    } finally {
      setLoadingPlan(false);
    }
  }

  async function handleExecute() {
    if (!canExecute) return;

    setExecuting(true);
    setError(null);
    try {
      const nextResult = await executeMotoSyncTest({
        direction,
        sourceProfileCode: profiles.sourceProfileCode,
        targetProfileCode: profiles.targetProfileCode,
        maKhoaHoc: trimmedMaKhoaHoc,
        syncMode,
        confirmText,
      });
      setResult(nextResult);
      if (nextResult.plan) {
        setPlan(nextResult.plan);
      }
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Không thể thực thi đồng bộ Moto TEST.');
    } finally {
      setExecuting(false);
    }
  }

  return (
    <section className="moto-sync-page">
      <div className="toolbar moto-sync-hero">
        <div>
          <strong>Đồng bộ Moto V1/V2 - TEST DATABASE</strong>
          <p>
            Màn hình này chỉ dùng cho CSDT_V1 và CSDT_V2 test. Luôn lập kế hoạch trước, không tự động thực thi sau khi plan.
          </p>
        </div>
        <span className="status-pill status-pill--warn">TEST ONLY</span>
      </div>

      {error && <div className="pdf-preview-panel__error">{error}</div>}

      <div className="panel moto-sync-form">
        <div className="toolbar__row">
          <label className="field">
            <span className="field__label">Hướng đồng bộ</span>
            <select
              className="field__input"
              value={direction}
              onChange={(event) => handleDirectionChange(event.target.value as MotoSyncDirection)}
            >
              <option value="V1_TO_V2">CSDT_V1 → CSDT_V2</option>
              <option value="V2_TO_V1">CSDT_V2 → CSDT_V1</option>
            </select>
          </label>

          <label className="field">
            <span className="field__label">Nguồn</span>
            <input className="field__input" value={profiles.sourceProfileCode} readOnly />
          </label>

          <label className="field">
            <span className="field__label">Đích</span>
            <input className="field__input" value={profiles.targetProfileCode} readOnly />
          </label>

          <label className="field">
            <span className="field__label">Mã khóa học</span>
            <input
              className="field__input"
              value={maKhoaHoc}
              onChange={(event) => handleMaKhoaHocChange(event.target.value)}
              placeholder="66016K26A1004"
            />
          </label>

          <label className="field">
            <span className="field__label">Chế độ</span>
            <select
              className="field__input"
              value={syncMode}
              onChange={(event) => handleSyncModeChange(event.target.value as MotoSyncMode)}
            >
              <option value="INSERT_ONLY">Chỉ thêm mới</option>
              <option value="INSERT_AND_UPDATE">Thêm mới + cập nhật</option>
            </select>
          </label>

          <div className="toolbar__actions">
            <button type="button" className="btn btn--primary" onClick={handlePlan} disabled={loadingPlan || executing}>
              {loadingPlan ? 'Đang lập kế hoạch...' : 'Lập kế hoạch'}
            </button>
          </div>
        </div>

        <div className="moto-sync-safety-note">
          <strong>Lưu ý an toàn:</strong> INSERT_AND_UPDATE có thể ghi đè giá trị hiện có trong NguoiLX và NguoiLX_HoSo.
          Không có xóa dữ liệu. Giấy tờ vẫn insert-only trong giai đoạn này.
        </div>
      </div>

      <div className="moto-sync-layout">
        <div className="panel">
          <SectionTitle title="Kế hoạch đọc trước" hint={plan ? 'Plan mới nhất' : 'Chưa có plan'} />
          {!plan ? (
            <div className="state">Nhập Mã khóa học rồi bấm “Lập kế hoạch”.</div>
          ) : (
            <>
              {!planIsCurrent && (
                <div className="moto-sync-warning">Plan đã cũ vì bộ lọc đã thay đổi. Vui lòng lập kế hoạch lại trước khi execute.</div>
              )}
              <PlanMetrics plan={plan} />
              <MessageList title="Blockers" items={plan.blockers} variant="error" />
              <MessageList title="Warnings" items={plan.warnings} variant="warning" />
              <ErrorList errors={plan.errors} />
              <UpdateSamples samples={plan.updateSamples} />
            </>
          )}
        </div>

        <aside className="panel moto-sync-execute">
          <SectionTitle title="Thực thi TEST" hint={syncMode === 'INSERT_AND_UPDATE' ? 'Có cập nhật dòng cũ' : 'Insert-only'} />

          {planIsCurrent && plan && plan.plannedUpdate > 0 && syncMode === 'INSERT_ONLY' && (
            <div className="moto-sync-message-list moto-sync-message-list--warning">
              Chế độ Chỉ thêm mới sẽ bỏ qua các dòng cần cập nhật. Chọn Thêm mới + cập nhật nếu muốn ghi đè dữ liệu hiện có.
            </div>
          )}

          <div className="moto-sync-confirm-box">
            <p>Nhập đúng chuỗi xác nhận để bật nút thực thi:</p>
            <code>{requiredConfirm}</code>
            <input
              className="field__input"
              value={confirmText}
              onChange={(event) => setConfirmText(event.target.value)}
              placeholder="Nhập chuỗi xác nhận"
            />
          </div>

          <button type="button" className="btn btn--primary moto-sync-execute__button" onClick={handleExecute} disabled={!canExecute}>
            {executing ? 'Đang thực thi TEST...' : 'Thực thi TEST'}
          </button>

          {!canExecute && (
            <div className="moto-sync-muted">
              Execute chỉ mở khi plan hiện tại executable, không có blocker/error, và confirm text khớp tuyệt đối.
            </div>
          )}

          <ExecuteResult result={result} />
        </aside>
      </div>
    </section>
  );
}

function getProfiles(direction: MotoSyncDirection): { sourceProfileCode: string; targetProfileCode: string } {
  return direction === 'V1_TO_V2'
    ? { sourceProfileCode: 'CSDT_V1', targetProfileCode: 'CSDT_V2' }
    : { sourceProfileCode: 'CSDT_V2', targetProfileCode: 'CSDT_V1' };
}

function SectionTitle({ title, hint }: { title: string; hint?: string }) {
  return (
    <div className="moto-sync-section-title">
      <strong>{title}</strong>
      {hint && <span>{hint}</span>}
    </div>
  );
}

function PlanMetrics({ plan }: { plan: MotoSyncPlan }) {
  const rows = [
    ['sourceRows', plan.sourceRows],
    ['targetRows', plan.targetRows],
    ['exactMaDkOverlap', plan.exactMaDkOverlap],
    ['sourceOnly', plan.sourceOnly],
    ['targetOnly', plan.targetOnly],
    ['plannedInsertNguoiLX', plan.plannedInsertNguoiLX],
    ['plannedInsertNguoiLXHoSo', plan.plannedInsertNguoiLXHoSo],
    ['plannedInsertGiayTo', plan.plannedInsertGiayTo],
    ['plannedUpdate', plan.plannedUpdate],
    ['plannedUpdateNguoiLX', plan.plannedUpdateNguoiLX],
    ['plannedUpdateNguoiLXHoSo', plan.plannedUpdateNguoiLXHoSo],
  ] as const;

  return (
    <div className="moto-sync-metrics">
      {rows.map(([label, value]) => (
        <div key={label} className="moto-sync-metric">
          <span>{label}</span>
          <strong>{formatNumber(value)}</strong>
        </div>
      ))}
      <div className={`moto-sync-metric ${plan.executable ? 'is-ok' : 'is-blocked'}`}>
        <span>executable</span>
        <strong>{plan.executable ? 'Có' : 'Không'}</strong>
      </div>
    </div>
  );
}

function MessageList({ title, items, variant }: { title: string; items: string[]; variant: 'error' | 'warning' }) {
  if (items.length === 0) {
    return null;
  }

  return (
    <div className={`moto-sync-message-list moto-sync-message-list--${variant}`}>
      <strong>{title}</strong>
      <ul>
        {items.map((item) => (
          <li key={item}>{item}</li>
        ))}
      </ul>
    </div>
  );
}

function ErrorList({ errors }: { errors: MotoSyncPlan['errors'] }) {
  if (errors.length === 0) return null;

  return (
    <div className="moto-sync-message-list moto-sync-message-list--error">
      <strong>Errors</strong>
      <ul>
        {errors.map((error, index) => (
          <li key={`${error.code}-${index}`}>
            {error.code}: {error.message}
            {error.recordKey ? ` (${error.recordKey})` : ''}
          </li>
        ))}
      </ul>
    </div>
  );
}

function UpdateSamples({ samples }: { samples: MotoSyncPlan['updateSamples'] }) {
  if (samples.length === 0) return null;

  return (
    <div className="moto-sync-samples">
      <SectionTitle title="Mẫu dòng sẽ cập nhật" hint="Không hiển thị giá trị dữ liệu cá nhân" />
      <div className="table-wrap">
        <table className="table table--moto-sync-samples">
          <thead>
            <tr>
              <th>MaDK</th>
              <th>Bảng</th>
              <th>Cột thay đổi</th>
            </tr>
          </thead>
          <tbody>
            {samples.map((sample) => (
              <tr key={`${sample.tableName}-${sample.maDK}-${sample.changedColumnNames.join('|')}`}>
                <td>{sample.maDK}</td>
                <td>{sample.tableName}</td>
                <td>{sample.changedColumnNames.join(', ')}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}

function ExecuteResult({ result }: { result: MotoSyncExecuteResult | null }) {
  if (!result) return null;

  const summary = result.summary;
  return (
    <div className="moto-sync-result">
      <SectionTitle title="Kết quả" hint={result.status} />
      <p>
        <strong>executed:</strong> {result.executed ? 'true' : 'false'}
      </p>
      <p>{result.message}</p>
      {summary && (
        <div className="moto-sync-result-grid">
          <span>insertedNguoiLX</span><strong>{formatNumber(summary.insertedNguoiLX)}</strong>
          <span>insertedNguoiLXHoSo</span><strong>{formatNumber(summary.insertedNguoiLXHoSo)}</strong>
          <span>insertedGiayTo</span><strong>{formatNumber(summary.insertedGiayTo)}</strong>
          <span>updatedNguoiLX</span><strong>{formatNumber(summary.updatedNguoiLX)}</strong>
          <span>updatedNguoiLXHoSo</span><strong>{formatNumber(summary.updatedNguoiLXHoSo)}</strong>
          <span>updatedRows</span><strong>{formatNumber(summary.updatedRows)}</strong>
          <span>deletedRows</span><strong>{formatNumber(summary.deletedRows)}</strong>
          <span>durationMs</span><strong>{formatNumber(summary.durationMs)}</strong>
        </div>
      )}
    </div>
  );
}

function formatNumber(value: number): string {
  return new Intl.NumberFormat('vi-VN').format(value);
}
