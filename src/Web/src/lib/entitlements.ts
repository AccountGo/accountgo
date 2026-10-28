import React, { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState } from 'react';
import { useAuth, getOrgSlugFromLocation } from '../auth/AuthContext';
import type { ModuleKey, PlanKey } from '../types';
import { ENTITLEMENT_DENIED_EVENT } from './entitlementEvents';
import {
  adoptEntitlementsVersion,
  registerCapabilitiesReloader,
} from './entitlementVersion';

export {
  ENTITLEMENT_CODES, ENTITLEMENT_DENIED_EVENT, announceEntitlementDenial, isEntitlementProblem,
  type EntitlementCode, type EntitlementProblem,
} from './entitlementEvents';

export type MeteredResource = 'salesDocuments' | 'bills' | 'seats' | 'bankAccounts';

/** Resource → module for create buttons. Seats (invites) have no module. */
export const CREATE_RESOURCE_MODULE: Partial<Record<MeteredResource, ModuleKey>> = {
  salesDocuments: 'sales',
  bills: 'purchases',
};

export interface ResourceLimit {
  /** null = unlimited */
  limit: number | null;
  used: number;
  period: 'month' | null;
  overLimit?: boolean;
}

export interface EntitlementLifecycle {
  status: string;
  graceEndsAt?: string | null;
  cancelAtPeriodEnd?: boolean;
  periodEnd?: string | null;
  reducedBy?: string | null;
}

/** GET /api/entitlements — what the active workspace's plan includes and how much is used. */
export interface Entitlements {
  enabled: boolean;
  plan: PlanKey | string;
  effectivePlan?: PlanKey | string;
  tier: 'free' | 'pro' | 'comp' | string;
  limited: boolean;
  modules: ModuleKey[];
  readOnlyModules: ModuleKey[];
  features: string[];
  selfServicePlans: PlanKey[];
  allowedTransitions: string[];
  lifecycle: EntitlementLifecycle | null;
  limits: Partial<Record<MeteredResource, ResourceLimit>>;
  version: string | null;
  /** Present on the wire; PlanLimitDialog ignores hard-coded upgrade.available. */
  upgrade: { available: boolean; contact: string | null };
}

/** Human label for a metered resource, e.g. in usage hints. */
export const RESOURCE_LABELS: Record<MeteredResource, string> = {
  salesDocuments: 'invoices & quotes',
  bills: 'bills',
  seats: 'users',
  bankAccounts: 'bank & cash accounts',
};

function asStringArray(value: unknown): string[] {
  return Array.isArray(value) ? value.filter((item): item is string => typeof item === 'string') : [];
}

function asModuleArray(value: unknown): ModuleKey[] {
  return asStringArray(value) as ModuleKey[];
}

/** Missing features / readOnlyModules / allowedTransitions → [] so a web-before-API deploy cannot crash. */
export function normalizeEntitlementsPayload(raw: unknown): Entitlements {
  const body = (raw && typeof raw === 'object' ? raw : {}) as Record<string, unknown>;
  const limitsRaw = (body.limits && typeof body.limits === 'object' ? body.limits : {}) as Record<string, Record<string, unknown>>;
  const limits: Entitlements['limits'] = {};
  for (const [key, value] of Object.entries(limitsRaw)) {
    limits[key as MeteredResource] = {
      limit: typeof value?.limit === 'number' ? value.limit : value?.limit === null ? null : null,
      used: typeof value?.used === 'number' ? value.used : 0,
      period: value?.period === 'month' ? 'month' : null,
      overLimit: !!value?.overLimit,
    };
  }
  const lifecycleRaw = body.lifecycle && typeof body.lifecycle === 'object'
    ? body.lifecycle as Record<string, unknown>
    : null;
  const upgradeRaw = body.upgrade && typeof body.upgrade === 'object'
    ? body.upgrade as Record<string, unknown>
    : null;

  return {
    enabled: !!body.enabled,
    plan: (typeof body.plan === 'string' ? body.plan : 'accounting') as PlanKey,
    effectivePlan: typeof body.effectivePlan === 'string' ? body.effectivePlan : undefined,
    tier: typeof body.tier === 'string' ? body.tier : 'free',
    limited: !!body.limited,
    modules: asModuleArray(body.modules),
    readOnlyModules: asModuleArray(body.readOnlyModules),
    features: asStringArray(body.features),
    selfServicePlans: asStringArray(body.selfServicePlans) as PlanKey[],
    allowedTransitions: asStringArray(body.allowedTransitions),
    lifecycle: lifecycleRaw
      ? {
          status: typeof lifecycleRaw.status === 'string' ? lifecycleRaw.status : 'active',
          graceEndsAt: typeof lifecycleRaw.graceEndsAt === 'string' ? lifecycleRaw.graceEndsAt : null,
          cancelAtPeriodEnd: !!lifecycleRaw.cancelAtPeriodEnd,
          periodEnd: typeof lifecycleRaw.periodEnd === 'string' ? lifecycleRaw.periodEnd : null,
          reducedBy: typeof lifecycleRaw.reducedBy === 'string' ? lifecycleRaw.reducedBy : null,
        }
      : null,
    limits,
    version: typeof body.version === 'string' ? body.version : null,
    upgrade: {
      available: !!upgradeRaw?.available,
      contact: typeof upgradeRaw?.contact === 'string' ? upgradeRaw.contact : null,
    },
  };
}

