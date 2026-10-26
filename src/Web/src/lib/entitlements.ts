import { useCallback, useEffect, useState } from 'react';
import { useAuth } from '../auth/AuthContext';
import type { ModuleKey, PlanKey } from '../types';
import { ENTITLEMENT_DENIED_EVENT } from './entitlementEvents';

export {
  ENTITLEMENT_CODES, ENTITLEMENT_DENIED_EVENT, announceEntitlementDenial, isEntitlementProblem,
  type EntitlementCode, type EntitlementProblem,
} from './entitlementEvents';

export type MeteredResource = 'salesDocuments' | 'bills' | 'seats' | 'bankAccounts';

export interface ResourceLimit {
  /** null = unlimited */
  limit: number | null;
  used: number;
  period: 'month' | null;
}

/** GET /api/entitlements — what the active workspace's plan includes and how much is used. */
export interface Entitlements {
  enabled: boolean;
  plan: PlanKey;
  tier: 'free' | 'pro' | 'comp' | string;
  limited: boolean;
  modules: ModuleKey[];
  selfServicePlans: PlanKey[];
  limits: Partial<Record<MeteredResource, ResourceLimit>>;
  upgrade: { available: boolean; contact: string | null };
}

/** Human label for a metered resource, e.g. in usage hints. */
export const RESOURCE_LABELS: Record<MeteredResource, string> = {
  salesDocuments: 'invoices & quotes',
  bills: 'bills',
  seats: 'users',
  bankAccounts: 'bank & cash accounts',
};

export function useEntitlements() {
  const { authFetch } = useAuth();
  const [entitlements, setEntitlements] = useState<Entitlements | null>(null);
  const [error, setError] = useState<string | null>(null);

  const reload = useCallback(async () => {
    try {
      const response = await authFetch('/api/entitlements');
      if (!response.ok) throw new Error(`Could not load plan usage (HTTP ${response.status}).`);
      setEntitlements((await response.json()) as Entitlements);
      setError(null);
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'Could not load plan usage.');
    }
  }, [authFetch]);

  useEffect(() => {
    void reload();
    // A denial means the numbers on screen are stale.
    const onDenied = () => void reload();
    window.addEventListener(ENTITLEMENT_DENIED_EVENT, onDenied);
    return () => window.removeEventListener(ENTITLEMENT_DENIED_EVENT, onDenied);
  }, [reload]);

  return { entitlements, error, reload };
}
