/**
 * Tiered container capacity. Phase 0: one shared writer, sized at start(). Phase 1 (below): pools by workspace tier,
 * keyed by workspace — never per user.
 */

export const NAMED_CONTAINER_INSTANCES = [
  "lite",
  "standard-1",
  "standard-2",
  "standard-3",
  "standard-4",
] as const;

export type NamedContainerInstance = (typeof NAMED_CONTAINER_INSTANCES)[number];

const DEFAULT_INSTANCE: NamedContainerInstance = "standard-1";

export function isTieredCapacityEnabled(
  value: string | undefined | null
): boolean {
  return (value ?? "").trim().toLowerCase() === "true";
}

/**
 * Resolve V2 start() instance from ops/env.
 * durable_object policy accepts lite | standard-1…4 only (not "basic").
 * "basic" / "dev" / "standard" aliases map to the nearest supported size.
 */
export function resolveContainerInstanceType(
  value: string | undefined | null
): NamedContainerInstance {
  const normalized = (value ?? "").trim().toLowerCase();
  if (
    (NAMED_CONTAINER_INSTANCES as readonly string[]).includes(normalized)
  ) {
    return normalized as NamedContainerInstance;
  }
  // Legacy / default-policy names — not accepted by durable_object start().
  if (normalized === "dev") return "lite";
  if (normalized === "basic" || normalized === "standard") return "standard-1";
  return DEFAULT_INSTANCE;
}

/** Single shared writer name (Phase 0). Same shape for V1 and V2. */
export function resolveWriterContainerName(
  containerName: string | undefined,
  versionId: string | undefined
): string {
  return `${containerName || "default"}-${versionId || "dev"}`;
}

/**
 * Phase 1 pools: paid workspaces (paid plan or comp) get a V2 container on a bigger instance; free workspaces stay on
 * V1 (see worker.ts for why not V2 lite). All containers share the Postgres database, so a request routed to the wrong
 * pool is only slower, never inconsistent.
 */
export type CapacityPool = "free" | "paid";

/** Response header the API sets when the request already resolved the workspace's plan (paid | free). */
export const CAPACITY_HINT_HEADER = "x-accountgo-capacity";
/** Request header the Worker uses to tell the V2 Durable Object which pool it serves. */
export const CAPACITY_POOL_HEADER = "x-accountgo-capacity-pool";
/** Request header carrying the workspace slug (TenantResolutionMiddleware.HeaderName). */
export const WORKSPACE_HEADER = "x-accountgo-org";

export function parseCapacityPool(value: string | null | undefined): CapacityPool | null {
  const normalized = (value ?? "").trim().toLowerCase();
  return normalized === "paid" || normalized === "free" ? normalized : null;
}

/** Instance size per pool: CONTAINER_INSTANCE_TYPE_PAID (default standard-1) / _FREE (default lite). */
export function resolvePoolInstanceType(
  pool: CapacityPool,
  env: { CONTAINER_INSTANCE_TYPE_PAID?: string; CONTAINER_INSTANCE_TYPE_FREE?: string }
): NamedContainerInstance {
  const configured = pool === "paid" ? env.CONTAINER_INSTANCE_TYPE_PAID : env.CONTAINER_INSTANCE_TYPE_FREE;
  if (!configured || !configured.trim()) return pool === "paid" ? "standard-1" : "lite";
  return resolveContainerInstanceType(configured);
}

/** One writer per pool per deploy version, e.g. "v10-paid-<versionId>". */
export function resolvePoolContainerName(
  containerName: string | undefined,
  pool: CapacityPool,
  versionId: string | undefined
): string {
  return `${containerName || "default"}-${pool}-${versionId || "dev"}`;
}

/**
 * Remembers which pool a workspace slug belongs to, learnt from the API's capacity hint. Unknown slugs (and requests
 * without a workspace: sign-in, refresh, the workspace list) go to the free pool. Per Worker isolate; entries expire
 * so an upgrade or downgrade takes effect within the TTL.
 */
export class CapacityRouter {
  private readonly entries = new Map<string, { pool: CapacityPool; expiresAt: number }>();
  private readonly ttlMs: number;
  private readonly maxEntries: number;
  private readonly now: () => number;

  constructor(ttlMs = 10 * 60 * 1000, maxEntries = 10_000, now: () => number = Date.now) {
    this.ttlMs = ttlMs;
    this.maxEntries = maxEntries;
    this.now = now;
  }

  poolFor(slug: string | null | undefined): CapacityPool {
    const key = normalizeSlug(slug);
    if (!key) return "free";
    const entry = this.entries.get(key);
    if (!entry) return "free";
    if (entry.expiresAt <= this.now()) {
      this.entries.delete(key);
      return "free";
    }
    return entry.pool;
  }

  remember(slug: string | null | undefined, pool: CapacityPool): void {
    const key = normalizeSlug(slug);
    if (!key) return;
    this.entries.delete(key);
    if (this.entries.size >= this.maxEntries) {
      // Oldest insertion first (Map keeps insertion order).
      const oldest = this.entries.keys().next().value;
      if (oldest !== undefined) this.entries.delete(oldest);
    }
    this.entries.set(key, { pool, expiresAt: this.now() + this.ttlMs });
  }
}

function normalizeSlug(slug: string | null | undefined): string | null {
  const trimmed = (slug ?? "").trim().toLowerCase();
  return trimmed.length > 0 && trimmed.length <= 100 ? trimmed : null;
}
