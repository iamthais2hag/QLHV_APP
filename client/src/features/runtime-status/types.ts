export type TimeHealthStatus = 'HEALTHY' | 'WARNING' | 'BLOCKED';

export interface TimeHealth {
  health: TimeHealthStatus;
  reasonCode: string;
  writesAllowed: boolean;
  databaseClockAvailable: boolean;
  serverUtcNow: string;
  databaseUtcNow: string | null;
  databaseUtcQueryMilliseconds: number;
  clockSkewMilliseconds: number | null;
  monotonicQueryMilliseconds: number;
  timeZone: string;
  displayTimeZone: string;
  windowsTimeServiceState: string;
  configuredPeer: string | null;
  currentSource: string | null;
  lastSuccessfulSyncUtc: string | null;
  lastSyncError: number | null;
  phaseOffsetMilliseconds: number | null;
  lastSuccessfulSyncAgeSeconds: number | null;
  effectivePollIntervalSeconds: number | null;
  evaluatedAtUtc: string;
  messages: string[];
}

export interface RuntimeStatus {
  isReady?: boolean;
  version: string;
  environment: string;
  configurationReady?: boolean;
  databaseConnected: boolean;
  databaseName: string | null;
  authenticationReady: boolean;
  requiredSchemaReady: boolean;
  backupProfilesReady: boolean;
  backupStorageReady?: boolean;
  fileStorageReady: boolean;
  runtimeStorageReady?: boolean;
  timeContractVersion: '2.0';
  time: TimeHealth;
  messages: string[];
}

export type RealtimeControlState = 'OFF' | 'ON' | 'BLOCKED';

export interface RealtimeProfileBacklog {
  sourceProfileCode: string;
  checkpointVersion: number;
  currentVersion: number;
  minimumValidVersion: number;
  backlogVersions: number;
  isWindowValid: boolean;
}

export interface RealtimeRunRequest {
  runRequestId: string;
  status: 'PENDING' | 'RUNNING' | 'COMPLETED' | 'BLOCKED';
  requestedBy: string;
  requestedAtUtc: string;
  workerInstanceId: string | null;
}

export interface RealtimeControlStatus {
  state: RealtimeControlState;
  updatedAtUtc: string;
  updatedBy: string;
  reason: string | null;
  rowVersion: string;
  workerStatus: string;
  workerRunning: boolean;
  workerInstanceId: string | null;
  lastHeartbeatUtc: string | null;
  lastSuccessfulCycleUtc: string | null;
  cycleOutcome: string | null;
  blockerReason: string | null;
  profiles: RealtimeProfileBacklog[];
  activeRunOnce: RealtimeRunRequest | null;
}

export interface RealtimeIntegrityProfilePreview {
  sourceProfileCode: string;
  status: 'MATCHED' | 'DRIFT_DETECTED';
  sourceRows: number;
  targetRows: number;
  plannedInsertRows: number;
  plannedUpdateRows: number;
  targetOnlyRows: number;
  duplicateGroups: number;
  manualReviewRows: number;
}

export interface RealtimeIntegrityPreview {
  isReadOnly: true;
  observedAtUtc: string;
  status: 'MATCHED' | 'DRIFT_DETECTED';
  profiles: RealtimeIntegrityProfilePreview[];
}

export function isRuntimeReady(status: RuntimeStatus): boolean {
  if (!isTimeMutationAllowed(status.time)) {
    return false;
  }

  if (typeof status.isReady === 'boolean') {
    return status.isReady;
  }

  return status.databaseConnected
    && status.databaseName?.toLocaleUpperCase('en-US') === 'QLHV_APP'
    && status.authenticationReady
    && status.requiredSchemaReady
    && status.backupProfilesReady
    && status.fileStorageReady;
}

export function isTimeMutationAllowed(time: TimeHealth): boolean {
  return time.databaseClockAvailable
    && time.databaseUtcNow !== null
    && time.writesAllowed;
}
