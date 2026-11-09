// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

export type ServiceUnavailableCode =
  | 'SERVICE_UPDATING'
  | 'SERVICE_STARTING'
  | 'SERVICE_MAINTENANCE'
  | 'SERVICE_INTERRUPTED'
  | 'SERVICE_UNREACHABLE'
  | 'SERVICE_MISCONFIGURED';

export const KNOWN_SERVICE_CODES: readonly ServiceUnavailableCode[] = [
  'SERVICE_UPDATING',
  'SERVICE_STARTING',
  'SERVICE_MAINTENANCE',
  'SERVICE_INTERRUPTED',
  'SERVICE_UNREACHABLE',
  'SERVICE_MISCONFIGURED',
] as const;

export const SAFE_NON_GET_RETRY_CODES = new Set<ServiceUnavailableCode>([
  'SERVICE_STARTING',
  'SERVICE_UPDATING',
  'SERVICE_MAINTENANCE',
  'SERVICE_MISCONFIGURED',
]);

export interface ServiceProblem {
  code: ServiceUnavailableCode;
  retryAfterSeconds?: number;
  message?: string;
  until?: string;
}

export interface ServiceStatusState {
  state: 'ok' | 'unavailable';
  code?: ServiceUnavailableCode;
  since?: number;
  lastRetryAfter?: number;
  writeFailed: boolean;
  collapsed: boolean;
  message?: string;
  until?: string;
}

export function parseServiceProblem(
  status: number,
  contentType: string | null | undefined,
  bodyJson: unknown
): ServiceProblem | null {
  if (status !== 503) return null;
  if (contentType && !contentType.toLowerCase().includes('json')) return null;
  if (!bodyJson || typeof bodyJson !== 'object') return null;

  const rawCode = (bodyJson as Record<string, unknown>).code;
  if (typeof rawCode !== 'string' || !(KNOWN_SERVICE_CODES as readonly string[]).includes(rawCode)) {
    return null;
  }

  const code = rawCode as ServiceUnavailableCode;
  const rawRetry = (bodyJson as Record<string, unknown>).retryAfterSeconds;
  const retryAfterSeconds =
    typeof rawRetry === 'number' && Number.isFinite(rawRetry) && rawRetry > 0
      ? rawRetry
      : undefined;

  const rawMsg =
    (bodyJson as Record<string, unknown>).message ??
    (bodyJson as Record<string, unknown>).detail;
  const message = typeof rawMsg === 'string' && rawMsg.trim().length > 0 ? rawMsg.trim() : undefined;

  const rawUntil = (bodyJson as Record<string, unknown>).until;
  let until: string | undefined = undefined;
  if (typeof rawUntil === 'string' && !Number.isNaN(Date.parse(rawUntil))) {
    until = rawUntil;
  }

  return {
    code,
    retryAfterSeconds,
    message,
    until,
  };
}

export async function parseResponseServiceProblem(res: Response): Promise<ServiceProblem | null> {
  if (res.status !== 503) return null;
  const contentType = res.headers.get('content-type');
  const body = await res.json().catch(() => null);
  return parseServiceProblem(res.status, contentType, body);
}

export function shouldRetry503(
  method: string,
  problem: ServiceProblem | null
): boolean {
  const m = method.toUpperCase();
  if (m === 'GET' || m === 'HEAD') {
    return true;
  }
  if (problem && SAFE_NON_GET_RETRY_CODES.has(problem.code)) {
    return true;
  }
  return false;
}

export function nextProbeDelayMs(elapsedMs: number, retryAfterSeconds?: number): number {
  if (elapsedMs < 2 * 60_000) {
    const s =
      typeof retryAfterSeconds === 'number' && Number.isFinite(retryAfterSeconds) && retryAfterSeconds > 0
        ? retryAfterSeconds
        : 5;
    return Math.min(10_000, Math.max(3_000, s * 1000));
  }
  if (elapsedMs < 5 * 60_000) {
    return 15_000;
  }
  return 30_000;
}

export function isTakingLong(elapsedMs: number): boolean {
  return elapsedMs >= 10 * 60_000;
}

