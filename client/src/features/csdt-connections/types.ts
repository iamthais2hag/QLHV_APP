export interface CsdtConnectionProfileListItem {
  profileCode: string;
  displayName: string;
  profileGroup: string;
  authMode: string;
  isConfigured: boolean;
  isPasswordConfigured: boolean;
  isActive: boolean;
  lastTestedAt: string | null;
  lastTestStatus: string | null;
  lastTestMessage: string | null;
}

export interface CsdtConnectionProfileDetail extends CsdtConnectionProfileListItem {
  serverName: string | null;
  databaseName: string | null;
  userName: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface SaveCsdtConnectionProfileRequest {
  displayName?: string | null;
  serverName?: string | null;
  databaseName?: string | null;
  authMode: string;
  userName?: string | null;
  isActive: boolean;
  passwordPlainText?: string | null;
}

export interface TestCsdtConnectionProfileRequest {
  serverName?: string | null;
  databaseName?: string | null;
  authMode?: string | null;
  userName?: string | null;
  passwordPlainText?: string | null;
}

export interface TestCsdtConnectionProfileResult {
  profileCode: string;
  isReadOnly: boolean;
  canTest: boolean;
  succeeded: boolean;
  status: string;
  message: string;
  testedAt: string;
}
