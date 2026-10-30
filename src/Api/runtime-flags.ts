// Kept free of imports so the node tests can load it directly.

/** KV key that switches tiered capacity at runtime ("true" / "false"); see RuntimeFlags. */
export const TIERED_CAPACITY_FLAG_KEY = "tiered_capacity";

/** The part of a Workers KV namespace binding this module uses. */
export interface FlagStore {
  get(key: string, options?: { cacheTtl?: number }): Promise<string | null>;
}

/**
 * Switches ops can flip without a deploy: a value in the optional FLAGS KV namespace overrides the deployed var.
 * Each isolate re-reads a key at most every `ttlMs`, so a change takes effect within that time. A missing binding,
 * a missing or unrecognised value, or a KV error falls back to the deployed default.
 */
export class RuntimeFlags {
  private readonly cache = new Map<string, { value: boolean | null; expiresAt: number }>();
  private readonly ttlMs: number;
  private readonly now: () => number;

  constructor(ttlMs = 30_000, now: () => number = Date.now) {
    this.ttlMs = ttlMs;
    this.now = now;
  }

  async isEnabled(store: FlagStore | undefined, key: string, fallback: boolean): Promise<boolean> {
    if (!store) return fallback;
    const cached = this.cache.get(key);
    if (cached && cached.expiresAt > this.now()) return cached.value ?? fallback;

    let value: boolean | null = null;
    try {
      // KV's own edge cache (minimum 30 s) keeps reads cheap; the isolate cache avoids one read per request.
      value = parseFlag(await store.get(key, { cacheTtl: 30 }));
    } catch (err) {
      console.warn(`[worker] flag ${key} unreadable, using deployed default: ${err instanceof Error ? err.message : String(err)}`);
    }
    this.cache.set(key, { value, expiresAt: this.now() + this.ttlMs });
    return value ?? fallback;
  }
}

export function parseFlag(value: string | null | undefined): boolean | null {
  const normalized = (value ?? "").trim().toLowerCase();
  if (normalized === "true" || normalized === "on" || normalized === "1") return true;
  if (normalized === "false" || normalized === "off" || normalized === "0") return false;
  return null;
}
