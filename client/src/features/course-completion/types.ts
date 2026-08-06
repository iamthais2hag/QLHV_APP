export type CourseCompletionStatusCode =
  | 'NOT_COMPLETED'
  | 'COMPLETED'
  | 'CORRECTION_REQUIRED';

export interface CourseCompletionDriftDiagnostic {
  addedLearners: number;
  missingLearners: number;
  changedLearners: number;
  statusOrResultChanges: number;
}

export interface CourseCompletionStatus {
  status: CourseCompletionStatusCode;
  khoaHocId: number;
  sourceProfileCode: string | null;
  sourceCourseKey: string | null;
  completionBusinessDate: string | null;
  completedAtUtc: string | null;
  completedBy: string | null;
  learnerCount: number | null;
  contractVersion: string | null;
  sourceSnapshotHash: string | null;
  drift: CourseCompletionDriftDiagnostic | null;
  warnings: string[];
}

export interface CourseCompletionPreview {
  previewToken: string;
  expiresAtUtc: string;
  status: string;
  canConfirm: boolean;
  contractVersion: string;
  sourceProfileCode: string;
  sourceCourseKey: string;
  sourceSnapshotHash: string;
  learnerCount: number;
  passedCount: number;
  failedCount: number;
  downstreamCount: number;
  blockers: string[];
  warnings: string[];
}

export interface CourseCompletionConfirmResult {
  operationId: string;
  courseCompletionId: number;
  resultCode: 'COMPLETED' | 'NO_CHANGE';
  completionBusinessDate: string;
  completedAtUtc: string;
  completedBy: string;
  learnerCount: number;
  contractVersion: string;
  sourceSnapshotHash: string;
}
