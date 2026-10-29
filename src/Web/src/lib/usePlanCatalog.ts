import { useEffect, useState } from 'react';
import { useAuth } from '../auth/AuthContext';
import type { ModuleKey, PlanKey } from '../types';

/** One plan as served by GET /api/Subscription/plans — the server is the only source of the plan → modules map. */
export interface PlanCatalogEntry {
  key: PlanKey;
  name: string;
  description: string;
  includedModules: ModuleKey[];
  requiredModules: ModuleKey[];
  /** False when the operator assigns this plan (e.g. Enterprise on hosted). */
  selfService: boolean;
  /** Null when the plan has no price; never defaulted client-side. */
  monthlyPrice: number | null;
}

let cached: PlanCatalogEntry[] | null = null;
let inflight: Promise<PlanCatalogEntry[]> | null = null;

/** Seeds the shared catalog (tests and server rendering, where effects don't run). */
export function primePlanCatalog(plans: PlanCatalogEntry[] | null) {
  cached = plans;
  inflight = null;
}

/** Loads the plan catalog once per page load and shares it between components. */
export function usePlanCatalog() {
  const { authFetch } = useAuth();
  const [plans, setPlans] = useState<PlanCatalogEntry[] | null>(cached);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (cached) return;
    let mounted = true;
    inflight ??= authFetch('/api/Subscription/plans').then(async (response) => {
      if (!response.ok) throw new Error(`Could not load plans (HTTP ${response.status}).`);
      const data = (await response.json()) as PlanCatalogEntry[];
      cached = data;
      return data;
    });
    inflight
      .then((data) => {
        if (mounted) setPlans(data);
      })
      .catch((err: unknown) => {
        inflight = null; // allow a retry on next mount
        if (mounted) setError(err instanceof Error ? err.message : 'Could not load plans.');
      });
    return () => {
      mounted = false;
    };
  }, [authFetch]);

  const find = (key: PlanKey) => plans?.find((plan) => plan.key === key);
  return {
    plans,
    error,
    loading: !plans && !error,
    includedModules: (key: PlanKey): ModuleKey[] => find(key)?.includedModules ?? [],
    isIncluded: (key: PlanKey, moduleKey: ModuleKey) => find(key)?.includedModules.includes(moduleKey) ?? false,
    isRequired: (key: PlanKey, moduleKey: ModuleKey) => find(key)?.requiredModules.includes(moduleKey) ?? false,
  };
}