let state: ServiceStatusState = {
  state: 'ok',
  code: undefined,
  since: undefined,
  lastRetryAfter: undefined,
  writeFailed: false,
  collapsed: false,
  message: undefined,
  until: undefined,
};

let networkFailureTimestamps: number[] = [];
let pendingUserWrite = false;
const subscribers = new Set<(s: ServiceStatusState) => void>();

function notify(): void {
  for (const fn of subscribers) {
    try {
      fn(state);
    } catch {
      // Ignore subscriber exceptions
    }
  }
}

export function getServiceStatus(): ServiceStatusState {
  return state;
}

export function subscribeServiceStatus(fn: (s: ServiceStatusState) => void): () => void {
  subscribers.add(fn);
  fn(state);
  return () => {
    subscribers.delete(fn);
  };
}

export function setServiceStatusCollapsed(collapsed: boolean): void {
  if (state.collapsed === collapsed) return;
  state = { ...state, collapsed };
  notify();
}

export function dismissWriteFailedNotice(): void {
  if (!state.writeFailed) return;
  state = { ...state, writeFailed: false };
  notify();
}

function checkIsOnline(explicit?: boolean): boolean {
  if (typeof explicit === 'boolean') return explicit;
  if (typeof navigator !== 'undefined' && typeof navigator.onLine === 'boolean') {
    return navigator.onLine;
  }
  return true;
}

export interface ReportServiceProblemOptions {
  method?: string;
  now?: number;
  isOnline?: boolean;
  userWrite?: boolean;
}

export function reportServiceProblem(
  problem: ServiceProblem,
  options: ReportServiceProblemOptions = {}
): void {
  const isOnline = checkIsOnline(options.isOnline);
  if (!isOnline) return;

  const now = options.now ?? Date.now();
  const isUserWrite = Boolean(options.userWrite);

  state = {
    ...state,
    state: 'unavailable',
    code: problem.code,
    since: state.state === 'unavailable' && state.since ? state.since : now,
    lastRetryAfter: problem.retryAfterSeconds ?? state.lastRetryAfter ?? 5,
    writeFailed: isUserWrite || state.writeFailed || pendingUserWrite,
    message: problem.message ?? (state.code === problem.code ? state.message : undefined),
    until: problem.until ?? (state.code === problem.code ? state.until : undefined),
  };

  notify();
}

export interface ReportNetworkFailureOptions {
  status?: number;
  method?: string;
  now?: number;
  isOnline?: boolean;
  userWrite?: boolean;
}

export function reportNetworkFailure(
  options: ReportNetworkFailureOptions = {}
): void {
  const isOnline = checkIsOnline(options.isOnline);
  if (!isOnline) return;

  const now = options.now ?? Date.now();
  const isUserWrite = Boolean(options.userWrite);

  if (isUserWrite) {
    pendingUserWrite = true;
    if (state.state === 'unavailable') {
      state = { ...state, writeFailed: true };
      notify();
    }
  }

  // Retain only failures within the 15-second window
  networkFailureTimestamps = networkFailureTimestamps.filter((t) => now - t <= 15_000);
  networkFailureTimestamps.push(now);

  if (networkFailureTimestamps.length >= 2) {
    state = {
      ...state,
      state: 'unavailable',
      code: 'SERVICE_UNREACHABLE',
      since: state.state === 'unavailable' && state.since ? state.since : now,
      lastRetryAfter: state.lastRetryAfter ?? 5,
      writeFailed: pendingUserWrite || state.writeFailed,
    };
    notify();
  }
}

export function reportServiceRestored(_options: { now?: number } = {}): void {
  networkFailureTimestamps = [];
  pendingUserWrite = false;

  if (state.state === 'unavailable') {
    state = {
      ...state,
      state: 'ok',
      collapsed: false,
      code: undefined,
      since: undefined,
      lastRetryAfter: undefined,
      message: undefined,
      until: undefined,
      // Note: writeFailed stays as is until dismissed so the user notice can be shown.
    };
    notify();

    if (typeof window !== 'undefined') {
      window.dispatchEvent(new CustomEvent('accountgo:service-restored'));
    }
  }
}

