export interface PagedResult<T> {
  items: T[];
  totalItems: number;
  page: number;
  pageSize: number;
  totalPages: number;
}

export type SourceProfileCode = 'CSDT_OTO' | 'CSDT_MOTO';

export interface GiaoVienSourceItem {
  giaoVienId: number;
  sourceProfileCode: string;
  maGv: string;
  hoTen: string;
  ngaySinh: string | null;
  soCccd: string | null;
  hangDaoTao: string | null;
  trangThai: string;
  isActive: boolean;
  courseUsageCount: number;
  studentUsageCount: number;
  isManualReview: boolean;
}

export interface GiaoVienHoSoItem {
  giaoVienHsId: number;
  maGiaoVienHs: string;
  hoTen: string;
  ngaySinh: string | null;
  soCccd: string | null;
  trangThai: string;
  ghiChu: string | null;
  isDeleted: boolean;
  referenceCount: number;
  rowVersion: string;
  updatedAtUtc: string | null;
  updatedBy: string | null;
}

export interface GiaoVienHoSoCommand {
  maGiaoVienHs: string;
  hoTen: string;
  ngaySinh: string | null;
  soCccd: string | null;
  trangThai: string;
  ghiChu: string | null;
  reason: string;
  rowVersion?: string;
}

export interface CatalogHistoryItem {
  occurredAtUtc: string;
  actor: string | null;
  action: string;
  reason: string | null;
}

export interface GiaoVienHoSoHistory {
  referenceCount: number;
  items: CatalogHistoryItem[];
}

export interface XeTapSourceItem {
  xeTapId: number;
  sourceProfileCode: string;
  maXe: string;
  bienSoXe: string;
  soKhung: string | null;
  soMay: string | null;
  hangXe: string | null;
  loaiXe: string | null;
  hangDaoTao: string | null;
  trangThai: string;
  isActive: boolean;
  courseUsageCount: number;
  groupUsageCount: number;
  studentUsageCount: number;
  isManualReview: boolean;
}

export interface KhoaHocListItem {
  khoaHocId: number;
  sourceProfileCode: string;
  maKhoa: string;
  tenKhoa: string | null;
  hangDaoTao: string | null;
  loaiDaoTao: string | null;
  ngayKhaiGiang: string | null;
  ngayBeGiang: string | null;
  soQuyetDinh: string | null;
  trangThai: string;
  isActive: boolean;
  learnerCount: number;
  unassignedCount: number;
  manualReviewCount: number;
}

export interface KhoaHocDetail extends KhoaHocListItem {
  rowVersion: string | null;
}

export interface AssignmentReference {
  id: number;
  code: string;
  label: string;
  isActive: boolean;
  isManualReview?: boolean;
  sourceProfileCode?: string | null;
}

export interface TrainingGroup {
  groupId: number;
  maNhom: string;
  tenNhom: string;
  thuTu: number;
  trangThai: string;
  isActive: boolean;
  defaultClassTeacher: AssignmentReference | null;
  defaultTrainingVehicle: AssignmentReference | null;
  defaultFigure10Vehicle: AssignmentReference | null;
  studentCount: number;
  rowVersion: string;
}

export type AssignmentStatus = 'ASSIGNED' | 'UNASSIGNED' | 'MANUAL_REVIEW';

export interface StudentAssignmentItem {
  hocVienId: number;
  maDangKy: string;
  hoTen: string;
  ngaySinh: string | null;
  maKhoa: string;
  sourceProfileCode: string;
  hangHoc: string | null;
  groupId: number | null;
  groupCode: string | null;
  dossierReceiver: AssignmentReference | null;
  classTeacher: AssignmentReference | null;
  trainingVehicle: AssignmentReference | null;
  figure10Vehicle: AssignmentReference | null;
  overrideClassTeacher: boolean;
  overrideTrainingVehicle: boolean;
  overrideFigure10Vehicle: boolean;
  assignmentRowVersion: string | null;
  assignmentStatus: AssignmentStatus;
  warnings: string[];
}

export interface AssignmentLookups {
  dossierReceivers: AssignmentReference[];
  teachers: AssignmentReference[];
  vehicles: AssignmentReference[];
}

export interface AssignmentSummary {
  learnerCount: number;
  assignedCount: number;
  unassignedCount: number;
  manualReviewCount: number;
}

export interface CourseAssignmentDetail {
  course: KhoaHocDetail;
  students: PagedResult<StudentAssignmentItem>;
  groups: TrainingGroup[];
  lookups: AssignmentLookups;
  summary: AssignmentSummary;
}

export interface TrainingGroupCommand {
  maNhom: string;
  tenNhom: string;
  thuTu: number;
  defaultClassTeacherId: number | null;
  defaultTrainingVehicleId: number | null;
  defaultFigure10VehicleId: number | null;
  reason: string;
  rowVersion?: string;
}

export type PropagationMode =
  | 'UNOVERRIDDEN_ONLY'
  | 'REPLACE_ALL'
  | 'NO_CURRENT_CHANGE';

