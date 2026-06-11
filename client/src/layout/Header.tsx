interface HeaderProps {
  title: string;
  subtitle?: string;
  onToggleSidebar: () => void;
}

export default function Header({ title, subtitle, onToggleSidebar }: HeaderProps) {
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
        <span>Quản trị viên</span>
        <span className="header__avatar" aria-hidden="true">
          QT
        </span>
      </div>
    </header>
  );
}