export function resetServiceStatusForTesting(): void {
  networkFailureTimestamps = [];
  pendingUserWrite = false;
  state = {
    state: 'ok',
    code: undefined,
    since: undefined,
    lastRetryAfter: undefined,
    writeFailed: false,
    collapsed: false,
    message: undefined,
    until: undefined,
  };
  subscribers.clear();
}

export interface ServiceStatusProbeEnv {
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
    addEventListener: (type: string, listener: () => void) => void;
    removeEventListener: (type: string, listener: () => void) => void;
  };
  onSchedule?: (delayMs: number) => void;
}

export function startServiceStatusProbe(env: ServiceStatusProbeEnv = {}): {
  stop: () => void;
  checkNow: () => Promise<void>;
  getNextCheckAt: () => number;
} {
  const fetchImpl = env.fetchImpl ?? fetch;
  const setTimeoutFn = env.setTimeout ?? setTimeout;
  const clearTimeoutFn = env.clearTimeout ?? ((id: any) => {
    if (typeof clearTimeout !== 'undefined') {
      clearTimeout(id);
    }
  });
  const nowFn = env.now ?? Date.now;
  const doc = env.document ?? (typeof document !== 'undefined' ? document : undefined);
  const win = env.window ?? (typeof window !== 'undefined' ? window : undefined);
  const onSchedule = env.onSchedule;

  let stopped = false;
  let timerId: any = null;
  let nextCheckAt = 0;

  function scheduleNext(delayMs: number): void {
    if (stopped) return;
    if (timerId !== null) {
      clearTimeoutFn(timerId);
      timerId = null;
    }

    nextCheckAt = nowFn() + delayMs;
    onSchedule?.(delayMs);

    timerId = setTimeoutFn(async () => {
      timerId = null;
      await runCheck();
    }, delayMs);
  }

  async function runCheck(): Promise<void> {
    if (stopped) return;

    try {
      const res = await fetchImpl('/api/status', {
        credentials: 'include',
        cache: 'no-store',
      });

      // 2xx or 4xx counts as restored
      if (res.status >= 200 && res.status < 500) {
        reportServiceRestored({ now: nowFn() });
        return; // Stops loop because state became ok
      }

      // 503 with known problem body
      if (res.status === 503) {
        const problem = await parseResponseServiceProblem(res);
        if (problem) {
          reportServiceProblem(problem, { now: nowFn() });
        } else {
          // 5xx without known problem body counts as failure
          reportNetworkFailure({ status: res.status, now: nowFn() });
        }
      } else {
        // Other 5xx count as failure
        reportNetworkFailure({ status: res.status, now: nowFn() });
      }
    } catch {
      // Network error counts as failure
      reportNetworkFailure({ now: nowFn() });
    }

    // After every check, schedule next probe unless state became ok
    const current = getServiceStatus();
    if (current.state === 'unavailable' && !stopped) {
      const elapsed = current.since ? nowFn() - current.since : 0;
      const delay = nextProbeDelayMs(elapsed, current.lastRetryAfter);
      scheduleNext(delay);
    }
  }

  // Initial schedule if unavailable
  const current = getServiceStatus();
  if (current.state === 'unavailable') {
    const elapsed = current.since ? nowFn() - current.since : 0;
    scheduleNext(nextProbeDelayMs(elapsed, current.lastRetryAfter));
  }

  const handleVisibilityOrOnline = () => {
    if (stopped) return;
    if (!doc || doc.visibilityState === 'visible') {
      void runCheck();
    }
  };

  doc?.addEventListener('visibilitychange', handleVisibilityOrOnline);
  win?.addEventListener('online', handleVisibilityOrOnline);

  return {
    stop() {
      stopped = true;
      if (timerId !== null) {
        clearTimeoutFn(timerId);
        timerId = null;
      }
      doc?.removeEventListener('visibilitychange', handleVisibilityOrOnline);
      win?.removeEventListener('online', handleVisibilityOrOnline);
    },
    async checkNow() {
      await runCheck();
    },
    getNextCheckAt() {
      return nextCheckAt;
    },
  };
}
