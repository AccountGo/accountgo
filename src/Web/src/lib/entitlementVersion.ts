/** Header middleware / entitlements GET attach when policy is already resolved for the request. */
export const ENTITLEMENTS_VERSION_HEADER = 'X-AccountGo-Entitlements-Version';

export type CapabilitiesReloader = (orgSlug: string) => Promise<string | null>;

let cachedOrgSlug: string | null = null;
let cachedVersion: string | null = null;
let reloader: CapabilitiesReloader | null = null;
let reloadInFlight: Promise<void> | null = null;

/** Bind the capabilities hook's reload so version-header mismatches can refresh once. */
export function registerCapabilitiesReloader(next: CapabilitiesReloader | null): void {
  reloader = next;
}

/** After a successful /api/entitlements load (or org switch reset), adopt that response's version. */
export function adoptEntitlementsVersion(orgSlug: string | null, version: string | null): void {
  cachedOrgSlug = orgSlug;
  cachedVersion = version;
}

/**
 * Observe a response's entitlements version header.
 * - Compares against the cached version from the last capabilities load.
 * - Ignores responses whose request org differs from the current org (in-flight after a switch).
 * - Allows only one reload in flight; adopts the version from that reload so it cannot loop.
 */
export function observeEntitlementsVersionHeader(
  requestOrgSlug: string | null,
  currentOrgSlug: string | null,
  headerVersion: string | null | undefined,
): void {
  if (!headerVersion || !requestOrgSlug) return;
  if (currentOrgSlug != null && requestOrgSlug !== currentOrgSlug) return;
  if (cachedOrgSlug != null && requestOrgSlug !== cachedOrgSlug) return;
  // No capabilities load yet — do not spuriously reload from unrelated API traffic.
  if (cachedVersion == null) return;
  if (headerVersion === cachedVersion) return;
  void triggerCapabilitiesReload(requestOrgSlug);
}

function triggerCapabilitiesReload(orgSlug: string): Promise<void> {
  if (reloadInFlight) return reloadInFlight;
  if (!reloader) return Promise.resolve();

  reloadInFlight = (async () => {
    try {
      const nextVersion = await reloader!(orgSlug);
      // Only adopt if we are still looking at the same org the reload was for.
      if (cachedOrgSlug == null || cachedOrgSlug === orgSlug) {
        cachedOrgSlug = orgSlug;
        cachedVersion = nextVersion;
      }
    } finally {
      reloadInFlight = null;
    }
  })();

  return reloadInFlight;
}

/** Test seam — reset module state between cases. */
export function resetEntitlementVersionMonitorForTests(): void {
  cachedOrgSlug = null;
  cachedVersion = null;
  reloader = null;
  reloadInFlight = null;
}

export function getCachedEntitlementsVersionForTests(): { orgSlug: string | null; version: string | null; reloadInFlight: boolean } {
  return { orgSlug: cachedOrgSlug, version: cachedVersion, reloadInFlight: reloadInFlight != null };
}
