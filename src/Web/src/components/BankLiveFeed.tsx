import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { useAuth } from '../auth/AuthContext';
import { useIntegrations, type Connection } from '../lib/integrations';

export function BankLiveFeed({ orgSlug, bankId }: { orgSlug: string; bankId: number }) {
  const { capabilities } = useIntegrations();
  const { authFetch } = useAuth();
  const [connections, setConnections] = useState<Connection[]>([]);
  const [error, setError] = useState(false);
  useEffect(() => {
    let active = true;
    setConnections([]); setError(false);
    if (!capabilities?.canManage) return;
    void authFetch('/api/integrations/connections').then(async res => {
      if (!res.ok) throw new Error();
      const data = await res.json() as Connection[];
      if (active) setConnections(data.filter(c => c.bankId === bankId));
    }).catch(() => { if (active) setError(true); });
    return () => { active = false; };
  }, [authFetch, bankId, capabilities?.canManage]);
  if (!capabilities?.canManage && !capabilities?.canReview) return null;
  return <section className="rounded-xl border border-[var(--border-subtle)] p-4 space-y-2">
    <h2 className="font-semibold text-sm">Live feed</h2>
    {error && <p role="alert" className="text-sm">Feed status is unavailable. Open Integrations to check the connection.</p>}
    {connections.map(c => <Link className="block text-sm underline" key={c.id}
      to={`/org/${orgSlug}/settings?tab=integrations&connection=${c.id}`}>{c.label}: {c.status.replaceAll('_', ' ')}</Link>)}
    {capabilities.canManage && <Link className="block text-sm underline" to={`/org/${orgSlug}/settings?tab=integrations&bankId=${bankId}`}>Manage live feeds</Link>}
    {capabilities.canReview && <Link className="block text-sm underline" to={`/org/${orgSlug}/banking/needs-review?bankId=${bankId}`}>Review incoming transactions</Link>}
  </section>;
}
