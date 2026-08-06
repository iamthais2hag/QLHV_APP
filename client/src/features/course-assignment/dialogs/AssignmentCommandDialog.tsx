import {
  useMemo,
  useState,
  type FormEvent,
} from 'react';
import {
  AssignmentApiError,
  confirmAssignment,
  previewAssignment,
} from '../api';
import type {
  AssignmentAction,
  AssignmentFieldAction,
  AssignmentFieldCommands,
  AssignmentLookups,
  AssignmentOperation,
  AssignmentPreview,
  AssignmentReference,
  AssignmentSelection,
  KhoaHocDetail,
  StudentAssignmentItem,
  TrainingGroup,
} from '../types';
import {
  createIdempotencyKey,
  formatDateTime,
  Modal,
  PageMessage,
  PreviewStatusBadge,
  ReferenceLabel,
} from '../ui';
import SearchLookup, { filterLookupOptions } from '../../../components/SearchLookup';

const MAX_RENDERED_PREVIEW_ROWS = 200;

interface FieldEditorValue {
  action: AssignmentAction;
  id: number | null;
}

interface EditorState {
  groupId: number | null;
  dossierReceiver: FieldEditorValue;
  classTeacher: FieldEditorValue;
  trainingVehicle: FieldEditorValue;
  figure10Vehicle: FieldEditorValue;
  reason: string;
}

const EMPTY_ACTION: FieldEditorValue = { action: 'KEEP', id: null };

