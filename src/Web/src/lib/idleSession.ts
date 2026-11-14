// Automatic sign-out after inactivity, shared by every open tab.
//
// Activity only records a timestamp. One timer wakes at the next deadline and re-checks, so mouse moves never
// reschedule timers. Deadlines are compared against the clock, so background-tab throttling and laptop sleep can't
// stretch them: when a tab becomes visible again it checks at once. Tabs share activity, refreshes and sign-out over a
// BroadcastChannel that carries timestamps only — never tokens.

export const IDLE_WARNING_MS = 60_000;
export const EXPIRY_WARNING_MS = 2 * 60_000;
/** Keep in step with SessionTimeoutPolicy.ActivityRefreshInterval on the server. */
export const ACTIVITY_REFRESH_MS = 5 * 60_000;
const BROADCAST_THROTTLE_MS = 15_000;
const CHANNEL_NAME = 'accountgo-activity';

export const ACTIVITY_EVENTS = ['pointermove', 'pointerdown', 'keydown', 'wheel', 'scroll', 'touchstart'] as const;

export type SignOutReason = 'idle' | 'expired';
export type SessionWarning = { kind: 'idle' | 'expiry'; deadline: number } | null;

type Message =
  | { type: 'active'; at: number }
  | { type: 'refreshed'; at: number; idleMs?: number }
  | { type: 'signout'; reason: SignOutReason };

interface Channel {
  postMessage(message: Message): void;
  onmessage: ((event: { data: unknown }) => void) | null;
  close(): void;
}

interface EventSource {
  addEventListener(type: string, listener: () => void, options?: AddEventListenerOptions): void;
  removeEventListener(type: string, listener: () => void, options?: EventListenerOptions): void;
}

export interface IdleSessionEnv {
  now(): number;
  setTimeout(fn: () => void, ms: number): unknown;
  clearTimeout(id: unknown): void;
  channel: Channel | null;
  window: EventSource;
  document: EventSource & { visibilityState: string };
}

export interface IdleSessionOptions {
  idleMs: number;
  /** Hard end of the sign-in (epoch ms); activity never extends it. */
  sessionExpiresAt: number | null;
  /** When this tab last refreshed the session (epoch ms). */
  refreshedAt: number;
  onWarning(warning: SessionWarning): void;
  /** The user is active and the server's idle window should move forward. */
  onRefreshDue(): void;
  /** `origin` is true in the tab that detected it; other tabs only drop their in-memory session. */
  onSignOut(reason: SignOutReason, origin: boolean): void;
  /** Invoked when another tab broadcasts an updated idle timeout. */
  onRemoteIdleMs?(idleMs: number, refreshedAt: number): void;
}

export interface IdleSession {
  /** Explicit "Stay signed in": the only local activity that closes an open idle warning. */
  markActive(): void;
  /** Record a completed session refresh, here or in another tab. */
  markRefreshed(at?: number, idleMs?: number): void;
  /** Hide the 12-hour notice until sign-out. */
  dismissExpiryWarning(): void;
  stop(): void;
}

function browserEnv(): IdleSessionEnv {
  const channel = typeof BroadcastChannel === 'function' ? (new BroadcastChannel(CHANNEL_NAME) as unknown as Channel) : null;
  return {
    now: () => Date.now(),
    setTimeout: (fn, ms) => window.setTimeout(fn, ms),
    clearTimeout: (id) => window.clearTimeout(id as number),
    channel,
    window,
    document,
  };
}

