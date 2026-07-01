export type MotoSyncDirection = 'V1_TO_V2' | 'V2_TO_V1';

export type MotoSyncMode = 'INSERT_ONLY' | 'INSERT_AND_UPDATE';

export interface MotoSyncPlanRequest {
  direction: MotoSyncDirection;
  sourceProfileCode: string;
  targetProfileCode: string;
  maKhoaHoc: string;
  allowDirtyData?: boolean;
}

export interface MotoSyncExecuteRequest {
  direction: MotoSyncDirection;
  sourceProfileCode: string;
  targetProfileCode: string;
  maKhoaHoc: string;
  syncMode: MotoSyncMode;
  confirmText: string;
}

export interface MotoSyncError {
  recordKey?: string | null;
  code: string;
  message: string;
}

export interface MotoSyncUpdateSample {
  maDK: string;
  tableName: string;
  changedColumnNames: string[];
}

export interface MotoSyncPlan {
  isReadOnly: boolean;
  direction: MotoSyncDirection;
  sourceProfileCode: string;
  targetProfileCode: string;
  maKhoaHoc: string | null;
  allowDirtyData: boolean;
  sourceRows: number;
  targetRows: number;
  exactMaDkOverlap: number;
  sourceOnly: number;
  targetOnly: number;
  duplicateBusinessKeyGroups: number;
  shortFullMaDkPairs: number;
  missingKhoaHocDependencies: number;
  plannedInsertKhoaHoc: number;
  plannedInsertBaoCaoI: number;
  plannedInsertNguoiLX: number;
  plannedInsertNguoiLXHoSo: number;
  plannedInsertGiayTo: number;
  plannedUpdate: number;
  plannedUpdateNguoiLX: number;
  plannedUpdateNguoiLXHoSo: number;
  updateSamples: MotoSyncUpdateSample[];
  executable: boolean;
  blockers: string[];
  warnings: string[];
  errors: MotoSyncError[];
}

export interface MotoSyncExecuteSummary {
  direction: MotoSyncDirection;
  syncMode: MotoSyncMode;
  sourceProfileCode: string;
  targetProfileCode: string;
  maKhoaHoc: string | null;
  insertedKhoaHoc: number;
  insertedBaoCaoI: number;
  insertedNguoiLX: number;
  insertedNguoiLXHoSo: number;
  insertedGiayTo: number;
  updatedNguoiLX: number;
  updatedNguoiLXHoSo: number;
  updatedRows: number;
  deletedRows: number;
  startedAt: string;
  endedAt: string;
  durationMs: number;
}

export interface MotoSyncExecuteResult {
  executed: boolean;
  status: string;
  message: string;
  summary: MotoSyncExecuteSummary | null;
  plan: MotoSyncPlan | null;
  beforePlan: MotoSyncPlan | null;
  afterPlan: MotoSyncPlan | null;
  hasRemainingWork: boolean;
}
