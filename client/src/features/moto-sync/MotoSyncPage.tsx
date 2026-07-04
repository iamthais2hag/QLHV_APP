import { useCallback, useEffect, useMemo, useState } from 'react';
import {
  executeMotoCenterTransferTest,
  executeMotoSyncTest,
  getMotoCenterTransferPlan,
  getMotoSyncKhoaHocOptions,
  getMotoSyncPlan,
  getMotoSyncRunHistory,
  getMotoSyncRunHistoryDetail,
} from './api';
import type {
  MotoCenterTransferExecuteResult,
  MotoCenterTransferPlan,
  MotoSyncDirection,
  MotoSyncExecuteResult,
  MotoSyncKhoaHocOption,
  MotoSyncMode,
  MotoSyncPlan,
  MotoSyncRunHistoryDetail,
  MotoSyncRunHistoryListItem,
} from './types';

const CENTER_TRANSFER_CONFIRM = 'CHUYEN MA CSDT TEST';
const INSERT_ONLY_CONFIRM = 'SYNC TEST DATABASE';
const INSERT_AND_UPDATE_CONFIRM = 'SYNC TEST DATABASE UPDATE';

interface PlanJsonSummary {
  sourceRows: number | null;
  targetRows: number | null;
  sourceOnly: number | null;
  targetOnly: number | null;
  plannedInsertKhoaHoc: number | null;
  plannedInsertBaoCaoI: number | null;
  plannedInsertNguoiLX: number | null;
  plannedInsertNguoiLXGPLX: number | null;
  plannedInsertNguoiLXHoSo: number | null;
  plannedInsertGiayTo: number | null;
  plannedUpdate: number | null;
  blockersCount: number;
  warningsCount: number;
  errorsCount: number;
}

