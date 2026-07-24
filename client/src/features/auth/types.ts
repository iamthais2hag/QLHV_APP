export type AppUserRole = 'Admin' | 'Employee' | 'Viewer';

export interface AuthenticatedUser {
  id: number;
  username: string;
  displayName: string;
  role: AppUserRole;
  mustChangePassword: boolean;
}

export interface LoginRequest {
  username: string;
  password: string;
}

export interface ChangePasswordRequest {
  currentPassword: string;
  newPassword: string;
}
