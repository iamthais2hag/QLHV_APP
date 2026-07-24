import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import {
  approveQlhvProcessedPhoto,
  getQlhvPhotoPreviewUrl,
  getQlhvPhotoProcessingPage,
  reprocessQlhvPhoto,
} from './api';
import type {
  QlhvImportSourceProfileCode,
  QlhvPhotoProcessingItem,
  QlhvPhotoProcessingPage,
  QlhvPhotoProcessingStatus,
} from './types';

const PAGE_SIZE = 12;

export interface PhotoProcessingPanelProps {
  isAdmin: boolean;
  photoVersion?: string | number | null;
  reloadToken: number;
  writeBlockedReason?: string | null;
}

export default function PhotoProcessingPanel({
  isAdmin,
  photoVersion,
  reloadToken,
  writeBlockedReason = null,
}: PhotoProcessingPanelProps) {
  const [sourceProfileCode, setSourceProfileCode] = useState<
    QlhvImportSourceProfileCode | ''
  >('');
  const [statusFilter, setStatusFilter] = useState<QlhvPhotoProcessingStatus | ''>('');
  const [reviewRequired, setReviewRequired] = useState(false);
  const [page, setPage] = useState(1);
  const [data, setData] = useState<QlhvPhotoProcessingPage | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [pendingIds, setPendingIds] = useState<Set<number>>(() => new Set());
  const pendingIdsRef = useRef<Set<number>>(new Set());
  const abortRef = useRef<AbortController | null>(null);

  const load = useCallback(async () => {
    abortRef.current?.abort();
    const controller = new AbortController();
    abortRef.current = controller;
    setLoading(true);
    try {
      const result = await getQlhvPhotoProcessingPage({
        sourceProfileCode: sourceProfileCode || undefined,
        status: statusFilter || undefined,
        reviewRequired: reviewRequired || undefined,
        page,
        pageSize: PAGE_SIZE,
      }, controller.signal);
      setData(result);
      setError(null);
    } catch (reason) {
      if (controller.signal.aborted) return;
      setData(null);
      setError(reason instanceof Error
        ? reason.message
        : 'Không thể tải danh sách xử lý ảnh.');
    } finally {
      if (!controller.signal.aborted) setLoading(false);
    }
  }, [page, reviewRequired, sourceProfileCode, statusFilter]);

  useEffect(() => {
    void load();
    return () => abortRef.current?.abort();
  }, [load, photoVersion, reloadToken]);

  function changeFilters(update: () => void) {
    setNotice(null);
    setPage(1);
    update();
  }

  async function handleAction(
    item: QlhvPhotoProcessingItem,
    action: 'approve' | 'reprocess',
  ) {
    if (!isAdmin || writeBlockedReason || !data?.engineReady || pendingIdsRef.current.has(item.id)) {
      return;
    }
    pendingIdsRef.current.add(item.id);
    setPendingIds((current) => new Set(current).add(item.id));
    setError(null);
    setNotice(null);
    try {
      const updated = action === 'approve'
        ? await approveQlhvProcessedPhoto(item.id)
        : await reprocessQlhvPhoto(item.id);
      setData((current) => current
        ? {
            ...current,
            items: current.items.map((candidate) =>
              candidate.id === updated.id ? updated : candidate),
          }
        : current);
      setNotice(action === 'approve'
        ? `Đã chấp nhận ảnh của ${item.sourceMaDK}.`
        : `Đã đưa ảnh ${item.sourceMaDK} vào hàng đợi xử lý lại.`);
      await load();
    } catch (reason) {
      setError(reason instanceof Error
        ? reason.message
        : 'Không thể cập nhật trạng thái ảnh.');
    } finally {
      pendingIdsRef.current.delete(item.id);
      setPendingIds((current) => {
        const next = new Set(current);
        next.delete(item.id);
        return next;
      });
    }
  }

  const counts = data?.counts;
  const canGoPrevious = page > 1 && !loading;
  const canGoNext = !!data && page < data.totalPages && !loading;
  const versionLabel = useMemo(
    () => photoVersion === undefined || photoVersion === null
      ? 'chưa có'
      : String(photoVersion),
    [photoVersion],
  );

  return (
    <section className="panel qlhv-photo-processing" aria-label="Quản lý ảnh thẻ">
      <div className="qlhv-import-section-heading">
        <strong>Ảnh thẻ</strong>
        <span>Ảnh gốc chỉ đọc · ảnh nền xanh dẫn xuất có kiểm soát chất lượng</span>
      </div>

      <div className="qlhv-photo-processing__readiness">
        <span>Photo version: <strong>{versionLabel}</strong></span>
        <span>
          Engine: <strong className={data?.engineReady ? 'is-ready' : 'is-not-ready'}>
            {data?.engineReady ? 'Sẵn sàng' : 'Chưa sẵn sàng'}
          </strong>
        </span>
        {data?.readinessMessage && <span>{data.readinessMessage}</span>}
      </div>

      <div className="qlhv-photo-processing__counts">
        <PhotoCount label="Tổng" value={counts?.total} />
        <PhotoCount label="Thành công" value={(counts?.succeeded ?? 0) + (counts?.approved ?? 0)} tone="ok" />
        <PhotoCount label="Đã duyệt" value={counts?.approved} tone="ok" />
        <PhotoCount label="Cần kiểm tra" value={counts?.reviewRequired} tone="warning" />
        <PhotoCount label="Thất bại" value={counts?.failed} tone="failed" />
        <PhotoCount label="Đang chờ/xử lý" value={(counts?.pending ?? 0) + (counts?.processing ?? 0)} />
      </div>

      <div className="qlhv-photo-processing__filters">
        <label>
          <span>Nguồn</span>
          <select
            className="field__input"
            value={sourceProfileCode}
            onChange={(event) => changeFilters(() =>
              setSourceProfileCode(event.target.value as QlhvImportSourceProfileCode | ''))}
          >
            <option value="">Tất cả OTO/MOTO</option>
            <option value="CSDT_OTO">Ô tô · CSDT_OTO</option>
            <option value="CSDT_MOTO">Mô tô · CSDT_MOTO</option>
          </select>
        </label>
        <label>
          <span>Trạng thái</span>
          <select
            className="field__input"
            value={statusFilter}
            onChange={(event) => changeFilters(() =>
              setStatusFilter(event.target.value as QlhvPhotoProcessingStatus | ''))}
          >
            <option value="">Tất cả trạng thái</option>
            {PHOTO_STATUSES.map((item) => (
              <option value={item} key={item}>{formatPhotoStatus(item)}</option>
            ))}
          </select>
        </label>
        <label className="qlhv-photo-processing__review-filter">
          <input
            type="checkbox"
            checked={reviewRequired}
            onChange={(event) => changeFilters(() => setReviewRequired(event.target.checked))}
          />
          Chỉ xem “Cần kiểm tra”
        </label>
        <button
          type="button"
          className="btn btn--ghost"
          onClick={() => void load()}
          disabled={loading}
        >
          {loading ? 'Đang tải...' : 'Tải lại ảnh'}
        </button>
      </div>

      {!isAdmin && (
        <div className="qlhv-import-permission-note" role="status">
          Bạn không có quyền thực hiện. Viewer chỉ được xem ảnh và trạng thái xử lý.
        </div>
      )}
      {isAdmin && writeBlockedReason && (
        <div className="qlhv-import-permission-note" role="status">
          {writeBlockedReason}
        </div>
      )}
      {notice && <div className="qlhv-import-success" role="status">{notice}</div>}
      {error && <div className="qlhv-import-error" role="alert">{error}</div>}
      {loading && !data && <div className="qlhv-import-empty">Đang tải ảnh...</div>}
      {!loading && data?.items.length === 0 && (
        <div className="qlhv-import-empty">Không có ảnh phù hợp bộ lọc.</div>
      )}

      {data && data.items.length > 0 && (
        <div className="qlhv-photo-processing__grid">
          {data.items.map((item) => {
            const pending = pendingIds.has(item.id);
            const sourceUrl = getSafePreviewUrl(
              item.sourcePreviewUrl,
              getQlhvPhotoPreviewUrl(item.id, 'source', photoVersion),
            );
            const outputUrl = getSafePreviewUrl(
              item.outputPreviewUrl,
              getQlhvPhotoPreviewUrl(item.id, 'output', photoVersion),
            );
            return (
              <article className="qlhv-photo-card" key={item.id}>
                <header>
                  <div>
                    <strong>{item.studentName || item.sourceMaDK}</strong>
                    <span>{item.sourceMaDK}</span>
                  </div>
                  <span className={`qlhv-photo-status is-${item.processingStatus.toLowerCase()}`}>
                    {formatPhotoStatus(item.processingStatus)}
                  </span>
                </header>
                <div className="qlhv-photo-card__comparison">
                  <PhotoPreview title="Ảnh gốc" url={sourceUrl} emptyText="Không đọc được ảnh gốc" />
                  <PhotoPreview title="Ảnh nền xanh" url={outputUrl} emptyText="Chưa có ảnh dẫn xuất" />
                </div>
                <dl>
                  <dt>Nguồn</dt><dd>{item.sourceProfileCode}</dd>
                  <dt>Khóa</dt><dd>{item.maKhoaHoc ?? '—'}</dd>
                  <dt>Đường dẫn gốc</dt>
                  <dd>{formatSourcePath(item.sourcePathStatus, item.sourcePathKind)}</dd>
                  <dt>Confidence</dt><dd>{formatConfidence(item.processingConfidence)}</dd>
                  <dt>Xử lý lúc</dt><dd>{formatDate(item.processedAtUtc)}</dd>
                  <dt>Duyệt lúc</dt><dd>{formatDate(item.approvedAtUtc)}</dd>
                </dl>
                {item.errorMessage && <p className="qlhv-photo-card__error">{item.errorMessage}</p>}
                <div className="qlhv-photo-card__actions">
                  <button
                    type="button"
                    className="btn btn--primary btn--sm"
                    onClick={() => void handleAction(item, 'approve')}
                    disabled={!isAdmin || !!writeBlockedReason || pending || !data.engineReady || !canApprove(item)}
                    title={!isAdmin
                      ? 'Bạn không có quyền thực hiện'
                      : writeBlockedReason
                        ? writeBlockedReason
                      : !data.engineReady
                        ? data.readinessMessage ?? 'Engine ảnh chưa sẵn sàng'
                        : undefined}
                    aria-busy={pending}
                  >
                    {pending ? 'Đang xử lý...' : 'Chấp nhận'}
                  </button>
                  <button
                    type="button"
                    className="btn btn--ghost btn--sm"
                    onClick={() => void handleAction(item, 'reprocess')}
                    disabled={!isAdmin || !!writeBlockedReason || pending || !data.engineReady}
                    title={!isAdmin
                      ? 'Bạn không có quyền thực hiện'
                      : writeBlockedReason
                        ? writeBlockedReason
                      : !data.engineReady
                        ? data.readinessMessage ?? 'Engine ảnh chưa sẵn sàng'
                        : undefined}
                    aria-busy={pending}
                  >
                    {pending ? 'Đang xử lý...' : 'Xử lý lại'}
                  </button>
                </div>
              </article>
            );
          })}
        </div>
      )}

      {data && data.totalItems > 0 && (
        <div className="pager">
          <span>
            Tổng {data.totalItems.toLocaleString('vi-VN')} ảnh · Trang {page}/{Math.max(data.totalPages, 1)}
          </span>
          <div className="pager__controls">
            <button
              type="button"
              className="btn btn--ghost btn--sm"
              disabled={!canGoPrevious}
              onClick={() => setPage((current) => Math.max(1, current - 1))}
            >
              Trang trước
            </button>
            <button
              type="button"
              className="btn btn--ghost btn--sm"
              disabled={!canGoNext}
              onClick={() => setPage((current) => current + 1)}
            >
              Trang sau
            </button>
          </div>
        </div>
      )}
    </section>
  );
}

