/**
 * Phase 0 tiered-capacity helpers (shared writer).
 * Phase 1 will route by workspace Company.Tier — never per-user.
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
