export interface RuntimeStatus {
  isReady?: boolean;
  version: string;
  environment: string;
  configurationReady?: boolean;
  databaseConnected: boolean;
  databaseName: string | null;
  authenticationReady: boolean;
  requiredSchemaReady: boolean;
  backupProfilesReady: boolean;
  backupStorageReady?: boolean;
  fileStorageReady: boolean;
  runtimeStorageReady?: boolean;
  messages: string[];
}

export function isRuntimeReady(status: RuntimeStatus): boolean {
  if (typeof status.isReady === 'boolean') {
    return status.isReady;
  }

  return status.databaseConnected
    && status.databaseName?.toLocaleUpperCase('en-US') === 'QLHV_APP'
    && status.authenticationReady
    && status.requiredSchemaReady
    && status.backupProfilesReady
    && status.fileStorageReady;
}