export default function AssignmentCommandDialog({
  course,
  groups,
  lookups,
  operation,
  student,
  selection,
  expectedRowVersions,
  onClose,
  onConfirmed,
  onConflict,
}: {
  course: KhoaHocDetail;
  groups: TrainingGroup[];
  lookups: AssignmentLookups;
  operation: AssignmentOperation;
  student: StudentAssignmentItem | null;
  selection: AssignmentSelection;
  expectedRowVersions: Record<string, string | null>;
  onClose: () => void;
  onConfirmed: (message: string) => void;
  onConflict: (message: string) => void;
}) {
  const [form, setForm] = useState<EditorState>({
    groupId: student?.groupId ?? null,
    dossierReceiver: EMPTY_ACTION,
    classTeacher: EMPTY_ACTION,
    trainingVehicle: EMPTY_ACTION,
    figure10Vehicle: EMPTY_ACTION,
    reason: '',
  });
  const [preview, setPreview] = useState<AssignmentPreview | null>(null);
  const [previewOperation, setPreviewOperation] = useState<AssignmentOperation>(operation);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const title = operation === 'PUT_IN_GROUP'
    ? 'Đưa học viên vào nhóm'
    : operation === 'BULK_ASSIGN'
      ? 'Phân công hàng loạt'
      : `Gán / ghi đè · ${student?.maDangKy || ''}`;
  const activeGroups = groups.filter((group) => group.isActive);
  const selectedCount = selection.mode === 'FILTER' ? null : selection.hocVienIds.length;
  const currentGroup = student?.groupId
    ? groups.find((group) => group.groupId === student.groupId) ?? null
    : null;

  const changedFieldCount = useMemo(
    () => [
      form.dossierReceiver,
      form.classTeacher,
      form.trainingVehicle,
      form.figure10Vehicle,
    ].filter((field) => field.action !== 'KEEP').length,
    [form],
  );

  async function requestPreview(
    event: FormEvent<HTMLFormElement>,
    requestedOperation = operation,
  ) {
    event.preventDefault();
    const validation = validateForm(requestedOperation);
    if (validation) {
      setError(validation);
      return;
    }
    setSubmitting(true);
    setError(null);
    try {
      const result = await previewAssignment({
        khoaHocId: course.khoaHocId,
        sourceProfileCode: course.sourceProfileCode,
        selection,
        operation: requestedOperation,
        groupId: requestedOperation === 'PUT_IN_GROUP' ? form.groupId : undefined,
        fields: requestedOperation === 'STUDENT_OVERRIDE' || requestedOperation === 'BULK_ASSIGN'
          ? toFieldCommands(form)
          : undefined,
        expectedRowVersions,
        reason: form.reason.trim(),
      });
      setPreviewOperation(requestedOperation);
      setPreview(result);
    } catch (previewError) {
      handleFailure(previewError, 'Không thể tạo bản xem trước.');
    } finally {
      setSubmitting(false);
    }
  }

  async function confirm() {
    if (!preview || submitting) return;
    setSubmitting(true);
    setError(null);
    try {
      const result = await confirmAssignment({
        previewToken: preview.previewToken,
        idempotencyKey: createIdempotencyKey(),
        reason: form.reason.trim(),
      });
      onConfirmed(
        `Đã hoàn tất ${result.changedCount.toLocaleString('vi-VN')} thay đổi; `
        + `${result.noChangeCount.toLocaleString('vi-VN')} học viên không đổi.`,
      );
    } catch (confirmError) {
      handleFailure(confirmError, 'Không thể xác nhận phân công.');
    } finally {
      setSubmitting(false);
    }
  }

  function handleFailure(failure: unknown, fallback: string) {
    const message = failure instanceof Error ? failure.message : fallback;
    if (failure instanceof AssignmentApiError && failure.isConcurrencyConflict) {
      onConflict(message);
      return;
    }
    setError(message);
    setPreview(null);
  }

  function validateForm(requestedOperation: AssignmentOperation): string | null {
    if (!form.reason.trim()) return 'Vui lòng nhập lý do thay đổi.';
    if (requestedOperation === 'PUT_IN_GROUP' && !form.groupId) {
      return 'Vui lòng chọn một nhóm đang hoạt động.';
    }
    if ((requestedOperation === 'STUDENT_OVERRIDE' || requestedOperation === 'BULK_ASSIGN')
        && changedFieldCount === 0) {
      return 'Hãy chọn ít nhất một trường SET, CLEAR hoặc INHERIT.';
    }
    for (const [label, field] of [
      ['Người nhận hồ sơ', form.dossierReceiver],
      ['Giáo viên đứng lớp', form.classTeacher],
      ['Xe tập lái', form.trainingVehicle],
      ['Xe bài số 10', form.figure10Vehicle],
    ] as const) {
      if (field.action === 'SET' && !field.id) return `${label}: vui lòng chọn giá trị cần gán.`;
    }
    return null;
  }

  if (preview) {
    return (
      <Modal title="Xác nhận bản xem trước đã niêm phong" onClose={() => !submitting && setPreview(null)} wide>
        {error && <PageMessage kind="error">{error}</PageMessage>}
        <PreviewSummary preview={preview} operation={previewOperation} />
        <div className="assignment-modal__actions">
          <button type="button" className="btn btn--ghost" onClick={() => setPreview(null)} disabled={submitting}>
            Quay lại
          </button>
          <button
            type="button"
            className="btn btn--primary"
            onClick={() => void confirm()}
            disabled={
              submitting
              || preview.readyCount === 0
              || preview.conflictCount > 0
              || preview.invalidCount > 0
            }
          >
            {submitting ? 'Đang xác nhận...' : 'Xác nhận áp dụng nguyên tử'}
          </button>
        </div>
      </Modal>
    );
  }

  return (
    <Modal title={title} onClose={onClose} wide={operation !== 'PUT_IN_GROUP'}>
      <form className="assignment-form" onSubmit={(event) => void requestPreview(event)}>
        {error && <PageMessage kind="error">{error}</PageMessage>}
        <PageMessage kind="info">
          Phạm vi: {selection.mode === 'FILTER'
            ? 'toàn bộ kết quả lọc hiện tại, server sẽ materialize khi preview'
            : `${selectedCount ?? 0} HocVienId đã chọn`}
          {' '}· Khóa {course.maKhoa} · {course.sourceProfileCode}.
        </PageMessage>

        {operation === 'PUT_IN_GROUP' ? (
          <SearchLookup
            id="assignment-command-group"
            label="Nhóm đào tạo"
            required
            value={activeGroups.find((group) => group.groupId === form.groupId) ?? null}
            onChange={(option) => setForm({ ...form, groupId: option?.groupId ?? null })}
            loadOptions={async (keyword) => filterLookupOptions(
              activeGroups,
              keyword,
              (group) => `${group.maNhom} ${group.tenNhom}`,
              20,
            )}
            getKey={(group) => group.groupId}
            getLabel={(group) => `${group.maNhom} · ${group.tenNhom}`}
            getDescription={(group) => `${group.studentCount} học viên`}
            placeholder="Mã nhóm hoặc tên nhóm"
            emptyText="Không có nhóm active phù hợp"
            errorText="Không tải được danh sách Nhóm."
            disabled={submitting}
          />
        ) : (
          <>
            {student && (
              <div className="assignment-current-state">
                <strong>Trạng thái hiện tại</strong>
                <div className="assignment-current-state__grid">
                  <span>Nhóm <b>{currentGroup?.maNhom || 'Chưa vào nhóm'}</b></span>
                  <span>Người nhận HS <ReferenceLabel value={student.dossierReceiver} /></span>
                  <span>Giáo viên <ReferenceLabel value={student.classTeacher} overridden={student.overrideClassTeacher} /></span>
                  <span>Xe tập lái <ReferenceLabel value={student.trainingVehicle} overridden={student.overrideTrainingVehicle} /></span>
                  <span>Xe bài 10 <ReferenceLabel value={student.figure10Vehicle} overridden={student.overrideFigure10Vehicle} /></span>
                </div>
              </div>
            )}
            <div className="assignment-field-command-grid">
              <FieldCommand
                label="Người nhận hồ sơ"
                value={form.dossierReceiver}
                references={lookups.dossierReceivers}
                allowInherit={false}
                onChange={(value) => setForm({ ...form, dossierReceiver: value })}
              />
              <FieldCommand
                label="Giáo viên đứng lớp"
                value={form.classTeacher}
                references={lookups.teachers}
                allowInherit
                onChange={(value) => setForm({ ...form, classTeacher: value })}
              />
              <FieldCommand
                label="Xe tập lái"
                value={form.trainingVehicle}
                references={lookups.vehicles}
                allowInherit
                onChange={(value) => setForm({ ...form, trainingVehicle: value })}
              />
              <FieldCommand
                label="Xe bài số 10"
                value={form.figure10Vehicle}
                references={lookups.vehicles}
                allowInherit
                onChange={(value) => setForm({ ...form, figure10Vehicle: value })}
              />
            </div>
          </>
        )}

        <label className="field">
          <span className="field__label">Lý do thay đổi *</span>
          <input
            className="field__input"
            value={form.reason}
            onChange={(event) => setForm({ ...form, reason: event.target.value })}
            maxLength={500}
            disabled={submitting}
            placeholder="Lý do được lưu vào lịch sử/audit"
          />
        </label>

        <PageMessage kind="warning">
          Preview chỉ đọc và có thời hạn. Confirm sẽ revalidate exact khóa/profile, từng HocVienId,
          tham chiếu active và RowVersion; nếu dữ liệu đổi, toàn bộ giao dịch bị từ chối.
        </PageMessage>

        <div className="assignment-modal__actions">
          {student && operation === 'STUDENT_OVERRIDE' && (
            <button
              type="button"
              className="btn btn--secondary"
              onClick={(event) => void requestPreview(event as unknown as FormEvent<HTMLFormElement>, 'CLEAR_ASSIGNMENT')}
              disabled={submitting || !form.reason.trim()}
              title="Đóng snapshot hiện hành; không tạo snapshot rỗng"
            >
              Xóa toàn bộ phân công
            </button>
          )}
          <span className="assignment-modal__spacer" />
          <button type="button" className="btn btn--ghost" onClick={onClose} disabled={submitting}>Hủy</button>
          <button type="submit" className="btn btn--primary" disabled={submitting}>
            {submitting ? 'Đang lập preview...' : 'Xem trước thay đổi'}
          </button>
        </div>
      </form>
    </Modal>
  );
}

