import { useCallback, useEffect, useState, type FormEvent } from 'react';
import { getRuntimeStatus } from '../runtime-status/api';
import { isRuntimeReady, type RuntimeStatus } from '../runtime-status/types';
import { LoginRequestError } from './api';
import { useAuth } from './AuthContext';

type ReadinessState = 'checking' | 'ready' | 'not-ready' | 'unavailable';

export default function LoginPage() {
  const { login } = useAuth();
  const [username, setUsername] = useState('');
  const [password, setPassword] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [correlationId, setCorrelationId] = useState<string | null>(null);
  const [runtimeStatus, setRuntimeStatus] = useState<RuntimeStatus | null>(null);
  const [readiness, setReadiness] = useState<ReadinessState>('checking');

  const checkReadiness = useCallback(async (signal?: AbortSignal) => {
    setReadiness('checking');
    setError(null);
    setCorrelationId(null);
    try {
      const status = await getRuntimeStatus(signal);
      setRuntimeStatus(status);
      setReadiness(isRuntimeReady(status) ? 'ready' : 'not-ready');
    } catch (statusError) {
      if (!(statusError instanceof DOMException && statusError.name === 'AbortError')) {
        setRuntimeStatus(null);
        setReadiness('unavailable');
      }
    }
  }, []);

  useEffect(() => {
    const controller = new AbortController();
    void checkReadiness(controller.signal);
    return () => controller.abort();
  }, [checkReadiness]);

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (submitting || readiness !== 'ready' || !username.trim() || !password) {
      return;
    }

    setSubmitting(true);
    setError(null);
    setCorrelationId(null);
    try {
      await login({ username: username.trim(), password });
      setPassword('');
    } catch (loginError) {
      setPassword('');
      setError(loginError instanceof Error ? loginError.message : 'Không thể đăng nhập.');
      if (loginError instanceof LoginRequestError) {
        setCorrelationId(loginError.correlationId);
        if (loginError.kind === 'runtime-unavailable') {
          setReadiness('not-ready');
        }
      }
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <main className="auth-page">
      <section className="auth-card" aria-labelledby="auth-login-title">
        <div className="auth-card__brand" aria-hidden="true">TC</div>
        <div className="auth-card__heading">
          <span>QLHV Thành Công</span>
          <h1 id="auth-login-title">Đăng nhập</h1>
          <p>Đăng nhập bằng tài khoản được quản trị viên cấp.</p>
        </div>

        <RuntimeReadiness
          state={readiness}
          status={runtimeStatus}
          onRetry={() => void checkReadiness()}
        />

        <form className="auth-form" onSubmit={handleSubmit}>
          {error && <div className="auth-error" role="alert">{error}</div>}
          {correlationId && (
            <div className="auth-correlation-id">Mã tham chiếu: <code>{correlationId}</code></div>
          )}
          <label className="field">
            <span className="field__label">Tên đăng nhập</span>
            <input
              className="field__input"
              value={username}
              onChange={(event) => setUsername(event.target.value)}
              autoComplete="username"
              autoFocus
              disabled={submitting || readiness !== 'ready'}
            />
          </label>
          <label className="field">
            <span className="field__label">Mật khẩu</span>
            <input
              className="field__input"
              type="password"
              value={password}
              onChange={(event) => setPassword(event.target.value)}
              autoComplete="current-password"
              disabled={submitting || readiness !== 'ready'}
            />
          </label>
          <button
            type="submit"
            className="btn btn--primary auth-form__submit"
            disabled={submitting || readiness !== 'ready' || !username.trim() || !password}
          >
            {submitting ? 'Đang đăng nhập...' : 'Đăng nhập'}
          </button>
        </form>

        <footer className="auth-version">
          Phiên bản {runtimeStatus?.version ?? 'không xác định'}
        </footer>
      </section>
    </main>
  );
}

function RuntimeReadiness({
  state,
  status,
  onRetry,
}: {
  state: ReadinessState;
  status: RuntimeStatus | null;
  onRetry: () => void;
}) {
  if (state === 'ready') {
    return (
      <div className="auth-readiness auth-readiness--ready" role="status">
        <span aria-hidden="true">✓</span>
        Hệ thống đã sẵn sàng.
      </div>
    );
  }

  if (state === 'checking') {
    return (
      <div className="auth-readiness auth-readiness--checking" role="status" aria-live="polite">
        Đang kiểm tra trạng thái hệ thống...
      </div>
    );
  }

  const messages = status?.messages ?? [];
  return (
    <div className="auth-readiness auth-readiness--not-ready" role="alert">
      <strong>Hệ thống chưa sẵn sàng. Vui lòng liên hệ quản trị viên.</strong>
      {messages.length > 0 && (
        <ul>
          {messages.slice(0, 5).map((message, index) => <li key={`${index}-${message}`}>{message}</li>)}
        </ul>
      )}
      <button type="button" className="btn btn--secondary auth-readiness__retry" onClick={onRetry}>
        Thử lại
      </button>
    </div>
  );
}
