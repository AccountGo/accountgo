/**
 * Keeps the workspace permissions fresh without a reload. They are loaded once when the workspace opens, so a role change made by
 * someone else (an admin approving a request) would otherwise leave the member locked out until they reload.
 *
 * `throttledRefresh` guarantees at most one refresh per workspace and user per minute, counted from when it starts (whether it
 * succeeded or not), and shares one in-flight refresh between callers, so a failing request can never turn into a loop.
 */
export const PERMISSION_REFRESH_MIN_INTERVAL_MS = 60_000;

interface Entry {
  startedAt: number;
  inflight: Promise<boolean> | null;
}

const entries = new Map<string, Entry>();

/** Runs `run` unless one is in flight (then shares it) or one started within the last minute (then resolves false without running). */
export function throttledRefresh(key: string, run: () => Promise<boolean>, now: () => number = Date.now): Promise<boolean> {
  const entry = entries.get(key);
  if (entry?.inflight) return entry.inflight;
  if (entry && now() - entry.startedAt < PERMISSION_REFRESH_MIN_INTERVAL_MS) return Promise.resolve(false);

  const next: Entry = { startedAt: now(), inflight: null };
  next.inflight = run()
    .catch(() => false)
    .finally(() => {
      next.inflight = null;
    });
  entries.set(key, next);
  return next.inflight;
}

/** For tests: forget every refresh, as in a fresh page load. */
export function resetPermissionRefresh(): void {
  entries.clear();
}

export interface WorkspaceAccessSnapshot {
  slug?: string;
  role?: string;
  permissions?: readonly string[];
}

const sameSet = (a: readonly string[] = [], b: readonly string[] = []) => a.length === b.length && a.every((value) => b.includes(value));

/**
 * Reads the workspace as the server sees it now and applies the member's role and permissions when they changed. Returns true only
 * when something changed. A failed read, or an answer for a different workspace than the one on screen, changes nothing.
 */
export async function refreshWorkspaceAccess(options: {
  fetchCurrent: () => Promise<Response>;
  getCurrent: () => WorkspaceAccessSnapshot | null;
  apply: (updates: { permissions: string[]; role: string }) => void;
}): Promise<boolean> {
  const response = await options.fetchCurrent();
  if (!response.ok) return false;
  const server = (await response.json()) as { slug?: string; role?: string; permissions?: string[] };
  const shown = options.getCurrent();
  if (!shown || !server.permissions || !server.role || (shown.slug && server.slug && shown.slug !== server.slug)) return false;
  if (shown.role === server.role && sameSet(shown.permissions, server.permissions)) return false;
  options.apply({ permissions: server.permissions, role: server.role });
  return true;
}