function FieldCommand({
  label,
  value,
  references,
  allowInherit,
  onChange,
}: {
  label: string;
  value: FieldEditorValue;
  references: AssignmentReference[];
  allowInherit: boolean;
  onChange: (value: FieldEditorValue) => void;
}) {
  return (
    <fieldset className="assignment-field-command">
      <legend>{label}</legend>
      <label className="field">
        <span className="field__label">Hành động</span>
        <select
          className="field__input"
          value={value.action}
          onChange={(event) => onChange({
            action: event.target.value as AssignmentAction,
            id: null,
          })}
        >
          <option value="KEEP">KEEP · Giữ nguyên</option>
          <option value="SET">SET · Gán giá trị</option>
          <option value="CLEAR">CLEAR · Xóa giá trị</option>
          {allowInherit && <option value="INHERIT">INHERIT · Theo mặc định nhóm</option>}
        </select>
      </label>
      {value.action === 'SET' && (
        <SearchLookup
          id={`assignment-reference-${lookupId(label)}`}
          label={`Tra cứu ${label}`}
          required
          value={references.find((reference) => reference.id === value.id) ?? null}
          onChange={(reference) => onChange({ ...value, id: reference?.id ?? null })}
          loadOptions={async (keyword) => filterLookupOptions(
            references.filter((reference) => reference.isActive),
            keyword,
            (reference) => `${reference.code} ${reference.label} ${reference.sourceProfileCode ?? ''}`,
            20,
          )}
          getKey={(reference) => reference.id}
          getLabel={(reference) => `${reference.code} · ${reference.label}`}
          getDescription={(reference) => reference.sourceProfileCode ?? null}
          placeholder={`Mã hoặc tên ${label.toLocaleLowerCase('vi-VN')}`}
          emptyText={`Không có ${label.toLocaleLowerCase('vi-VN')} active phù hợp`}
          errorText={`Không tải được danh sách ${label}.`}
        />
      )}
      {value.action === 'INHERIT' && (
        <small>Chỉ hợp lệ khi học viên thuộc một nhóm phù hợp cùng khóa.</small>
      )}
    </fieldset>
  );
}

