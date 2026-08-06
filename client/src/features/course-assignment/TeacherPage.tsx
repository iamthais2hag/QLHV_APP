import {
  useCallback,
  useEffect,
  useState,
  type FormEvent,
} from 'react';
import { useAuth } from '../auth/AuthContext';
import { hasPermission } from '../auth/permissions';
import { useDataVersionRefresh } from '../data-version/useDataVersionRefresh';
import {
  AssignmentApiError,
  createDossierReceiver,
  deleteDossierReceiver,
  getDossierReceiverHistory,
  inactivateDossierReceiver,
  searchSourceTeachers,
  updateDossierReceiver,
} from './api';
import type {
  GiaoVienHoSoCommand,
  GiaoVienHoSoHistory,
  GiaoVienHoSoItem,
  GiaoVienSourceItem,
  PagedResult,
} from './types';
import {
  EmptyState,
  formatDate,
  formatDateTime,
  Modal,
  PageMessage,
  Pager,
  StatusBadge,
} from './ui';

const PAGE_SIZE = 25;

type TeacherTab = 'source' | 'dossier';

const EMPTY_RECEIVER_FORM: GiaoVienHoSoCommand = {
  maGiaoVienHs: '',
  hoTen: '',
  ngaySinh: null,
  soCccd: null,
  trangThai: 'ACTIVE',
  ghiChu: null,
  reason: '',
};

