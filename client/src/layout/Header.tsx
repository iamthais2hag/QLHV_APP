import { useState } from 'react';
import { useAuth } from '../features/auth/AuthContext';
import ChangePasswordDialog from '../features/auth/ChangePasswordDialog';
import { getRoleDisplayName } from '../features/auth/permissions';

interface HeaderProps {
  title: string;
  subtitle?: string;
  onToggleSidebar: () => void;
}

export default function Header({ title, subtitle, onToggleSidebar }: HeaderProps) {
  const { user, logout } = useAuth();
  const [loggingOut, setLoggingOut] = useState(false);
  const [logoutError, setLogoutError] = useState<string | null>(null);
  const [showChangePassword, setShowChangePassword] = useState(false);

  async function handleLogout() {
    if (loggingOut) {
      return;
    }
    setLoggingOut(true);
    setLogoutError(null);
    try {
      await logout();
    } catch (error) {
      setLogoutError(error instanceof Error ? error.message : 'Không thể đăng xuất.');
    } finally {
      setLoggingOut(false);
    }
  }

  return (
    <header className="header">
      <button
        type="button"
        className="header__menu-btn"
        onClick={onToggleSidebar}
        aria-label="Mở/đóng menu"
      >
        ☰
      </button>
      <div className="header__heading">
        <h1 className="header__title">{title}</h1>
        {subtitle && <p className="header__subtitle">{subtitle}</p>}
      </div>
      <div className="header__spacer" />
      <div className="header__user">
        <span className="header__user-details">
          <strong>{user?.displayName || user?.username}</strong>
          <small>{getRoleDisplayName(user?.role)}</small>
        </span>
        <span className="header__avatar" aria-hidden="true">
          {getInitials(user?.displayName || user?.username || '')}
        </span>
        <button
          type="button"
          className="header__logout"
          onClick={() => setShowChangePassword(true)}
          disabled={loggingOut}
        >
          Đổi mật khẩu
        </button>
        <button
          type="button"
          className="header__logout"
          onClick={() => void handleLogout()}
          disabled={loggingOut}
        >
          {loggingOut ? 'Đang thoát...' : 'Đăng xuất'}
        </button>
        {logoutError && <span className="header__logout-error" role="alert">{logoutError}</span>}
      </div>
      {showChangePassword && (
        <ChangePasswordDialog onClose={() => setShowChangePassword(false)} />
      )}
    </header>
  );
}

function getInitials(value: string): string {
  const words = value.trim().split(/\s+/).filter(Boolean);
  if (words.length === 0) {
    return '?';
  }
  return words.slice(-2).map((word) => word[0]).join('').toLocaleUpperCase('vi-VN');
}
