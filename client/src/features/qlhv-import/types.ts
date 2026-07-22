export type QlhvImportSourceKind = 'OTO' | 'MOTO';

export type QlhvImportSourceProfileCode = 'CSDT_OTO' | 'CSDT_MOTO';

export interface QlhvImportFormState {
  sourceKind: QlhvImportSourceKind;
  maKhoaHocInput: string;
}

export interface QlhvImportRequest {
  sourceProfileCode: QlhvImportSourceProfileCode;
  maCSDT: string;
  maKhoaHoc: string | null;
}

export interface QlhvImportExecuteRequest extends QlhvImportRequest {
  confirmText: string;
}

export interface QlhvImportDiagnostics {
  isReadOnly: boolean;
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
  blockers: string[];
  warnings: string[];
}

export interface QlhvImportPlan {
  isReadOnly: boolean;
  sourceProfileCode: string;
  maCSDT: string;
  maKhoaHoc: string | null;
  sourceHocVienRows: number;
  sourceKhoaHocRows: number;
  currentAppHocVienRows: number;
  currentAppKhoaHocRows: number;
  plannedInsertHocVienRows: number;
  plannedUpdateHocVienRows: number;
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
  skippedHocVienRows: number;
}

export interface QlhvImportSnapshot<T> {
  request: QlhvImportRequest;
  data: T;
}

export type QlhvImportExecuteOutcome =
  | { kind: 'executed'; httpStatus: number; result: QlhvImportExecuteResult }
  | { kind: 'blocked'; httpStatus: number; result: QlhvImportExecuteResult };