const PHOTO_STATUSES: readonly QlhvPhotoProcessingStatus[] = [
  'PENDING',
  'PROCESSING',
  'SUCCEEDED',
  'REVIEW_REQUIRED',
  'FAILED',
  'APPROVED',
];

function PhotoCount({
  label,
  value = 0,
  tone = 'default',
}: {
  label: string;
  value?: number;
  tone?: 'default' | 'ok' | 'warning' | 'failed';
}) {
  return (
    <div className={`is-${tone}`}>
      <span>{label}</span>
      <strong>{value.toLocaleString('vi-VN')}</strong>
    </div>
  );
}

function PhotoPreview({
  title,
  url,
  emptyText,
}: {
  title: string;
  url: string;
  emptyText: string;
}) {
  const [failed, setFailed] = useState(false);
  useEffect(() => setFailed(false), [url]);
  return (
    <figure>
      <figcaption>{title}</figcaption>
      {!failed
        ? <img src={url} alt={title} onError={() => setFailed(true)} />
        : <div>{emptyText}</div>}
    </figure>
  );
}

function getSafePreviewUrl(candidate: string | null, fallback: string): string {
  return candidate?.startsWith('/api/') ? candidate : fallback;
}

function canApprove(item: QlhvPhotoProcessingItem): boolean {
  return item.processingStatus === 'REVIEW_REQUIRED'
    || item.processingStatus === 'SUCCEEDED';
}

