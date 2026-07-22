export type QlhvImportSourceKind = 'OTO' | 'MOTO';

export type QlhvImportSourceProfileCode = 'CSDT_OTO' | 'CSDT_MOTO';

export type QlhvOperationState = 'idle' | 'refreshing' | 'syncing' | 'succeeded' | 'failed';

export type QlhvOperationType = 'REFRESH_BACKUP' | 'FULL_SYNC';

export interface QlhvImportRequest {
  sourceProfileCode: QlhvImportSourceProfileCode;
  maCSDT: string;
  maKhoaHoc: null;
}

export interface QlhvImportExecuteRequest extends QlhvImportRequest {
  expectedSnapshotToken: string;
}

export interface QlhvImportDiagnostics {
  isReadOnly: boolean;
  sourceDatabaseName: string;
  sourceProfileCode: string;
  maCSDT: string;
  maKhoaHoc: string | null;
  sourceHocVienRows: number;
  sourceDistinctMaDkRows: number;
  duplicateSourceMaDkRows: number;
  currentAppHocVienRows: number;
  targetRowsForSourceProfile: number;
  targetExactIdentityMatches: number;
  targetMaDkConflictsOtherProfiles: number;
  softDeletedIdentityConflicts: number;
  sourceProfileConstraintExists: boolean;
  sourceProfileAllowedByConstraint: boolean;
  plannedInsertHocVienRows: number;
  plannedUpdateHocVienRows: number;
  plannedReactivateHocVienRows: number;
  plannedSoftDeleteHocVienRows: number;
  plannedSkipHocVienRows: number;
  plannedUpsertHocVienRows: number;
  executable: boolean;
  blockers: string[];
  warnings: string[];
}

export interface QlhvImportPlan {
  isReadOnly: boolean;
  sourceDatabaseName: string;
  sourceProfileCode: string;
  maCSDT: string;
  maKhoaHoc: string | null;
  backupSnapshotToken: string;
  generatedAtUtc: string;
  sourceHocVienRows: number;
  sourceDistinctMaDkRows: number;
  duplicateSourceMaDkRows: number;
  sourceKhoaHocRows: number;
  currentAppHocVienRows: number;
  currentAppKhoaHocRows: number;
  targetRowsForSourceProfile: number;
  targetExactIdentityMatches: number;
  targetMaDkConflictsOtherProfiles: number;
  softDeletedIdentityConflicts: number;
  sourceProfileConstraintExists: boolean;
  sourceProfileAllowedByConstraint: boolean;
  plannedInsertHocVienRows: number;
  plannedUpdateHocVienRows: number;
  plannedReactivateHocVienRows: number;
  plannedSoftDeleteHocVienRows: number;
  plannedSkipHocVienRows: number;
  plannedUpsertHocVienRows: number;
  plannedUpsertKhoaHocRows: number;
  executable: boolean;
  blockers: string[];
  warnings: string[];
}

export interface QlhvImportExecuteResult {
  executed: boolean;
  status: string;
  message: string;
  plan: QlhvImportPlan;
  insertedHocVienRows: number;
  updatedHocVienRows: number;
  reactivatedHocVienRows: number;
  softDeletedHocVienRows: number;
  skippedHocVienRows: number;
}

export interface QlhvImportSnapshot<T> {
  request: QlhvImportRequest;
  data: T;
}

export type QlhvImportExecuteOutcome =
  | { kind: 'executed'; httpStatus: number; result: QlhvImportExecuteResult }
  | { kind: 'blocked'; httpStatus: number; result: QlhvImportExecuteResult };

export interface QlhvOperationsRowCounts {
  nguoiLX: number;
  nguoiLXHoSo: number;
  khoaHoc: number;
}

export interface QlhvOperationsStatus {
  sourceType: QlhvImportSourceKind;
  liveDatabaseName: string;
  backupDatabaseName: string;
  maCSDT: string;
  sourceProfileCode: QlhvImportSourceProfileCode;
  state: QlhvOperationState;
  activeOperationId: string | null;
  backupLastRefreshTimeUtc: string | null;
  backupSnapshotToken: string | null;
  liveRows: QlhvOperationsRowCounts;
  backupRows: QlhvOperationsRowCounts;
  targetActiveRows: number;
  lastSyncTimeUtc: string | null;
  lastError: string | null;
  canRefresh: boolean;
  canSync: boolean;
}

export interface QlhvRefreshBackupRequest {
  sourceType: QlhvImportSourceKind;
}

export interface QlhvRefreshBackupResult {
  operationId: string;
  sourceType: QlhvImportSourceKind;
  status: string;
  message: string;
}

export interface QlhvOperationHistoryItem {
  operationId: string;
  sourceType: QlhvImportSourceKind;
  operationType: QlhvOperationType;
  status: string;
  startedAtUtc: string | null;
  completedAtUtc: string | null;
  sourceRows: number;
  insertedRows: number;
  updatedRows: number;
  reactivatedRows: number;
  softDeletedRows: number;
  skippedRows: number;
  snapshotToken: string | null;
  errorMessage: string | null;
  detailJson: string | null;
}
