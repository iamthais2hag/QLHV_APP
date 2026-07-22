import { useState, type FormEvent } from 'react';
import { useAuth } from './AuthContext';

export default function LoginPage() {
  const { login } = useAuth();
  const [username, setUsername] = useState('');
  const [password, setPassword] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (submitting || !username.trim() || !password) {
      return;
    }

    setSubmitting(true);
    setError(null);
    try {
      await login({ username: username.trim(), password });
      setPassword('');
    } catch (loginError) {
      setPassword('');
      setError(loginError instanceof Error ? loginError.message : 'Không thể đăng nhập.');
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

        <form className="auth-form" onSubmit={handleSubmit}>
          {error && <div className="auth-error" role="alert">{error}</div>}
          <label className="field">
            <span className="field__label">Tên đăng nhập</span>
            <input
              className="field__input"
              value={username}
              onChange={(event) => setUsername(event.target.value)}
              autoComplete="username"
              autoFocus
              disabled={submitting}
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
              disabled={submitting}
            />
          </label>
          <button
            type="submit"
            className="btn btn--primary auth-form__submit"
            disabled={submitting || !username.trim() || !password}
          >
            {submitting ? 'Đang đăng nhập...' : 'Đăng nhập'}
          </button>
        </form>
      </section>
    </main>
  );
}
