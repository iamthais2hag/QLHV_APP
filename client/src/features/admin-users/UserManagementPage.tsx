import {
  useCallback,
  useEffect,
  useState,
  type FormEvent,
  type ReactNode,
} from 'react';
import { useAuth } from '../auth/AuthContext';
import { getRoleDisplayName } from '../auth/permissions';
import type { AppUserRole } from '../auth/types';
import {
  createManagedUser,
  getManagedUsers,
  resetManagedUserPassword,
  updateManagedUser,
} from './api';
import type {
  CreateManagedUserRequest,
  ManagedUser,
  ResetManagedUserPasswordRequest,
  UpdateManagedUserRequest,
} from './types';

const MINIMUM_PASSWORD_LENGTH = 12;
const MAXIMUM_PASSWORD_LENGTH = 512;
const USERNAME_PATTERN = /^[A-Za-z0-9][A-Za-z0-9._-]{2,99}$/;
const ROLE_OPTIONS: AppUserRole[] = ['Admin', 'Employee', 'Viewer'];
const DATE_FORMAT = new Intl.DateTimeFormat('vi-VN', {
  dateStyle: 'short',
  timeStyle: 'medium',
});

interface AccountFormState {
  username: string;
  displayName: string;
  role: AppUserRole;
  isActive: boolean;
  mustChangePassword: boolean;
  temporaryPassword: string;
  confirmPassword: string;
}

const EMPTY_CREATE_FORM: AccountFormState = {
  username: '',
  displayName: '',
  role: 'Employee',
  isActive: true,
  mustChangePassword: true,
  temporaryPassword: '',
  confirmPassword: '',
};