function formatPhotoStatus(status: QlhvPhotoProcessingStatus): string {
  const labels: Record<QlhvPhotoProcessingStatus, string> = {
    PENDING: 'Đang chờ',
    PROCESSING: 'Đang xử lý',
    SUCCEEDED: 'Thành công',
    REVIEW_REQUIRED: 'Cần kiểm tra',
    FAILED: 'Thất bại',
    APPROVED: 'Đã duyệt',
  };
  return labels[status];
}

function formatConfidence(value: number | null): string {
  if (value === null) return 'Chưa có';
  const normalized = value <= 1 ? value * 100 : value;
  return `${normalized.toLocaleString('vi-VN', { maximumFractionDigits: 1 })}%`;
}

function formatSourcePath(status: string, kind: string): string {
  const labels: Record<string, string> = {
    FOUND: 'Đã tìm thấy',
    MISSING: 'Thiếu ảnh',
    INVALID_PATH: 'Đường dẫn không an toàn',
    CURRENT_PATH: 'cấu trúc hiện tại',
    LEGACY_PATH: 'cấu trúc legacy',
    FALLBACK_PATH: 'đường dẫn fallback',
  };
  return `${labels[status] ?? status} · ${labels[kind] ?? kind}`;
}

function formatDate(value: string | null | undefined): string {
  if (!value) return 'Chưa có';
  const date = new Date(value);
  return Number.isNaN(date.getTime())
    ? value
    : date.toLocaleString('vi-VN');
}
