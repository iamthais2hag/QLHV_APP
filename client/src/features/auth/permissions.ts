import type { AppUserRole } from './types';

export type AppPermission =
  | 'CanViewBusinessData'
  | 'CanEditBusinessData'
  | 'CanSynchronizeCSDT'
  | 'CanImportData'
  | 'CanManageUsers';

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
    return permission === 'CanViewBusinessData' || permission === 'CanEditBusinessData';
  }

  return permission === 'CanViewBusinessData';
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