/**
 * Nav / lists / reports: subscribed modules plus read-only grace modules.
 * No snapshot yet (loading or first load failed) → fail open on bootstrapModules.
 */
export function evaluateCanModule(snapshot: Entitlements | null, moduleKey: ModuleKey, bootstrapModules?: ModuleKey[]): boolean {
  if (snapshot) {
    return snapshot.modules.includes(moduleKey) || snapshot.readOnlyModules.includes(moduleKey);
  }
  return (bootstrapModules || []).includes(moduleKey);
}

/**
 * Mutative UI: write modules only (false when the module is read-only).
 * No snapshot → fail open on bootstrapModules (same as canModule until policy loads).
 */
export function evaluateCanWrite(snapshot: Entitlements | null, moduleKey: ModuleKey, bootstrapModules?: ModuleKey[]): boolean {
  if (snapshot) {
    return snapshot.modules.includes(moduleKey);
  }
  return (bootstrapModules || []).includes(moduleKey);
}

/** Features fail closed when there is no snapshot. */
export function evaluateCanFeature(snapshot: Entitlements | null, feature: string): boolean {
  if (!snapshot) return false;
  return snapshot.features.includes(feature);
}

/**
 * Create rule: !limited || (limit == null) || (!overLimit && limit − used > 0).
 * No snapshot → fail open (true). Server remains the authority; this is best-effort UI.
 */
export function evaluateCanCreate(snapshot: Entitlements | null, resource: MeteredResource): boolean {
  if (!snapshot) return true;
  if (!snapshot.limited) return true;
  const usage = snapshot.limits[resource];
  if (!usage || usage.limit == null) return true;
  if (usage.overLimit) return false;
  return usage.limit - usage.used > 0;
}

/** permission ∧ canWrite(module?) ∧ canCreate(resource). Seats have no module. */
export function evaluateCanCreateAction(
  snapshot: Entitlements | null,
  resource: MeteredResource,
  hasPermission: boolean,
  bootstrapModules?: ModuleKey[],
): boolean {
  if (!hasPermission) return false;
  const moduleKey = CREATE_RESOURCE_MODULE[resource];
  if (moduleKey && !evaluateCanWrite(snapshot, moduleKey, bootstrapModules)) return false;
  return evaluateCanCreate(snapshot, resource);
}

export interface Capabilities {
  entitlements: Entitlements | null;
  /** True after the first /api/entitlements response for this org (success or failure). */
  ready: boolean;
  /** True only until the first /api/entitlements response; bootstrap modules apply solely in this window. */
  bootstrapActive: boolean;
  error: string | null;
  version: string | null;
  lifecycle: EntitlementLifecycle | null;
  allowedTransitions: string[];
  canModule: (moduleKey: ModuleKey) => boolean;
  canWrite: (moduleKey: ModuleKey) => boolean;
  canFeature: (feature: string) => boolean;
  canCreate: (resource: MeteredResource) => boolean;
  canManageBilling: boolean;
  reload: () => Promise<Entitlements | null>;
  /** After Checkout/Portal return: poll every 5s for up to 2 min; stop when version or effectivePlan changes. */
  startBillingReturnPoll: () => void;
}

const BILLING_RETURN_POLL_MS = 5_000;
const BILLING_RETURN_POLL_MAX_MS = 120_000;

const CapabilitiesContext = createContext<Capabilities | null>(null);

export interface CapabilitiesProviderProps {
  bootstrapModules?: ModuleKey[];
  hasPermission: (permission: string | string[]) => boolean;
  /** Workspace slug — changing it resets bootstrap and refetches. */
  orgSlug: string;
  children: React.ReactNode;
}

/**
 * Single SPA capabilities source backed by GET /api/entitlements.
 * Bootstrap modules seed canModule/canWrite only while no snapshot has loaded for this org;
 * once a snapshot lands it owns everything (a later failed reload keeps the last good snapshot).
 */
