import { useCallback, useEffect, useState } from 'react';
import { getRuntimeStatus } from './api';
import { isRuntimeReady, type RuntimeStatus } from './types';

interface StatusItem {
  label: string;
  ready: boolean;
  detail: string;
}

export default function RuntimeStatusPage() {
  const [status, setStatus] = useState<RuntimeStatus | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [checkedAt, setCheckedAt] = useState<Date | null>(null);

  const loadStatus = useCallback(async (signal?: AbortSignal) => {
    setLoading(true);
    setError(null);
    try {
      const result = await getRuntimeStatus(signal);
      setStatus(result);
      setCheckedAt(new Date());
    } catch (loadError) {
      if (!(loadError instanceof DOMException && loadError.name === 'AbortError')) {
        setError(loadError instanceof Error ? loadError.message : 'Không thể đọc trạng thái hệ thống.');
      }
    } finally {
      if (!signal?.aborted) {
        setLoading(false);
      }
    }
  }, []);

  useEffect(() => {
    const controller = new AbortController();
    void loadStatus(controller.signal);
    return () => controller.abort();
  }, [loadStatus]);

  const items = status ? buildStatusItems(status) : [];

  return (
    <div className="runtime-status-page">
      <section className="panel runtime-status-hero">
        <div>
          <span className="runtime-status-eyebrow">Chỉ đọc · Quản trị viên</span>
          <h2>Trạng thái hệ thống</h2>
          <p>Kiểm tra mức sẵn sàng của cơ sở dữ liệu, xác thực, nguồn BAK và lưu trữ tệp.</p>
        </div>
        <div className="runtime-status-actions">
          {status && (
            <span className={`runtime-status-overall ${isRuntimeReady(status) ? 'is-ready' : 'is-not-ready'}`}>
              {isRuntimeReady(status) ? 'Hệ thống sẵn sàng' : 'Hệ thống chưa sẵn sàng'}
            </span>
          )}
          <button
            type="button"
            className="btn btn--secondary"
            onClick={() => void loadStatus()}
            disabled={loading}
          >
            {loading ? 'Đang kiểm tra...' : 'Kiểm tra lại'}
          </button>
        </div>
      </section>

      {error && (
        <div className="runtime-status-error" role="alert">
          <strong>Không thể kiểm tra trạng thái.</strong>
          <span>{error}</span>
        </div>
      )}

      {loading && !status && (
        <div className="state" aria-live="polite" aria-busy="true">
          <div className="spinner" />
          Đang đọc trạng thái hệ thống...
        </div>
      )}

      {status && (
        <>
          <section className="runtime-status-grid" aria-label="Kết quả kiểm tra trạng thái">
            {items.map((item) => (
              <article className={`runtime-status-card ${item.ready ? 'is-ready' : 'is-not-ready'}`} key={item.label}>
                <div className="runtime-status-card__heading">
                  <h3>{item.label}</h3>
                  <span>{item.ready ? 'Đạt' : 'Chưa đạt'}</span>
                </div>
                <p>{item.detail}</p>
              </article>
            ))}
          </section>

          <section className="panel runtime-status-details">
            <div className="runtime-status-details__heading">
              <div>
                <h3>Thông tin an toàn từ máy chủ</h3>
                <p>Không hiển thị connection string, mật khẩu, token hoặc dữ liệu bí mật.</p>
              </div>
              {checkedAt && <time dateTime={checkedAt.toISOString()}>Kiểm tra lúc {formatTime(checkedAt)}</time>}
            </div>
            {status.messages.length > 0 ? (
              <ul>
                {status.messages.map((message, index) => <li key={`${index}-${message}`}>{message}</li>)}
              </ul>
            ) : (
              <p className="runtime-status-details__empty">Máy chủ không báo thêm vấn đề.</p>
            )}
          </section>
        </>
      )}
    </div>
  );
}

function buildStatusItems(status: RuntimeStatus): StatusItem[] {
  const databaseNameIsValid = status.databaseName?.toLocaleUpperCase('en-US') === 'QLHV_APP';
  const items: StatusItem[] = [
    ...(status.configurationReady === undefined ? [] : [{
      label: 'Cấu hình Production',
      ready: status.configurationReady,
      detail: status.configurationReady
        ? 'Cấu hình runtime local đã được nạp và hợp lệ.'
        : 'Cấu hình runtime local đang thiếu hoặc không hợp lệ.',
    }]),
    {
      label: 'Cơ sở dữ liệu QLHV_APP',
      ready: status.databaseConnected && databaseNameIsValid,
      detail: status.databaseConnected
        ? `Đã kết nối: ${status.databaseName ?? 'không xác định'}`
        : 'Chưa kết nối được cơ sở dữ liệu.',
    },
    {
      label: 'Xác thực tài khoản',
      ready: status.authenticationReady,
      detail: status.authenticationReady
        ? 'Role Admin/Viewer và tài khoản quản trị đã sẵn sàng.'
        : 'Cấu hình xác thực hoặc tài khoản quản trị chưa sẵn sàng.',
    },
    {
      label: 'Schema bắt buộc',
      ready: status.requiredSchemaReady,
      detail: status.requiredSchemaReady
        ? 'Các bảng bắt buộc đã đầy đủ.'
        : 'Thiếu một hoặc nhiều bảng bắt buộc.',
    },
    {
      label: 'Connection profile BAK',
      ready: status.backupProfilesReady,
      detail: status.backupProfilesReady
        ? 'CSDT_OTO_BAK và CSDT_MOTO_BAK truy cập được.'
        : 'Một hoặc nhiều profile/database BAK chưa sẵn sàng.',
    },
    ...(status.backupStorageReady === undefined ? [] : [{
      label: 'Thư mục SQL backup',
      ready: status.backupStorageReady,
      detail: status.backupStorageReady
        ? 'Thư mục backup của SQL Server đã sẵn sàng.'
        : 'Thư mục backup của SQL Server chưa sẵn sàng.',
    }]),
    {
      label: 'Lưu trữ tệp',
      ready: status.fileStorageReady,
      detail: status.fileStorageReady
        ? 'Các thư mục lưu trữ bắt buộc đã sẵn sàng.'
        : 'Một hoặc nhiều thư mục lưu trữ chưa sẵn sàng.',
    },
    ...(status.runtimeStorageReady === undefined ? [] : [{
      label: 'Lưu trữ runtime',
      ready: status.runtimeStorageReady,
      detail: status.runtimeStorageReady
        ? 'Thư mục logs/run có thể ghi.'
        : 'Thư mục logs/run chưa có quyền ghi phù hợp.',
    }]),
    {
      label: 'Phiên bản ứng dụng',
      ready: !['Không xác định', 'unknown'].includes(status.version),
      detail: `${status.version} · ${status.environment}`,
    },
  ];

  return items;
}

function formatTime(value: Date): string {
  return new Intl.DateTimeFormat('vi-VN', {
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
    day: '2-digit',
    month: '2-digit',
    year: 'numeric',
  }).format(value);
}