function lookupId(label: string): string {
  return label
    .normalize('NFD')
    .replace(/[\u0300-\u036f]/g, '')
    .replace(/đ/gi, 'd')
    .replace(/[^a-z0-9]+/gi, '-')
    .replace(/^-|-$/g, '')
    .toLowerCase();
}

function PreviewSummary({
  preview,
  operation,
}: {
  preview: AssignmentPreview;
  operation: AssignmentOperation;
}) {
  return (
    <div className="assignment-preview">
      <PageMessage kind={preview.conflictCount > 0 || preview.invalidCount > 0 ? 'warning' : 'info'}>
        Preview {operation} hết hạn lúc {formatDateTime(preview.expiresAtUtc)}.
        Fingerprint: <code>{preview.targetFingerprint}</code>
      </PageMessage>
      <div className="assignment-preview-counts">
        <span><b>{preview.totalTargets}</b> mục tiêu</span>
        <span className="is-ready"><b>{preview.readyCount}</b> sẵn sàng</span>
        <span><b>{preview.noChangeCount}</b> không đổi</span>
        <span className="is-warning"><b>{preview.conflictCount}</b> xung đột</span>
        <span className="is-warning"><b>{preview.invalidCount}</b> không hợp lệ</span>
      </div>
      {preview.warnings.length > 0 && (
        <ul className="assignment-warning-list">
          {preview.warnings.map((warning, index) => <li key={`${warning}:${index}`}>{warning}</li>)}
        </ul>
      )}
      <div className="table-wrap assignment-preview-table-wrap">
        <table className="table assignment-table">
          <thead>
            <tr>
              <th>Mã đăng ký</th>
              <th>Học viên</th>
              <th>Kết quả</th>
              <th>Trước</th>
              <th>Sau</th>
              <th>Thông báo</th>
            </tr>
          </thead>
          <tbody>
            {preview.rows.slice(0, MAX_RENDERED_PREVIEW_ROWS).map((row) => (
              <tr key={row.hocVienId || row.maDangKy}>
                <td><strong>{row.maDangKy}</strong></td>
                <td>{row.hoTen}</td>
                <td><PreviewStatusBadge status={row.status} /></td>
                <td><DisplayState state={row.before} /></td>
                <td><DisplayState state={row.after} /></td>
                <td>{row.messages.join(' · ') || '—'}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      {preview.rows.length > MAX_RENDERED_PREVIEW_ROWS && (
        <PageMessage kind="info">
          Đang hiển thị {MAX_RENDERED_PREVIEW_ROWS}/{preview.rows.length} dòng đầu để giữ giao diện ổn định.
          Tổng số và confirm vẫn áp dụng theo toàn bộ target set đã niêm phong phía server.
        </PageMessage>
      )}
    </div>
  );
}

function DisplayState({
  state,
}: {
  state: AssignmentPreview['rows'][number]['before'];
}) {
  if (!state) return <span className="assignment-muted">Không có snapshot</span>;
  return (
    <span className="assignment-preview-state">
      <span>Nhóm: {formatIdentity(state.groupId)}</span>
      <span>HS: {formatIdentity(state.dossierReceiverId)}</span>
      <span>GV: {formatIdentity(state.classTeacherId)}</span>
      <span>Xe: {formatIdentity(state.trainingVehicleId)}</span>
      <span>Xe 10: {formatIdentity(state.figure10VehicleId)}</span>
    </span>
  );
}

function toFieldCommands(form: EditorState): AssignmentFieldCommands {
  return {
    dossierReceiver: toFieldAction(form.dossierReceiver),
    classTeacher: toFieldAction(form.classTeacher),
    trainingVehicle: toFieldAction(form.trainingVehicle),
    figure10Vehicle: toFieldAction(form.figure10Vehicle),
  };
}

function toFieldAction(value: FieldEditorValue): AssignmentFieldAction {
  return value.action === 'SET'
    ? { action: value.action, id: value.id }
    : { action: value.action };
}

function formatIdentity(value: number | null): string {
  return value === null ? '—' : `#${value}`;
}