export default function UserManagementPage() {
  const { user } = useAuth();
  const [rows, setRows] = useState<ManagedUser[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [showCreate, setShowCreate] = useState(false);
  const [createForm, setCreateForm] = useState<AccountFormState>(EMPTY_CREATE_FORM);
  const [creating, setCreating] = useState(false);
  const [editing, setEditing] = useState<ManagedUser | null>(null);
  const [resetting, setResetting] = useState<ManagedUser | null>(null);
  const [pendingUserId, setPendingUserId] = useState<number | null>(null);

  const load = useCallback(async (signal?: AbortSignal) => {
    setLoading(true);
    setError(null);
    try {
      setRows(await getManagedUsers(signal));
    } catch (loadError) {
      if (!(loadError instanceof DOMException && loadError.name === 'AbortError')) {
        setError(loadError instanceof Error ? loadError.message : 'Không thể tải danh sách tài khoản.');
      }
    } finally {
      if (!signal?.aborted) {
        setLoading(false);
      }
    }
  }, []);

  useEffect(() => {
    const controller = new AbortController();
    void load(controller.signal);
    return () => controller.abort();
  }, [load]);

  function replaceRow(next: ManagedUser) {
    setRows((current) => current.map((row) => row.id === next.id ? next : row));
  }

  async function handleCreate(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (creating) {
      return;
    }

    const validation = validateCreateForm(createForm);
    if (validation) {
      setError(validation);
      return;
    }

    const request: CreateManagedUserRequest = {
      username: createForm.username.trim(),
      displayName: createForm.displayName.trim(),
      role: createForm.role,
      temporaryPassword: createForm.temporaryPassword,
      isActive: createForm.isActive,
      mustChangePassword: createForm.mustChangePassword,
    };

    setCreating(true);
    setError(null);
    setNotice(null);
    try {
      const created = await createManagedUser(request);
      setRows((current) => [created, ...current]);
      setCreateForm(EMPTY_CREATE_FORM);
      setShowCreate(false);
      setNotice(`Đã tạo tài khoản ${created.username}.`);
    } catch (createError) {
      setCreateForm((current) => ({
        ...current,
        temporaryPassword: '',
        confirmPassword: '',
      }));
      setError(createError instanceof Error ? createError.message : 'Không thể tạo tài khoản.');
    } finally {
      setCreating(false);
    }
  }

  async function handleToggleActive(row: ManagedUser) {
    if (pendingUserId !== null || (row.id === user?.id && row.isActive)) {
      return;
    }

    const request: UpdateManagedUserRequest = {
      displayName: row.displayName,
      role: row.role,
      isActive: !row.isActive,
      mustChangePassword: row.mustChangePassword,
    };

    setPendingUserId(row.id);
    setError(null);
    setNotice(null);
    try {
      const updated = await updateManagedUser(row.id, request);
      replaceRow(updated);
      setNotice(updated.isActive
        ? `Đã mở khóa tài khoản ${updated.username}.`
        : `Đã khóa tài khoản ${updated.username}.`);
    } catch (updateError) {
      setError(updateError instanceof Error ? updateError.message : 'Không thể cập nhật tài khoản.');
    } finally {
      setPendingUserId(null);
    }
  }

  return (
    <div className="admin-users-page">
      <section className="panel admin-users-toolbar">
        <div>
          <strong>Quản lý tài khoản tập trung</strong>
          <p>
            Tài khoản được dùng chung khi đăng nhập từ máy chủ hoặc máy trạm.
            Mật khẩu không bao giờ được hiển thị lại.
          </p>
        </div>
        <div className="admin-users-toolbar__actions">
          <button
            type="button"
            className="btn btn--ghost"
            onClick={() => void load()}
            disabled={loading}
          >
            {loading ? 'Đang tải...' : 'Tải lại'}
          </button>
          <button
            type="button"
            className="btn btn--primary"
            onClick={() => {
              setError(null);
              setNotice(null);
              setCreateForm(EMPTY_CREATE_FORM);
              setShowCreate(true);
            }}
          >
            Tạo tài khoản
          </button>
        </div>
      </section>

      {error && <div className="admin-users-message is-error" role="alert">{error}</div>}
      {notice && <div className="admin-users-message is-success" role="status">{notice}</div>}

      <section className="panel">
        {loading && rows.length === 0 && <div className="state">Đang tải danh sách tài khoản...</div>}
        {!loading && rows.length === 0 && <div className="state">Chưa có tài khoản.</div>}
        {rows.length > 0 && (
          <div className="table-wrap">
            <table className="table admin-users-table">
              <thead>
                <tr>
                  <th>Tên đăng nhập</th>
                  <th>Họ và tên</th>
                  <th>Vai trò</th>
                  <th>Trạng thái</th>
                  <th>Lần đăng nhập gần nhất</th>
                  <th>Ngày tạo</th>
                  <th>Người tạo</th>
                  <th>Thao tác</th>
                </tr>
              </thead>
              <tbody>
                {rows.map((row) => {
                  const isCurrentUser = row.id === user?.id;
                  return (
                    <tr key={row.id}>
                      <td>
                        <strong>{row.username}</strong>
                        {isCurrentUser && <small className="admin-users-self-label">Tài khoản của bạn</small>}
                      </td>
                      <td>{row.displayName}</td>
                      <td>{getRoleDisplayName(row.role)}</td>
                      <td>
                        <span className={`admin-user-status ${row.isActive ? 'is-active' : 'is-locked'}`}>
                          {row.isActive ? 'Hoạt động' : 'Đã khóa'}
                        </span>
                        {row.mustChangePassword && (
                          <small className="admin-users-must-change">Phải đổi mật khẩu</small>
                        )}
                      </td>
                      <td>{formatDate(row.lastLoginAtUtc)}</td>
                      <td>{formatDate(row.createdAtUtc)}</td>
                      <td>{row.createdBy || 'Hệ thống'}</td>
                      <td>
                        <div className="admin-users-row-actions">
                          <button
                            type="button"
                            className="btn btn--ghost btn--sm"
                            onClick={() => {
                              setError(null);
                              setNotice(null);
                              setEditing(row);
                            }}
                            disabled={pendingUserId !== null}
                          >
                            Sửa
                          </button>
                          <button
                            type="button"
                            className="btn btn--ghost btn--sm"
                            onClick={() => void handleToggleActive(row)}
                            disabled={pendingUserId !== null || (isCurrentUser && row.isActive)}
                            title={isCurrentUser && row.isActive
                              ? 'Không thể tự khóa tài khoản đang đăng nhập.'
                              : undefined}
                          >
                            {pendingUserId === row.id
                              ? 'Đang lưu...'
                              : row.isActive ? 'Khóa' : 'Mở khóa'}
                          </button>
                          <button
                            type="button"
                            className="btn btn--ghost btn--sm"
                            onClick={() => {
                              setError(null);
                              setNotice(null);
                              setResetting(row);
                            }}
                            disabled={pendingUserId !== null}
                          >
                            Đặt lại mật khẩu
                          </button>
                        </div>
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        )}
      </section>

      {showCreate && (
        <AccountModal title="Tạo tài khoản" onClose={() => !creating && setShowCreate(false)}>
          <AccountForm
            value={createForm}
            mode="create"
            submitting={creating}
            onChange={setCreateForm}
            onSubmit={handleCreate}
            onCancel={() => {
              setCreateForm(EMPTY_CREATE_FORM);
              setShowCreate(false);
            }}
          />
        </AccountModal>
      )}

      {editing && (
        <EditAccountModal
          account={editing}
          currentUserId={user?.id ?? null}
          onSaved={(updated) => {
            replaceRow(updated);
            setEditing(null);
            setNotice(`Đã cập nhật tài khoản ${updated.username}.`);
          }}
          onError={setError}
          onClose={() => setEditing(null)}
        />
      )}

      {resetting && (
        <ResetPasswordModal
          account={resetting}
          onSaved={() => {
            setResetting(null);
            setNotice(`Đã đặt lại mật khẩu cho tài khoản ${resetting.username}.`);
            void load();
          }}
          onError={setError}
          onClose={() => setResetting(null)}
        />
      )}
    </div>
  );
}

function AccountForm({
  value,
  mode,
  submitting,
  onChange,
  onSubmit,
  onCancel,
  lockActive,
}: {
  value: AccountFormState;
  mode: 'create' | 'edit';
  submitting: boolean;
  onChange: (next: AccountFormState) => void;
  onSubmit: (event: FormEvent<HTMLFormElement>) => void;
  onCancel: () => void;
  lockActive?: boolean;
}) {
  return (
    <form className="admin-user-form" onSubmit={onSubmit}>
      <label className="field">
        <span className="field__label">Tên đăng nhập</span>
        <input
          className="field__input"
          value={value.username}
          onChange={(event) => onChange({ ...value, username: event.target.value })}
          autoComplete="off"
          maxLength={100}
          disabled={mode === 'edit' || submitting}
          autoFocus
        />
        {mode === 'create' && (
          <small>
            3–100 ký tự; bắt đầu bằng chữ hoặc số, sau đó có thể dùng dấu chấm,
            gạch dưới hoặc gạch ngang.
          </small>
        )}
      </label>

      <label className="field">
        <span className="field__label">Họ và tên</span>
        <input
          className="field__input"
          value={value.displayName}
          onChange={(event) => onChange({ ...value, displayName: event.target.value })}
          autoComplete="name"
          maxLength={200}
          disabled={submitting}
        />
      </label>

      <label className="field">
        <span className="field__label">Vai trò</span>
        <select
          className="field__input"
          value={value.role}
          onChange={(event) => onChange({ ...value, role: event.target.value as AppUserRole })}
          disabled={submitting}
        >
          {ROLE_OPTIONS.map((role) => (
            <option key={role} value={role}>{getRoleDisplayName(role)}</option>
          ))}
        </select>
      </label>

      {mode === 'create' && (
        <>
          <label className="field">
            <span className="field__label">Mật khẩu tạm</span>
            <input
              className="field__input"
              type="password"
              value={value.temporaryPassword}
              onChange={(event) => onChange({ ...value, temporaryPassword: event.target.value })}
              autoComplete="new-password"
              minLength={MINIMUM_PASSWORD_LENGTH}
              maxLength={MAXIMUM_PASSWORD_LENGTH}
              disabled={submitting}
            />
          </label>
          <label className="field">
            <span className="field__label">Xác nhận mật khẩu</span>
            <input
              className="field__input"
              type="password"
              value={value.confirmPassword}
              onChange={(event) => onChange({ ...value, confirmPassword: event.target.value })}
              autoComplete="new-password"
              minLength={MINIMUM_PASSWORD_LENGTH}
              maxLength={MAXIMUM_PASSWORD_LENGTH}
              disabled={submitting}
            />
          </label>
        </>
      )}

      <label className="admin-user-check">
        <input
          type="checkbox"
          checked={value.isActive}
          onChange={(event) => onChange({ ...value, isActive: event.target.checked })}
          disabled={submitting || lockActive}
        />
        Tài khoản hoạt động
      </label>

      <label className="admin-user-check">
        <input
          type="checkbox"
          checked={value.mustChangePassword}
          onChange={(event) => onChange({ ...value, mustChangePassword: event.target.checked })}
          disabled={submitting}
        />
        Yêu cầu đổi mật khẩu ở lần đăng nhập tiếp theo
      </label>

      <div className="admin-user-form__actions">
        <button type="button" className="btn btn--ghost" onClick={onCancel} disabled={submitting}>
          Hủy
        </button>
        <button type="submit" className="btn btn--primary" disabled={submitting}>
          {submitting ? 'Đang lưu...' : mode === 'create' ? 'Tạo tài khoản' : 'Lưu thay đổi'}
        </button>
      </div>
    </form>
  );
}

function EditAccountModal({
  account,
  currentUserId,
  onSaved,
  onError,
  onClose,
}: {
  account: ManagedUser;
  currentUserId: number | null;
  onSaved: (updated: ManagedUser) => void;
  onError: (message: string) => void;
  onClose: () => void;
}) {
  const [form, setForm] = useState<AccountFormState>({
    username: account.username,
    displayName: account.displayName,
    role: account.role,
    isActive: account.isActive,
    mustChangePassword: account.mustChangePassword,
    temporaryPassword: '',
    confirmPassword: '',
  });
  const [submitting, setSubmitting] = useState(false);

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (submitting) {
      return;
    }
    if (!form.displayName.trim()) {
      onError('Vui lòng nhập họ và tên.');
      return;
    }
    if (account.id === currentUserId && !form.isActive) {
      onError('Không thể tự khóa tài khoản đang đăng nhập.');
      return;
    }

    setSubmitting(true);
    onError('');
    try {
      const request: UpdateManagedUserRequest = {
        displayName: form.displayName.trim(),
        role: form.role,
        isActive: form.isActive,
        mustChangePassword: form.mustChangePassword,
      };
      onSaved(await updateManagedUser(account.id, request));
    } catch (updateError) {
      onError(updateError instanceof Error ? updateError.message : 'Không thể cập nhật tài khoản.');
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <AccountModal title={`Sửa tài khoản ${account.username}`} onClose={() => !submitting && onClose()}>
      <AccountForm
        value={form}
        mode="edit"
        submitting={submitting}
        onChange={setForm}
        onSubmit={handleSubmit}
        onCancel={onClose}
        lockActive={account.id === currentUserId && account.isActive}
      />
    </AccountModal>
  );
}

function ResetPasswordModal({
  account,
  onSaved,
  onError,
  onClose,
}: {
  account: ManagedUser;
  onSaved: () => void;
  onError: (message: string) => void;
  onClose: () => void;
}) {
  const [temporaryPassword, setTemporaryPassword] = useState('');
  const [confirmPassword, setConfirmPassword] = useState('');
  const [mustChangePassword, setMustChangePassword] = useState(true);
  const [submitting, setSubmitting] = useState(false);

  function clearPasswords() {
    setTemporaryPassword('');
    setConfirmPassword('');
  }

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (submitting) {
      return;
    }
    const validation = validatePasswordPair(temporaryPassword, confirmPassword);
    if (validation) {
      onError(validation);
      return;
    }

    const request: ResetManagedUserPasswordRequest = {
      temporaryPassword,
      mustChangePassword,
    };
    setSubmitting(true);
    onError('');
    try {
      await resetManagedUserPassword(account.id, request);
      clearPasswords();
      onSaved();
    } catch (resetError) {
      clearPasswords();
      onError(resetError instanceof Error ? resetError.message : 'Không thể đặt lại mật khẩu.');
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <AccountModal title={`Đặt lại mật khẩu · ${account.username}`} onClose={() => !submitting && onClose()}>
      <form className="admin-user-form" onSubmit={handleSubmit}>
        <p>Mật khẩu hiện tại sẽ hết hiệu lực ngay sau khi đặt lại thành công.</p>
        <label className="field">
          <span className="field__label">Mật khẩu tạm mới</span>
          <input
            className="field__input"
            type="password"
            value={temporaryPassword}
            onChange={(event) => setTemporaryPassword(event.target.value)}
            autoComplete="new-password"
            minLength={MINIMUM_PASSWORD_LENGTH}
            maxLength={MAXIMUM_PASSWORD_LENGTH}
            disabled={submitting}
            autoFocus
          />
        </label>
        <label className="field">
          <span className="field__label">Xác nhận mật khẩu</span>
          <input
            className="field__input"
            type="password"
            value={confirmPassword}
            onChange={(event) => setConfirmPassword(event.target.value)}
            autoComplete="new-password"
            minLength={MINIMUM_PASSWORD_LENGTH}
            maxLength={MAXIMUM_PASSWORD_LENGTH}
            disabled={submitting}
          />
        </label>
        <label className="admin-user-check">
          <input
            type="checkbox"
            checked={mustChangePassword}
            onChange={(event) => setMustChangePassword(event.target.checked)}
            disabled={submitting}
          />
          Yêu cầu đổi mật khẩu ở lần đăng nhập tiếp theo
        </label>
        <div className="admin-user-form__actions">
          <button type="button" className="btn btn--ghost" onClick={onClose} disabled={submitting}>
            Hủy
          </button>
          <button type="submit" className="btn btn--primary" disabled={submitting}>
            {submitting ? 'Đang đặt lại...' : 'Đặt lại mật khẩu'}
          </button>
        </div>
      </form>
    </AccountModal>
  );
}

function AccountModal({
  title,
  children,
  onClose,
}: {
  title: string;
  children: ReactNode;
  onClose: () => void;
}) {
  return (
    <div
      className="account-modal"
      role="dialog"
      aria-modal="true"
      aria-label={title}
      onMouseDown={(event) => {
        if (event.target === event.currentTarget) {
          onClose();
        }
      }}
    >
      <section className="panel account-modal__dialog">
        <div className="account-modal__header">
          <strong>{title}</strong>
          <button type="button" className="photo-modal__close" aria-label="Đóng" onClick={onClose}>
            ×
          </button>
        </div>
        {children}
      </section>
    </div>
  );
}

function validateCreateForm(form: AccountFormState): string | null {
  const username = form.username.trim();
  if (!USERNAME_PATTERN.test(username)) {
    return 'Tên đăng nhập phải có 3–100 ký tự, bắt đầu bằng chữ hoặc số; chỉ dùng thêm dấu chấm, gạch dưới hoặc gạch ngang.';
  }
  if (!form.displayName.trim()) {
    return 'Vui lòng nhập họ và tên.';
  }
  return validatePasswordPair(form.temporaryPassword, form.confirmPassword);
}

function validatePasswordPair(password: string, confirmation: string): string | null {
  if (password.length < MINIMUM_PASSWORD_LENGTH) {
    return `Mật khẩu phải có ít nhất ${MINIMUM_PASSWORD_LENGTH} ký tự.`;
  }
  if (password.length > MAXIMUM_PASSWORD_LENGTH) {
    return 'Mật khẩu quá dài.';
  }
  if (password !== confirmation) {
    return 'Xác nhận mật khẩu không khớp.';
  }
  return null;
}

function formatDate(value: string | null): string {
  if (!value) {
    return 'Chưa có';
  }
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? value : DATE_FORMAT.format(date);
}
