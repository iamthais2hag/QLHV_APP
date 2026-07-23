export type QlhvImportSourceKind = 'OTO' | 'MOTO';

export type QlhvImportSourceProfileCode = 'CSDT_OTO' | 'CSDT_MOTO';

export type QlhvOperationState = 'idle' | 'refreshing' | 'syncing' | 'succeeded' | 'failed';

export type QlhvOperationType =
  | 'REFRESH_BACKUP'
  | 'FULL_SYNC'
  | 'AUTO_SYNC'
  | 'PHOTO_PROCESSING';

export interface QlhvImportRequest {
  sourceProfileCode: QlhvImportSourceProfileCode;
  maCSDT: string;
  maKhoaHoc: null;
}

export interface QlhvImportExecuteRequest extends QlhvImportRequest {
  expectedSnapshotToken: string;
}

export interface QlhvImportEntityCounts {
  sourceRows: number;
  insert: number;
  update: number;
  reactivate: number;
  softDelete: number;
  skip: number;
  duplicateSourceKeys: number;
}

export interface QlhvImportPhotoCounts {
  found: number;
  missing: number;
  pending: number;
  toReprocess: number;
  reviewRequired: number;
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
  hocVien: QlhvImportEntityCounts;
  khoaHoc: QlhvImportEntityCounts;
  giaoVien: QlhvImportEntityCounts;
  photo?: QlhvImportPhotoCounts;
  duplicateSourceKeys: number;
  relationConflicts: number;
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
  hocVien: QlhvImportEntityCounts;
  khoaHoc: QlhvImportEntityCounts;
  giaoVien: QlhvImportEntityCounts;
  photo?: QlhvImportPhotoCounts;
  duplicateSourceKeys: number;
  relationConflicts: number;
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
  hocVien?: QlhvImportEntityCounts;
  khoaHoc?: QlhvImportEntityCounts;
  giaoVien?: QlhvImportEntityCounts;
  photo?: QlhvImportPhotoCounts;
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
  dryRun: boolean;
  targetWritesEnabled: boolean;
  currentUserRole: string;
  writeAuthorized: boolean;
  refreshBlockers: string[];
  syncBlockers: string[];
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
  actor?: string | null;
}

export type QlhvAutoSyncState =
  | 'disabled'
  | 'not-found'
  | 'idle'
  | 'queued'
  | 'running'
  | 'succeeded'
  | 'partial-failed'
  | 'failed';

export interface QlhvAutoSyncSourceResult {
  sourceType: QlhvImportSourceKind;
  status: string;
  refreshOperationId: string | null;
  syncOperationId: string | null;
  message: string | null;
  startedAtUtc: string | null;
  completedAtUtc: string | null;
}

export interface QlhvAutoSyncStatus {
  found: boolean;
  enabled: boolean;
  runOnServerStartup: boolean;
  refreshBackupBeforeSync: boolean;
  state: QlhvAutoSyncState;
  runId: string | null;
  activeRunId: string | null;
  triggerType: string | null;
  actor: string | null;
  currentSourceType: QlhvImportSourceKind | null;
  currentStage: string | null;
  startedAtUtc: string | null;
  completedAtUtc: string | null;
  lastSuccessfulSyncUtc: string | null;
  oto: QlhvAutoSyncSourceResult | null;
  moto: QlhvAutoSyncSourceResult | null;
  lastError: string | null;
}

export interface QlhvAutoSyncRunResult {
  accepted: boolean;
  joinedExisting: boolean;
  isConflict: boolean;
  isUnavailable: boolean;
  runId: string | null;
  status: string;
  message: string;
}

export type QlhvPhotoProcessingStatus =
  | 'PENDING'
  | 'PROCESSING'
  | 'SUCCEEDED'
  | 'REVIEW_REQUIRED'
  | 'FAILED'
  | 'APPROVED';

export interface QlhvPhotoProcessingCounts {
  total: number;
  pending: number;
  processing: number;
  succeeded: number;
  reviewRequired: number;
  failed: number;
  approved: number;
}

export interface QlhvPhotoProcessingItem {
  id: number;
  sourceProfileCode: QlhvImportSourceProfileCode;
  sourceMaDK: string;
  studentName: string | null;
  maKhoaHoc: string | null;
  sourceImagePath: string | null;
  outputImagePath: string | null;
  sourcePathStatus: string;
  sourcePathKind: string;
  sourcePreviewUrl: string | null;
  outputPreviewUrl: string | null;
  processingStatus: QlhvPhotoProcessingStatus;
  processingConfidence: number | null;
  processedAtUtc: string | null;
  errorMessage: string | null;
  reviewRequired: boolean;
  approvedAtUtc: string | null;
  approvedBy: string | null;
}

export interface QlhvPhotoProcessingPage {
  items: QlhvPhotoProcessingItem[];
  page: number;
  pageSize: number;
  totalItems: number;
  totalPages: number;
  engineReady: boolean;
  readinessMessage: string | null;
  counts: QlhvPhotoProcessingCounts;
}

export interface QlhvPhotoProcessingQuery {
  sourceProfileCode?: QlhvImportSourceProfileCode;
  status?: QlhvPhotoProcessingStatus;
  reviewRequired?: boolean;
  page: number;
  pageSize: number;
}
