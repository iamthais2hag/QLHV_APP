import { NavLink } from 'react-router-dom';
import { MENU_ITEMS } from '../navigation/menu';

interface SidebarProps {
  open: boolean;
  onNavigate: () => void;
}

export default function Sidebar({ open, onNavigate }: SidebarProps) {
  return (
    <>
      <aside className={`sidebar${open ? ' is-open' : ''}`}>
        <div className="sidebar__brand">
          <span className="sidebar__logo">TC</span>
          <span>QLHV Thành Công</span>
        </div>
        <nav className="sidebar__nav">
          {MENU_ITEMS.map((item) => (
            <NavLink
              key={item.path}
              to={item.path}
              end={item.path === '/'}
              onClick={onNavigate}
              className={({ isActive }) => `sidebar__link${isActive ? ' is-active' : ''}`}
            >
              <span className="sidebar__icon" aria-hidden="true">
                {item.icon}
              </span>
              <span>{item.label}</span>
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