export const CapabilitiesProvider: React.FC<CapabilitiesProviderProps> = ({
  bootstrapModules,
  hasPermission,
  orgSlug,
  children,
}) => {
  const { authFetch } = useAuth();
  const [entitlements, setEntitlements] = useState<Entitlements | null>(null);
  const [ready, setReady] = useState(false);
  const [bootstrapActive, setBootstrapActive] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const entitlementsRef = useRef(entitlements);
  entitlementsRef.current = entitlements;
  const pollTimerRef = useRef<number | null>(null);
  const bootstrapRef = useRef(bootstrapModules);
  bootstrapRef.current = bootstrapModules;

  const reload = useCallback(async (): Promise<Entitlements | null> => {
    const requestOrg = orgSlug;
    try {
      const response = await authFetch('/api/entitlements');
      if (!response.ok) throw new Error(`Could not load plan usage (HTTP ${response.status}).`);
      const normalized = normalizeEntitlementsPayload(await response.json());
      // Ignore late responses after a workspace switch.
      if (requestOrg !== getOrgSlugFromLocation()) return entitlementsRef.current;
      setEntitlements(normalized);
      setBootstrapActive(false);
      setReady(true);
      setError(null);
      const headerVersion = response.headers.get('X-AccountGo-Entitlements-Version');
      adoptEntitlementsVersion(requestOrg, normalized.version ?? headerVersion);
      return normalized;
    } catch (err: unknown) {
      if (requestOrg !== getOrgSlugFromLocation()) return entitlementsRef.current;
      setError(err instanceof Error ? err.message : 'Could not load plan usage.');
      setBootstrapActive(false);
      setReady(true);
      // Keep last good snapshot; only "never loaded for this org" stays null and fails open via bootstrap.
      return entitlementsRef.current;
    }
  }, [authFetch, orgSlug]);

  useEffect(() => {
    setReady(false);
    setBootstrapActive(true);
    setEntitlements(null);
    adoptEntitlementsVersion(orgSlug, null);
    void reload();
    // A denial means the numbers on screen are stale.
    const onDenied = () => void reload();
    window.addEventListener(ENTITLEMENT_DENIED_EVENT, onDenied);
    return () => window.removeEventListener(ENTITLEMENT_DENIED_EVENT, onDenied);
  }, [reload, orgSlug]);

  useEffect(() => {
    registerCapabilitiesReloader(async (slug) => {
      if (slug !== getOrgSlugFromLocation()) return entitlementsRef.current?.version ?? null;
      const next = await reload();
      return next?.version ?? null;
    });
    return () => registerCapabilitiesReloader(null);
  }, [reload]);

  const stopBillingReturnPoll = useCallback(() => {
    if (pollTimerRef.current != null) {
      window.clearInterval(pollTimerRef.current);
      pollTimerRef.current = null;
    }
  }, []);

  const startBillingReturnPoll = useCallback(() => {
    stopBillingReturnPoll();
    const started = Date.now();
    const versionBefore = entitlementsRef.current?.version ?? null;
    const planBefore = entitlementsRef.current?.effectivePlan ?? entitlementsRef.current?.plan ?? null;
    pollTimerRef.current = window.setInterval(() => {
      void reload().then((next) => {
        const versionChanged = next != null && next.version != null && next.version !== versionBefore;
        const planChanged = next != null
          && (next.effectivePlan ?? next.plan) != null
          && (next.effectivePlan ?? next.plan) !== planBefore;
        if (versionChanged || planChanged || Date.now() - started >= BILLING_RETURN_POLL_MAX_MS) {
          stopBillingReturnPoll();
        }
      });
    }, BILLING_RETURN_POLL_MS);
  }, [reload, stopBillingReturnPoll]);

  useEffect(() => () => stopBillingReturnPoll(), [stopBillingReturnPoll]);

  const value = useMemo<Capabilities>(() => {
    const bootstrap = bootstrapRef.current;
    // Snapshot owns everything when present; null snapshot (loading or never loaded) fails open on bootstrap.
    const canModule = (moduleKey: ModuleKey) => evaluateCanModule(entitlements, moduleKey, bootstrap);
    const canWrite = (moduleKey: ModuleKey) => evaluateCanWrite(entitlements, moduleKey, bootstrap);
    const canFeature = (feature: string) => evaluateCanFeature(entitlements, feature);
    const canCreate = (resource: MeteredResource) => evaluateCanCreate(entitlements, resource);
    return {
      entitlements,
      ready,
      bootstrapActive,
      error,
      version: entitlements?.version ?? null,
      lifecycle: entitlements?.lifecycle ?? null,
      allowedTransitions: entitlements?.allowedTransitions ?? [],
      canModule,
      canWrite,
      canFeature,
      canCreate,
      canManageBilling: hasPermission('admin.subscription.manage'),
      reload,
      startBillingReturnPoll,
    };
  }, [
    entitlements, ready, bootstrapActive, error, hasPermission, reload, startBillingReturnPoll,
  ]);

  return React.createElement(CapabilitiesContext.Provider, { value }, children);
};

/** Single capabilities hook — must be used under CapabilitiesProvider (OrgLayout). */
export function useCapabilities(): Capabilities {
  const ctx = useContext(CapabilitiesContext);
  if (!ctx) {
    throw new Error('useCapabilities requires CapabilitiesProvider');
  }
  return ctx;
}

/** Snapshot-only convenience for pages that already sit under OrgLayout. */
export function useEntitlements() {
  const caps = useCapabilities();
  return { entitlements: caps.entitlements, error: caps.error, reload: caps.reload };
}

export { observeEntitlementsVersionHeader, ENTITLEMENTS_VERSION_HEADER } from './entitlementVersion';
