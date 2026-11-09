// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

export const POLL_INTERVAL_MS = 60_000;

/**
 * Reads the build ID injected by Vite during bundle build.
 * Guarded so Node tests run safely when __APP_BUILD_ID__ is undeclared.
 */
export function readCurrentBuildId(): string {
  if (typeof __APP_BUILD_ID__ === 'string') {
    return __APP_BUILD_ID__;
  }
  return '';
}

/**
 * Parses /version.json response body and returns a trimmed buildId, or null.
 */
export function parseVersionJson(json: unknown): string | null {
  if (!json || typeof json !== 'object') return null;
  const raw = (json as Record<string, unknown>).buildId;
  if (typeof raw !== 'string') return null;
  const trimmed = raw.trim();
  return trimmed.length > 0 ? trimmed : null;
}

/**
 * Returns true if deployed and current are both non-empty and differ.
 */
export function isNewerDeploy(deployed: string, current: string): boolean {
  const d = (deployed || '').trim();
  const c = (current || '').trim();
  return Boolean(d && c && d !== c);
}

// Module-level deferred update store
let updateDeferredBuildId: string | null = null;
const deferredListeners = new Set<(id: string | null) => void>();

export function getDeferredUpdate(): string | null {
  return updateDeferredBuildId;
}

export function subscribeDeferredUpdate(listener: (id: string | null) => void): () => void {
  deferredListeners.add(listener);
  return () => {
    deferredListeners.delete(listener);
  };
}

export function deferUpdate(id: string): void {
  updateDeferredBuildId = id;
  for (const listener of deferredListeners) {
    listener(updateDeferredBuildId);
  }
}

export function resetDeferredUpdateForTesting(): void {
  updateDeferredBuildId = null;
  deferredListeners.clear();
}

export interface ShouldOpenUpdateDialogParams {
  pendingId: string | null | undefined;
  deferredId: string | null | undefined;
  serviceState: 'ok' | 'unavailable';
  bootSuppressed: boolean;
}

/**
 * Pure decision function: determines if the update modal should be shown.
 */
export function shouldOpenUpdateDialog(params: ShouldOpenUpdateDialogParams): boolean {
  const { pendingId, deferredId, serviceState, bootSuppressed } = params;
  if (!pendingId) return false;
  if (serviceState === 'unavailable') return false;
  if (bootSuppressed) return false;
  if (deferredId && pendingId === deferredId) return false;
  return true;
}

export interface VersionWatchEnv {
  fetchImpl?: (url: string, init?: RequestInit) => Promise<Response>;
  setTimeout?: (fn: () => void, ms: number) => any;
  clearTimeout?: (id: any) => void;
  now?: () => number;
  document?: {
    visibilityState: string;
    addEventListener: (type: string, listener: () => void) => void;
    removeEventListener: (type: string, listener: () => void) => void;
  };
  window?: {
    addEventListener: (type: string, listener: (event?: any) => void) => void;
    removeEventListener: (type: string, listener: (event?: any) => void) => void;
  };
  currentBuildId?: string;
  onUpdate: (buildId: string) => void;
  enabled?: boolean;
}

/**
 * Polling controller that periodically checks /version.json while the tab is visible.
 */
export function startVersionWatch(env: VersionWatchEnv): {
  stop: () => void;
  checkNow: () => Promise<void>;
} {
  if (env.enabled === false) {
    return {
      stop: () => {},
      checkNow: async () => {},
    };
  }

  const fetchImpl = env.fetchImpl ?? fetch;
  const setTimeoutFn = env.setTimeout ?? setTimeout;
  const clearTimeoutFn = env.clearTimeout ?? ((id: any) => {
    if (typeof clearTimeout !== 'undefined') clearTimeout(id);
  });
  const nowFn = env.now ?? Date.now;
  const doc = env.document ?? (typeof document !== 'undefined' ? document : undefined);
  const win = env.window ?? (typeof window !== 'undefined' ? window : undefined);
  const currentBuildId = env.currentBuildId ?? readCurrentBuildId();
  const onUpdate = env.onUpdate;

  let stopped = false;
  let timerId: any = null;
  let lastReportedId: string | null = null;

  function isDocumentVisible(): boolean {
    return !doc || doc.visibilityState === 'visible';
  }

  function scheduleNext(): void {
    if (stopped) return;
    if (timerId !== null) {
      clearTimeoutFn(timerId);
      timerId = null;
    }
    if (!isDocumentVisible()) return;

    timerId = setTimeoutFn(async () => {
      timerId = null;
      await runCheck();
    }, POLL_INTERVAL_MS);
  }

  async function runCheck(): Promise<void> {
    if (stopped) return;
    if (timerId !== null) {
      clearTimeoutFn(timerId);
      timerId = null;
    }

    try {
      const url = `/version.json?t=${nowFn()}`;
      const res = await fetchImpl(url, {
        cache: 'no-store',
      });
      if (res && res.ok) {
        const data = await res.json().catch(() => null);
        const deployedId = parseVersionJson(data);
        if (
          deployedId &&
          isNewerDeploy(deployedId, currentBuildId) &&
          deployedId !== lastReportedId
        ) {
          lastReportedId = deployedId;
          onUpdate(deployedId);
        }
      }
    } catch {
      // Fetch failures, 404s, or bad JSON ignored silently
    } finally {
      if (!stopped) {
        scheduleNext();
      }
    }
  }

  const handleVisibilityChange = () => {
    if (stopped) return;
    if (isDocumentVisible()) {
      void runCheck();
    } else if (timerId !== null) {
      clearTimeoutFn(timerId);
      timerId = null;
    }
  };

  const handleImmediateTrigger = () => {
    if (stopped) return;
    if (isDocumentVisible()) {
      void runCheck();
    }
  };

  const handlePreloadError = (event: any) => {
    if (stopped) return;
    if (event && typeof event.preventDefault === 'function') {
      event.preventDefault();
    }
    if (lastReportedId !== 'preload-error') {
      lastReportedId = 'preload-error';
      onUpdate('preload-error');
    }
  };

  doc?.addEventListener('visibilitychange', handleVisibilityChange);
  win?.addEventListener('focus', handleImmediateTrigger);
  win?.addEventListener('online', handleImmediateTrigger);
  win?.addEventListener('accountgo:service-restored', handleImmediateTrigger);
  win?.addEventListener('vite:preloadError', handlePreloadError);

  // Initial schedule if document is currently visible
  if (isDocumentVisible()) {
    scheduleNext();
  }

  return {
    stop() {
      stopped = true;
      if (timerId !== null) {
        clearTimeoutFn(timerId);
        timerId = null;
      }
      doc?.removeEventListener('visibilitychange', handleVisibilityChange);
      win?.removeEventListener('focus', handleImmediateTrigger);
      win?.removeEventListener('online', handleImmediateTrigger);
      win?.removeEventListener('accountgo:service-restored', handleImmediateTrigger);
      win?.removeEventListener('vite:preloadError', handlePreloadError);
    },
    async checkNow() {
      await runCheck();
    },
  };
}