export function startIdleSession(options: IdleSessionOptions, env: IdleSessionEnv = browserEnv()): IdleSession {
  const { sessionExpiresAt, onWarning, onRefreshDue, onSignOut, onRemoteIdleMs } = options;
  let idleMs = options.idleMs;
  const expiry = sessionExpiresAt ?? Number.POSITIVE_INFINITY;
  let lastActivity = env.now();
  let refreshedAt = options.refreshedAt;
  let lastBroadcast = 0;
  let warning: SessionWarning = null;
  let expiryDismissed = false;
  let timer: unknown = null;
  let stopped = false;

  const post = (message: Message) => {
    try {
      env.channel?.postMessage(message);
    } catch {
      // A closed channel only loses cross-tab sync; this tab still enforces its own deadlines.
    }
  };

  const setWarning = (next: SessionWarning) => {
    if (next?.kind === warning?.kind && next?.deadline === warning?.deadline) return;
    warning = next;
    onWarning(next);
  };

  const signOut = (reason: SignOutReason, origin: boolean) => {
    if (stopped) return;
    if (origin) post({ type: 'signout', reason });
    cleanup();
    onWarning(null);
    onSignOut(reason, origin);
  };

  const evaluate = () => {
    if (stopped) return;
    const now = env.now();
    const idleDeadline = lastActivity + idleMs;
    if (now >= expiry) return signOut('expired', true);
    if (now >= idleDeadline) return signOut('idle', true);

    if (expiry <= idleDeadline && expiry - now <= EXPIRY_WARNING_MS && !expiryDismissed) {
      setWarning({ kind: 'expiry', deadline: expiry });
    } else if (idleDeadline - now <= IDLE_WARNING_MS) {
      setWarning({ kind: 'idle', deadline: idleDeadline });
    } else {
      setWarning(null);
    }

    const wakeAt = Math.min(
      ...[idleDeadline - IDLE_WARNING_MS, idleDeadline, expiry - EXPIRY_WARNING_MS, expiry].filter((t) => t > now),
    );
    env.clearTimeout(timer);
    // Cap the delay: browsers fire timers longer than ~24.8 days immediately.
    timer = env.setTimeout(evaluate, Math.min(wakeAt - now, 2 ** 31 - 1));
  };

  // `ambient`: mouse/keyboard/touch in this tab. `explicit`: "Stay signed in". `remote`: another tab's activity.
  const recordActivity = (at: number, source: 'ambient' | 'explicit' | 'remote') => {
    // Once "Are you still there?" is up, reaching for its buttons must not dismiss it: only an explicit answer
    // (here or in another tab) keeps the session.
    if (source === 'ambient' && warning?.kind === 'idle') return;
    if (at <= lastActivity) return;
    lastActivity = at;
    if (source !== 'remote') {
      if (warning || at - lastBroadcast >= BROADCAST_THROTTLE_MS) {
        lastBroadcast = at;
        post({ type: 'active', at });
      }
      if (at - refreshedAt >= ACTIVITY_REFRESH_MS) {
        refreshedAt = at; // one request per interval, even if it is slow
        onRefreshDue();
      }
    }
    // Only an open warning needs an immediate re-check; otherwise the pending timer re-reads lastActivity.
    if (warning?.kind === 'idle') evaluate();
  };

  const onActivity = () => recordActivity(env.now(), 'ambient');
  const onVisibility = () => {
    if (env.document.visibilityState === 'visible') evaluate();
  };

  if (env.channel) {
    env.channel.onmessage = (event) => {
      const message = event.data as Message;
      if (!message || typeof message !== 'object') return;
      if (message.type === 'active' && typeof message.at === 'number') recordActivity(message.at, 'remote');
      else if (message.type === 'refreshed' && typeof message.at === 'number') {
        refreshedAt = Math.max(refreshedAt, message.at);
        if (typeof message.idleMs === 'number' && Number.isFinite(message.idleMs) && message.idleMs > 0) {
          idleMs = message.idleMs;
          onRemoteIdleMs?.(message.idleMs, message.at);
          evaluate();
        }
      }
      else if (message.type === 'signout' && (message.reason === 'idle' || message.reason === 'expired')) signOut(message.reason, false);
    };
  }

  const listenerOptions: AddEventListenerOptions = { passive: true, capture: true };
  for (const type of ACTIVITY_EVENTS) env.window.addEventListener(type, onActivity, listenerOptions);
  env.document.addEventListener('visibilitychange', onVisibility);

  function cleanup() {
    stopped = true;
    env.clearTimeout(timer);
    for (const type of ACTIVITY_EVENTS) env.window.removeEventListener(type, onActivity, { capture: true });
    env.document.removeEventListener('visibilitychange', onVisibility);
    if (env.channel) {
      env.channel.onmessage = null;
      env.channel.close();
    }
  }

  evaluate();

  return {
    markActive: () => recordActivity(env.now(), 'explicit'),
    markRefreshed(at = env.now(), nextIdleMs?: number) {
      refreshedAt = Math.max(refreshedAt, at);
      if (typeof nextIdleMs === 'number' && Number.isFinite(nextIdleMs) && nextIdleMs > 0) {
        idleMs = nextIdleMs;
        post({ type: 'refreshed', at, idleMs: nextIdleMs });
        evaluate();
      } else {
        post({ type: 'refreshed', at });
      }
    },
    dismissExpiryWarning() {
      expiryDismissed = true;
      evaluate();
    },
    stop() {
      if (!stopped) cleanup();
    },
  };
}
