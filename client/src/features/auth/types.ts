export type AppUserRole = 'Admin' | 'Viewer';

export interface AuthenticatedUser {
  id: number;
  username: string;
  displayName: string;
  role: AppUserRole;
}

export interface LoginRequest {
  username: string;
  password: string;
}
