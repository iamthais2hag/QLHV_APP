import { useCallback, useEffect, useState } from 'react';
import { getRuntimeStatus } from './api';
import { isRuntimeReady, isTimeMutationAllowed, type RuntimeStatus } from './types';
import RealtimeMasterControlPanel from './RealtimeMasterControlPanel';

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
  const browserNow = status ? new Date() : null;
  const browserSkewMilliseconds = status && browserNow
    ? browserNow.getTime() - new Date(status.time.serverUtcNow).getTime()
    : null;

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
          <RealtimeMasterControlPanel />

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

          {browserNow && (
            <section className="panel runtime-status-details" aria-label="Thẩm quyền thời gian">
              <div className="runtime-status-details__heading">
                <div>
                  <h3>Thẩm quyền thời gian</h3>
                  <p>Audit dùng UTC của SQL/API; ngày nghiệp vụ là trường người dùng chọn riêng.</p>
                </div>
                <strong>{status.time.health}</strong>
              </div>
              <dl>
                <div><dt>Giờ máy chủ</dt><dd>{formatTime(new Date(status.time.serverUtcNow))}</dd></div>
                <div><dt>Giờ cơ sở dữ liệu</dt><dd>{formatOptionalTime(status.time.databaseUtcNow)}</dd></div>
                <div><dt>Giờ trình duyệt</dt><dd>{formatTime(browserNow)}</dd></div>
                <div><dt>Lệch API/SQL</dt><dd>{formatSkew(status.time.clockSkewMilliseconds)}</dd></div>
                <div><dt>Lệch trình duyệt/máy chủ</dt><dd>{formatSkew(browserSkewMilliseconds)}</dd></div>
                <div><dt>Windows Time</dt><dd>{status.time.windowsTimeServiceState}</dd></div>
                <div><dt>Peer cấu hình</dt><dd>{status.time.configuredPeer}</dd></div>
                <div><dt>Nguồn hiện tại</dt><dd>{status.time.currentSource}</dd></div>
                <div><dt>Đồng bộ thành công gần nhất</dt><dd>{formatOptionalTime(status.time.lastSuccessfulSyncUtc)}</dd></div>
                <div><dt>Last Sync Error</dt><dd>{status.time.lastSyncError}</dd></div>
                <div><dt>Phase offset</dt><dd>{formatSkew(status.time.phaseOffsetMilliseconds)}</dd></div>
                <div><dt>Tuổi lần sync tốt</dt><dd>{formatSeconds(status.time.lastSuccessfulSyncAgeSeconds)}</dd></div>
                <div><dt>Chu kỳ poll hiệu lực</dt><dd>{formatSeconds(status.time.effectivePollIntervalSeconds)}</dd></div>
                <div><dt>SQL clock sẵn sàng</dt><dd>{status.time.databaseClockAvailable ? 'Có' : 'Không'}</dd></div>
                <div><dt>TimeHealth</dt><dd>{status.time.health}</dd></div>
                <div><dt>Reason code</dt><dd>{status.time.reasonCode}</dd></div>
                <div><dt>Đánh giá lúc</dt><dd>{formatTime(new Date(status.time.evaluatedAtUtc))}</dd></div>
              </dl>
              {browserSkewMilliseconds !== null && Math.abs(browserSkewMilliseconds) > 5_000 && (
                <p className="runtime-status-details__empty" role="status">
                  Cảnh báo: giờ máy đang mở trình duyệt lệch đáng kể so với máy chủ.
                </p>
              )}
              <p><strong>Giờ máy người dùng chỉ dùng để hiển thị và so sánh, không phải thời gian có thẩm quyền của hệ thống.</strong></p>
              <p>Giờ máy người dùng không được dùng làm thời điểm ghi nhận hệ thống.</p>
            </section>
          )}

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
    {
      label: 'SQL UTC có thẩm quyền',
      ready: isTimeMutationAllowed(status.time),
      detail: isTimeMutationAllowed(status.time)
        ? 'SQL Server trả SYSUTCDATETIME(); W32Time/NTP chỉ dùng để chẩn đoán.'
        : 'Không đọc được SYSUTCDATETIME() từ SQL Server; thao tác ghi bị chặn.',
    },
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
        ? 'Role Admin/Employee/Viewer và tài khoản quản trị đã sẵn sàng.'
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
    timeZone: 'Asia/Ho_Chi_Minh',
  }).format(value);
}

function formatOptionalTime(value: string | null): string {
  return value ? formatTime(new Date(value)) : 'Không đọc được';
}

function formatSkew(value: number | null): string {
  if (value === null || !Number.isFinite(value)) {
    return 'Không đọc được';
  }
  return `${Math.round(value)} ms`;
}

function formatSeconds(value: number | null): string {
  if (value === null || !Number.isFinite(value)) {
    return 'Không đọc được';
  }
  return `${Math.round(value)} giây`;
}
