import type { AppUserRole } from '../auth/types';

export interface ManagedUser {
  id: number;
  username: string;
  displayName: string;
  role: AppUserRole;
  isActive: boolean;
  mustChangePassword: boolean;
  lastLoginAtUtc: string | null;
  createdAtUtc: string;
  createdBy: string | null;
}

export interface CreateManagedUserRequest {
  username: string;
  displayName: string;
  role: AppUserRole;
  temporaryPassword: string;
  isActive: boolean;
  mustChangePassword: boolean;
}

export interface UpdateManagedUserRequest {
  displayName: string;
  role: AppUserRole;
  isActive: boolean;
  mustChangePassword: boolean;
}

export interface ResetManagedUserPasswordRequest {
  temporaryPassword: string;
  mustChangePassword: boolean;
}
