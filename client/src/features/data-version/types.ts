export type DataVersionValue = string | number;

export type DataVersionResource =
  | 'hocVienVersion'
  | 'khoaHocVersion'
  | 'giaoVienVersion'
  | 'photoVersion';

export interface SystemDataVersion {
  hocVienVersion: DataVersionValue;
  khoaHocVersion: DataVersionValue;
  giaoVienVersion: DataVersionValue;
  photoVersion: DataVersionValue;
  lastSuccessfulSyncUtc: string | null;
}
