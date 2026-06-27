import { useEffect, useMemo, useState } from 'react';
import {
  getCsdtConnectionProfile,
  getCsdtConnectionProfiles,
  saveCsdtConnectionProfile,
  testCsdtConnectionProfile,
} from './api';
import type {
  CsdtConnectionProfileDetail,
  CsdtConnectionProfileListItem,
  SaveCsdtConnectionProfileRequest,
} from './types';

interface ProfileFormState {
  displayName: string;
  serverName: string;
  databaseName: string;
  authMode: string;
  userName: string;
  isActive: boolean;
}

const BOOTSTRAP_PROFILE = 'QLHV_APP';

export default function CsdtConnectionProfilesPage() {
  const [profiles, setProfiles] = useState<CsdtConnectionProfileListItem[]>([]);
  const [selectedCode, setSelectedCode] = useState<string | null>(null);
  const [detail, setDetail] = useState<CsdtConnectionProfileDetail | null>(null);
  const [form, setForm] = useState<ProfileFormState>(emptyForm());
  const [loadingList, setLoadingList] = useState(true);
  const [loadingDetail, setLoadingDetail] = useState(false);
  const [saving, setSaving] = useState(false);
  const [testing, setTesting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [actionMessage, setActionMessage] = useState<string | null>(null);

  const selectedProfile = useMemo(
    () => profiles.find((item) => item.profileCode === selectedCode) ?? null,
    [profiles, selectedCode],
  );
  const isBootstrapProfile = selectedCode === BOOTSTRAP_PROFILE;

  async function loadProfiles(signal?: AbortSignal, keepSelection = true) {
    setLoadingList(true);
    setError(null);
    try {
      const rows = await getCsdtConnectionProfiles(signal);
      setProfiles(rows);
      if (!keepSelection || !selectedCode || !rows.some((row) => row.profileCode === selectedCode)) {
        setSelectedCode(rows[0]?.profileCode ?? null);
      }
    } catch (err) {
      if (!isAbortError(err)) {
        setError(err instanceof Error ? err.message : 'Không thể tải danh sách cấu hình kết nối CSDT.');
      }
    } finally {
      setLoadingList(false);
    }
  }

  useEffect(() => {
    const controller = new AbortController();
    void loadProfiles(controller.signal, false);
    return () => controller.abort();
    // Chỉ load danh sách khi mở trang.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  useEffect(() => {
    if (!selectedCode) {
      setDetail(null);
      setForm(emptyForm());
      return;
    }

    const controller = new AbortController();
    setLoadingDetail(true);
    setError(null);
    setActionMessage(null);
    getCsdtConnectionProfile(selectedCode, controller.signal)
      .then((value) => {
        setDetail(value);
        setForm(toForm(value));
      })
      .catch((err) => {
        if (!isAbortError(err)) {
          setError(err instanceof Error ? err.message : 'Không thể tải chi tiết profile.');
        }
      })
      .finally(() => setLoadingDetail(false));

    return () => controller.abort();
  }, [selectedCode]);

  async function handleReload() {
    setActionMessage(null);
    await loadProfiles(undefined, true);
    if (selectedCode) {
      const value = await getCsdtConnectionProfile(selectedCode);
      setDetail(value);
      setForm(toForm(value));
    }
  }

  async function handleSave() {
    if (!selectedCode || isBootstrapProfile) return;

    setSaving(true);
    setError(null);
    setActionMessage(null);
    try {
      const request: SaveCsdtConnectionProfileRequest = {
        displayName: form.displayName || selectedCode,
        serverName: form.serverName || null,
        databaseName: form.databaseName || null,
        authMode: form.authMode,
        userName: form.authMode === 'SqlLogin' ? form.userName || null : null,
        isActive: form.isActive,
        passwordPlainText: null,
      };
      const saved = await saveCsdtConnectionProfile(selectedCode, request);
      setDetail(saved);
      setForm(toForm(saved));
      setActionMessage('Đã lưu metadata cấu hình. Password chưa được gửi/lưu trong giai đoạn này.');
      await loadProfiles(undefined, true);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Không thể lưu cấu hình.');
    } finally {
      setSaving(false);
    }
  }

  async function handleTest() {
    if (!selectedCode) return;

    setTesting(true);
    setError(null);
    setActionMessage(null);
    try {
      const result = await testCsdtConnectionProfile(
        selectedCode,
        isBootstrapProfile
          ? undefined
          : {
              serverName: form.serverName || null,
              databaseName: form.databaseName || null,
              authMode: form.authMode,
              userName: form.authMode === 'SqlLogin' ? form.userName || null : null,
              passwordPlainText: null,
            },
      );
      setActionMessage(`${result.succeeded ? 'Test thành công' : 'Test chưa thành công'}: ${result.message}`);
      await loadProfiles(undefined, true);
      const value = await getCsdtConnectionProfile(selectedCode);
      setDetail(value);
      setForm(toForm(value));
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Không thể test kết nối.');
    } finally {
      setTesting(false);
    }
  }

  return (
    <section className="csdt-profiles-page">
      <div className="toolbar csdt-profiles-toolbar">
        <div>
          <strong>Hệ thống / Cấu hình kết nối CSDT</strong>
          <p>
            Quản lý 7 profile cố định. Profile chưa cấu hình chỉ khóa chức năng liên quan, không làm lỗi toàn hệ thống.
          </p>
        </div>
        <button type="button" className="btn btn--ghost" onClick={handleReload} disabled={loadingList || loadingDetail}>
          Tải lại
        </button>
      </div>

      {error && <div className="pdf-preview-panel__error">{error}</div>}
      {actionMessage && <div className="csdt-profiles-message">{actionMessage}</div>}

      <div className="csdt-profiles-layout">
        <div className="panel csdt-profiles-list">
          <div className="csdt-profiles-section-title">
            <strong>Danh sách profile</strong>
            <span>{profiles.length} profile</span>
          </div>

          {loadingList ? (
            <div className="state">
              <div className="spinner" />
              Đang tải cấu hình kết nối...
            </div>
          ) : profiles.length === 0 ? (
            <div className="state">Chưa có profile nào. Cần chạy patch seed 7 profile trên DB test/local.</div>
          ) : (
            <div className="table-wrap csdt-profiles-table-wrap">
              <table className="table table--csdt-profiles">
                <thead>
                  <tr>
                    <th>Profile</th>
                    <th>Tên hiển thị</th>
                    <th>Nhóm</th>
                    <th>Auth</th>
                    <th>Cấu hình</th>
                    <th>Password</th>
                    <th>Active</th>
                    <th>Test</th>
                  </tr>
                </thead>
                <tbody>
                  {profiles.map((profile) => (
                    <tr
                      key={profile.profileCode}
                      className={profile.profileCode === selectedCode ? 'is-selected' : ''}
                      onClick={() => setSelectedCode(profile.profileCode)}
                    >
                      <td>
                        <button type="button" className="csdt-profile-code-button">
                          {profile.profileCode}
                        </button>
                      </td>
                      <td title={profile.displayName}>{profile.displayName}</td>
                      <td>{profile.profileGroup}</td>
                      <td>{profile.authMode}</td>
                      <td>{profile.isConfigured ? 'Đã cấu hình' : 'Chưa cấu hình'}</td>
                      <td>{profile.isPasswordConfigured ? 'Đã có' : 'Chưa có'}</td>
                      <td>
                        <span className={`status-pill ${profile.isActive ? 'status-pill--ok' : ''}`}>
                          {profile.isActive ? 'Active' : 'Inactive'}
                        </span>
                      </td>
                      <td title={profile.lastTestMessage ?? undefined}>
                        <span className={statusClass(profile.lastTestStatus)}>{formatTestStatus(profile.lastTestStatus)}</span>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </div>

        <aside className="panel csdt-profile-detail">
          <div className="csdt-profiles-section-title">
            <strong>Chi tiết profile</strong>
            {selectedProfile && <span>{selectedProfile.profileCode}</span>}
          </div>

          {!selectedCode ? (
            <div className="state">Chọn một profile để xem chi tiết.</div>
          ) : loadingDetail ? (
            <div className="state">
              <div className="spinner" />
              Đang tải chi tiết...
            </div>
          ) : detail ? (
            <>
              {isBootstrapProfile && (
                <div className="csdt-profile-note">
                  <strong>QLHV_APP là bootstrap profile.</strong>
                  <span>Giai đoạn này chỉ hiển thị/test trạng thái; cấu hình thật vẫn lấy từ server config bảo vệ.</span>
                </div>
              )}

              <div className="csdt-profile-form-grid">
                <label className="field">
                  <span className="field__label">Tên hiển thị</span>
                  <input
                    className="field__input"
                    value={form.displayName}
                    onChange={(event) => setForm((current) => ({ ...current, displayName: event.target.value }))}
                    disabled={isBootstrapProfile}
                  />
                </label>

                <label className="field">
                  <span className="field__label">ServerName</span>
                  <input
                    className="field__input"
                    value={form.serverName}
                    onChange={(event) => setForm((current) => ({ ...current, serverName: event.target.value }))}
                    disabled={isBootstrapProfile}
                    placeholder="Chưa cấu hình"
                  />
                </label>

                <label className="field">
                  <span className="field__label">DatabaseName</span>
                  <input
                    className="field__input"
                    value={form.databaseName}
                    onChange={(event) => setForm((current) => ({ ...current, databaseName: event.target.value }))}
                    disabled={isBootstrapProfile}
                    placeholder="Chưa cấu hình"
                  />
                </label>

                <label className="field">
                  <span className="field__label">AuthMode</span>
                  <select
                    className="field__input"
                    value={form.authMode}
                    onChange={(event) => setForm((current) => ({ ...current, authMode: event.target.value }))}
                    disabled={isBootstrapProfile}
                  >
                    <option value="Windows">Windows</option>
                    <option value="SqlLogin">SQL Login</option>
                  </select>
                </label>

                <label className="field">
                  <span className="field__label">UserName</span>
                  <input
                    className="field__input"
                    value={form.userName}
                    onChange={(event) => setForm((current) => ({ ...current, userName: event.target.value }))}
                    disabled={isBootstrapProfile || form.authMode !== 'SqlLogin'}
                    placeholder={form.authMode === 'SqlLogin' ? 'Nhập khi dùng SQL Login' : 'Không dùng với Windows'}
                  />
                </label>

                <label className="field">
                  <span className="field__label">Password</span>
                  <input
                    className="field__input"
                    value={detail.isPasswordConfigured ? '********' : 'Chưa cấu hình'}
                    disabled
                    readOnly
                  />
                </label>

                <label className="csdt-profile-checkbox">
                  <input
                    type="checkbox"
                    checked={form.isActive}
                    onChange={(event) => setForm((current) => ({ ...current, isActive: event.target.checked }))}
                    disabled={isBootstrapProfile}
                  />
                  Active
                </label>
              </div>

              <div className="csdt-profile-note">
                <strong>Password chưa nhập ở UI giai đoạn này.</strong>
                <span>
                  Không hiển thị, không lưu localStorage/sessionStorage, không gửi plaintext. Sẽ bật sau khi chốt mã hóa mật khẩu.
                </span>
              </div>

              <div className="csdt-profile-meta">
                <span>Trạng thái test: {formatTestStatus(detail.lastTestStatus)}</span>
                <span>Lần test cuối: {formatDateTime(detail.lastTestedAt)}</span>
                {detail.lastTestMessage && <span title={detail.lastTestMessage}>Thông báo: {detail.lastTestMessage}</span>}
              </div>

              <div className="csdt-profile-actions">
                <button
                  type="button"
                  className="btn btn--primary"
                  onClick={handleSave}
                  disabled={isBootstrapProfile || saving || loadingDetail}
                >
                  {saving ? 'Đang lưu...' : 'Lưu metadata'}
                </button>
                <button
                  type="button"
                  className="btn btn--ghost"
                  onClick={handleTest}
                  disabled={testing || loadingDetail}
                >
                  {testing ? 'Đang test...' : 'Test kết nối'}
                </button>
              </div>
            </>
          ) : (
            <div className="state">Không tìm thấy profile đã chọn.</div>
          )}
        </aside>
      </div>
    </section>
  );
}

function emptyForm(): ProfileFormState {
  return {
    displayName: '',
    serverName: '',
    databaseName: '',
    authMode: 'Windows',
    userName: '',
    isActive: false,
  };
}

function toForm(detail: CsdtConnectionProfileDetail): ProfileFormState {
  return {
    displayName: detail.displayName,
    serverName: detail.serverName ?? '',
    databaseName: detail.databaseName ?? '',
    authMode: detail.authMode || 'Windows',
    userName: detail.userName ?? '',
    isActive: detail.isActive,
  };
}

function formatTestStatus(value: string | null): string {
  if (!value || value === 'NotConfigured') return 'Chưa test';
  if (value === 'Success') return 'Thành công';
  if (value === 'Failed') return 'Lỗi';
  return value;
}

function statusClass(value: string | null): string {
  if (value === 'Success') return 'status-pill status-pill--ok';
  if (value === 'Failed') return 'status-pill status-pill--warn';
  return 'status-pill';
}

function formatDateTime(value: string | null): string {
  if (!value) return 'Chưa có';
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return value;
  return date.toLocaleString('vi-VN');
}

function isAbortError(error: unknown): boolean {
  return error instanceof DOMException && error.name === 'AbortError';
}
