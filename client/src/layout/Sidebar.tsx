import { NavLink } from 'react-router-dom';
import { MENU_ITEMS } from '../navigation/menu';

interface SidebarProps {
  open: boolean;
  collapsed: boolean;
  onToggleCollapsed: () => void;
  onNavigate: () => void;
}

export default function Sidebar({ open, collapsed, onToggleCollapsed, onNavigate }: SidebarProps) {
  return (
    <>
      <aside className={`sidebar${open ? ' is-open' : ''}`}>
        <div className="sidebar__brand">
          <span className="sidebar__logo">TC</span>
          <span className="sidebar__brand-text">QLHV Thành Công</span>
          <button
            type="button"
            className="sidebar__toggle"
            onClick={onToggleCollapsed}
            aria-label={collapsed ? 'Mở rộng menu' : 'Thu gọn menu'}
            aria-expanded={!collapsed}
            title={collapsed ? 'Mở rộng menu' : 'Thu gọn menu'}
          >
            {collapsed ? '»' : '«'}
          </button>
        </div>
        <nav className="sidebar__nav">
          {MENU_ITEMS.map((item) => (
            <NavLink
              key={item.path}
              to={item.path}
              end={item.path === '/'}
              onClick={onNavigate}
              title={item.label}
              className={({ isActive }) => `sidebar__link${isActive ? ' is-active' : ''}`}
            >
              <span className="sidebar__icon" aria-hidden="true">
                {item.icon}
              </span>
              <span className="sidebar__text">{item.label}</span>
            </NavLink>
          ))}
        </nav>
      </aside>
      <div
        className={`sidebar__backdrop${open ? ' is-open' : ''}`}
        onClick={onNavigate}
        aria-hidden="true"
      />
    </>
  );
}
