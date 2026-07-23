import { useCallback, useEffect, useRef, useState } from 'react';
import { getSystemDataVersion } from './api';
import type {
  DataVersionResource,
  SystemDataVersion,
} from './types';

const DEFAULT_POLL_INTERVAL_MS = 30_000;

export interface DataVersionRefreshOptions {
  resources: readonly DataVersionResource[];
  onVersionChanged: (
    next: SystemDataVersion,
    previous: SystemDataVersion | null,
  ) => void | Promise<void>;
  enabled?: boolean;
  pollIntervalMs?: number;
}

export interface DataVersionRefreshState {
  version: SystemDataVersion | null;
  checking: boolean;
  error: string | null;
  lastCheckedAt: Date | null;
  reload: () => Promise<void>;
}

export function useDataVersionRefresh({
  resources,
  onVersionChanged,
  enabled = true,
  pollIntervalMs = DEFAULT_POLL_INTERVAL_MS,
}: DataVersionRefreshOptions): DataVersionRefreshState {
  const [version, setVersion] = useState<SystemDataVersion | null>(null);
  const [checking, setChecking] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [lastCheckedAt, setLastCheckedAt] = useState<Date | null>(null);
  const versionRef = useRef<SystemDataVersion | null>(null);
  const callbackRef = useRef(onVersionChanged);
  const checkingRef = useRef(false);
  const pendingManualRef = useRef(false);
  const refreshPendingRef = useRef(false);
  const resourcesRef = useRef(resources);

  callbackRef.current = onVersionChanged;
  resourcesRef.current = resources;

  const check = useCallback(async (manual: boolean, signal?: AbortSignal) => {
    if (!enabled) {
      return;
    }
    if (checkingRef.current) {
      pendingManualRef.current = pendingManualRef.current || manual;
      return;
    }

    checkingRef.current = true;
    setChecking(true);
    try {
      const next = await getSystemDataVersion(signal);
      const previous = versionRef.current;
      // The first version read must also refresh business data. Otherwise a
      // sync can commit between the page's initial business GET and this
      // baseline GET, leaving the page stale forever at the new baseline.
      const changed = previous === null
        || resourcesRef.current.some((key) => previous[key] !== next[key]);

      if (manual || changed || refreshPendingRef.current) {
        refreshPendingRef.current = true;
        await callbackRef.current(next, previous);
        refreshPendingRef.current = false;
      }

      versionRef.current = next;
      setVersion(next);
      setError(null);
      setLastCheckedAt(new Date());
    } catch (reason) {
      if (signal?.aborted) {
        return;
      }
      setError(reason instanceof Error
        ? reason.message
        : 'Không thể kiểm tra phiên bản dữ liệu.');
    } finally {
      checkingRef.current = false;
      setChecking(false);
      if (pendingManualRef.current) {
        pendingManualRef.current = false;
        void Promise.resolve().then(() => check(true));
      }
    }
  }, [enabled]);

  useEffect(() => {
    if (!enabled) {
      return undefined;
    }

    const controller = new AbortController();
    void check(false, controller.signal);

    const timer = window.setInterval(
      () => void check(false),
      Math.max(10_000, pollIntervalMs),
    );
    const handleFocus = () => void check(false);
    window.addEventListener('focus', handleFocus);

    return () => {
      controller.abort();
      window.clearInterval(timer);
      window.removeEventListener('focus', handleFocus);
    };
  }, [check, enabled, pollIntervalMs]);

  const reload = useCallback(
    () => check(true),
    [check],
  );

  return { version, checking, error, lastCheckedAt, reload };
}
