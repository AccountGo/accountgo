import { useCallback, useEffect, useState } from 'react';
import { useAuth } from '../auth/AuthContext';
import type { PlanKey } from '../types';

/** Billing-only state. Limits / features / usage live on GET /api/entitlements via useCapabilities. */
export interface BillingState {
  billingEnabled: boolean;
  plan: PlanKey | string;
  tier: string;
  status: string;
  currentPeriodEnd: string | null;
  cancelAtPeriodEnd: boolean;
  portalAvailable: boolean;
}

/** What a paid-to-paid change will charge; shown before the owner confirms. Amounts in major units. */
export interface PlanChangePreview {
  planName: string;
  amountDueNow: number;
  currency: string;
  recurringAmount: number;
  interval: string;
  nextBillingDate: string | null;
  paymentMethod: string | null;
}

export type UpgradeOutcome =
  | { kind: 'confirm'; planKey: string; billingInterval: 'month' | 'year'; preview: PlanChangePreview }
  | { kind: 'redirected' }
  | { kind: 'applied' };

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
      setBilling({
        billingEnabled: !!raw.billingEnabled,
        plan: raw.plan,
        tier: raw.tier,
        status: raw.status,
        currentPeriodEnd: raw.currentPeriodEnd ?? null,
        cancelAtPeriodEnd: !!raw.cancelAtPeriodEnd,
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

  const upgradePlan = useCallback(async (
    planKey: string,
    billingInterval: 'month' | 'year' = 'month',
    options?: { confirmed?: boolean },
  ): Promise<UpgradeOutcome> => {
    const returnUrl = window.location.href;
    const response = await authFetch('/api/Subscription/update', {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ plan: planKey, billingInterval, returnUrl, confirmed: options?.confirmed === true }),
    });
    if (!response.ok) {
      let message = `Unable to start upgrade (HTTP ${response.status}).`;
      try {
        const body = await response.json();
        if (typeof body?.message === 'string' && body.message.trim()) message = body.message;
        else if (typeof body?.detail === 'string' && body.detail.trim()) message = body.detail;
        else if (typeof body?.title === 'string' && body.title.trim()) message = body.title;
      } catch { /* keep default */ }
      throw new Error(message);
    }
    const body = await response.json() as { redirectUrl?: string | null; confirmationRequired?: boolean; preview?: PlanChangePreview };
    // A change that would charge a saved card answers with the charge first; nothing is charged yet.
    if (body.confirmationRequired && body.preview) {
      return { kind: 'confirm', planKey, billingInterval, preview: body.preview };
    }
    if (body.redirectUrl) {
      window.location.assign(body.redirectUrl);
      return { kind: 'redirected' };
    }
    await reload();
    return { kind: 'applied' };
  }, [authFetch, reload]);

  const isPastDue = billing?.status === 'past_due';

  return {
    billing,
    billingEnabled: billing?.billingEnabled ?? false,
    plan: billing?.plan,
    status: billing?.status,
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