export default function TeacherPage() {
  const { user } = useAuth();
  const canManage = !!user && hasPermission(user.role, 'CanManageDossierReceivers');
  const [tab, setTab] = useState<TeacherTab>('source');
  const [keywordDraft, setKeywordDraft] = useState('');
  const [keyword, setKeyword] = useState('');
  const [sourceProfileCode, setSourceProfileCode] = useState('');
  const [trangThai, setTrangThai] = useState('');
  const [page, setPage] = useState(1);
  const [sourceRows, setSourceRows] = useState<PagedResult<GiaoVienSourceItem> | null>(null);
  const [selectedSourceTeacher, setSelectedSourceTeacher] = useState<GiaoVienSourceItem | null>(null);
  const [receiverRows, setReceiverRows] = useState<PagedResult<GiaoVienHoSoItem> | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [editing, setEditing] = useState<GiaoVienHoSoItem | 'create' | null>(null);
  const [catalogAction, setCatalogAction] = useState<{
    item: GiaoVienHoSoItem;
    action: 'inactive' | 'delete';
  } | null>(null);
  const [historyItem, setHistoryItem] = useState<GiaoVienHoSoItem | null>(null);

  const load = useCallback(async (signal?: AbortSignal) => {
    setLoading(true);
    setError(null);
    try {
      const query = {
        keyword,
        sourceProfileCode,
        trangThai,
        page,
        pageSize: PAGE_SIZE,
      };
      if (tab === 'source') {
        setSourceRows(await searchSourceTeachers(query, signal));
      } else {
        setReceiverRows({ items: [], page: 1, pageSize: PAGE_SIZE, totalItems: 0, totalPages: 0 });
      }
    } catch (loadError) {
      if (!(loadError instanceof DOMException && loadError.name === 'AbortError')) {
        setError(toErrorMessage(loadError, 'Không thể tải danh sách giáo viên.'));
      }
    } finally {
      if (!signal?.aborted) setLoading(false);
    }
  }, [keyword, page, sourceProfileCode, tab, trangThai]);

  useEffect(() => {
    const controller = new AbortController();
    void load(controller.signal);
    return () => controller.abort();
  }, [load]);

  const versionRefresh = useDataVersionRefresh({
    resources: ['giaoVienVersion'],
    onVersionChanged: async () => load(),
  });

  function selectTab(next: TeacherTab) {
    setTab(next);
    setPage(1);
    setError(null);
    setNotice(null);
  }

  function submitSearch(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setPage(1);
    setKeyword(keywordDraft.trim());
  }

  return (
    <div className="assignment-page">
      <section className="panel assignment-hero">
        <div>
          <span className="assignment-eyebrow">Danh mục tích hợp</span>
          <h2>Giáo viên</h2>
          <p>
            Giáo viên đào tạo được đồng bộ từ CSDL nguồn và chỉ đọc.
            Quan hệ giáo viên hồ sơ chỉ được hiển thị khi có bằng chứng mapping từ CSDL nguồn.
          </p>
        </div>
        <span className="assignment-readonly-chip">Không sửa dữ liệu nguồn</span>
      </section>

      <div className="assignment-tabs" role="tablist" aria-label="Loại giáo viên">
        <button
          type="button"
          role="tab"
          aria-selected={tab === 'source'}
          className={tab === 'source' ? 'is-active' : ''}
          onClick={() => selectTab('source')}
        >
          Giáo viên đào tạo
          <small>App_GiaoVien · chỉ đọc nguồn</small>
        </button>
        <button
          type="button"
          role="tab"
          aria-selected={tab === 'dossier'}
          className={tab === 'dossier' ? 'is-active' : ''}
          onClick={() => selectTab('dossier')}
        >
          Giáo viên hồ sơ
          <small>Chờ bằng chứng mapping nguồn · chỉ đọc</small>
        </button>
      </div>

      <form className="toolbar" onSubmit={submitSearch}>
        <div className="toolbar__row">
          <label className="field">
            <span className="field__label">
              {tab === 'source' ? 'Mã giáo viên hoặc họ tên' : 'Mã hồ sơ, họ tên hoặc CCCD'}
            </span>
            <input
              className="field__input"
              value={keywordDraft}
              onChange={(event) => setKeywordDraft(event.target.value)}
              maxLength={120}
              placeholder="Nhập từ khóa..."
            />
          </label>
          {tab === 'source' && (
            <label className="field">
              <span className="field__label">Nguồn</span>
              <select
                className="field__input"
                value={sourceProfileCode}
                onChange={(event) => {
                  setSourceProfileCode(event.target.value);
                  setPage(1);
                }}
              >
                <option value="">Tất cả OTO/MOTO</option>
                <option value="CSDT_OTO">OTO</option>
                <option value="CSDT_MOTO">MOTO</option>
              </select>
            </label>
          )}
          <label className="field">
            <span className="field__label">Trạng thái</span>
            <select
              className="field__input"
              value={trangThai}
              onChange={(event) => {
                setTrangThai(event.target.value);
                setPage(1);
              }}
            >
              <option value="">Tất cả trạng thái</option>
              <option value="ACTIVE">Hoạt động</option>
              <option value="INACTIVE">Ngừng hoạt động</option>
              <option value="MANUAL_REVIEW">Cần kiểm tra</option>
            </select>
          </label>
          <div className="toolbar__actions">
            <button type="submit" className="btn btn--primary" disabled={loading}>
              Tìm kiếm
            </button>
            <button type="button" className="btn btn--ghost" onClick={() => void load()} disabled={loading}>
              {loading ? 'Đang tải...' : 'Tải lại'}
            </button>
            {false && tab === 'dossier' && canManage && (
              <button
                type="button"
                className="btn btn--secondary"
                onClick={() => setEditing('create')}
              >
                Thêm người nhận hồ sơ
              </button>
            )}
          </div>
        </div>
      </form>

      {error && <PageMessage kind="error">{error}</PageMessage>}
      {notice && <PageMessage kind="success">{notice}</PageMessage>}
      {versionRefresh.error && <PageMessage kind="warning">{versionRefresh.error}</PageMessage>}

      {tab === 'source' ? (
        <SourceTeacherTable
          result={sourceRows}
          loading={loading}
          onPage={setPage}
          onDetail={setSelectedSourceTeacher}
        />
      ) : (
        <div className="panel">
          <PageMessage kind="warning">
            Chưa có bằng chứng ổn định để ánh xạ “Giáo viên hồ sơ/Người nhận hồ sơ” từ CSDL_OTO hoặc CSDL_MOTO.
            Chức năng này đang chờ mapping nguồn và không cho thêm, sửa hoặc nhập tay.
          </PageMessage>
          <EmptyState>Chờ bằng chứng quan hệ nguồn.</EmptyState>
          {false && (
            <DossierReceiverTable
              result={receiverRows}
              loading={loading}
              canManage={canManage}
              onPage={setPage}
              onEdit={setEditing}
              onHistory={setHistoryItem}
              onAction={(item, action) => setCatalogAction({ item, action })}
            />
          )}
        </div>
      )}

      {editing && (
        <DossierReceiverEditor
          item={editing === 'create' ? null : editing}
          onClose={() => setEditing(null)}
          onSaved={(saved) => {
            setEditing(null);
            setNotice(
              editing === 'create'
                ? `Đã tạo ${saved.maGiaoVienHs} · ${saved.hoTen}.`
                : `Đã cập nhật ${saved.maGiaoVienHs} · ${saved.hoTen}.`,
            );
            void load();
          }}
          onError={(message) => {
            setEditing(null);
            setError(message);
            void load();
          }}
        />
      )}

      {catalogAction && (
        <CatalogActionDialog
          item={catalogAction.item}
          action={catalogAction.action}
          onClose={() => setCatalogAction(null)}
          onDone={() => {
            setCatalogAction(null);
            setNotice(
              catalogAction.action === 'delete'
                ? 'Đã xóa mềm người nhận hồ sơ; lịch sử và tham chiếu được giữ nguyên.'
                : 'Đã chuyển người nhận hồ sơ sang trạng thái ngừng hoạt động.',
            );
            void load();
          }}
          onError={(message) => {
            setCatalogAction(null);
            setError(message);
            void load();
          }}
        />
      )}

      {historyItem && (
        <DossierReceiverHistoryDialog
          item={historyItem}
          onClose={() => setHistoryItem(null)}
        />
      )}

      {selectedSourceTeacher && (
        <SourceTeacherDetailDialog
          item={selectedSourceTeacher}
          onClose={() => setSelectedSourceTeacher(null)}
        />
      )}
    </div>
  );
}