export default function MotoSyncPage() {
  const [centerMaKhoaHocCu, setCenterMaKhoaHocCu] = useState('');
  const [centerMaCSDTCu, setCenterMaCSDTCu] = useState('');
  const [centerMaCSDTMoi, setCenterMaCSDTMoi] = useState('');
  const [centerMaSoGTVTMoi, setCenterMaSoGTVTMoi] = useState('');
  const [centerConfirmText, setCenterConfirmText] = useState('');
  const [centerPlan, setCenterPlan] = useState<MotoCenterTransferPlan | null>(null);
  const [centerResult, setCenterResult] = useState<MotoCenterTransferExecuteResult | null>(null);
  const [centerLoadingPlan, setCenterLoadingPlan] = useState(false);
  const [centerExecuting, setCenterExecuting] = useState(false);
  const [centerError, setCenterError] = useState<string | null>(null);
  const [direction, setDirection] = useState<MotoSyncDirection>('V1_TO_V2');
  const [maKhoaHoc, setMaKhoaHoc] = useState('');
  const [syncMode, setSyncMode] = useState<MotoSyncMode>('INSERT_ONLY');
  const [confirmText, setConfirmText] = useState('');
  const [plan, setPlan] = useState<MotoSyncPlan | null>(null);
  const [result, setResult] = useState<MotoSyncExecuteResult | null>(null);
  const [loadingPlan, setLoadingPlan] = useState(false);
  const [executing, setExecuting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [courseSearch, setCourseSearch] = useState('');
  const [courseOptions, setCourseOptions] = useState<MotoSyncKhoaHocOption[]>([]);
  const [courseOptionsLoading, setCourseOptionsLoading] = useState(false);
  const [courseOptionsError, setCourseOptionsError] = useState<string | null>(null);
  const [history, setHistory] = useState<MotoSyncRunHistoryListItem[]>([]);
  const [historyLoading, setHistoryLoading] = useState(false);
  const [historyError, setHistoryError] = useState<string | null>(null);
  const [historyDetail, setHistoryDetail] = useState<MotoSyncRunHistoryDetail | null>(null);
  const [historyDetailLoading, setHistoryDetailLoading] = useState(false);
  const [historyDetailError, setHistoryDetailError] = useState<string | null>(null);

  const loadHistory = useCallback(async () => {
    setHistoryLoading(true);
    setHistoryError(null);
    try {
      const rows = await getMotoSyncRunHistory(50);
      setHistory(rows);
    } catch (err) {
      setHistoryError(err instanceof Error ? err.message : 'Không thể tải lịch sử đồng bộ Moto TEST.');
    } finally {
      setHistoryLoading(false);
    }
  }, []);

  useEffect(() => {
    void loadHistory();
  }, [loadHistory]);

  const profiles = useMemo(() => getProfiles(direction), [direction]);
  const requiredConfirm = syncMode === 'INSERT_AND_UPDATE' ? INSERT_AND_UPDATE_CONFIRM : INSERT_ONLY_CONFIRM;
  const trimmedMaKhoaHoc = maKhoaHoc.trim();
  const centerSourceProfileCode = 'CSDT_V1';
  const centerTargetProfileCode = 'CSDT_V2';
  const trimmedCenterMaKhoaHocCu = centerMaKhoaHocCu.trim();
  const trimmedCenterMaCSDTCu = centerMaCSDTCu.trim();
  const trimmedCenterMaCSDTMoi = centerMaCSDTMoi.trim();
  const trimmedCenterMaSoGTVTMoi = centerMaSoGTVTMoi.trim();
  const centerPlanIsCurrent =
    !!centerPlan &&
    centerPlan.sourceProfileCode === centerSourceProfileCode &&
    centerPlan.targetProfileCode === centerTargetProfileCode &&
    centerPlan.maKhoaHocCu === trimmedCenterMaKhoaHocCu &&
    centerPlan.maCSDTCu === trimmedCenterMaCSDTCu &&
    centerPlan.maCSDTMoi === trimmedCenterMaCSDTMoi &&
    centerPlan.maSoGTVTMoi === trimmedCenterMaSoGTVTMoi;
  const canExecuteCenterTransfer =
    centerPlanIsCurrent &&
    !!centerPlan &&
    centerPlan.executable &&
    centerPlan.blockers.length === 0 &&
    centerConfirmText === CENTER_TRANSFER_CONFIRM &&
    !centerExecuting &&
    !centerLoadingPlan;
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

  function invalidateCenterPlan() {
    setCenterPlan(null);
    setCenterResult(null);
    setCenterError(null);
    setCenterConfirmText('');
  }

  function handleCenterFieldChange(setter: (value: string) => void, value: string) {
    setter(value);
    invalidateCenterPlan();
  }

  async function handleCenterPlan() {
    if (!trimmedCenterMaKhoaHocCu || !trimmedCenterMaCSDTCu || !trimmedCenterMaCSDTMoi || !trimmedCenterMaSoGTVTMoi) {
      setCenterError('Vui lòng nhập đủ Mã khóa học cũ, MaCSDT cũ, MaCSDT mới và Mã Sở GTVT mới.');
      return;
    }

    setCenterLoadingPlan(true);
    setCenterError(null);
    setCenterResult(null);
    try {
      const nextPlan = await getMotoCenterTransferPlan({
        sourceProfileCode: centerSourceProfileCode,
        targetProfileCode: centerTargetProfileCode,
        maKhoaHocCu: trimmedCenterMaKhoaHocCu,
        maCSDTCu: trimmedCenterMaCSDTCu,
        maCSDTMoi: trimmedCenterMaCSDTMoi,
        maSoGTVTMoi: trimmedCenterMaSoGTVTMoi,
      });
      setCenterPlan(nextPlan);
    } catch (err) {
      setCenterPlan(null);
      setCenterError(err instanceof Error ? err.message : 'Không thể lập kế hoạch chuyển MaCSDT Moto TEST.');
    } finally {
      setCenterLoadingPlan(false);
    }
  }

  async function handleCenterExecute() {
    if (!canExecuteCenterTransfer) return;

    setCenterExecuting(true);
    setCenterError(null);
    try {
      const nextResult = await executeMotoCenterTransferTest({
        sourceProfileCode: centerSourceProfileCode,
        targetProfileCode: centerTargetProfileCode,
        maKhoaHocCu: trimmedCenterMaKhoaHocCu,
        maCSDTCu: trimmedCenterMaCSDTCu,
        maCSDTMoi: trimmedCenterMaCSDTMoi,
        maSoGTVTMoi: trimmedCenterMaSoGTVTMoi,
        confirmText: centerConfirmText,
      });
      setCenterResult(nextResult);
      if (nextResult.plan) {
        setCenterPlan(nextResult.plan);
      }
    } catch (err) {
      setCenterError(err instanceof Error ? err.message : 'Không thể thực thi chuyển MaCSDT Moto TEST.');
    } finally {
      setCenterExecuting(false);
    }
  }

  function invalidatePlan() {
    setPlan(null);
    setResult(null);
    setError(null);
    setConfirmText('');
  }

  function clearCourseOptions() {
    setCourseOptions([]);
    setCourseOptionsError(null);
  }

  function handleDirectionChange(value: MotoSyncDirection) {
    setDirection(value);
    clearCourseOptions();
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

  async function handleLoadCourseOptions() {
    setCourseOptionsLoading(true);
    setCourseOptionsError(null);
    try {
      const options = await getMotoSyncKhoaHocOptions({
        direction,
        sourceProfileCode: profiles.sourceProfileCode,
        targetProfileCode: profiles.targetProfileCode,
        search: courseSearch,
        take: 50,
      });
      setCourseOptions(options);
    } catch (err) {
      setCourseOptions([]);
      setCourseOptionsError(err instanceof Error ? err.message : 'Không thể tải danh sách khóa học Moto TEST.');
    } finally {
      setCourseOptionsLoading(false);
    }
  }

  function handleChooseCourse(option: MotoSyncKhoaHocOption) {
    setMaKhoaHoc(option.maKhoaHoc);
    setCourseSearch(option.maKhoaHoc);
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
      await loadHistory();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Không thể thực thi đồng bộ Moto TEST.');
    } finally {
      setExecuting(false);
    }
  }

  async function handleOpenHistoryDetail(id: number) {
    setHistoryDetailLoading(true);
    setHistoryDetailError(null);
    try {
      const detail = await getMotoSyncRunHistoryDetail(id);
      setHistoryDetail(detail);
    } catch (err) {
      setHistoryDetail(null);
      setHistoryDetailError(err instanceof Error ? err.message : 'Không thể tải chi tiết lịch sử đồng bộ Moto TEST.');
    } finally {
      setHistoryDetailLoading(false);
    }
  }

  function handleCloseHistoryDetail() {
    setHistoryDetail(null);
    setHistoryDetailError(null);
    setHistoryDetailLoading(false);
  }

  return (
    <section className="moto-sync-page">
      <div className="toolbar moto-sync-hero">
        <div>
          <strong>Đồng bộ dữ liệu Moto - TEST DATABASE</strong>
          <p>
            Màn hình này chỉ dùng cho CSDT_V1 và CSDT_V2 test. Luôn lập kế hoạch trước, không tự động thực thi sau khi plan.
          </p>
        </div>
        <span className="status-pill status-pill--warn">TEST ONLY</span>
      </div>

      {centerError && <div className="pdf-preview-panel__error">{centerError}</div>}
      {error && <div className="pdf-preview-panel__error">{error}</div>}

      <div className="panel moto-sync-form moto-sync-option-panel">
        <SectionTitle
          title="Sync MaCSDT cũ -> MaCSDT mới"
          hint="Copy một khóa từ CSDT_V1 sang CSDT_V2 rồi đổi mã trung tâm trong phạm vi khóa vừa chuyển."
        />
        <div className="toolbar__row">
          <label className="field">
            <span className="field__label">Nguồn</span>
            <input className="field__input" value={centerSourceProfileCode} readOnly />
          </label>

          <label className="field">
            <span className="field__label">Đích</span>
            <input className="field__input" value={centerTargetProfileCode} readOnly />
          </label>

          <label className="field">
            <span className="field__label">Mã khóa học cũ</span>
            <input
              className="field__input"
              value={centerMaKhoaHocCu}
              onChange={(event) => handleCenterFieldChange(setCenterMaKhoaHocCu, event.target.value)}
              placeholder="66016K26A1003"
            />
          </label>

          <label className="field">
            <span className="field__label">MaCSDT cũ</span>
            <input
              className="field__input"
              value={centerMaCSDTCu}
              onChange={(event) => handleCenterFieldChange(setCenterMaCSDTCu, event.target.value)}
              placeholder="66016"
            />
          </label>

          <label className="field">
            <span className="field__label">MaCSDT mới</span>
            <input
              className="field__input"
              value={centerMaCSDTMoi}
              onChange={(event) => handleCenterFieldChange(setCenterMaCSDTMoi, event.target.value)}
              placeholder="Nhập MaCSDT mới"
            />
          </label>

          <label className="field">
            <span className="field__label">Mã Sở GTVT mới</span>
            <input
              className="field__input"
              value={centerMaSoGTVTMoi}
              onChange={(event) => handleCenterFieldChange(setCenterMaSoGTVTMoi, event.target.value)}
              placeholder="Nhập mã Sở GTVT mới"
            />
          </label>

          <div className="toolbar__actions">
            <button type="button" className="btn btn--primary" onClick={handleCenterPlan} disabled={centerLoadingPlan || centerExecuting}>
              {centerLoadingPlan ? 'Đang lập kế hoạch...' : 'Lập kế hoạch chuyển mã'}
            </button>
          </div>
        </div>

        <div className="moto-sync-safety-note">
          <strong>Lưu ý:</strong> Chức năng này chỉ dùng cho TEST, không xóa dữ liệu, không merge. Khi execute sẽ copy dữ liệu khóa cũ sang đích,
          sau đó đổi MaCSDT/MaKhoaHoc/MaDK trong phạm vi khóa vừa chọn.
        </div>

        {centerPlan && (
          <div className="moto-sync-center-grid">
            <div>
              {!centerPlanIsCurrent && (
                <div className="moto-sync-warning">Plan chuyển mã đã cũ vì thông tin nhập đã thay đổi. Vui lòng lập kế hoạch lại.</div>
              )}
              <CenterTransferPlanMetrics plan={centerPlan} />
              <MessageList title="Blockers" items={centerPlan.blockers} variant="error" />
              <MessageList title="Warnings" items={centerPlan.warnings} variant="warning" />
            </div>

            <aside className="moto-sync-execute moto-sync-center-execute">
              <SectionTitle title="Thực thi chuyển MaCSDT TEST" hint="Có transaction và rollback khi lỗi" />
              <div className="moto-sync-confirm-box">
                <p>Nhập đúng chuỗi xác nhận để bật nút thực thi:</p>
                <code>{CENTER_TRANSFER_CONFIRM}</code>
                <input
                  className="field__input"
                  value={centerConfirmText}
                  onChange={(event) => setCenterConfirmText(event.target.value)}
                  placeholder="Nhập chuỗi xác nhận"
                />
              </div>
              <button
                type="button"
                className="btn btn--primary moto-sync-execute__button"
                onClick={handleCenterExecute}
                disabled={!canExecuteCenterTransfer}
              >
                {centerExecuting ? 'Đang chuyển MaCSDT TEST...' : 'Thực thi chuyển MaCSDT TEST'}
              </button>
              {!canExecuteCenterTransfer && (
                <div className="moto-sync-muted">
                  Execute chỉ mở khi plan hiện tại executable, không có blocker, và confirm text khớp tuyệt đối.
                </div>
              )}
              <CenterTransferResult result={centerResult} />
            </aside>
          </div>
        )}
      </div>

      <div className="panel moto-sync-form">
        <SectionTitle
          title="Sync V1/V2 cùng MaCSDT"
          hint="Luồng sync hiện có, giữ nguyên MaDK và MaKhoaHoc."
        />
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

        <div className="moto-sync-course-picker">
          <div className="moto-sync-section-title">
            <strong>Tìm khóa học</strong>
            <span>Chỉ đọc từ nguồn theo hướng đang chọn, không tự thực thi đồng bộ.</span>
          </div>
          <div className="toolbar__row">
            <label className="field moto-sync-course-search">
              <span className="field__label">Từ khóa</span>
              <input
                className="field__input"
                value={courseSearch}
                onChange={(event) => setCourseSearch(event.target.value)}
                placeholder="Nhập mã khóa hoặc tên khóa"
              />
            </label>
            <div className="toolbar__actions">
              <button type="button" className="btn btn--ghost" onClick={() => void handleLoadCourseOptions()} disabled={courseOptionsLoading || executing}>
                {courseOptionsLoading ? 'Đang tải khóa...' : 'Tải danh sách khóa'}
              </button>
            </div>
          </div>
          {courseOptionsError && <div className="moto-sync-message-list moto-sync-message-list--error">{courseOptionsError}</div>}
          <CourseOptionsTable
            options={courseOptions}
            loading={courseOptionsLoading}
            onChoose={handleChooseCourse}
          />
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

      <div className="panel moto-sync-history">
        <div className="moto-sync-section-title">
          <strong>Lịch sử đồng bộ</strong>
          <button type="button" className="btn btn--ghost" onClick={() => void loadHistory()} disabled={historyLoading}>
            {historyLoading ? 'Đang tải...' : 'Tải lại lịch sử'}
          </button>
        </div>
        {historyError && <div className="moto-sync-message-list moto-sync-message-list--error">{historyError}</div>}
        <RunHistoryTable rows={history} loading={historyLoading} onOpenDetail={(id) => void handleOpenHistoryDetail(id)} />
        {(historyDetailLoading || historyDetailError || historyDetail) && (
          <RunHistoryDetailPanel
            detail={historyDetail}
            loading={historyDetailLoading}
            error={historyDetailError}
            onClose={handleCloseHistoryDetail}
          />
        )}
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

function CourseOptionsTable({
  options,
  loading,
  onChoose,
}: {
  options: MotoSyncKhoaHocOption[];
  loading: boolean;
  onChoose: (option: MotoSyncKhoaHocOption) => void;
}) {
  if (loading && options.length === 0) {
    return <div className="state">Đang tải danh sách khóa học...</div>;
  }

  if (options.length === 0) {
    return <div className="moto-sync-muted">Chưa tải danh sách khóa hoặc không có khóa phù hợp.</div>;
  }

  return (
    <div className="table-wrap moto-sync-course-options">
      <table className="table table--moto-sync-course-options">
        <thead>
          <tr>
            <th>Mã khóa</th>
            <th>Tên khóa</th>
            <th>Hạng</th>
            <th>Ngày khai giảng</th>
            <th>Nguồn</th>
            <th>Đích</th>
            <th>Khóa đích</th>
            <th>Chênh lệch</th>
            <th>Chọn</th>
          </tr>
        </thead>
        <tbody>
          {options.map((option) => (
            <tr key={option.maKhoaHoc}>
              <td>{option.maKhoaHoc}</td>
              <td>{option.tenKhoaHoc ?? '-'}</td>
              <td>{option.hangDaoTao ?? option.hangGPLX ?? '-'}</td>
              <td>{formatOptionalDate(option.ngayKhaiGiang)}</td>
              <td>{formatNumber(option.sourceHocVienCount)}</td>
              <td>{formatNumber(option.targetHocVienCount)}</td>
              <td>{option.hasTargetKhoaHoc ? 'Có' : 'Không'}</td>
              <td>
                +{formatNumber(option.sourceOnlyHocVienCount)} / -{formatNumber(option.targetOnlyHocVienCount)}
              </td>
              <td>
                <button type="button" className="btn btn--ghost btn--sm" onClick={() => onChoose(option)}>
                  Chọn
                </button>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function CenterTransferPlanMetrics({ plan }: { plan: MotoCenterTransferPlan }) {
  const rows = [
    ['maKhoaHocCu', plan.maKhoaHocCu],
    ['maKhoaHocMoi', plan.maKhoaHocMoi],
    ['MaCSDT mới trong danh mục', formatDonViCatalogStatus(plan.targetMaCSDTMoiExists, plan.targetMaCSDTMoiTenDV)],
    ['Mã Sở GTVT mới trong danh mục', formatDonViCatalogStatus(plan.targetMaSoGTVTMoiExists, plan.targetMaSoGTVTMoiTenDV)],
    ['sourceKhoaHoc', plan.sourceKhoaHocCount],
    ['sourceBaoCaoI', plan.sourceBaoCaoICount],
    ['sourceNguoiLX', plan.sourceNguoiLXCount],
    ['sourceNguoiLXHoSo', plan.sourceNguoiLXHoSoCount],
    ['sourceNguoiLXHSGiayTo', plan.sourceNguoiLXHSGiayToCount],
    ['targetKhoaHocCu', plan.targetKhoaHocCuCount],
    ['targetKhoaHocMoi', plan.targetKhoaHocMoiCount],
    ['targetBaoCaoICu', plan.targetBaoCaoICuCount],
    ['targetBaoCaoIMoi', plan.targetBaoCaoIMoiCount],
    ['targetNguoiLXHoSoCu', plan.targetNguoiLXHoSoCuCount],
    ['targetNguoiLXHoSoMoi', plan.targetNguoiLXHoSoMoiCount],
    ['targetNguoiLXHSGiayToCu', plan.targetNguoiLXHSGiayToCuCount],
    ['targetNguoiLXHSGiayToMoi', plan.targetNguoiLXHSGiayToMoiCount],
    ['plannedCopyNguoiLXHSGiayTo', plan.plannedCopyNguoiLXHSGiayTo],
  ] as const;

  return (
    <div className="moto-sync-metrics">
      {rows.map(([label, value]) => (
        <div key={label} className="moto-sync-metric">
          <span>{label}</span>
          <strong>{typeof value === 'number' ? formatNumber(value) : value}</strong>
        </div>
      ))}
      <div className={`moto-sync-metric ${plan.executable ? 'is-ok' : 'is-blocked'}`}>
        <span>executable</span>
        <strong>{plan.executable ? 'Có' : 'Không'}</strong>
      </div>
    </div>
  );
}

function formatDonViCatalogStatus(exists: boolean, tenDV: string | null) {
  if (!exists) {
    return 'Không';
  }

  return tenDV?.trim() ? `Có - ${tenDV.trim()}` : 'Có';
}

function PlanMetrics({ plan }: { plan: MotoSyncPlan }) {
  const rows = [
    ['sourceRows', plan.sourceRows],
    ['targetRows', plan.targetRows],
    ['exactMaDkOverlap', plan.exactMaDkOverlap],
    ['sourceOnly', plan.sourceOnly],
    ['targetOnly', plan.targetOnly],
    ['plannedInsertKhoaHoc', plan.plannedInsertKhoaHoc],
    ['plannedInsertBaoCaoI', plan.plannedInsertBaoCaoI],
    ['plannedInsertNguoiLX', plan.plannedInsertNguoiLX],
    ['plannedInsertNguoiLXGPLX', plan.plannedInsertNguoiLXGPLX],
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

function CenterTransferResult({ result }: { result: MotoCenterTransferExecuteResult | null }) {
  if (!result) return null;

  const summary = result.summary;
  return (
    <div className="moto-sync-result">
      <SectionTitle title="Kết quả chuyển MaCSDT" hint={result.status} />
      <p>
        <strong>executed:</strong> {result.executed ? 'true' : 'false'}
      </p>
      <p>{result.message}</p>
      {summary && (
        <div className="moto-sync-result-grid">
          <span>copiedKhoaHoc</span><strong>{formatNumber(summary.copiedKhoaHoc)}</strong>
          <span>copiedBaoCaoI</span><strong>{formatNumber(summary.copiedBaoCaoI)}</strong>
          <span>copiedNguoiLX</span><strong>{formatNumber(summary.copiedNguoiLX)}</strong>
          <span>copiedNguoiLXHoSo</span><strong>{formatNumber(summary.copiedNguoiLXHoSo)}</strong>
          <span>copiedNguoiLXHSGiayTo</span><strong>{formatNumber(summary.copiedNguoiLXHSGiayTo)}</strong>
          <span>updatedNguoiLXHoSo</span><strong>{formatNumber(summary.updatedNguoiLXHoSo)}</strong>
          <span>updatedNguoiLX</span><strong>{formatNumber(summary.updatedNguoiLX)}</strong>
          <span>updatedKhoaHoc</span><strong>{formatNumber(summary.updatedKhoaHoc)}</strong>
          <span>updatedBaoCaoI</span><strong>{formatNumber(summary.updatedBaoCaoI)}</strong>
          <span>updatedGiayTo</span><strong>{formatNumber(summary.updatedGiayTo)}</strong>
          <span>updatedNguoiLXHSGiayTo</span><strong>{formatNumber(summary.updatedNguoiLXHSGiayTo)}</strong>
          <span>targetKhoaHocMoiCountAfter</span><strong>{formatNumber(summary.targetKhoaHocMoiCountAfter)}</strong>
          <span>targetBaoCaoIMoiCountAfter</span><strong>{formatNumber(summary.targetBaoCaoIMoiCountAfter)}</strong>
          <span>targetNguoiLXHoSoMoiCountAfter</span><strong>{formatNumber(summary.targetNguoiLXHoSoMoiCountAfter)}</strong>
          <span>targetNguoiLXHSGiayToMoiCountAfter</span><strong>{formatNumber(summary.targetNguoiLXHSGiayToMoiCountAfter)}</strong>
          <span>targetNguoiLXMoiCountAfter</span><strong>{formatNumber(summary.targetNguoiLXMoiCountAfter)}</strong>
          <span>durationMs</span><strong>{formatNumber(summary.durationMs)}</strong>
        </div>
      )}
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
          <span>insertedKhoaHoc</span><strong>{formatNumber(summary.insertedKhoaHoc)}</strong>
          <span>insertedBaoCaoI</span><strong>{formatNumber(summary.insertedBaoCaoI)}</strong>
          <span>insertedNguoiLX</span><strong>{formatNumber(summary.insertedNguoiLX)}</strong>
          <span>insertedNguoiLXGPLX</span><strong>{formatNumber(summary.insertedNguoiLXGPLX)}</strong>
          <span>insertedNguoiLXHoSo</span><strong>{formatNumber(summary.insertedNguoiLXHoSo)}</strong>
          <span>insertedGiayTo</span><strong>{formatNumber(summary.insertedGiayTo)}</strong>
          <span>updatedNguoiLX</span><strong>{formatNumber(summary.updatedNguoiLX)}</strong>
          <span>updatedNguoiLXHoSo</span><strong>{formatNumber(summary.updatedNguoiLXHoSo)}</strong>
          <span>updatedRows</span><strong>{formatNumber(summary.updatedRows)}</strong>
          <span>deletedRows</span><strong>{formatNumber(summary.deletedRows)}</strong>
          <span>durationMs</span><strong>{formatNumber(summary.durationMs)}</strong>
        </div>
      )}
      {result.afterPlan && (
        <div className="moto-sync-after-plan">
          <SectionTitle title="Kế hoạch sau thực thi" hint={result.hasRemainingWork ? 'Cần kiểm tra lại' : 'Đã sạch'} />
          <div className={`moto-sync-message-list moto-sync-message-list--${result.hasRemainingWork ? 'warning' : 'success'}`}>
            {result.hasRemainingWork
              ? 'Sau thực thi vẫn còn dữ liệu cần xử lý. Vui lòng lập kế hoạch lại để kiểm tra.'
              : 'Sau thực thi không còn dữ liệu cần đồng bộ cho khóa này.'}
          </div>
          <PlanMetrics plan={result.afterPlan} />
          <MessageList title="Blockers sau thực thi" items={result.afterPlan.blockers} variant="error" />
          <MessageList title="Warnings sau thực thi" items={result.afterPlan.warnings} variant="warning" />
          <ErrorList errors={result.afterPlan.errors} />
        </div>
      )}
    </div>
  );
}

function RunHistoryTable({
  rows,
  loading,
  onOpenDetail,
}: {
  rows: MotoSyncRunHistoryListItem[];
  loading: boolean;
  onOpenDetail: (id: number) => void;
}) {
  if (loading && rows.length === 0) {
    return <div className="state">Đang tải lịch sử đồng bộ...</div>;
  }

  if (rows.length === 0) {
    return <div className="state">Chưa có lịch sử đồng bộ Moto TEST.</div>;
  }

  return (
    <div className="table-wrap">
      <table className="table table--moto-sync-history">
        <thead>
          <tr>
            <th>Thời gian</th>
            <th>Mã khóa</th>
            <th>Hướng</th>
            <th>Chế độ</th>
            <th>Trạng thái</th>
            <th>Đã thêm</th>
            <th>Đã cập nhật</th>
            <th>Đã xóa</th>
            <th>Thời gian chạy</th>
            <th>Còn việc</th>
            <th>Chi tiết</th>
          </tr>
        </thead>
        <tbody>
          {rows.map((row) => (
            <tr key={row.id}>
              <td>{formatDateTime(row.createdAt)}</td>
              <td>{row.maKhoaHoc ?? '-'}</td>
              <td>{row.direction}</td>
              <td>{row.syncMode}</td>
              <td>{row.status}</td>
              <td>{formatNumber(row.insertedTotal)}</td>
              <td>{formatNumber(row.updatedRows)}</td>
              <td>{formatNumber(row.deletedRows)}</td>
              <td>{formatNumber(row.durationMs)} ms</td>
              <td>{row.hasRemainingWork ? 'Có' : 'Không'}</td>
              <td>
                <button type="button" className="btn btn--ghost btn--sm" onClick={() => onOpenDetail(row.id)}>
                  Chi tiết
                </button>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function RunHistoryDetailPanel({
  detail,
  loading,
  error,
  onClose,
}: {
  detail: MotoSyncRunHistoryDetail | null;
  loading: boolean;
  error: string | null;
  onClose: () => void;
}) {
  return (
    <div className="moto-sync-history-detail">
      <div className="moto-sync-section-title">
        <strong>Chi tiết lần đồng bộ</strong>
        <button type="button" className="btn btn--ghost btn--sm" onClick={onClose}>
          Đóng
        </button>
      </div>

      {loading && <div className="state">Đang tải chi tiết lịch sử...</div>}
      {error && <div className="moto-sync-message-list moto-sync-message-list--error">{error}</div>}
      {!loading && !error && detail && (
        <>
          <RunHistoryDetailFields detail={detail} />
          <div className="moto-sync-plan-detail-grid">
            <PlanJsonBlock title="Kế hoạch trước thực thi" json={detail.beforePlanJson} emptyText="Không có kế hoạch trước thực thi." />
            <PlanJsonBlock title="Kế hoạch sau thực thi" json={detail.afterPlanJson} emptyText="Không có kế hoạch sau thực thi." />
          </div>
        </>
      )}
    </div>
  );
}

function RunHistoryDetailFields({ detail }: { detail: MotoSyncRunHistoryDetail }) {
  const rows: Array<[string, string | number | boolean | null]> = [
    ['ID', detail.id],
    ['Ngày tạo', formatDateTime(detail.createdAt)],
    ['Bắt đầu', formatDateTime(detail.startedAt)],
    ['Kết thúc', formatDateTime(detail.endedAt)],
    ['Thời gian chạy', `${formatNumber(detail.durationMs)} ms`],
    ['Hướng', detail.direction],
    ['Chế độ', detail.syncMode],
    ['Nguồn', detail.sourceProfileCode],
    ['Đích', detail.targetProfileCode],
    ['Mã khóa học', detail.maKhoaHoc],
    ['Confirm text khớp', detail.confirmTextMatched ? 'Có' : 'Không'],
    ['Đã thực thi', detail.executed ? 'Có' : 'Không'],
    ['Trạng thái', detail.status],
    ['Thông báo', detail.message],
    ['Thêm KhoaHoc', detail.insertedKhoaHoc],
    ['Thêm BaoCaoI', detail.insertedBaoCaoI],
    ['Thêm NguoiLX', detail.insertedNguoiLX],
    ['Thêm NguoiLX_GPLX', detail.insertedNguoiLXGPLX],
    ['Thêm NguoiLX_HoSo', detail.insertedNguoiLXHoSo],
    ['Thêm giấy tờ', detail.insertedGiayTo],
    ['Tổng đã thêm', detail.insertedTotal],
    ['Cập nhật NguoiLX', detail.updatedNguoiLX],
    ['Cập nhật NguoiLX_HoSo', detail.updatedNguoiLXHoSo],
    ['Tổng cập nhật', detail.updatedRows],
    ['Đã xóa', detail.deletedRows],
    ['Còn việc', detail.hasRemainingWork ? 'Có' : 'Không'],
  ];

  return (
    <div className="moto-sync-detail-grid">
      {rows.map(([label, value]) => (
        <div key={label} className="moto-sync-detail-item">
          <span>{label}</span>
          <strong className={label === 'Trạng thái' ? statusClassName(String(value ?? '')) : undefined}>
            {value === null || value === '' ? '-' : String(value)}
          </strong>
        </div>
      ))}
    </div>
  );
}

function PlanJsonBlock({ title, json, emptyText }: { title: string; json: string | null; emptyText: string }) {
  if (!json) {
    return (
      <div className="moto-sync-plan-json-card">
        <SectionTitle title={title} />
        <div className="moto-sync-muted">{emptyText}</div>
      </div>
    );
  }

  const summary = parsePlanJsonSummary(json);
  return (
    <div className="moto-sync-plan-json-card">
      <SectionTitle title={title} hint={summary ? 'Đọc được JSON' : 'Không parse được JSON'} />
      {summary ? (
        <div className="moto-sync-plan-summary-grid">
          {planSummaryRows(summary).map(([label, value]) => (
            <div key={label} className="moto-sync-detail-item">
              <span>{label}</span>
              <strong>{value === null ? '-' : formatNumber(value)}</strong>
            </div>
          ))}
        </div>
      ) : (
        <div className="moto-sync-message-list moto-sync-message-list--warning">
          Không đọc được JSON kế hoạch. Có thể backend đã đổi format hoặc dữ liệu cũ không đúng cấu trúc.
        </div>
      )}
      <details className="moto-sync-raw-json">
        <summary>JSON gốc</summary>
        <pre>{json}</pre>
      </details>
    </div>
  );
}

function parsePlanJsonSummary(json: string): PlanJsonSummary | null {
  try {
    const parsed = JSON.parse(json) as Record<string, unknown>;
    return {
      sourceRows: readNumber(parsed.sourceRows),
      targetRows: readNumber(parsed.targetRows),
      sourceOnly: readNumber(parsed.sourceOnly),
      targetOnly: readNumber(parsed.targetOnly),
      plannedInsertKhoaHoc: readNumber(parsed.plannedInsertKhoaHoc),
      plannedInsertBaoCaoI: readNumber(parsed.plannedInsertBaoCaoI),
      plannedInsertNguoiLX: readNumber(parsed.plannedInsertNguoiLX),
      plannedInsertNguoiLXGPLX: readNumber(parsed.plannedInsertNguoiLXGPLX),
      plannedInsertNguoiLXHoSo: readNumber(parsed.plannedInsertNguoiLXHoSo),
      plannedInsertGiayTo: readNumber(parsed.plannedInsertGiayTo),
      plannedUpdate: readNumber(parsed.plannedUpdate),
      blockersCount: readArrayCount(parsed.blockers),
      warningsCount: readArrayCount(parsed.warnings),
      errorsCount: readArrayCount(parsed.errors),
    };
  } catch {
    return null;
  }
}

function planSummaryRows(summary: PlanJsonSummary): Array<[string, number | null]> {
  return [
    ['sourceRows', summary.sourceRows],
    ['targetRows', summary.targetRows],
    ['sourceOnly', summary.sourceOnly],
    ['targetOnly', summary.targetOnly],
    ['plannedInsertKhoaHoc', summary.plannedInsertKhoaHoc],
    ['plannedInsertBaoCaoI', summary.plannedInsertBaoCaoI],
    ['plannedInsertNguoiLX', summary.plannedInsertNguoiLX],
    ['plannedInsertNguoiLXGPLX', summary.plannedInsertNguoiLXGPLX],
    ['plannedInsertNguoiLXHoSo', summary.plannedInsertNguoiLXHoSo],
    ['plannedInsertGiayTo', summary.plannedInsertGiayTo],
    ['plannedUpdate', summary.plannedUpdate],
    ['blockers', summary.blockersCount],
    ['warnings', summary.warningsCount],
    ['errors', summary.errorsCount],
  ];
}

function readNumber(value: unknown): number | null {
  return typeof value === 'number' && Number.isFinite(value) ? value : null;
}

function readArrayCount(value: unknown): number {
  return Array.isArray(value) ? value.length : 0;
}

function statusClassName(status: string): string {
  if (status === 'ThanhCong') return 'moto-sync-status moto-sync-status--success';
  if (status === 'BiChan') return 'moto-sync-status moto-sync-status--blocked';
  if (status === 'Loi') return 'moto-sync-status moto-sync-status--error';
  return 'moto-sync-status';
}

function formatNumber(value: number): string {
  return new Intl.NumberFormat('vi-VN').format(value);
}

function formatDateTime(value: string): string {
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) {
    return value;
  }

  return new Intl.DateTimeFormat('vi-VN', {
    dateStyle: 'short',
    timeStyle: 'medium',
  }).format(date);
}

function formatOptionalDate(value: string | null): string {
  const trimmed = value?.trim();
  if (!trimmed) {
    return '-';
  }

  const date = parseOptionalDate(trimmed);
  if (!date) {
    return trimmed;
  }

  return new Intl.DateTimeFormat('vi-VN', {
    dateStyle: 'short',
  }).format(date);
}

function parseOptionalDate(value: string): Date | null {
  const isoDate = /^(\d{4})-(\d{2})-(\d{2})(?:[T\s].*)?$/.exec(value);
  if (isoDate) {
    const year = Number(isoDate[1]);
    const month = Number(isoDate[2]);
    const day = Number(isoDate[3]);
    const date = new Date(year, month - 1, day);
    if (date.getFullYear() === year && date.getMonth() === month - 1 && date.getDate() === day) {
      return date;
    }

    return null;
  }

  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? null : date;
}
