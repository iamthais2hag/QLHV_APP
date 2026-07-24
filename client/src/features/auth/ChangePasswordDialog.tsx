import { useState, type FormEvent } from 'react';
import { useAuth } from './AuthContext';

const MINIMUM_PASSWORD_LENGTH = 12;
const MAXIMUM_PASSWORD_LENGTH = 512;

export interface ChangePasswordDialogProps {
  required?: boolean;
  onClose?: () => void;
}

export default function ChangePasswordDialog({
  required = false,
  onClose,
}: ChangePasswordDialogProps) {
  const { changePassword, logout, user } = useAuth();
  const [currentPassword, setCurrentPassword] = useState('');
  const [newPassword, setNewPassword] = useState('');
  const [confirmPassword, setConfirmPassword] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  function clearPasswords() {
    setCurrentPassword('');
    setNewPassword('');
    setConfirmPassword('');
  }

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (submitting) {
      return;
    }

    setError(null);
    setNotice(null);
    if (!currentPassword) {
      setError('Vui lòng nhập mật khẩu hiện tại.');
      return;
    }
    if (newPassword.length < MINIMUM_PASSWORD_LENGTH) {
      setError(`Mật khẩu mới phải có ít nhất ${MINIMUM_PASSWORD_LENGTH} ký tự.`);
      return;
    }
    if (newPassword.length > MAXIMUM_PASSWORD_LENGTH) {
      setError('Mật khẩu mới quá dài.');
      return;
    }
    if (newPassword !== confirmPassword) {
      setError('Xác nhận mật khẩu mới không khớp.');
      return;
    }
    if (newPassword === currentPassword) {
      setError('Mật khẩu mới phải khác mật khẩu hiện tại.');
      return;
    }

    setSubmitting(true);
    try {
      await changePassword({ currentPassword, newPassword });
      clearPasswords();
      if (required) {
        setNotice('Đã đổi mật khẩu. Đang mở ứng dụng...');
      } else {
        onClose?.();
      }
    } catch (changeError) {
      clearPasswords();
      setError(changeError instanceof Error ? changeError.message : 'Không thể đổi mật khẩu.');
    } finally {
      setSubmitting(false);
    }
  }

  const content = (
    <section
      className={`auth-card change-password-card${required ? ' is-required' : ''}`}
      aria-labelledby="change-password-title"
    >
      <div className="auth-card__heading">
        <span>{user?.displayName || user?.username}</span>
        <h1 id="change-password-title">
          {required ? 'Đổi mật khẩu lần đầu' : 'Đổi mật khẩu'}
        </h1>
        <p>
          {required
            ? 'Bạn phải đổi mật khẩu tạm trước khi tiếp tục sử dụng ứng dụng.'
            : 'Nhập mật khẩu hiện tại và mật khẩu mới của bạn.'}
        </p>
      </div>

      <form className="auth-form" onSubmit={handleSubmit}>
        {error && <div className="auth-error" role="alert">{error}</div>}
        {notice && <div className="auth-readiness auth-readiness--ready" role="status">{notice}</div>}

        <label className="field">
          <span className="field__label">Mật khẩu hiện tại</span>
          <input
            className="field__input"
            type="password"
            value={currentPassword}
            onChange={(event) => setCurrentPassword(event.target.value)}
            autoComplete="current-password"
            autoFocus
            disabled={submitting}
          />
        </label>

        <label className="field">
          <span className="field__label">Mật khẩu mới</span>
          <input
            className="field__input"
            type="password"
            value={newPassword}
            onChange={(event) => setNewPassword(event.target.value)}
            autoComplete="new-password"
            minLength={MINIMUM_PASSWORD_LENGTH}
            maxLength={MAXIMUM_PASSWORD_LENGTH}
            disabled={submitting}
          />
          <small>Tối thiểu {MINIMUM_PASSWORD_LENGTH} ký tự.</small>
        </label>

        <label className="field">
          <span className="field__label">Xác nhận mật khẩu mới</span>
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

        <div className="change-password-actions">
          {!required && (
            <button
              type="button"
              className="btn btn--ghost"
              onClick={() => {
                clearPasswords();
                onClose?.();
              }}
              disabled={submitting}
            >
              Hủy
            </button>
          )}
          {required && (
            <button
              type="button"
              className="btn btn--ghost"
              onClick={() => void logout()}
              disabled={submitting}
            >
              Đăng xuất
            </button>
          )}
          <button
            type="submit"
            className="btn btn--primary"
            disabled={submitting || !currentPassword || !newPassword || !confirmPassword}
          >
            {submitting ? 'Đang đổi mật khẩu...' : 'Đổi mật khẩu'}
          </button>
        </div>
      </form>
    </section>
  );

  if (required) {
    return <main className="auth-page">{content}</main>;
  }

  return (
    <div
      className="account-modal"
      role="dialog"
      aria-modal="true"
      aria-label="Đổi mật khẩu"
      onMouseDown={(event) => {
        if (event.target === event.currentTarget && !submitting) {
          clearPasswords();
          onClose?.();
        }
      }}
    >
      {content}
    </div>
  );
}
