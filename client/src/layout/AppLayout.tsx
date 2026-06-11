import { useEffect, useState } from 'react';
import { Outlet, useLocation } from 'react-router-dom';
import Sidebar from './Sidebar';
import Header from './Header';
import { MENU_ITEMS } from '../navigation/menu';

const SIDEBAR_COLLAPSED_STORAGE_KEY = 'qlhv.sidebar.collapsed';

function getInitialSidebarCollapsed(): boolean {
  try {
    return window.localStorage.getItem(SIDEBAR_COLLAPSED_STORAGE_KEY) === 'true';
  } catch {
    return false;
  }
}

export default function AppLayout() {
  const [sidebarOpen, setSidebarOpen] = useState(false);
  const [sidebarCollapsed, setSidebarCollapsed] = useState(getInitialSidebarCollapsed);
  const location = useLocation();

  useEffect(() => {
    try {
      window.localStorage.setItem(SIDEBAR_COLLAPSED_STORAGE_KEY, String(sidebarCollapsed));
    } catch {
      // localStorage can be unavailable in restricted browser contexts.
    }
  }, [sidebarCollapsed]);

  const current = MENU_ITEMS.find((item) =>
    item.path === '/' ? location.pathname === '/' : location.pathname.startsWith(item.path),
  );
  const title = current?.label ?? 'QLHV Thành Công';
  const subtitle = current?.description;

  return (
    <div className={`app-shell${sidebarCollapsed ? ' is-sidebar-collapsed' : ''}`}>
      <Sidebar
        open={sidebarOpen}
        collapsed={sidebarCollapsed}
        onToggleCollapsed={() => setSidebarCollapsed((value) => !value)}
        onNavigate={() => setSidebarOpen(false)}
      />
      <div className="main">
        <Header
          title={title}
          subtitle={subtitle}
          onToggleSidebar={() => setSidebarOpen((v) => !v)}
        />
        <main className="content">
          <Outlet />
        </main>
      </div>
    </div>
  );
}
