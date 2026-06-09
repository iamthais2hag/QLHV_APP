import { Routes, Route, Navigate } from 'react-router-dom';
import AppLayout from './layout/AppLayout';
import Dashboard from './pages/Dashboard';
import ModulePage from './pages/ModulePage';
import HocVienPage from './features/hoc-vien/HocVienPage';
import { MENU_ITEMS } from './navigation/menu';

export default function App() {
  return (
    <Routes>
      <Route element={<AppLayout />}>
        <Route index element={<Dashboard />} />
        <Route path="/hoc-vien" element={<HocVienPage />} />
        {MENU_ITEMS.filter((item) => item.path !== '/' && item.path !== '/hoc-vien').map((item) => (
          <Route
            key={item.path}
            path={item.path}
            element={<ModulePage title={item.label} description={item.description} />}
          />
        ))}
        <Route path="*" element={<Navigate to="/" replace />} />
      </Route>
    </Routes>
  );
}
