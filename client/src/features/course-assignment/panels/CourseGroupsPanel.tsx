import {
  useState,
  type FormEvent,
} from 'react';
import {
  AssignmentApiError,
  confirmGroupDefaults,
  createTrainingGroup,
  inactivateTrainingGroup,
  previewGroupDefaults,
  updateTrainingGroup,
} from '../api';
import type {
  AssignmentLookups,
  AssignmentPreview,
  AssignmentReference,
  KhoaHocDetail,
  PropagationMode,
  TrainingGroup,
  TrainingGroupCommand,
} from '../types';
import {
  createIdempotencyKey,
  EmptyState,
  formatDateTime,
  Modal,
  PageMessage,
  PreviewStatusBadge,
  ReferenceLabel,
  StatusBadge,
} from '../ui';
import SearchLookup, { filterLookupOptions, normalizeLookupText } from '../../../components/SearchLookup';

const MAX_RENDERED_PREVIEW_ROWS = 200;

export default function CourseGroupsPanel({
  mode,
  course,
  groups,
  lookups,
  canManage,
  onChanged,
  onError,
}: {
  mode: 'groups' | 'resources';
  course: KhoaHocDetail;
  groups: TrainingGroup[];
  lookups: AssignmentLookups;
  canManage: boolean;
  onChanged: (message: string) => void;
  onError: (message: string) => void;
}) {
  const [editing, setEditing] = useState<TrainingGroup | 'create' | null>(null);
  const [defaultsGroup, setDefaultsGroup] = useState<TrainingGroup | null>(null);
  const [inactivating, setInactivating] = useState<TrainingGroup | null>(null);

  if (mode === 'resources') {
    return (
      <section className="assignment-stack">
        <section className="panel">
          <header className="assignment-section-heading">
            <div>
              <span className="assignment-eyebrow">4 · Giáo viên và xe</span>
              <h3>Danh mục chọn và mặc định nhóm</h3>
              <p>
                Lookup mới chỉ gồm tham chiếu active. Tham chiếu lịch sử inactive vẫn được giữ nhãn,
                không tự thay thế.
              </p>
            </div>
          </header>
          <div className="assignment-resource-grid">
            <ResourceList title="Giáo viên đứng lớp" items={lookups.teachers} />
            <ResourceList title="Xe tập lái / xe bài số 10" items={lookups.vehicles} />
            <ResourceList title="Người nhận hồ sơ" items={lookups.dossierReceivers} />
          </div>
        </section>
        <GroupTable
          groups={groups}
          canManage={canManage}
          resourceMode
          onEdit={setEditing}
          onDefaults={setDefaultsGroup}
          onInactive={setInactivating}
        />
        {defaultsGroup && (
          <GroupDefaultsDialog
            group={defaultsGroup}
            lookups={lookups}
            onClose={() => setDefaultsGroup(null)}
            onConfirmed={(message) => {
              setDefaultsGroup(null);
              onChanged(message);
            }}
            onError={(message) => {
              setDefaultsGroup(null);
              onError(message);
            }}
          />
        )}
      </section>
    );
  }

  return (
    <section className="assignment-stack">
      <section className="panel assignment-section-heading">
        <div>
          <span className="assignment-eyebrow">3 · Nhóm đào tạo</span>
          <h3>Nhóm trong khóa {course.maKhoa}</h3>
          <p>
            Mã nhóm duy nhất trong khóa. Xóa là chuyển inactive; tất cả lịch sử và tham chiếu được giữ.
          </p>
        </div>
        {canManage && (
          <button type="button" className="btn btn--primary" onClick={() => setEditing('create')}>
            Tạo nhóm
          </button>
        )}
      </section>
      <GroupTable
        groups={groups}
        canManage={canManage}
        onEdit={setEditing}
        onDefaults={setDefaultsGroup}
        onInactive={setInactivating}
      />
      {editing && (
        <GroupEditorDialog
          course={course}
          group={editing === 'create' ? null : editing}
          lookups={lookups}
          onClose={() => setEditing(null)}
          onSaved={(message) => {
            setEditing(null);
            onChanged(message);
          }}
          onError={(message) => {
            setEditing(null);
            onError(message);
          }}
        />
      )}
      {defaultsGroup && (
        <GroupDefaultsDialog
          group={defaultsGroup}
          lookups={lookups}
          onClose={() => setDefaultsGroup(null)}
          onConfirmed={(message) => {
            setDefaultsGroup(null);
            onChanged(message);
          }}
          onError={(message) => {
            setDefaultsGroup(null);
            onError(message);
          }}
        />
      )}
      {inactivating && (
        <InactivateGroupDialog
          course={course}
          group={inactivating}
          onClose={() => setInactivating(null)}
          onDone={() => {
            setInactivating(null);
            onChanged(`Đã chuyển nhóm ${inactivating.maNhom} sang trạng thái ngừng hoạt động.`);
          }}
          onError={(message) => {
            setInactivating(null);
            onError(message);
          }}
        />
      )}
    </section>
  );
}

