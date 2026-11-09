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
let pendingWriteFailed = false;
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

export function reportServiceProblem(
  problem: ServiceProblem,
  options: { method?: string; now?: number; isOnline?: boolean } = {}
): void {
  const isOnline = checkIsOnline(options.isOnline);
  if (!isOnline) return;

  const now = options.now ?? Date.now();
  const method = (options.method || 'GET').toUpperCase();
  const isWrite = method !== 'GET' && method !== 'HEAD';

  state = {
    ...state,
    state: 'unavailable',
    code: problem.code,
    since: state.state === 'unavailable' && state.since ? state.since : now,
    lastRetryAfter: problem.retryAfterSeconds ?? state.lastRetryAfter ?? 5,
    writeFailed: isWrite || state.writeFailed || pendingWriteFailed,
    message: problem.message ?? (state.code === problem.code ? state.message : undefined),
    until: problem.until ?? (state.code === problem.code ? state.until : undefined),
  };

  notify();
}

export function reportNetworkFailure(
  options: { status?: number; method?: string; now?: number; isOnline?: boolean } = {}
): void {
  const isOnline = checkIsOnline(options.isOnline);
  if (!isOnline) return;

  const now = options.now ?? Date.now();
  const method = (options.method || 'GET').toUpperCase();
  const isWrite = method !== 'GET' && method !== 'HEAD';

  if (isWrite) {
    pendingWriteFailed = true;
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
      writeFailed: pendingWriteFailed || state.writeFailed,
    };
    notify();
  }
}

export function reportServiceRestored(_options: { now?: number } = {}): void {
  networkFailureTimestamps = [];
  pendingWriteFailed = false;

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
  pendingWriteFailed = false;
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
