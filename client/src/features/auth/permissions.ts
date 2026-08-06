import type { AppUserRole } from './types';

export type AppPermission =
  | 'CanViewBusinessData'
  | 'CanEditBusinessData'
  | 'CanSynchronizeCSDT'
  | 'CanImportData'
  | 'CanManageUsers'
  | 'CanViewAssignmentCatalogs'
  | 'CanManageDossierReceivers'
  | 'CanManageCourseGroups'
  | 'CanAssignStudents'
  | 'CanBulkAssignStudents'
  | 'CanPreviewAssignmentImport'
  | 'CanConfirmAssignmentImport'
  | 'CanExportAssignments'
  | 'CanViewAssignmentHistory'
  | 'CanViewCourseCompletionStatus'
  | 'CanPreviewCourseCompletion'
  | 'CanCompleteCourse';

const ROLE_LABELS: Record<AppUserRole, string> = {
  Admin: 'Quản trị viên',
  Employee: 'Nhân viên',
  Viewer: 'Chỉ xem',
};

export function getRoleDisplayName(role: AppUserRole | null | undefined): string {
  return role ? ROLE_LABELS[role] : '';
}

export function hasPermission(role: AppUserRole, permission: AppPermission): boolean {
  if (role === 'Admin') {
    return true;
  }

  if (role === 'Employee') {
    return [
      'CanViewBusinessData',
      'CanEditBusinessData',
      'CanViewAssignmentCatalogs',
      'CanManageDossierReceivers',
      'CanManageCourseGroups',
      'CanAssignStudents',
      'CanBulkAssignStudents',
      'CanPreviewAssignmentImport',
      'CanExportAssignments',
      'CanViewAssignmentHistory',
      'CanViewCourseCompletionStatus',
    ].includes(permission);
  }

  return permission === 'CanViewBusinessData'
    || permission === 'CanViewAssignmentCatalogs'
    || permission === 'CanViewAssignmentHistory'
    || permission === 'CanViewCourseCompletionStatus';
}

export function canOperateBusinessData(role: AppUserRole): boolean {
  return hasPermission(role, 'CanEditBusinessData');
}

export function canManageUsers(role: AppUserRole): boolean {
  return hasPermission(role, 'CanManageUsers');
}

export function canSynchronizeCsdt(role: AppUserRole): boolean {
  return hasPermission(role, 'CanSynchronizeCSDT');
}
