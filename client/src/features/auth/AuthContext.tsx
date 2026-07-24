import {
  createContext,
  useContext,
  useEffect,
  useMemo,
  useRef,
  useState,
  type ReactNode,
} from 'react';
import { AUTH_SESSION_EXPIRED_EVENT } from '../../api/apiFetch';
import {
  changePassword as changePasswordRequest,
  getCurrentUser,
  login as loginRequest,
  logout as logoutRequest,
} from './api';
import type {
  AuthenticatedUser,
  ChangePasswordRequest,
  LoginRequest,
} from './types';
import { ensureQlhvFresh } from '../qlhv-import/api';

interface AuthContextValue {
  user: AuthenticatedUser | null;
  loading: boolean;
  login: (request: LoginRequest) => Promise<void>;
  logout: () => Promise<void>;
  changePassword: (request: ChangePasswordRequest) => Promise<void>;
}

const AuthContext = createContext<AuthContextValue | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<AuthenticatedUser | null>(null);
  const [loading, setLoading] = useState(true);
  const ensuredFreshUserIdRef = useRef<number | null>(null);

  useEffect(() => {
    const handleSessionExpired = () => setUser(null);
    window.addEventListener(AUTH_SESSION_EXPIRED_EVENT, handleSessionExpired);

    const controller = new AbortController();
    let cancelled = false;
    getCurrentUser(controller.signal)
      .then((currentUser) => {
        if (!cancelled) {
          setUser(currentUser);
        }
      })
      .catch((error) => {
        if (!cancelled && !(error instanceof DOMException && error.name === 'AbortError')) {
          setUser(null);
        }
      })
      .finally(() => {
        if (!cancelled) {
          setLoading(false);
        }
      });

    return () => {
      window.removeEventListener(AUTH_SESSION_EXPIRED_EVENT, handleSessionExpired);
      cancelled = true;
      controller.abort();
    };
  }, []);

  useEffect(() => {
    if (!user) {
      ensuredFreshUserIdRef.current = null;
      return;
    }
    if (user.mustChangePassword
      || user.role === 'Viewer'
      || ensuredFreshUserIdRef.current === user.id) {
      return;
    }

    ensuredFreshUserIdRef.current = user.id;
    // Best effort only: opening the app must never wait for or be blocked by Auto Sync.
    void ensureQlhvFresh().catch(() => undefined);
  }, [user]);

  const value = useMemo<AuthContextValue>(() => ({
    user,
    loading,
    login: async (request) => {
      const authenticatedUser = await loginRequest(request);
      setUser(authenticatedUser);
    },
    logout: async () => {
      await logoutRequest();
      setUser(null);
    },
    changePassword: async (request) => {
      await changePasswordRequest(request);
      const refreshedUser = await getCurrentUser();
      if (!refreshedUser) {
        setUser(null);
        throw new Error('Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại.');
      }
      setUser(refreshedUser);
    },
  }), [loading, user]);

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthContextValue {
  const value = useContext(AuthContext);
  if (!value) {
    throw new Error('useAuth must be used inside AuthProvider.');
  }
  return value;
}
