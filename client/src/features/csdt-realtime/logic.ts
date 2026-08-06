import type {
  CsdtRealtimeStreamCode,
  CsdtRealtimeStreamStatus,
  CsdtRealtimeVehicleType,
  CsdtReversePlan,
} from './types';

export const CSDT_REALTIME_STREAMS: readonly CsdtRealtimeStreamCode[] = [
  'OTO_V2_TO_V1',
  'MOTO_V2_TO_V1',
];

export const CSDT_REALTIME_PRESENTATION: Record<
CsdtRealtimeStreamCode,
{
  title: string;
  vehicleType: CsdtRealtimeVehicleType;
  maCSDT: string;
}
> = {
  OTO_V2_TO_V1: {
    title: '\u00d4 t\u00f4',
    vehicleType: 'OTO',
    maCSDT: '66029',
  },
  MOTO_V2_TO_V1: {
    title: 'M\u00f4 t\u00f4',
    vehicleType: 'MOTO',
    maCSDT: '66030',
  },
};

export function hasExpectedStreamMapping(status: CsdtRealtimeStreamStatus): boolean {
  const expected = CSDT_REALTIME_PRESENTATION[status.streamCode];
  return status.vehicleType === expected.vehicleType
    && status.maCSDT === expected.maCSDT
    && status.sourceProfileCode.length > 0
    && status.targetProfileCode.length > 0
    && status.sourceDatabaseName.length > 0
    && status.targetDatabaseName.length > 0;
}

export function isRealtimeBusy(status: CsdtRealtimeStreamStatus): boolean {
  const state = normalizeStatus(status.state);
  return !!status.activeRunId
    || state === 'BASELINING'
    || state === 'CATCHINGUP'
    || state === 'RUNNINGBASELINE'
    || state === 'RETRYING';
}

export function shouldPollRealtimeFast(streams: CsdtRealtimeStreamStatus[]): boolean {
  return streams.some(isRealtimeBusy);
}

export function canRetryRealtime(status: CsdtRealtimeStreamStatus): boolean {
  const state = normalizeStatus(status.state);
  return state === 'ERROR'
    || state === 'FAILED'
    || state === 'RETRYWAITING';
}

export function streamActionDisabledReason(
  status: CsdtRealtimeStreamStatus,
  isAdmin: boolean,
  pending: boolean,
): string | null {
  if (!isAdmin || !status.writeAuthorized || status.currentUserRole !== 'Admin') {
    return 'Bạn không có quyền thực hiện: cần vai trò Admin.';
  }
  if (!hasExpectedStreamMapping(status)) {
    return 'Mapping stream từ máy chủ không khớp allowlist cố định.';
  }
  if (pending) {
    return 'Thao tác trước đang được xử lý.';
  }
  if (!status.stateToken) {
    return 'Chưa có state token hợp lệ. Hãy tải lại trạng thái.';
  }
  if (isRealtimeBusy(status)) {
    return 'Stream đang xử lý một operation khác.';
  }
  if (status.actionBlockers.length > 0) {
    return status.actionBlockers[0];
  }
  return null;
}

export function reverseExecuteDisabledReason(
  plan: CsdtReversePlan | null,
  isAdmin: boolean,
  pending: boolean,
): string | null {
  if (!isAdmin) {
    return 'Bạn không có quyền thực hiện: cần vai trò Admin.';
  }
  if (pending) {
    return 'Thao tác trước đang được xử lý.';
  }
  if (!plan) {
    return 'Cần lập kế hoạch read-only trước khi thực thi.';
  }
  if (!plan.planToken) {
    return 'Kế hoạch không có plan token hợp lệ.';
  }
  if (plan.blockers.length > 0) {
    return plan.blockers[0];
  }
  if (!plan.executable) {
    return 'Kế hoạch hiện không đủ điều kiện thực thi.';
  }
  if (plan.safeInsertRows + plan.safeUpdateRows <= 0) {
    return 'Không có dòng an toàn cần ghi.';
  }
  return null;
}

export function formatRealtimeState(value: string): string {
  const normalized = normalizeStatus(value);
  const labels: Record<string, string> = {
    DISABLED: 'Đã tắt',
    NEEDSBASELINE: 'Cần baseline',
    BASELINING: 'Đang baseline',
    CATCHINGUP: 'Đang bắt kịp thay đổi',
    RUNNING: 'Đang theo dõi realtime',
    RETRYWAITING: 'Đang chờ thử lại',
    RETRYING: 'Đang thử lại',
    ERROR: 'Có lỗi',
    FAILED: 'Thất bại',
    IDLE: 'Sẵn sàng',
  };
  return labels[normalized] ?? value;
}

export function formatBaselineStatus(value: string): string {
  const normalized = normalizeStatus(value);
  const labels: Record<string, string> = {
    NOTSTARTED: 'Chưa chạy',
    PENDING: 'Đang chờ',
    RUNNING: 'Đang chạy',
    SUCCEEDED: 'Hoàn tất',
    COMPLETED: 'Hoàn tất',
    FAILED: 'Thất bại',
    EXPIRED: 'Checkpoint hết hạn',
  };
  return labels[normalized] ?? value;
}

export function formatNumber(value: number | null | undefined): string {
  return value === null || value === undefined
    ? 'Chưa có'
    : new Intl.NumberFormat('vi-VN').format(value);
}

export function formatDateTime(value: string | null | undefined): string {
  if (!value) {
    return 'Chưa có';
  }
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString('vi-VN');
}

export function statusTone(value: string): 'ok' | 'busy' | 'warning' | 'failed' | 'default' {
  const normalized = normalizeStatus(value);
  if (normalized === 'RUNNING' || normalized === 'SUCCEEDED' || normalized === 'COMPLETED') {
    return 'ok';
  }
  if (normalized === 'BASELINING' || normalized === 'CATCHINGUP' || normalized === 'RETRYING') {
    return 'busy';
  }
  if (normalized === 'ERROR' || normalized === 'FAILED') {
    return 'failed';
  }
  if (
    normalized === 'DISABLED' ||
    normalized === 'NEEDSBASELINE' ||
    normalized === 'RETRYWAITING' ||
    normalized === 'SKIPPED' ||
    normalized === 'SKIPPEDUNSUPPORTEDSCHEMA'
  ) {
    return 'warning';
  }
  return 'default';
}

function normalizeStatus(value: string): string {
  return value.trim().replace(/[-_\s]/g, '').toUpperCase();
}
