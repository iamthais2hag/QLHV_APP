import { useState } from 'react';
import { useLocation } from 'react-router-dom';
import { useDataVersionRefresh } from '../features/data-version/useDataVersionRefresh';
import type { DataVersionResource } from '../features/data-version/types';

/**
 * Khung trang dùng chung cho các phân hệ. Tiêu đề và mô tả đã hiển thị ở top bar.
 */
export default function ModulePage() {
  const location = useLocation();
  const [reloadCount, setReloadCount] = useState(0);
  const resource = getResourceForPath(location.pathname);
  const dataVersion = useDataVersionRefresh({
    resources: resource ? [resource] : [],
    enabled: resource !== null,
    onVersionChanged: () => setReloadCount((current) => current + 1),
  });

  return (
    <div className="panel">
      <p className="card__label" style={{ margin: 0 }}>
        Chưa có dữ liệu để hiển thị.
      </p>
      {resource && (
        <div className="module-data-refresh">
          <button
            type="button"
            className="btn btn--ghost"
            onClick={() => void dataVersion.reload()}
            disabled={dataVersion.checking}
          >
            {dataVersion.checking ? 'Đang tải lại...' : 'Tải lại dữ liệu'}
          </button>
          <span>
            Phiên bản: {String(dataVersion.version?.[resource] ?? 'chưa có')}
            {reloadCount > 0 ? ` · đã cập nhật ${reloadCount} lần` : ''}
          </span>
          {dataVersion.error && <span className="field__warning">{dataVersion.error}</span>}
        </div>
      )}
    </div>
  );
}

function getResourceForPath(path: string): DataVersionResource | null {
  if (path === '/khoa-hoc') {
    return 'khoaHocVersion';
  }
  if (path === '/giao-vien') {
    return 'giaoVienVersion';
  }
  return null;
}
