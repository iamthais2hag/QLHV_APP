import {
  createContext,
  useContext,
  useEffect,
  useMemo,
  useState,
  type ReactNode,
} from 'react';
import { AUTH_SESSION_EXPIRED_EVENT } from '../../api/apiFetch';
import { getCurrentUser, login as loginRequest, logout as logoutRequest } from './api';
import type { AuthenticatedUser, LoginRequest } from './types';

interface AuthContextValue {
  user: AuthenticatedUser | null;
  loading: boolean;
  login: (request: LoginRequest) => Promise<void>;
  logout: () => Promise<void>;
}

const AuthContext = createContext<AuthContextValue | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<AuthenticatedUser | null>(null);
  const [loading, setLoading] = useState(true);

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
