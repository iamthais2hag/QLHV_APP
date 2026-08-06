import {
  useRef,
  useState,
  type ChangeEvent,
  type FormEvent,
} from 'react';
import { useAuth } from '../../auth/AuthContext';
import { hasPermission } from '../../auth/permissions';
import {
  AssignmentApiError,
  confirmAssignmentImport,
  downloadAssignmentImportResult,
  downloadAssignmentImportTemplate,
  exportCourseAssignments,
  previewAssignmentImport,
} from '../api';
import type {
  AssignmentImportPreview,
  KhoaHocDetail,
} from '../types';
import {
  createIdempotencyKey,
  downloadBlob,
  formatDateTime,
  PageMessage,
  PreviewStatusBadge,
} from '../ui';

const MAX_FILE_BYTES = 10 * 1024 * 1024;
const MAX_RENDERED_PREVIEW_ROWS = 200;

export const ASSIGNMENT_EXPORT_COLUMNS = [
  'STT',
  'Mã đăng ký',
  'Họ và tên',
  'Ngày sinh',
  'Giới tính',
  'Số CCCD',
  'Địa chỉ thường trú',
  'Hạng học',
  'Mã hạng học',
  'Số GPLX đã có',
  'Hạng GPLX đã có',
  'Người nhận hồ sơ',
  'Tên khóa',
  'Mã khóa',
  'Giáo viên đứng lớp',
  'Xe tập lái',
  'Xe bài số 10',
  'Mã giáo viên hồ sơ',
] as const;

