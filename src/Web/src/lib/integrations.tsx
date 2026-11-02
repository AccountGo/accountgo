import React, { createContext, useContext, useEffect, useState } from 'react';
import { useAuth } from '../auth/AuthContext';

export interface IntegrationCapabilities {
  companyId: number;
  canManage: boolean;
  canReview: boolean;
}

export interface IncomingSummary {
  companyId: number;
  pendingCount: number;
  purchasePendingCount: number;
  unreadCount: number;
  latestLineId: number;
}

export interface Connection {
  id: string;
  provider: string;
  environment: string;
  label: string;
  status: string;
  bankId: number;
  bankAccountName: string;
  currency: string;
  providerAccountRef: string | null;
  providerSubscriptionRef: string | null;
  lastDeliveryAt: string | null;
  revision: string;
}

export interface Delivery {
  id: string;
  receivedAt: string;
  eventType: string | null;
  verification: string;
  status: string;
  attempts: number;
  lastError: string | null;
  isTest: boolean;
  transferActivity: {
    transferId: string;
    profileId: string;
    occurredAt: string | null;
    currentState: string | null;
    previousState: string | null;
    activeCases: string[];
  } | null;
}

export interface SetupIdentity {
  deliveryId: string;
  accountReference: string;
  subscriptionReference: string;
  currency: string | null;
}

export interface IncomingItem {
  lineId: number;
  bankId: number;
  bankName: string;
  currency: string;
  postedDate: string;
  amount: number;
  payee: string | null;
  memo: string | null;
  reference: string | null;
  externalId: string | null;
  draftBillId: number | null;
  matchedGeneralLedgerLineId: number | null;
  state: 'needs_review' | 'draft_linked' | 'matched' | 'excluded';
  source: { provider: string; connectionId: string; label: string; providerAccountRef: string | null } | null;
}

const CHANGED = 'accountgo:incoming-review-changed';
export const refreshIncomingReview = () => window.dispatchEvent(new Event(CHANGED));

const IntegrationContext = createContext<{
  capabilities: IntegrationCapabilities | null;
  summary: IncomingSummary | null;
  error: string | null;
}>({ capabilities: null, summary: null, error: null });

export const useIntegrations = () => useContext(IntegrationContext);

export function IntegrationsProvider({ companyId, canView, canManage, children }: {
  companyId: string; canView: boolean; canManage: boolean; children: React.ReactNode;
}) {
  const { authFetch } = useAuth();
  const [capabilities, setCapabilities] = useState<IntegrationCapabilities | null>(null);
  const [summary, setSummary] = useState<IncomingSummary | null>(null);
  const [error, setError] = useState<string | null>(null);
  useEffect(() => {
    let active = true;
    let reviewAllowed = false;
    let loading = false;
    const abort = new AbortController();
    const loadSummary = async () => {
      if (!reviewAllowed || loading || document.visibilityState === 'hidden') return;
      loading = true;
      try {
        const res = await authFetch('/api/integrations/review/pending-count', { signal: abort.signal });
        if (!res.ok) throw new Error('Incoming activity could not be refreshed.');
        const data = await res.json() as IncomingSummary;
        if (String(data.companyId) !== companyId) throw new Error('Incoming activity workspace mismatch.');
        if (active) { setSummary(data); setError(null); }
      } catch {
        if (active) { setSummary(null); setError('Incoming activity is unavailable. Refresh to try again.'); }
      } finally { loading = false; }
    };
    void (async () => {
      if (!canView && !canManage) return;
      try {
        const res = await authFetch('/api/integrations/capabilities', { signal: abort.signal });
        if (res.status === 404 || res.status === 403) return;
        if (!res.ok) throw new Error();
        const data = await res.json() as IntegrationCapabilities;
        if (!active || String(data.companyId) !== companyId) return;
        reviewAllowed = canView && data.canReview;
        setCapabilities({ ...data, canReview: reviewAllowed, canManage: canManage && data.canManage });
        await loadSummary();
      } catch {
        if (active) setError('Integrations could not be loaded.');
      }
    })();
    const interval = window.setInterval(() => void loadSummary(), 30_000);
    window.addEventListener('focus', loadSummary);
    document.addEventListener('visibilitychange', loadSummary);
    window.addEventListener(CHANGED, loadSummary);
    return () => {
      active = false;
      abort.abort();
      window.clearInterval(interval);
      window.removeEventListener('focus', loadSummary);
      document.removeEventListener('visibilitychange', loadSummary);
      window.removeEventListener(CHANGED, loadSummary);
    };
  }, [authFetch, companyId, canView, canManage]);
  return <IntegrationContext.Provider value={{ capabilities, summary, error }}>{children}</IntegrationContext.Provider>;
}