function GroupTable({
  groups,
  canManage,
  resourceMode = false,
  onEdit,
  onDefaults,
  onInactive,
}: {
  groups: TrainingGroup[];
  canManage: boolean;
  resourceMode?: boolean;
  onEdit: (group: TrainingGroup) => void;
  onDefaults: (group: TrainingGroup) => void;
  onInactive: (group: TrainingGroup) => void;
}) {
  if (groups.length === 0) {
    return <div className="panel"><EmptyState>Khóa học chưa có nhóm đào tạo.</EmptyState></div>;
  }
  return (
    <div className="table-wrap">
      <table className="table assignment-table assignment-table--groups">
        <thead>
          <tr>
            <th>Thứ tự</th>
            <th>Nhóm</th>
            <th>Giáo viên mặc định</th>
            <th>Xe tập lái mặc định</th>
            <th>Xe bài số 10 mặc định</th>
            <th>Học viên</th>
            <th>Trạng thái</th>
            {canManage && <th>Thao tác</th>}
          </tr>
        </thead>
        <tbody>
          {groups.map((group) => (
            <tr key={group.groupId}>
              <td>{group.thuTu}</td>
              <td>
                <strong>{group.maNhom}</strong>
                <small className="assignment-cell-note">{group.tenNhom}</small>
              </td>
              <td><ReferenceLabel value={group.defaultClassTeacher} /></td>
              <td><ReferenceLabel value={group.defaultTrainingVehicle} /></td>
              <td><ReferenceLabel value={group.defaultFigure10Vehicle} /></td>
              <td>{group.studentCount.toLocaleString('vi-VN')}</td>
              <td><StatusBadge active={group.isActive} label={group.trangThai} /></td>
              {canManage && (
                <td>
                  <div className="assignment-row-actions">
                    {!resourceMode && (
                      <button type="button" className="btn btn--ghost btn--sm" onClick={() => onEdit(group)}>
                        Sửa nhóm
                      </button>
                    )}
                    <button type="button" className="btn btn--secondary btn--sm" onClick={() => onDefaults(group)}>
                      Đổi mặc định
                    </button>
                    {!resourceMode && group.isActive && (
                      <button type="button" className="btn btn--ghost btn--sm" onClick={() => onInactive(group)}>
                        Ngừng dùng
                      </button>
                    )}
                  </div>
                </td>
              )}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function ResourceList({
  title,
  items,
}: {
  title: string;
  items: AssignmentReference[];
}) {
  const active = items.filter((item) => item.isActive);
  return (
    <article className="assignment-resource-card">
      <header>
        <strong>{title}</strong>
        <span>{active.length.toLocaleString('vi-VN')} active</span>
      </header>
      <div>
        {active.slice(0, 8).map((item) => (
          <span key={item.id}>
            <b>{item.code}</b>
            {item.label}
            {item.isManualReview && <StatusBadge active={false} manualReview />}
          </span>
        ))}
        {active.length === 0 && <span className="assignment-muted">Chưa có tham chiếu active.</span>}
        {active.length > 8 && <small>Và {active.length - 8} mục khác trong ô chọn.</small>}
      </div>
    </article>
  );
}

function GroupEditorDialog({
  course,
  group,
  lookups,
  onClose,
  onSaved,
  onError,
}: {
  course: KhoaHocDetail;
  group: TrainingGroup | null;
  lookups: AssignmentLookups;
  onClose: () => void;
  onSaved: (message: string) => void;
  onError: (message: string) => void;
}) {
  const [form, setForm] = useState<TrainingGroupCommand>({
    maNhom: group?.maNhom ?? '',
    tenNhom: group?.tenNhom ?? '',
    thuTu: group?.thuTu ?? 1,
    defaultClassTeacherId: group?.defaultClassTeacher?.id ?? null,
    defaultTrainingVehicleId: group?.defaultTrainingVehicle?.id ?? null,
    defaultFigure10VehicleId: group?.defaultFigure10Vehicle?.id ?? null,
    reason: '',
    rowVersion: group?.rowVersion,
  });
  const [submitting, setSubmitting] = useState(false);
  const [validation, setValidation] = useState<string | null>(null);

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!form.maNhom.trim() || !form.tenNhom.trim() || !form.reason.trim()) {
      setValidation('Mã nhóm, tên nhóm và lý do là bắt buộc.');
      return;
    }
    setSubmitting(true);
    setValidation(null);
    try {
      const command = {
        ...form,
        maNhom: form.maNhom.trim(),
        tenNhom: form.tenNhom.trim().replace(/\s+/g, ' '),
        reason: form.reason.trim(),
      };
      const saved = group
        ? await updateTrainingGroup(course.khoaHocId, group.groupId, command)
        : await createTrainingGroup(course.khoaHocId, command);
      onSaved(`${group ? 'Đã cập nhật' : 'Đã tạo'} nhóm ${saved.maNhom} · ${saved.tenNhom}.`);
    } catch (saveError) {
      onError(toMessage(saveError, 'Không thể lưu nhóm đào tạo.'));
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <Modal title={group ? `Sửa nhóm ${group.maNhom}` : 'Tạo nhóm đào tạo'} onClose={onClose}>
      <form className="assignment-form" onSubmit={submit}>
        {validation && <PageMessage kind="error">{validation}</PageMessage>}
        {group && (
          <PageMessage kind="info">
            Màn hình này sửa thông tin nhóm. Dùng “Đổi mặc định” để preview/confirm propagation.
          </PageMessage>
        )}
        <div className="assignment-form__grid">
          <label className="field">
            <span className="field__label">Mã nhóm *</span>
            <input
              className="field__input"
              value={form.maNhom}
              onChange={(event) => setForm({ ...form, maNhom: event.target.value })}
              maxLength={50}
              disabled={submitting}
              autoFocus
            />
          </label>
          <label className="field">
            <span className="field__label">Thứ tự *</span>
            <input
              type="number"
              min={0}
              max={9999}
              className="field__input"
              value={form.thuTu}
              onChange={(event) => setForm({ ...form, thuTu: Number(event.target.value) })}
              disabled={submitting}
            />
          </label>
        </div>
        <label className="field">
          <span className="field__label">Tên nhóm *</span>
          <input
            className="field__input"
            value={form.tenNhom}
            onChange={(event) => setForm({ ...form, tenNhom: event.target.value })}
            maxLength={255}
            disabled={submitting}
          />
        </label>
        {!group && (
          <div className="assignment-form__grid">
            <ReferenceSelect
              label="Giáo viên mặc định"
              value={form.defaultClassTeacherId}
              items={lookups.teachers}
              onChange={(value) => setForm({ ...form, defaultClassTeacherId: value })}
            />
            <ReferenceSelect
              label="Xe tập lái mặc định"
              value={form.defaultTrainingVehicleId}
              items={lookups.vehicles}
              onChange={(value) => setForm({ ...form, defaultTrainingVehicleId: value })}
            />
            <ReferenceSelect
              label="Xe bài số 10 mặc định"
              value={form.defaultFigure10VehicleId}
              items={lookups.vehicles}
              onChange={(value) => setForm({ ...form, defaultFigure10VehicleId: value })}
            />
          </div>
        )}
        <label className="field">
          <span className="field__label">Lý do *</span>
          <input
            className="field__input"
            value={form.reason}
            onChange={(event) => setForm({ ...form, reason: event.target.value })}
            maxLength={500}
            disabled={submitting}
          />
        </label>
        <div className="assignment-modal__actions">
          <button type="button" className="btn btn--ghost" onClick={onClose} disabled={submitting}>Hủy</button>
          <button type="submit" className="btn btn--primary" disabled={submitting}>
            {submitting ? 'Đang lưu...' : 'Lưu nhóm'}
          </button>
        </div>
      </form>
    </Modal>
  );
}

function GroupDefaultsDialog({
  group,
  lookups,
  onClose,
  onConfirmed,
  onError,
}: {
  group: TrainingGroup;
  lookups: AssignmentLookups;
  onClose: () => void;
  onConfirmed: (message: string) => void;
  onError: (message: string) => void;
}) {
  const [teacherId, setTeacherId] = useState(group.defaultClassTeacher?.id ?? null);
  const [trainingVehicleId, setTrainingVehicleId] = useState(group.defaultTrainingVehicle?.id ?? null);
  const [figure10VehicleId, setFigure10VehicleId] = useState(group.defaultFigure10Vehicle?.id ?? null);
  const [mode, setMode] = useState<PropagationMode>('UNOVERRIDDEN_ONLY');
  const [reason, setReason] = useState('');
  const [preview, setPreview] = useState<AssignmentPreview | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function makePreview(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!reason.trim()) {
      setError('Vui lòng nhập lý do thay đổi.');
      return;
    }
    setSubmitting(true);
    setError(null);
    try {
      setPreview(await previewGroupDefaults(group.groupId, {
        rowVersion: group.rowVersion,
        mode,
        defaultClassTeacherId: teacherId,
        defaultTrainingVehicleId: trainingVehicleId,
        defaultFigure10VehicleId: figure10VehicleId,
        reason: reason.trim(),
      }));
    } catch (previewError) {
      handleError(previewError, 'Không thể tạo preview thay mặc định nhóm.');
    } finally {
      setSubmitting(false);
    }
  }

  async function confirm() {
    if (!preview) return;
    setSubmitting(true);
    try {
      const result = await confirmGroupDefaults(group.groupId, {
        previewToken: preview.previewToken,
        idempotencyKey: createIdempotencyKey(),
        reason: reason.trim(),
      });
      onConfirmed(
        `Đã đổi mặc định nhóm ${group.maNhom}; ${result.changedCount} snapshot học viên được cập nhật, `
        + `${result.noChangeCount} không đổi.`,
      );
    } catch (confirmError) {
      handleError(confirmError, 'Không thể xác nhận mặc định nhóm.');
    } finally {
      setSubmitting(false);
    }
  }

  function handleError(failure: unknown, fallback: string) {
    const message = toMessage(failure, fallback);
    if (failure instanceof AssignmentApiError && failure.isConcurrencyConflict) {
      onError(message);
    } else {
      setError(message);
      setPreview(null);
    }
  }

  if (preview) {
    return (
      <Modal title={`Preview mặc định nhóm ${group.maNhom}`} onClose={() => !submitting && setPreview(null)} wide>
        {error && <PageMessage kind="error">{error}</PageMessage>}
        <PageMessage kind={preview.conflictCount || preview.invalidCount ? 'warning' : 'info'}>
          Token niêm phong hết hạn {formatDateTime(preview.expiresAtUtc)} · mode {mode} ·
          {' '}{preview.readyCount}/{preview.totalTargets} thay đổi sẵn sàng.
        </PageMessage>
        {preview.warnings.length > 0 && (
          <ul className="assignment-warning-list">
            {preview.warnings.map((warning, index) => <li key={`${warning}:${index}`}>{warning}</li>)}
          </ul>
        )}
        <div className="table-wrap assignment-preview-table-wrap">
          <table className="table assignment-table">
            <thead><tr><th>Mã đăng ký</th><th>Học viên</th><th>Kết quả</th><th>Thông báo</th></tr></thead>
            <tbody>
              {preview.rows.slice(0, MAX_RENDERED_PREVIEW_ROWS).map((row) => (
                <tr key={row.hocVienId || row.maDangKy}>
                  <td>{row.maDangKy}</td>
                  <td>{row.hoTen}</td>
                  <td><PreviewStatusBadge status={row.status} /></td>
                  <td>{row.messages.join(' · ') || '—'}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
        {preview.rows.length > MAX_RENDERED_PREVIEW_ROWS && (
          <PageMessage kind="info">
            Chỉ hiển thị {MAX_RENDERED_PREVIEW_ROWS}/{preview.rows.length} dòng đầu;
            confirm vẫn dùng toàn bộ preview đã niêm phong.
          </PageMessage>
        )}
        <div className="assignment-modal__actions">
          <button type="button" className="btn btn--ghost" onClick={() => setPreview(null)} disabled={submitting}>Quay lại</button>
          <button
            type="button"
            className="btn btn--primary"
            onClick={() => void confirm()}
            disabled={submitting || preview.conflictCount > 0 || preview.invalidCount > 0}
          >
            {submitting ? 'Đang xác nhận...' : 'Xác nhận propagation'}
          </button>
        </div>
      </Modal>
    );
  }

  return (
    <Modal title={`Đổi mặc định nhóm ${group.maNhom}`} onClose={onClose}>
      <form className="assignment-form" onSubmit={(event) => void makePreview(event)}>
        {error && <PageMessage kind="error">{error}</PageMessage>}
        <div className="assignment-form__grid">
          <ReferenceSelect label="Giáo viên mặc định" value={teacherId} items={lookups.teachers} onChange={setTeacherId} />
          <ReferenceSelect label="Xe tập lái mặc định" value={trainingVehicleId} items={lookups.vehicles} onChange={setTrainingVehicleId} />
          <ReferenceSelect label="Xe bài số 10 mặc định" value={figure10VehicleId} items={lookups.vehicles} onChange={setFigure10VehicleId} />
        </div>
        <fieldset className="assignment-propagation">
          <legend>Chế độ áp dụng *</legend>
          <label>
            <input type="radio" name="mode" value="UNOVERRIDDEN_ONLY" checked={mode === 'UNOVERRIDDEN_ONLY'} onChange={() => setMode('UNOVERRIDDEN_ONLY')} />
            <span><strong>Chỉ trường chưa ghi đè</strong><small>Giữ nguyên mọi override của học viên.</small></span>
          </label>
          <label>
            <input type="radio" name="mode" value="REPLACE_ALL" checked={mode === 'REPLACE_ALL'} onChange={() => setMode('REPLACE_ALL')} />
            <span><strong>Thay toàn bộ</strong><small>Ghi đè giá trị hiện tại và reset ba cờ override.</small></span>
          </label>
          <label>
            <input type="radio" name="mode" value="NO_CURRENT_CHANGE" checked={mode === 'NO_CURRENT_CHANGE'} onChange={() => setMode('NO_CURRENT_CHANGE')} />
            <span><strong>Không đổi phân công hiện tại</strong><small>Chỉ đổi mặc định nhóm, không tạo snapshot học viên.</small></span>
          </label>
        </fieldset>
        {mode === 'REPLACE_ALL' && (
          <PageMessage kind="warning">
            REPLACE_ALL có thể thay các giá trị học viên đã ghi đè. Preview sẽ nêu chính xác số dòng trước khi xác nhận.
          </PageMessage>
        )}
        <label className="field">
          <span className="field__label">Lý do *</span>
          <input className="field__input" value={reason} onChange={(event) => setReason(event.target.value)} maxLength={500} />
        </label>
        <div className="assignment-modal__actions">
          <button type="button" className="btn btn--ghost" onClick={onClose} disabled={submitting}>Hủy</button>
          <button type="submit" className="btn btn--primary" disabled={submitting}>
            {submitting ? 'Đang lập preview...' : 'Xem trước'}
          </button>
        </div>
      </form>
    </Modal>
  );
}

function ReferenceSelect({
  label,
  value,
  items,
  onChange,
}: {
  label: string;
  value: number | null;
  items: AssignmentReference[];
  onChange: (value: number | null) => void;
}) {
  return (
    <SearchLookup
      id={`group-default-${normalizeLookupText(label).toLowerCase().replace(/[^a-z0-9]+/g, '-')}`}
      label={label}
      value={items.find((item) => item.id === value) ?? null}
      onChange={(item) => onChange(item?.id ?? null)}
      loadOptions={async (keyword) => filterLookupOptions(
        items.filter((item) => item.isActive),
        keyword,
        (item) => `${item.code} ${item.label} ${item.sourceProfileCode ?? ''}`,
        20,
      )}
      getKey={(item) => item.id}
      getLabel={(item) => `${item.code} · ${item.label}`}
      getDescription={(item) => item.sourceProfileCode ?? null}
      placeholder={`Mã hoặc tên ${label.toLocaleLowerCase('vi-VN')}`}
      emptyText={`Không có ${label.toLocaleLowerCase('vi-VN')} active phù hợp`}
      errorText={`Không tải được danh sách ${label}.`}
    />
  );
}

function InactivateGroupDialog({
  course,
  group,
  onClose,
  onDone,
  onError,
}: {
  course: KhoaHocDetail;
  group: TrainingGroup;
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
      await inactivateTrainingGroup(course.khoaHocId, group, reason.trim());
      onDone();
    } catch (failure) {
      onError(toMessage(failure, 'Không thể ngừng sử dụng nhóm.'));
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <Modal title={`Ngừng sử dụng nhóm ${group.maNhom}`} onClose={onClose}>
      <form className="assignment-form" onSubmit={submit}>
        <PageMessage kind="warning">
          {group.studentCount} học viên đang thuộc nhóm. Nhóm và snapshot lịch sử không bị xóa;
          nhóm inactive không còn xuất hiện trong lookup gán mới.
        </PageMessage>
        <label className="field">
          <span className="field__label">Lý do *</span>
          <input className="field__input" value={reason} onChange={(event) => setReason(event.target.value)} maxLength={500} autoFocus />
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

function toMessage(error: unknown, fallback: string): string {
  return error instanceof Error ? error.message : fallback;
}