export default function AssignmentExcelPanel({
  course,
  onChanged,
  onError,
}: {
  course: KhoaHocDetail;
  onChanged: (message: string) => void;
  onError: (message: string) => void;
}) {
  const { user } = useAuth();
  const canExport = !!user && hasPermission(user.role, 'CanExportAssignments');
  const canPreview = !!user && hasPermission(user.role, 'CanPreviewAssignmentImport');
  const canConfirm = !!user && hasPermission(user.role, 'CanConfirmAssignmentImport');
  const fileInputRef = useRef<HTMLInputElement | null>(null);
  const [file, setFile] = useState<File | null>(null);
  const [preview, setPreview] = useState<AssignmentImportPreview | null>(null);
  const [reason, setReason] = useState('');
  const [busyAction, setBusyAction] = useState<string | null>(null);
  const [localError, setLocalError] = useState<string | null>(null);
  const [resultSession, setResultSession] = useState<number | null>(null);

  async function exportAssignments() {
    setBusyAction('export');
    setLocalError(null);
    try {
      const result = await exportCourseAssignments(course.khoaHocId);
      downloadBlob(result.blob, result.fileName);
    } catch (failure) {
      setLocalError(toMessage(failure, 'Không thể xuất phân công.'));
    } finally {
      setBusyAction(null);
    }
  }

  async function downloadTemplate() {
    setBusyAction('template');
    setLocalError(null);
    try {
      const result = await downloadAssignmentImportTemplate(course.khoaHocId);
      downloadBlob(result.blob, result.fileName);
    } catch (failure) {
      setLocalError(toMessage(failure, 'Không thể tải mẫu nhập.'));
    } finally {
      setBusyAction(null);
    }
  }

  function chooseFile(event: ChangeEvent<HTMLInputElement>) {
    const next = event.target.files?.[0] ?? null;
    setPreview(null);
    setResultSession(null);
    setLocalError(null);
    if (!next) {
      setFile(null);
      return;
    }
    if (!next.name.toLocaleLowerCase('vi-VN').endsWith('.xlsx')) {
      setFile(null);
      setLocalError('Chỉ chấp nhận tệp .xlsx; không nhận .xlsm, macro hoặc định dạng cũ.');
      event.target.value = '';
      return;
    }
    if (next.size > MAX_FILE_BYTES) {
      setFile(null);
      setLocalError('Tệp vượt giới hạn 10 MB.');
      event.target.value = '';
      return;
    }
    setFile(next);
  }

  async function makePreview(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!file) {
      setLocalError('Vui lòng chọn tệp Excel .xlsx.');
      return;
    }
    setBusyAction('preview');
    setLocalError(null);
    try {
      setPreview(await previewAssignmentImport(
        course.khoaHocId,
        course.sourceProfileCode,
        file,
      ));
    } catch (failure) {
      setLocalError(toMessage(failure, 'Không thể kiểm tra tệp Excel.'));
    } finally {
      setBusyAction(null);
    }
  }

  async function confirmImport() {
    if (!preview || !reason.trim()) {
      setLocalError('Vui lòng nhập lý do trước khi xác nhận import.');
      return;
    }
    setBusyAction('confirm');
    setLocalError(null);
    try {
      const result = await confirmAssignmentImport(course.khoaHocId, {
        previewToken: preview.previewToken,
        idempotencyKey: createIdempotencyKey(),
        reason: reason.trim(),
      });
      setResultSession(result.sessionId);
      setPreview(null);
      setFile(null);
      setReason('');
      if (fileInputRef.current) fileInputRef.current.value = '';
      onChanged(
        `Import hoàn tất nguyên tử: ${result.changedCount} thay đổi, ${result.noChangeCount} không đổi.`,
      );
    } catch (failure) {
      const message = toMessage(failure, 'Không thể xác nhận import.');
      if (failure instanceof AssignmentApiError && failure.isConcurrencyConflict) {
        setPreview(null);
        onError(message);
      } else {
        setLocalError(message);
      }
    } finally {
      setBusyAction(null);
    }
  }

  async function downloadResult() {
    if (resultSession === null) return;
    setBusyAction('result');
    setLocalError(null);
    try {
      const result = await downloadAssignmentImportResult(course.khoaHocId, resultSession);
      downloadBlob(result.blob, result.fileName);
    } catch (failure) {
      setLocalError(toMessage(failure, 'Không thể tải kết quả import.'));
    } finally {
      setBusyAction(null);
    }
  }

  const blockingCount = preview
    ? preview.counts.notFound
      + preview.counts.ambiguous
      + preview.counts.inactiveReference
      + preview.counts.invalid
      + preview.counts.conflict
    : 0;

  return (
    <section className="assignment-stack">
      <section className="panel assignment-section-heading">
        <div>
          <span className="assignment-eyebrow">5 · Nhập/Xuất Excel</span>
          <h3>Excel trong đúng khóa {course.maKhoa}</h3>
          <p>
            Scope cố định KhoaHocId {course.khoaHocId} · {course.sourceProfileCode}.
            Không match tên, không tạo master, blank = KEEP.
          </p>
        </div>
        <div className="assignment-row-actions">
          {canPreview && (
            <button type="button" className="btn btn--secondary" onClick={() => void downloadTemplate()} disabled={busyAction !== null}>
              {busyAction === 'template' ? 'Đang tải...' : 'Tải mẫu kỹ thuật'}
            </button>
          )}
          {canExport && (
            <button type="button" className="btn btn--primary" onClick={() => void exportAssignments()} disabled={busyAction !== null}>
              {busyAction === 'export' ? 'Đang xuất...' : 'Xuất 18 cột'}
            </button>
          )}
        </div>
      </section>

      {localError && <PageMessage kind="error">{localError}</PageMessage>}
      {resultSession !== null && (
        <PageMessage kind="success">
          Đã hoàn tất phiên import #{resultSession}.
          {canExport && (
            <>{' '}<button type="button" className="assignment-link-button" onClick={() => void downloadResult()} disabled={busyAction !== null}>
              Tải kết quả
            </button></>
          )}
        </PageMessage>
      )}

      <section className="assignment-excel-grid">
        <article className="panel">
          <h3>Xuất dữ liệu nghiệp vụ</h3>
          <p className="assignment-form__hint">
            Mã, CCCD và GPLX giữ dạng text; công thức Excel được vô hiệu hóa.
            Sheet kỹ thuật chứa exact identity và RowVersion.
          </p>
          <ol className="assignment-export-columns">
            {ASSIGNMENT_EXPORT_COLUMNS.map((column, index) => (
              <li key={column}><span>{index + 1}</span>{column}</li>
            ))}
          </ol>
        </article>

        <article className="panel">
          <h3>Nhập phân công V2</h3>
          {!canPreview ? (
            <PageMessage kind="warning">Bạn không có quyền preview import.</PageMessage>
          ) : (
            <form className="assignment-form" onSubmit={(event) => void makePreview(event)}>
              <label className="field">
                <span className="field__label">Tệp .xlsx (tối đa 10 MB, 5.000 dòng)</span>
                <input
                  ref={fileInputRef}
                  type="file"
                  className="assignment-file-input"
                  accept=".xlsx,application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
                  onChange={chooseFile}
                  disabled={busyAction !== null}
                />
              </label>
              {file && (
                <p className="assignment-selected-file">
                  <strong>{file.name}</strong>
                  <span>{Math.ceil(file.size / 1024).toLocaleString('vi-VN')} KB</span>
                </p>
              )}
              <PageMessage kind="info">
                CLEAR và INHERIT phải dùng action rõ ràng. Hệ thống kiểm tra key khóa/profile,
                MaDangKy + MaKhoa, optional HocVienId và AssignmentRowVersion.
              </PageMessage>
              <button type="submit" className="btn btn--primary" disabled={!file || busyAction !== null}>
                {busyAction === 'preview' ? 'Đang kiểm tra...' : 'Preview an toàn'}
              </button>
            </form>
          )}
        </article>
      </section>

      {preview && (
        <section className="panel assignment-import-preview">
          <header className="assignment-section-heading">
            <div>
              <span className="assignment-eyebrow">Preview chỉ đọc</span>
              <h3>{preview.fileName}</h3>
              <p>{preview.totalRows} dòng · token hết hạn {formatDateTime(preview.expiresAtUtc)}</p>
            </div>
          </header>
          <div className="assignment-preview-counts assignment-preview-counts--import">
            <span className="is-ready"><b>{preview.counts.ready}</b> READY</span>
            <span><b>{preview.counts.noChange}</b> NO_CHANGE</span>
            <span className="is-warning"><b>{preview.counts.notFound}</b> NOT_FOUND</span>
            <span className="is-warning"><b>{preview.counts.ambiguous}</b> AMBIGUOUS</span>
            <span className="is-warning"><b>{preview.counts.inactiveReference}</b> INACTIVE_REFERENCE</span>
            <span className="is-warning"><b>{preview.counts.invalid}</b> INVALID</span>
            <span className="is-warning"><b>{preview.counts.conflict}</b> CONFLICT</span>
          </div>
          <div className="table-wrap assignment-preview-table-wrap">
            <table className="table assignment-table">
              <thead><tr><th>Dòng</th><th>Mã đăng ký</th><th>Kết quả</th><th>Thông báo</th></tr></thead>
              <tbody>
                {preview.rows.slice(0, MAX_RENDERED_PREVIEW_ROWS).map((row) => (
                  <tr key={row.rowNumber}>
                    <td>{row.rowNumber}</td>
                    <td>{row.maDangKy || '—'}</td>
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
              thống kê và confirm vẫn bao phủ toàn bộ tệp.
            </PageMessage>
          )}
          {blockingCount > 0 && (
            <PageMessage kind="warning">
              Có {blockingCount} dòng chặn. Import all-or-nothing không thể confirm cho đến khi tệp được sửa và preview lại.
            </PageMessage>
          )}
          <label className="field">
            <span className="field__label">Lý do import *</span>
            <input
              className="field__input"
              value={reason}
              onChange={(event) => setReason(event.target.value)}
              maxLength={500}
              disabled={busyAction !== null}
            />
          </label>
          <div className="assignment-modal__actions">
            <button type="button" className="btn btn--ghost" onClick={() => setPreview(null)} disabled={busyAction !== null}>
              Bỏ preview
            </button>
            <button
              type="button"
              className="btn btn--primary"
              onClick={() => void confirmImport()}
              disabled={
                !canConfirm
                || busyAction !== null
                || blockingCount > 0
                || preview.counts.ready === 0
                || !reason.trim()
              }
            >
              {busyAction === 'confirm' ? 'Đang xác nhận...' : 'Xác nhận import nguyên tử'}
            </button>
          </div>
          {!canConfirm && <PageMessage kind="warning">Bạn có quyền preview nhưng không có quyền confirm import.</PageMessage>}
        </section>
      )}
    </section>
  );
}

function toMessage(error: unknown, fallback: string): string {
  return error instanceof Error ? error.message : fallback;
}
