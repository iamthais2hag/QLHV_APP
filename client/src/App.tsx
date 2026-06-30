import { Routes, Route, Navigate } from 'react-router-dom';
import AppLayout from './layout/AppLayout';
import Dashboard from './pages/Dashboard';
import ModulePage from './pages/ModulePage';
import CsdtConnectionProfilesPage from './features/csdt-connections/CsdtConnectionProfilesPage';
import HocVienPage from './features/hoc-vien/HocVienPage';
import HocVienCardPrintPage from './features/hoc-vien/HocVienCardPrintPage';
import MotoSyncPage from './features/moto-sync/MotoSyncPage';
import { MENU_ITEMS } from './navigation/menu';

export default function App() {
  return (
    <Routes>
      <Route element={<AppLayout />}>
        <Route index element={<Dashboard />} />
        <Route path="/hoc-vien" element={<HocVienPage />} />
        <Route path="/in-the-hoc-vien" element={<HocVienCardPrintPage />} />
        <Route path="/dong-bo-v2" element={<MotoSyncPage />} />
        <Route path="/cau-hinh-ket-noi-csdt" element={<CsdtConnectionProfilesPage />} />
        {MENU_ITEMS.filter((item) =>
          !['/', '/hoc-vien', '/in-the-hoc-vien', '/dong-bo-v2', '/cau-hinh-ket-noi-csdt'].includes(item.path),
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
