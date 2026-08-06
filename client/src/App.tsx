import { Routes, Route, Navigate } from 'react-router-dom';
import AppLayout from './layout/AppLayout';
import Dashboard from './pages/Dashboard';
import ModulePage from './pages/ModulePage';
import CsdtConnectionProfilesPage from './features/csdt-connections/CsdtConnectionProfilesPage';
import HocVienPage from './features/hoc-vien/HocVienPage';
import HocVienCardPrintPage from './features/hoc-vien/HocVienCardPrintPage';
import CsdtRealtimeSyncPage from './features/csdt-realtime/CsdtRealtimeSyncPage';
import QlhvImportPage from './features/qlhv-import/QlhvImportPage';
import RuntimeStatusPage from './features/runtime-status/RuntimeStatusPage';
import LoginPage from './features/auth/LoginPage';
import ChangePasswordDialog from './features/auth/ChangePasswordDialog';
import UserManagementPage from './features/admin-users/UserManagementPage';
import TeacherPage from './features/course-assignment/TeacherPage';
import VehiclePage from './features/course-assignment/VehiclePage';
import CourseListPage from './features/course-assignment/CourseListPage';
import CourseDetailPage from './features/course-assignment/CourseDetailPage';
import { useAuth } from './features/auth/AuthContext';
import {
  canManageUsers,
  canOperateBusinessData,
} from './features/auth/permissions';
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

  if (user.mustChangePassword) {
    return <ChangePasswordDialog required />;
  }

  const visibleMenuItems = MENU_ITEMS.filter((item) => canAccessMenuItem(item, user.role));
  const unauthorizedRedirect = <Navigate to="/" replace />;
  const canOperateBusiness = canOperateBusinessData(user.role);
  const canManageAccounts = canManageUsers(user.role);

  return (
    <Routes>
      <Route element={<AppLayout />}>
        <Route index element={<Dashboard />} />
        <Route path="/hoc-vien" element={<HocVienPage />} />
        <Route path="/giao-vien" element={<TeacherPage />} />
        <Route path="/xe-tap-lai" element={<VehiclePage />} />
        <Route path="/khoa-hoc" element={<CourseListPage />} />
        <Route path="/khoa-hoc/:khoaHocId" element={<CourseDetailPage />} />
        <Route
          path="/in-the-hoc-vien"
          element={canOperateBusiness ? <HocVienCardPrintPage /> : unauthorizedRedirect}
        />
        <Route path="/dong-bo-v2" element={<CsdtRealtimeSyncPage />} />
        <Route path="/qlhv-import" element={<QlhvImportPage />} />
        <Route
          path="/trang-thai-he-thong"
          element={user.role === 'Admin'
            ? <RuntimeStatusPage />
            : <Navigate to="/qlhv-import" replace />}
        />
        <Route
          path="/cau-hinh-ket-noi-csdt"
          element={user.role === 'Admin'
            ? <CsdtConnectionProfilesPage />
            : unauthorizedRedirect}
        />
        <Route
          path="/admin/users"
          element={canManageAccounts
            ? <UserManagementPage />
            : unauthorizedRedirect}
        />
        {visibleMenuItems.filter((item) =>
          ![
            '/',
            '/hoc-vien',
            '/giao-vien',
            '/xe-tap-lai',
            '/khoa-hoc',
            '/in-the-hoc-vien',
            '/dong-bo-v2',
            '/qlhv-import',
            '/trang-thai-he-thong',
            '/cau-hinh-ket-noi-csdt',
            '/admin/users',
          ].includes(item.path),
        ).map((item) => (
          <Route
            key={item.path}
            path={item.path}
            element={<ModulePage />}
          />
        ))}
        <Route path="*" element={<Navigate to="/" replace />} />
      </Route>
    </Routes>
  );
}
