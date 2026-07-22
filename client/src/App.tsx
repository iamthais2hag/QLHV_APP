import { Routes, Route, Navigate } from 'react-router-dom';
import AppLayout from './layout/AppLayout';
import Dashboard from './pages/Dashboard';
import ModulePage from './pages/ModulePage';
import CsdtConnectionProfilesPage from './features/csdt-connections/CsdtConnectionProfilesPage';
import HocVienPage from './features/hoc-vien/HocVienPage';
import HocVienCardPrintPage from './features/hoc-vien/HocVienCardPrintPage';
import MotoSyncPage from './features/moto-sync/MotoSyncPage';
import QlhvImportPage from './features/qlhv-import/QlhvImportPage';
import LoginPage from './features/auth/LoginPage';
import { useAuth } from './features/auth/AuthContext';
import { canAccessMenuItem, MENU_ITEMS } from './navigation/menu';

export default function App() {
  const { loading, user } = useAuth();

  if (loading) {
    return (
      <main className="auth-page" aria-busy="true">
        <div className="auth-loading">Đang kiểm tra phiên đăng nhập...</div>
      </main>
    );
  }

  if (!user) {
    return <LoginPage />;
  }

  const visibleMenuItems = MENU_ITEMS.filter((item) => canAccessMenuItem(item, user.role));
  const viewerRedirect = <Navigate to="/qlhv-import" replace />;

  return (
    <Routes>
      <Route element={<AppLayout />}>
        <Route index element={user.role === 'Admin' ? <Dashboard /> : viewerRedirect} />
        <Route path="/hoc-vien" element={user.role === 'Admin' ? <HocVienPage /> : viewerRedirect} />
        <Route path="/in-the-hoc-vien" element={user.role === 'Admin' ? <HocVienCardPrintPage /> : viewerRedirect} />
        <Route path="/dong-bo-v2" element={user.role === 'Admin' ? <MotoSyncPage /> : viewerRedirect} />
        <Route path="/qlhv-import" element={<QlhvImportPage />} />
        <Route
          path="/cau-hinh-ket-noi-csdt"
          element={user.role === 'Admin'
            ? <CsdtConnectionProfilesPage />
            : <Navigate to="/qlhv-import" replace />}
        />
        {visibleMenuItems.filter((item) =>
          !['/', '/hoc-vien', '/in-the-hoc-vien', '/dong-bo-v2', '/qlhv-import', '/cau-hinh-ket-noi-csdt'].includes(item.path),
        ).map((item) => (
          <Route
            key={item.path}
            path={item.path}
            element={<ModulePage />}
          />
        ))}
        <Route path="*" element={<Navigate to={user.role === 'Admin' ? '/' : '/qlhv-import'} replace />} />
      </Route>
    </Routes>
  );
}
