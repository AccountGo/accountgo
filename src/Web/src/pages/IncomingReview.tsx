import { useEffect, useState } from 'react';
import { Link, useOutletContext, useSearchParams } from 'react-router-dom';
import { BankingTabs } from '../components/BankingTabs';
import { useAuth } from '../auth/AuthContext';
import { useIntegrations, type IncomingItem } from '../lib/integrations';
import type { TenantContextType } from '../types';

interface ReviewPage { companyId: number; items: IncomingItem[]; totalCount: number; page: number; pageSize: number }
interface Options { companyId: number; connections: { id: string; label: string; provider: string }[]; banks: { bankId: number; bankName: string }[] }
const input = 'rounded-lg border border-[var(--border-subtle)] bg-[var(--bg-subtle)] p-2 text-sm';

export default function IncomingReview() {
  const { currentOrg } = useOutletContext<TenantContextType>();
  const { capabilities } = useIntegrations();
  const { authFetch } = useAuth();
  const [params, setParams] = useSearchParams();
  const [data, setData] = useState<ReviewPage | null>(null);
  const [options, setOptions] = useState<Options | null>(null);
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(true);
  const query = params.toString();
  useEffect(() => {
    if (!capabilities?.canReview) return;
    let active = true;
    const abort = new AbortController();
    setData(null); setLoading(true); setError('');
    void (async () => {
      try {
        const [itemsRes, optionsRes] = await Promise.all([
          authFetch(`/api/integrations/review/items?${query}`, { signal: abort.signal }),
          authFetch('/api/integrations/review/options', { signal: abort.signal }),
        ]);
        if (!itemsRes.ok || !optionsRes.ok) throw new Error('The review queue could not be loaded. Check access and refresh.');
        const items = await itemsRes.json() as ReviewPage;
        const nextOptions = await optionsRes.json() as Options;
        if (String(items.companyId) !== currentOrg.id || String(nextOptions.companyId) !== currentOrg.id)
          throw new Error('Review queue workspace mismatch.');
        if (active) { setData(items); setOptions(nextOptions); }
      } catch (err) { if (active) setError(err instanceof Error ? err.message : 'Could not load review items.'); }
      finally { if (active) setLoading(false); }
    })();
    return () => { active = false; abort.abort(); };
  }, [authFetch, capabilities?.canReview, currentOrg.id, query]);
  const filter = (key: string, value: string) => {
    const next = new URLSearchParams(params);
    if (value) next.set(key, value); else next.delete(key);
    if (key !== 'page') next.delete('page');
    setParams(next);
  };
  if (!capabilities?.canReview) return <p role="alert">Incoming review is unavailable or not permitted.</p>;
  return <div className="space-y-5">
    <h1 className="text-xl font-bold">Needs review</h1>
    <p className="text-sm text-[var(--text-secondary)]">Incoming transactions are unmatched statement lines, not bills or payments. Open an item to inspect suggestions, supply missing details and explicitly choose an accounting action, or leave it for later.</p>
    <BankingTabs active="review" orgSlug={currentOrg.slug} />
    <div className="flex flex-wrap gap-3">
      <label className="text-sm">Bank account<select className={`block ${input}`} value={params.get('bankId') || ''} onChange={e => filter('bankId', e.target.value)}>
        <option value="">All accounts</option>{options?.banks.map(b => <option key={b.bankId} value={b.bankId}>{b.bankName}</option>)}
      </select></label>
      <label className="text-sm">Provider<select className={`block ${input}`} value={params.get('provider') || ''} onChange={e => filter('provider', e.target.value)}>
        <option value="">All providers</option>{Array.from(new Set(options?.connections.map(c => c.provider) || [])).map(p => <option key={p}>{p}</option>)}
      </select></label>
      <label className="text-sm">Connection<select className={`block ${input}`} value={params.get('connectionId') || ''} onChange={e => filter('connectionId', e.target.value)}>
        <option value="">All connections</option>{options?.connections.map(c => <option key={c.id} value={c.id}>{c.label}</option>)}
      </select></label>
      <label className="text-sm">Review state<select className={`block ${input}`} value={params.get('state') || 'needs_review'} onChange={e => filter('state', e.target.value)}>
        <option value="needs_review">Needs review (including drafts)</option><option value="matched">Recorded / matched</option><option value="excluded">Excluded</option><option value="all">All states</option>
      </select></label>
      <label className="text-sm">Direction<select className={`block ${input}`} value={params.get('direction') || ''} onChange={e => filter('direction', e.target.value)}>
        <option value="">Money in and out</option><option value="out">Money out / purchase candidates</option><option value="in">Money in</option>
      </select></label>
      <label className="text-sm">From<input className={`block ${input}`} type="date" value={params.get('from') || ''} onChange={e => filter('from', e.target.value)} /></label>
      <label className="text-sm">To<input className={`block ${input}`} type="date" value={params.get('to') || ''} onChange={e => filter('to', e.target.value)} /></label>
    </div>
    {error && <p role="alert" className="text-sm text-rose-500">{error}</p>}
    {loading && <p role="status">Loading review items...</p>}
    {data && <><p role="status" className="text-sm">{data.totalCount} transaction{data.totalCount === 1 ? '' : 's'}</p>
      <div className="overflow-x-auto rounded-xl border border-[var(--border-subtle)]"><table className="w-full text-sm">
        <thead><tr className="text-left"><th className="p-3">Date / source</th><th className="p-3">Bank / details</th><th className="p-3">Amount</th><th className="p-3">Accounting review</th></tr></thead>
        <tbody>{data.items.map(item => <tr key={item.lineId} className="border-t border-[var(--border-subtle)]">
          <td className="p-3">{item.postedDate.slice(0, 10)}<p>{item.source ? `${item.source.provider} / ${item.source.label}` : 'Webhook feed'}</p><p className="text-xs">{item.source?.providerAccountRef}</p></td>
          <td className="p-3">{item.bankName}<p>Payee: {item.payee || 'Not supplied by provider'}</p><p>{item.reference || item.memo || 'Reference / description not supplied'}</p></td>
          <td className="p-3 font-mono">{new Intl.NumberFormat(undefined, { style: 'currency', currency: item.currency }).format(item.amount)}</td>
          <td className="p-3"><p>{item.state.replaceAll('_', ' ')}</p>
            <Link className="text-[var(--brand-primary)] underline" to={`/org/${currentOrg.slug}/banking/${item.bankId}/statement-import?lineId=${item.lineId}&status=all`}>Review transaction {item.lineId}</Link>
            {item.draftBillId && <Link className="block underline" to={`/org/${currentOrg.slug}/purchases/${item.draftBillId}`}>Open linked bill</Link>}
          </td>
        </tr>)}</tbody></table></div>
      {data.items.length === 0 && <p>No transactions in this view. Pending items remain available in the other filters.</p>}
      <div className="flex gap-3">
        <button className={input} type="button" disabled={data.page <= 1} onClick={() => filter('page', String(data.page - 1))}>Previous</button>
        <button className={input} type="button" disabled={data.page * data.pageSize >= data.totalCount} onClick={() => filter('page', String(data.page + 1))}>Next</button>
      </div>
    </>}
  </div>;
}