export type AssignmentAction = 'KEEP' | 'SET' | 'CLEAR' | 'INHERIT';

export interface AssignmentFieldAction {
  action: AssignmentAction;
  id?: number | null;
}

export interface AssignmentFieldCommands {
  dossierReceiver?: AssignmentFieldAction;
  classTeacher?: AssignmentFieldAction;
  trainingVehicle?: AssignmentFieldAction;
  figure10Vehicle?: AssignmentFieldAction;
}

export interface AssignmentFilter {
  keyword?: string;
  groupId?: number | null;
  unassignedOnly?: boolean;
}

export interface AssignmentSelection {
  mode: 'IDS' | 'FILTER';
  hocVienIds: number[];
  filter?: AssignmentFilter;
}

export type AssignmentOperation =
  | 'PUT_IN_GROUP'
  | 'BULK_ASSIGN'
  | 'STUDENT_OVERRIDE'
  | 'CLEAR_ASSIGNMENT';

export interface AssignmentPreviewRequest {
  khoaHocId: number;
  sourceProfileCode: string;
  selection: AssignmentSelection;
  operation: AssignmentOperation;
  groupId?: number | null;
  fields?: AssignmentFieldCommands;
  expectedRowVersions: Record<string, string | null>;
  reason: string;
}

export interface AssignmentDisplayState {
  groupId: number | null;
  dossierReceiverId: number | null;
  classTeacherId: number | null;
  trainingVehicleId: number | null;
  figure10VehicleId: number | null;
  overrideClassTeacher: boolean;
  overrideTrainingVehicle: boolean;
  overrideFigure10Vehicle: boolean;
}

export type PreviewRowStatus =
  | 'READY'
  | 'NO_CHANGE'
  | 'NOT_FOUND'
  | 'AMBIGUOUS'
  | 'INACTIVE_REFERENCE'
  | 'INVALID'
  | 'CONFLICT';

export interface AssignmentPreviewRow {
  hocVienId: number;
  maDangKy: string;
  hoTen: string;
  status: PreviewRowStatus;
  before: AssignmentDisplayState | null;
  after: AssignmentDisplayState | null;
  messages: string[];
}

export interface AssignmentPreview {
  previewToken: string;
  expiresAtUtc: string;
  targetFingerprint: string;
  totalTargets: number;
  readyCount: number;
  noChangeCount: number;
  conflictCount: number;
  invalidCount: number;
  warnings: string[];
  rows: AssignmentPreviewRow[];
}

export interface AssignmentConfirmRequest {
  previewToken: string;
  idempotencyKey: string;
  reason: string;
}

export interface AssignmentConfirmResult {
  operationId: string;
  changedCount: number;
  noChangeCount: number;
  completedAtUtc: string;
}

export interface GroupDefaultsPreviewRequest {
  rowVersion: string;
  mode: PropagationMode;
  defaultClassTeacherId: number | null;
  defaultTrainingVehicleId: number | null;
  defaultFigure10VehicleId: number | null;
  reason: string;
}

export interface AssignmentHistoryItem {
  assignmentId: number;
  effectiveFromUtc: string;
  effectiveToUtc: string | null;
  isCurrent: boolean;
  source: string;
  actor: string | null;
  reason: string | null;
  group: AssignmentReference | null;
  dossierReceiver: AssignmentReference | null;
  classTeacher: AssignmentReference | null;
  trainingVehicle: AssignmentReference | null;
  figure10Vehicle: AssignmentReference | null;
  overrideClassTeacher: boolean;
  overrideTrainingVehicle: boolean;
  overrideFigure10Vehicle: boolean;
}

export interface CourseAuditItem {
  occurredAtUtc: string;
  actor: string | null;
  action: string;
  reason: string | null;
  entityLabel: string | null;
}

export interface ImportPreviewStatusCounts {
  ready: number;
  noChange: number;
  notFound: number;
  ambiguous: number;
  inactiveReference: number;
  invalid: number;
  conflict: number;
}

export interface ImportPreviewRow {
  rowNumber: number;
  maDangKy: string | null;
  status: PreviewRowStatus;
  messages: string[];
}

export interface AssignmentImportPreview {
  previewToken: string;
  expiresAtUtc: string;
  fileName: string;
  totalRows: number;
  counts: ImportPreviewStatusCounts;
  rows: ImportPreviewRow[];
}

export interface AssignmentImportConfirmResult extends AssignmentConfirmResult {
  sessionId: number;
}

export interface DownloadResult {
  blob: Blob;
  fileName: string;
}

export interface ListQuery {
  keyword?: string;
  sourceProfileCode?: string;
  trangThai?: string;
  page: number;
  pageSize: number;
}

export interface CourseListQuery {
  maKhoa?: string;
  tenKhoa?: string;
  hangDaoTao?: string;
  loaiDaoTao?: string;
  trangThai?: string;
  sourceProfileCode?: string;
  tuNgay?: string;
  denNgay?: string;
  page: number;
  pageSize: number;
}

export interface CourseDetailQuery extends AssignmentFilter {
  page: number;
  pageSize: number;
}
