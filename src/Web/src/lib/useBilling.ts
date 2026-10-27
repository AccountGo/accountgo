import { useCallback, useEffect, useState } from 'react';
import { useAuth } from '../auth/AuthContext';
import type { PlanKey } from '../types';
import type { MeteredResource, ResourceLimit } from './entitlements';

export interface BillingState {
  billingEnabled: boolean;
  plan: PlanKey | string;
  tier: string;
  status: string;
  currentPeriodEnd: string | null;
  cancelAtPeriodEnd: boolean;
  limits: Partial<Record<MeteredResource, ResourceLimit>>;
  features: string[];
  portalAvailable: boolean;
}

export function useBilling() {
  const { authFetch } = useAuth();
  const [billing, setBilling] = useState<BillingState | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);

  const reload = useCallback(async () => {
    try {
      const response = await authFetch('/api/billing/state');
      if (!response.ok) throw new Error(`Could not load billing state (HTTP ${response.status}).`);
      const raw = await response.json();
      const limits: BillingState['limits'] = {};
      if (raw.limits && typeof raw.limits === 'object') {
        for (const [key, value] of Object.entries(raw.limits as Record<string, { limit?: number | null; used?: number; period?: string | null }>)) {
          limits[key as MeteredResource] = {
            limit: value?.limit ?? null,
            used: value?.used ?? 0,
            period: value?.period === 'month' ? 'month' : null,
          };
        }
      }
      setBilling({
        billingEnabled: !!raw.billingEnabled,
        plan: raw.plan,
        tier: raw.tier,
        status: raw.status,
        currentPeriodEnd: raw.currentPeriodEnd ?? null,
        cancelAtPeriodEnd: !!raw.cancelAtPeriodEnd,
        limits,
        features: Array.isArray(raw.features) ? raw.features : [],
        portalAvailable: !!raw.portalAvailable,
      });
      setError(null);
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'Could not load billing state.');
    } finally {
      setLoading(false);
    }
  }, [authFetch]);

  useEffect(() => {
    void reload();
  }, [reload]);

  const openPortal = useCallback(async () => {
    const returnUrl = window.location.href;
    const response = await authFetch('/api/billing/portal', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ returnUrl }),
    });
    if (!response.ok) {
      let message = `Unable to open billing portal (HTTP ${response.status}).`;
      try {
        const body = await response.json();
        if (body?.message) message = body.message;
      } catch { /* keep default */ }
      throw new Error(message);
    }
    const body = await response.json() as { url?: string };
    if (!body.url) throw new Error('Billing portal URL was empty.');
    window.location.assign(body.url);
  }, [authFetch]);

  const upgradePlan = useCallback(async (planKey: string, billingInterval: 'month' | 'year' = 'month') => {
    const response = await authFetch('/api/Subscription/update', {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ plan: planKey, billingInterval }),
    });
    if (!response.ok) {
      let message = `Unable to start upgrade (HTTP ${response.status}).`;
      try {
        const body = await response.json();
        if (body?.message) message = body.message;
      } catch { /* keep default */ }
      throw new Error(message);
    }
    const body = await response.json() as { redirectUrl?: string };
    if (body.redirectUrl) {
      window.location.assign(body.redirectUrl);
      return { redirected: true as const };
    }
    await reload();
    return { redirected: false as const };
  }, [authFetch, reload]);

  const isPastDue = billing?.status === 'past_due';

  return {
    billing,
    billingEnabled: billing?.billingEnabled ?? false,
    plan: billing?.plan,
    status: billing?.status,
    limits: billing?.limits,
    usage: billing?.limits,
    isPastDue,
    portalAvailable: billing?.portalAvailable ?? false,
    currentPeriodEnd: billing?.currentPeriodEnd,
    cancelAtPeriodEnd: billing?.cancelAtPeriodEnd ?? false,
    loading,
    error,
    reload,
    openPortal,
    upgradePlan,
  };
}
