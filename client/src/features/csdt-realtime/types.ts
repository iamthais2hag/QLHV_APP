export type CsdtRealtimeVehicleType = 'OTO' | 'MOTO';

export type CsdtRealtimeStreamCode =
  | 'OTO_V2_TO_V1'
  | 'MOTO_V2_TO_V1';

export interface CsdtRealtimeDomainStatus {
  domain: string;
  state: string;
  sourceRows: number;
  targetRows: number;
  insertedRows: number;
  updatedRows: number;
  skippedRows: number;
  errorRows: number;
  lastError: string | null;
}

export interface CsdtRealtimeStreamStatus {
  streamCode: CsdtRealtimeStreamCode;
  vehicleType: CsdtRealtimeVehicleType;
  sourceProfileCode: string;
  targetProfileCode: string;
  sourceDatabaseName: string;
  targetDatabaseName: string;
  maCSDT: string;
  enabled: boolean;
  state: string;
  baselineStatus: string;
  baselineVersion: number | null;
  lastSuccessfulVersion: number | null;
  currentSourceVersion: number | null;
  minimumValidVersion: number | null;
  lagVersions: number | null;
  activeRunId: string | null;
  retryCount: number;
  nextRetryAtUtc: string | null;
  lastStartedAtUtc: string | null;
  lastCompletedAtUtc: string | null;
  lastSuccessAtUtc: string | null;
  insertedRows: number;
  updatedRows: number;
  skippedRows: number;
  errorRows: number;
  deleteTombstoneCount: number;
  unresolvedConflictCount: number;
  lastError: string | null;
  currentUserRole: string;
  writeAuthorized: boolean;
  stateToken: string;
  actionBlockers: string[];
  domains: CsdtRealtimeDomainStatus[];
}

export interface CsdtRealtimeStreamsResponse {
  observedAtUtc: string;
  streams: CsdtRealtimeStreamStatus[];
}

export interface CsdtRealtimeRunDomain {
  domain: string;
  state: string;
  attemptCount: number;
  lastAttemptAtUtc: string | null;
  succeededAtUtc: string | null;
  insertedRows: number;
  updatedRows: number;
  skippedRows: number;
  errorRows: number;
  message: string | null;
}

export interface CsdtRealtimeHistoryItem {
  runId: string;
  streamCode: CsdtRealtimeStreamCode;
  runType: string;
  status: string;
  fromVersion: number | null;
  toVersion: number | null;
  startedAtUtc: string;
  completedAtUtc: string | null;
  insertedRows: number;
  updatedRows: number;
  skippedRows: number;
  errorRows: number;
  actor: string | null;
  errorMessage: string | null;
  canRetry: boolean;
  domains: CsdtRealtimeRunDomain[];
}

export interface CsdtRealtimeTombstone {
  id: number;
  streamCode: CsdtRealtimeStreamCode;
  domain: string;
  sourceKey: string;
  changeVersion: number;
  detectedAtUtc: string;
  status: string;
  message: string | null;
}

export interface CsdtRealtimeActionResult {
  accepted: boolean;
  joinedExisting: boolean;
  runId: string | null;
  status: string;
  message: string;
}

export interface CsdtRealtimeEnableRequest {
  enabled: boolean;
  expectedStateToken: string;
}

export interface CsdtRealtimeBaselineRequest {
  expectedStateToken: string;
}

export interface CsdtRealtimeRetryRequest {
  expectedStateToken: string;
}

export interface CsdtReverseDomainPlan {
  domain: string;
  sourceRows: number;
  safeInsertRows: number;
  safeUpdateRows: number;
  skippedRows: number;
  reviewRows: number;
}

export interface CsdtReversePlan {
  isReadOnly: boolean;
  vehicleType: CsdtRealtimeVehicleType;
  direction: 'V1_TO_V2';
  sourceDatabaseName: string;
  targetDatabaseName: string;
  maKhoaHoc: string | null;
  generatedAtUtc: string;
  expiresAtUtc: string | null;
  planToken: string;
  sourceRows: number;
  safeInsertRows: number;
  safeUpdateRows: number;
  skippedRows: number;
  v1OnlyRequiresReview: number;
  identityChanged: number;
  conflictRequiresReview: number;
  executable: boolean;
  blockers: string[];
  warnings: string[];
  domains: CsdtReverseDomainPlan[];
}

export interface CsdtReverseExecuteRequest {
  vehicleType: CsdtRealtimeVehicleType;
  maKhoaHoc: string | null;
  expectedPlanToken: string;
}

export interface CsdtReverseExecuteResult extends CsdtRealtimeActionResult {
  plan: CsdtReversePlan | null;
}
