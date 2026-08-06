import type { ReactNode } from 'react';
import type {
  AssignmentReference,
  PagedResult,
  PreviewRowStatus,
} from './types';

const DISPLAY_TIME_ZONE = 'Asia/Ho_Chi_Minh';
const DATE_FORMAT = new Intl.DateTimeFormat('vi-VN', {
  timeZone: DISPLAY_TIME_ZONE,
});
const DATE_TIME_FORMAT = new Intl.DateTimeFormat('vi-VN', {
  dateStyle: 'short',
  timeStyle: 'short',
  timeZone: DISPLAY_TIME_ZONE,
});

export function formatDate(value: string | null | undefined): string {
  if (!value) return '—';
  const businessDate = /^(\d{4})-(\d{2})-(\d{2})/.exec(value);
  if (businessDate) {
    return `${businessDate[3]}/${businessDate[2]}/${businessDate[1]}`;
  }
  const parsed = new Date(value);
  return Number.isNaN(parsed.getTime()) ? value : DATE_FORMAT.format(parsed);
}

export function formatDateTime(value: string | null | undefined): string {
  if (!value) return '—';
  const parsed = new Date(value);
  return Number.isNaN(parsed.getTime()) ? value : DATE_TIME_FORMAT.format(parsed);
}

export function StatusBadge({
  active,
  manualReview = false,
  label,
}: {
  active: boolean;
  manualReview?: boolean;
  label?: string;
}) {
  const tone = manualReview ? 'is-warning' : active ? 'is-active' : 'is-inactive';
  return (
    <span className={`assignment-badge ${tone}`}>
      {manualReview ? 'Cần kiểm tra' : label || (active ? 'Hoạt động' : 'Ngừng hoạt động')}
    </span>
  );
}

export function PreviewStatusBadge({ status }: { status: PreviewRowStatus }) {
  const labels: Record<PreviewRowStatus, string> = {
    READY: 'Sẵn sàng',
    NO_CHANGE: 'Không thay đổi',
    NOT_FOUND: 'Không tìm thấy',
    AMBIGUOUS: 'Không duy nhất',
    INACTIVE_REFERENCE: 'Tham chiếu ngừng dùng',
    INVALID: 'Không hợp lệ',
    CONFLICT: 'Xung đột',
  };
  const success = status === 'READY' || status === 'NO_CHANGE';
  return (
    <span className={`assignment-badge ${success ? 'is-active' : 'is-warning'}`}>
      {labels[status]}
    </span>
  );
}

export function ReferenceLabel({
  value,
  overridden,
}: {
  value: AssignmentReference | null;
  overridden?: boolean;
}) {
  if (!value) {
    return <span className="assignment-muted">Chưa gán</span>;
  }
  return (
    <span className="assignment-reference">
      <span>{value.code} · {value.label}</span>
      {!value.isActive && <StatusBadge active={false} label="Đã ngừng dùng" />}
      {value.isManualReview && <StatusBadge active={false} manualReview />}
      {overridden && <span className="assignment-badge is-info">Ghi đè</span>}
    </span>
  );
}

export function Pager<T>({
  result,
  onPage,
  disabled = false,
}: {
  result: Pick<PagedResult<T>, 'page' | 'totalPages' | 'totalItems'>;
  onPage: (page: number) => void;
  disabled?: boolean;
}) {
  return (
    <div className="pager">
      <span>{result.totalItems.toLocaleString('vi-VN')} kết quả</span>
      <div className="pager__controls">
        <button
          type="button"
          className="btn btn--ghost btn--sm"
          onClick={() => onPage(result.page - 1)}
          disabled={disabled || result.page <= 1}
        >
          Trước
        </button>
        <span>Trang {result.page}/{Math.max(1, result.totalPages)}</span>
        <button
          type="button"
          className="btn btn--ghost btn--sm"
          onClick={() => onPage(result.page + 1)}
          disabled={disabled || result.page >= result.totalPages}
        >
          Sau
        </button>
      </div>
    </div>
  );
}

export function Modal({
  title,
  children,
  onClose,
  wide = false,
}: {
  title: string;
  children: ReactNode;
  onClose: () => void;
  wide?: boolean;
}) {
  return (
    <div
      className="assignment-modal"
      role="dialog"
      aria-modal="true"
      aria-label={title}
      onMouseDown={(event) => {
        if (event.target === event.currentTarget) onClose();
      }}
    >
      <section className={`panel assignment-modal__dialog${wide ? ' is-wide' : ''}`}>
        <header className="assignment-modal__header">
          <h3>{title}</h3>
          <button type="button" className="photo-modal__close" onClick={onClose} aria-label="Đóng">
            ×
          </button>
        </header>
        {children}
      </section>
    </div>
  );
}

export function PageMessage({
  kind,
  children,
}: {
  kind: 'error' | 'success' | 'warning' | 'info';
  children: ReactNode;
}) {
  return (
    <div className={`assignment-message is-${kind}`} role={kind === 'error' ? 'alert' : 'status'}>
      {children}
    </div>
  );
}

export function EmptyState({ children }: { children: ReactNode }) {
  return <div className="state">{children}</div>;
}

export function createIdempotencyKey(): string {
  if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') {
    return crypto.randomUUID();
  }
  if (typeof crypto !== 'undefined' && typeof crypto.getRandomValues === 'function') {
    const bytes = crypto.getRandomValues(new Uint8Array(16));
    return Array.from(bytes, (value) => value.toString(16).padStart(2, '0')).join('');
  }
  throw new Error('Trình duyệt không hỗ trợ tạo khóa xác nhận an toàn.');
}

export function downloadBlob(blob: Blob, fileName: string): void {
  const url = URL.createObjectURL(blob);
  const anchor = document.createElement('a');
  anchor.href = url;
  anchor.download = fileName;
  document.body.appendChild(anchor);
  anchor.click();
  anchor.remove();
  URL.revokeObjectURL(url);
}