function SourceTeacherTable({
  result,
  loading,
  onPage,
  onDetail,
}: {
  result: PagedResult<GiaoVienSourceItem> | null;
  loading: boolean;
  onPage: (page: number) => void;
  onDetail: (item: GiaoVienSourceItem) => void;
}) {
  if (loading && !result) return <div className="panel"><EmptyState>Đang tải giáo viên...</EmptyState></div>;
  if (!result?.items.length) return <div className="panel"><EmptyState>Không có giáo viên phù hợp.</EmptyState></div>;
  return (
    <>
      <div className="table-wrap">
        <table className="table assignment-table">
          <thead>
            <tr>
              <th>Nguồn</th>
              <th>Mã GV</th>
              <th>Họ và tên</th>
              <th>Ngày sinh</th>
              <th>CCCD</th>
              <th>Hạng đào tạo</th>
              <th>Đang sử dụng</th>
              <th>Trạng thái</th>
              <th>Quyền sở hữu</th>
              <th>Thao tác</th>
            </tr>
          </thead>
          <tbody>
            {result.items.map((item) => (
              <tr key={`${item.sourceProfileCode}:${item.giaoVienId}`}>
                <td><span className="assignment-source">{item.sourceProfileCode}</span></td>
                <td><strong>{item.maGv}</strong></td>
                <td>{item.hoTen}</td>
                <td>{formatDate(item.ngaySinh)}</td>
                <td>{item.soCccd || '—'}</td>
                <td>{item.hangDaoTao || '—'}</td>
                <td>
                  {item.courseUsageCount} khóa · {item.studentUsageCount} học viên
                </td>
                <td>
                  <StatusBadge
                    active={item.isActive}
                    manualReview={item.isManualReview}
                    label={item.trangThai}
                  />
                </td>
                <td><span className="assignment-readonly-chip">Chỉ đọc nguồn</span></td>
                <td>
                  <button
                    type="button"
                    className="btn btn--ghost btn--sm"
                    onClick={() => onDetail(item)}
                  >
                    Mở chi tiết
                  </button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      <Pager result={result} onPage={onPage} disabled={loading} />
    </>
  );
}

function SourceTeacherDetailDialog({
  item,
  onClose,
}: {
  item: GiaoVienSourceItem;
  onClose: () => void;
}) {
  return (
    <Modal title={`Chi tiết giáo viên · ${item.maGv}`} onClose={onClose}>
      <section className="assignment-stack">
        <div className="assignment-row-actions">
          <StatusBadge
            active={item.isActive}
            manualReview={item.isManualReview}
            label={item.trangThai}
          />
          <span className="assignment-readonly-chip">App_GiaoVien · chỉ đọc nguồn</span>
        </div>
        <dl className="assignment-facts">
          <TeacherFact label="ID giáo viên" value={String(item.giaoVienId)} />
          <TeacherFact label="Nguồn" value={item.sourceProfileCode} />
          <TeacherFact label="Mã giáo viên" value={item.maGv} />
          <TeacherFact label="Họ và tên" value={item.hoTen} />
          <TeacherFact label="Ngày sinh" value={formatDate(item.ngaySinh)} />
          <TeacherFact label="CCCD" value={item.soCccd || '—'} />
          <TeacherFact label="Hạng đào tạo" value={item.hangDaoTao || '—'} />
          <TeacherFact
            label="Đang sử dụng"
            value={`${item.courseUsageCount} khóa · ${item.studentUsageCount} học viên`}
          />
        </dl>
      </section>
    </Modal>
  );
}

function TeacherFact({ label, value }: { label: string; value: string }) {
  return (
    <div>
      <dt>{label}</dt>
      <dd>{value}</dd>
    </div>
  );
}

function DossierReceiverTable({
  result,
  loading,
  canManage,
  onPage,
  onEdit,
  onHistory,
  onAction,
}: {
  result: PagedResult<GiaoVienHoSoItem> | null;
  loading: boolean;
  canManage: boolean;
  onPage: (page: number) => void;
  onEdit: (item: GiaoVienHoSoItem) => void;
  onHistory: (item: GiaoVienHoSoItem) => void;
  onAction: (item: GiaoVienHoSoItem, action: 'inactive' | 'delete') => void;
}) {
  if (loading && !result) return <div className="panel"><EmptyState>Đang tải người nhận hồ sơ...</EmptyState></div>;
  if (!result?.items.length) return <div className="panel"><EmptyState>Chưa có người nhận hồ sơ phù hợp.</EmptyState></div>;
  return (
    <>
      <div className="table-wrap">
        <table className="table assignment-table">
          <thead>
            <tr>
              <th>Mã hồ sơ</th>
              <th>Họ và tên</th>
              <th>Ngày sinh</th>
              <th>CCCD</th>
              <th>Tham chiếu</th>
              <th>Trạng thái</th>
              <th>Cập nhật gần nhất</th>
              <th>Thao tác</th>
            </tr>
          </thead>
          <tbody>
            {result.items.map((item) => (
              <tr key={item.giaoVienHsId}>
                <td><strong>{item.maGiaoVienHs}</strong></td>
                <td>{item.hoTen}</td>
                <td>{formatDate(item.ngaySinh)}</td>
                <td>{item.soCccd || '—'}</td>
                <td>{item.referenceCount.toLocaleString('vi-VN')}</td>
                <td>
                  <StatusBadge
                    active={!item.isDeleted && item.trangThai === 'ACTIVE'}
                    label={item.isDeleted ? 'Đã xóa mềm' : item.trangThai}
                  />
                </td>
                <td>
                  {formatDateTime(item.updatedAtUtc)}
                  {item.updatedBy && <small className="assignment-cell-note">{item.updatedBy}</small>}
                </td>
                <td>
                  <div className="assignment-row-actions">
                    <button type="button" className="btn btn--ghost btn--sm" onClick={() => onHistory(item)}>
                      Lịch sử
                    </button>
                    {canManage && !item.isDeleted && (
                      <>
                        <button type="button" className="btn btn--ghost btn--sm" onClick={() => onEdit(item)}>
                          Sửa
                        </button>
                        {item.trangThai === 'ACTIVE' && (
                          <button type="button" className="btn btn--ghost btn--sm" onClick={() => onAction(item, 'inactive')}>
                            Ngừng dùng
                          </button>
                        )}
                        <button type="button" className="btn btn--ghost btn--sm" onClick={() => onAction(item, 'delete')}>
                          Xóa mềm
                        </button>
                      </>
                    )}
                  </div>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      <Pager result={result} onPage={onPage} disabled={loading} />
    </>
  );
}

function DossierReceiverEditor({
  item,
  onClose,
  onSaved,
  onError,
}: {
  item: GiaoVienHoSoItem | null;
  onClose: () => void;
  onSaved: (saved: GiaoVienHoSoItem) => void;
  onError: (message: string) => void;
}) {
  const [form, setForm] = useState<GiaoVienHoSoCommand>(() => item
    ? {
        maGiaoVienHs: item.maGiaoVienHs,
        hoTen: item.hoTen,
        ngaySinh: item.ngaySinh,
        soCccd: item.soCccd,
        trangThai: item.trangThai,
        ghiChu: item.ghiChu,
        reason: '',
        rowVersion: item.rowVersion,
      }
    : EMPTY_RECEIVER_FORM);
  const [submitting, setSubmitting] = useState(false);
  const [validation, setValidation] = useState<string | null>(null);

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const message = validateReceiver(form);
    if (message) {
      setValidation(message);
      return;
    }
    const command: GiaoVienHoSoCommand = {
      ...form,
      maGiaoVienHs: form.maGiaoVienHs.trim(),
      hoTen: normalizeDisplayName(form.hoTen),
      soCccd: form.soCccd?.trim() || null,
      ghiChu: form.ghiChu?.trim() || null,
      reason: form.reason.trim(),
    };
    setSubmitting(true);
    setValidation(null);
    try {
      onSaved(item
        ? await updateDossierReceiver(item.giaoVienHsId, command)
        : await createDossierReceiver(command));
    } catch (saveError) {
      onError(toErrorMessage(saveError, 'Không thể lưu người nhận hồ sơ.'));
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <Modal title={item ? 'Cập nhật người nhận hồ sơ' : 'Thêm người nhận hồ sơ'} onClose={onClose}>
      <form className="assignment-form" onSubmit={submit}>
        {validation && <PageMessage kind="error">{validation}</PageMessage>}
        <label className="field">
          <span className="field__label">Mã giáo viên hồ sơ *</span>
          <input
            className="field__input"
            value={form.maGiaoVienHs}
            onChange={(event) => setForm({ ...form, maGiaoVienHs: event.target.value })}
            maxLength={50}
            disabled={submitting}
            autoFocus
          />
        </label>
        <label className="field">
          <span className="field__label">Họ và tên *</span>
          <input
            className="field__input"
            value={form.hoTen}
            onChange={(event) => setForm({ ...form, hoTen: event.target.value })}
            maxLength={255}
            disabled={submitting}
          />
        </label>
        <div className="assignment-form__grid">
          <label className="field">
            <span className="field__label">Ngày sinh</span>
            <input
              type="date"
              className="field__input"
              value={toDateInput(form.ngaySinh)}
              onChange={(event) => setForm({ ...form, ngaySinh: event.target.value || null })}
              disabled={submitting}
            />
          </label>
          <label className="field">
            <span className="field__label">Số CCCD</span>
            <input
              className="field__input"
              value={form.soCccd || ''}
              onChange={(event) => setForm({ ...form, soCccd: event.target.value || null })}
              inputMode="numeric"
              maxLength={12}
              disabled={submitting}
            />
          </label>
          <label className="field">
            <span className="field__label">Trạng thái</span>
            <select
              className="field__input"
              value={form.trangThai}
              onChange={(event) => setForm({ ...form, trangThai: event.target.value })}
              disabled={submitting}
            >
              <option value="ACTIVE">Hoạt động</option>
              <option value="INACTIVE">Ngừng hoạt động</option>
            </select>
          </label>
        </div>
        <label className="field">
          <span className="field__label">Ghi chú</span>
          <textarea
            className="assignment-textarea"
            value={form.ghiChu || ''}
            onChange={(event) => setForm({ ...form, ghiChu: event.target.value || null })}
            maxLength={1000}
            disabled={submitting}
          />
        </label>
        <label className="field">
          <span className="field__label">Lý do thay đổi *</span>
          <input
            className="field__input"
            value={form.reason}
            onChange={(event) => setForm({ ...form, reason: event.target.value })}
            maxLength={500}
            disabled={submitting}
          />
        </label>
        <p className="assignment-form__hint">
          Mã và CCCD phải duy nhất. Hệ thống chuẩn hóa họ tên và kiểm tra RowVersion khi lưu.
        </p>
        <div className="assignment-modal__actions">
          <button type="button" className="btn btn--ghost" onClick={onClose} disabled={submitting}>Hủy</button>
          <button type="submit" className="btn btn--primary" disabled={submitting}>
            {submitting ? 'Đang lưu...' : 'Lưu'}
          </button>
        </div>
      </form>
    </Modal>
  );
}

function CatalogActionDialog({
  item,
  action,
  onClose,
  onDone,
  onError,
}: {
  item: GiaoVienHoSoItem;
  action: 'inactive' | 'delete';
  onClose: () => void;
  onDone: () => void;
  onError: (message: string) => void;
}) {
  const [reason, setReason] = useState('');
  const [submitting, setSubmitting] = useState(false);

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!reason.trim()) return;
    setSubmitting(true);
    try {
      if (action === 'delete') {
        await deleteDossierReceiver(item, reason.trim());
      } else {
        await inactivateDossierReceiver(item, reason.trim());
      }
      onDone();
    } catch (actionError) {
      onError(toErrorMessage(actionError, 'Không thể cập nhật người nhận hồ sơ.'));
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <Modal title={action === 'delete' ? 'Xóa mềm người nhận hồ sơ' : 'Ngừng sử dụng người nhận hồ sơ'} onClose={onClose}>
      <form className="assignment-form" onSubmit={submit}>
        <PageMessage kind="warning">
          {item.maGiaoVienHs} · {item.hoTen} đang có {item.referenceCount} tham chiếu.
          Lịch sử không bị xóa.
        </PageMessage>
        <label className="field">
          <span className="field__label">Lý do *</span>
          <input
            className="field__input"
            value={reason}
            onChange={(event) => setReason(event.target.value)}
            maxLength={500}
            disabled={submitting}
            autoFocus
          />
        </label>
        <div className="assignment-modal__actions">
          <button type="button" className="btn btn--ghost" onClick={onClose} disabled={submitting}>Hủy</button>
          <button type="submit" className="btn btn--primary" disabled={submitting || !reason.trim()}>
            {submitting ? 'Đang lưu...' : 'Xác nhận'}
          </button>
        </div>
      </form>
    </Modal>
  );
}

function DossierReceiverHistoryDialog({
  item,
  onClose,
}: {
  item: GiaoVienHoSoItem;
  onClose: () => void;
}) {
  const [history, setHistory] = useState<GiaoVienHoSoHistory | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    const controller = new AbortController();
    getDossierReceiverHistory(item.giaoVienHsId, controller.signal)
      .then(setHistory)
      .catch((historyError) => {
        if (!(historyError instanceof DOMException && historyError.name === 'AbortError')) {
          setError(toErrorMessage(historyError, 'Không thể tải lịch sử.'));
        }
      });
    return () => controller.abort();
  }, [item.giaoVienHsId]);

  return (
    <Modal title={`Lịch sử · ${item.maGiaoVienHs}`} onClose={onClose}>
      {error && <PageMessage kind="error">{error}</PageMessage>}
      {!history && !error && <EmptyState>Đang tải lịch sử...</EmptyState>}
      {history && (
        <>
          <p className="assignment-form__hint">
            {history.referenceCount} tham chiếu đang giữ; không cho hard-delete khi đã được sử dụng.
          </p>
          <div className="assignment-timeline">
            {history.items.length === 0 && <EmptyState>Chưa có sự kiện lịch sử.</EmptyState>}
            {history.items.map((event, index) => (
              <article key={`${event.occurredAtUtc}:${index}`}>
                <strong>{event.action}</strong>
                <span>{formatDateTime(event.occurredAtUtc)} · {event.actor || 'Hệ thống'}</span>
                <p>{event.reason || 'Không có lý do.'}</p>
              </article>
            ))}
          </div>
        </>
      )}
    </Modal>
  );
}

function validateReceiver(form: GiaoVienHoSoCommand): string | null {
  if (!form.maGiaoVienHs.trim()) return 'Vui lòng nhập mã giáo viên hồ sơ.';
  if (!form.hoTen.trim()) return 'Vui lòng nhập họ và tên.';
  if (!form.reason.trim()) return 'Vui lòng nhập lý do thay đổi.';
  if (form.soCccd && !/^(?:\d{9}|\d{12})$/.test(form.soCccd.trim())) {
    return 'Số CCCD phải có đúng 9 hoặc 12 chữ số.';
  }
  return null;
}

function normalizeDisplayName(value: string): string {
  return value.trim().replace(/\s+/g, ' ');
}

function toDateInput(value: string | null): string {
  return value?.slice(0, 10) ?? '';
}

function toErrorMessage(error: unknown, fallback: string): string {
  if (error instanceof AssignmentApiError) return error.message;
  return error instanceof Error ? error.message : fallback;
}
