import { useEffect, useState } from 'react';
import { useAuth } from '../auth/AuthContext';
import { useIntegrations, type IncomingItem } from '../lib/integrations';

export function IncomingProvenance({ companyId, bankId, lineId }: { companyId: string; bankId: number; lineId: number }) {
  const { capabilities } = useIntegrations();
  const { authFetch } = useAuth();
  const [item, setItem] = useState<IncomingItem | null>(null);
  useEffect(() => {
    let active = true;
    setItem(null);
    if (!capabilities?.canReview) return;
    void authFetch(`/api/integrations/review/items?bankId=${bankId}&lineId=${lineId}&state=all`).then(async res => {
      if (!res.ok) return;
      const data = await res.json();
      if (active && String(data.companyId) === companyId) setItem(data.items[0] || null);
    }).catch(() => {});
    return () => { active = false; };
  }, [authFetch, capabilities?.canReview, companyId, bankId, lineId]);
  if (!item) return null;
  return <section className="rounded-xl border border-[var(--border-subtle)] p-4 text-sm space-y-2" aria-label="Incoming transaction source">
    <h2 className="font-semibold">Source: {item.source?.provider || 'Webhook feed'}</h2>
    <p>{item.source?.label} {item.source?.providerAccountRef}</p>
    <p>Payee: {item.payee || 'Not supplied by provider'}. Reference: {item.reference || 'Not supplied by provider'}.</p>
    <p>Review state: {item.state.replaceAll('_', ' ')}. Technical processing is not accounting approval.</p>
  </section>;
}
