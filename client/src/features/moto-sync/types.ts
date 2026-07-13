export type MotoSyncDirection = 'V1_TO_V2' | 'V2_TO_V1';

export type MotoSyncMode = 'INSERT_ONLY' | 'INSERT_AND_UPDATE';

export interface MotoSyncPlanRequest {
  direction: MotoSyncDirection;
  sourceProfileCode: string;
  targetProfileCode: string;
  maKhoaHoc: string;
  allowDirtyData?: boolean;
}

export interface MotoSyncKhoaHocOptionsQuery {
  direction: MotoSyncDirection;
  sourceProfileCode: string;
  targetProfileCode: string;
  search?: string;
  take?: number;
}

export interface MotoSyncKhoaHocOption {
  maKhoaHoc: string;
  tenKhoaHoc: string | null;
  hangDaoTao: string | null;
  hangGPLX: string | null;
  ngayKhaiGiang: string | null;
  sourceHocVienCount: number;
  targetHocVienCount: number;
  sourceKhoaHocExists: boolean;
  targetKhoaHocExists: boolean;
  hasTargetKhoaHoc: boolean;
  sourceOnlyHocVienCount: number;
  targetOnlyHocVienCount: number;
}

export interface MotoTargetDonViGTVTOptionsQuery {
  targetProfileCode: string;
  search?: string;
  take?: number;
}

export interface MotoTargetDonViGTVTOption {
  maDV: string;
  tenDV: string | null;
  maSoGTVT: string | null;
  displayText: string;
}

export interface MotoTargetDonViGTVTOptionsResult {
  isReadOnly: boolean;
  targetProfileCode: string;
  warnings: string[];
  items: MotoTargetDonViGTVTOption[];
}

export interface MotoCenterTransferPlanRequest {
  sourceProfileCode: string;
  targetProfileCode: string;
  maKhoaHocCu: string;
  maCSDTCu: string;
  maCSDTMoi: string;
  maSoGTVTMoi: string;
}

export interface MotoCenterTransferExecuteRequest extends MotoCenterTransferPlanRequest {
  confirmText: string;
}

export interface MotoCenterTransferPlan {
  isReadOnly: boolean;
  sourceProfileCode: string;
  targetProfileCode: string;
  maKhoaHocCu: string;
  maKhoaHocMoi: string;
  maCSDTCu: string;
  maCSDTMoi: string;
  maSoGTVTMoi: string;
  targetMaCSDTMoiExists: boolean;
  targetMaCSDTMoiTenDV: string | null;
  targetMaSoGTVTMoiExists: boolean;
  targetMaSoGTVTMoiTenDV: string | null;
  sourceKhoaHocCount: number;
  sourceBaoCaoICount: number;
  sourceNguoiLXCount: number;
  sourceNguoiLXHoSoCount: number;
  sourceNguoiLXHSGiayToCount: number;
  targetKhoaHocCuCount: number;
  targetKhoaHocMoiCount: number;
  targetBaoCaoICuCount: number;
  targetBaoCaoIMoiCount: number;
  targetNguoiLXHoSoCuCount: number;
  targetNguoiLXHoSoMoiCount: number;
  targetNguoiLXHSGiayToCuCount: number;
  targetNguoiLXHSGiayToMoiCount: number;
  plannedCopyNguoiLXHSGiayTo: number;
  executable: boolean;
  blockers: string[];
  warnings: string[];
}

export interface MotoCenterTransferSummary {
  sourceProfileCode: string;
  targetProfileCode: string;
  maKhoaHocCu: string;
  maKhoaHocMoi: string;
  copiedKhoaHoc: number;
  copiedBaoCaoI: number;
  copiedNguoiLX: number;
  copiedNguoiLXHoSo: number;
  copiedNguoiLXHSGiayTo: number;
  updatedNguoiLXHoSo: number;
  updatedNguoiLX: number;
  updatedKhoaHoc: number;
  updatedBaoCaoI: number;
  updatedGiayTo: number;
  updatedNguoiLXHSGiayTo: number;
  targetKhoaHocMoiCountAfter: number;
  targetBaoCaoIMoiCountAfter: number;
  targetNguoiLXHoSoMoiCountAfter: number;
  targetNguoiLXHSGiayToMoiCountAfter: number;
  targetNguoiLXMoiCountAfter: number;
  startedAt: string;
  endedAt: string;
  durationMs: number;
}

export interface MotoCenterTransferExecuteResult {
  executed: boolean;
  status: string;
  message: string;
  plan: MotoCenterTransferPlan | null;
  summary: MotoCenterTransferSummary | null;
}

export interface MotoCenterTransferRunHistoryListItem {
  id: number;
  sourceProfileCode: string;
  targetProfileCode: string;
  maKhoaHocCu: string;
  maKhoaHocMoi: string | null;
  maCSDTCu: string;
  maCSDTMoi: string;
  maSoGTVTMoi: string | null;
  confirmTextMatched: boolean;
  executed: boolean;
  status: string;
  message: string;
  copiedTotal: number;
  updatedTotal: number;
  durationMs: number;
  startedAt: string;
  endedAt: string | null;
}

export interface MotoCenterTransferRunHistoryDetail extends MotoCenterTransferRunHistoryListItem {
  copiedKhoaHoc: number;
  copiedBaoCaoI: number;
  copiedNguoiLX: number;
  copiedNguoiLXHoSo: number;
  copiedNguoiLXHSGiayTo: number;
  updatedNguoiLXHoSo: number;
  updatedNguoiLX: number;
  updatedKhoaHoc: number;
  updatedBaoCaoI: number;
  updatedNguoiLXHSGiayTo: number;
  targetKhoaHocMoiCountAfter: number | null;
  targetBaoCaoIMoiCountAfter: number | null;
  targetNguoiLXHoSoMoiCountAfter: number | null;
  targetNguoiLXHSGiayToMoiCountAfter: number | null;
  targetNguoiLXMoiCountAfter: number | null;
  planJson: string | null;
  summaryJson: string | null;
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
  plannedInsertNguoiLXGPLX: number;
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
  insertedNguoiLXGPLX: number;
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

export interface MotoSyncRunHistoryListItem {
  id: number;
  createdAt: string;
  direction: MotoSyncDirection;
  syncMode: MotoSyncMode;
  sourceProfileCode: string;
  targetProfileCode: string;
  maKhoaHoc: string | null;
  executed: boolean;
  status: string;
  message: string;
  insertedTotal: number;
  updatedRows: number;
  deletedRows: number;
  durationMs: number;
  hasRemainingWork: boolean;
}

export interface MotoSyncRunHistoryDetail extends MotoSyncRunHistoryListItem {
  confirmTextMatched: boolean;
  insertedKhoaHoc: number;
  insertedBaoCaoI: number;
  insertedNguoiLX: number;
  insertedNguoiLXGPLX: number;
  insertedNguoiLXHoSo: number;
  insertedGiayTo: number;
  updatedNguoiLX: number;
  updatedNguoiLXHoSo: number;
  startedAt: string;
  endedAt: string;
  beforePlanJson: string | null;
  afterPlanJson: string | null;
}
