import React, { useCallback, useEffect, useState } from 'react';
import { Link, useOutletContext, useSearchParams } from 'react-router-dom';
import { useAuth } from '../auth/AuthContext';
import { useIntegrations, type Connection, type Delivery, type SetupIdentity } from '../lib/integrations';
import type { TenantContextType } from '../types';
import type { BankAccount } from '../types/banking';
import { ConfirmDialog } from './ConfirmDialog';

const input = 'w-full rounded-xl border border-[var(--border-subtle)] bg-[var(--bg-subtle)] p-2 text-sm';
const button = 'rounded-xl border border-[var(--border-subtle)] px-3 py-2 text-sm font-semibold disabled:opacity-50';
interface Provider { slug: string; displayName: string; category: string; description: string; isAvailable: boolean; comingSoonNotice: string | null }

export function IntegrationsSettingsTab() {
  const { currentOrg } = useOutletContext<TenantContextType>();
  const { capabilities } = useIntegrations();
  const { authFetch } = useAuth();
  const [params] = useSearchParams();
  const [providers, setProviders] = useState<Provider[]>([]);
  const [connections, setConnections] = useState<Connection[]>([]);
  const [banks, setBanks] = useState<BankAccount[]>([]);
  const [selectedId, setSelectedId] = useState(params.get('connection') || '');
  const [deliveries, setDeliveries] = useState<Delivery[]>([]);
  const [deliveryPage, setDeliveryPage] = useState(1);
  const [revision, setRevision] = useState(0);
  const [provider, setProvider] = useState('');
  const [label, setLabel] = useState('');
  const [connectionName, setConnectionName] = useState('');
  const [environment, setEnvironment] = useState('sandbox');
  const [bankId, setBankId] = useState(params.get('bankId') || '');
  const [profile, setProfile] = useState('');
  const [balance, setBalance] = useState('');
  const [balanceCurrency, setBalanceCurrency] = useState('');
  const [subscription, setSubscription] = useState('');
  const [setupIdentity, setSetupIdentity] = useState<SetupIdentity | null>(null);
  const [confirmed, setConfirmed] = useState(false);
  const [oneTimeUrl, setOneTimeUrl] = useState('');
  const [grace, setGrace] = useState('');
  const [busy, setBusy] = useState(false);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [notice, setNotice] = useState('');
  const [confirm, setConfirm] = useState<{ action: string; title: string; message: string } | null>(null);
  const selected = connections.find(c => c.id === selectedId);
  const associatedBank = banks.find(b => b.id === Number(bankId));

  useEffect(() => {
    setBalanceCurrency(associatedBank?.currencyCode || '');
  }, [associatedBank?.id, associatedBank?.currencyCode]);

  useEffect(() => {
    setConnectionName(selected?.label || '');
  }, [selected?.id, selected?.label]);

  const request = useCallback(async (url: string, init?: RequestInit) => {
    const res = await authFetch(url, init);
    const data = await res.json().catch(() => null);
    if (!res.ok) throw new Error(data?.message || 'The integration request failed. Refresh and try again.');
    return data;
  }, [authFetch]);

  useEffect(() => {
    if (!capabilities?.canManage) return;
    let active = true;
    const abort = new AbortController();
    setLoading(true);
    void Promise.all([
      request('/api/integrations/providers', { signal: abort.signal }),
      request('/api/integrations/connections', { signal: abort.signal }),
      request('/api/Banking/Accounts', { signal: abort.signal }),
    ]).then(([nextProviders, nextConnections, nextBanks]) => {
      if (!active) return;
      setProviders(nextProviders); setConnections(nextConnections);
      setBanks((nextBanks.accounts || []).filter((b: BankAccount) => !b.archivedAt));
    }).catch(err => { if (active) setError(err.message); })
      .finally(() => { if (active) setLoading(false); });
    return () => { active = false; abort.abort(); };
  }, [capabilities?.canManage, currentOrg.id, request, revision]);

  useEffect(() => {
    setDeliveries([]);
    setSetupIdentity(null);
    setConfirmed(false);
    if (!selectedId || !capabilities?.canManage) return;
    let active = true;
    const abort = new AbortController();
    void Promise.all([
      request(`/api/integrations/connections/${encodeURIComponent(selectedId)}/deliveries?page=${deliveryPage}&pageSize=20`, { signal: abort.signal }),
      request(`/api/integrations/connections/${encodeURIComponent(selectedId)}/setup-identity`, { signal: abort.signal }),
    ]).then(([data, evidence]) => { if (active) { setDeliveries(data); setSetupIdentity(evidence.identity); } })
      .catch(err => { if (active) setError(err.message); });
    return () => { active = false; abort.abort(); };
  }, [selectedId, deliveryPage, capabilities?.canManage, currentOrg.id, request, revision]);

  if (!capabilities?.canManage) return <p role="alert">Integration management is unavailable or not permitted.</p>;

  const run = async (action: () => Promise<void>) => {
    if (busy) return;
    setBusy(true); setError(''); setNotice('');
    try { await action(); setRevision(r => r + 1); }
    catch (err) { setError(err instanceof Error ? err.message : 'The action failed. Refresh before retrying.'); }
    finally { setBusy(false); setConfirm(null); }
  };
  const create = (event: React.FormEvent) => {
    event.preventDefault();
    void run(async () => {
      const bank = banks.find(b => b.id === Number(bankId));
      if (!bank)
        throw new Error('Select a workspace bank account.');
      if (!bank.currencyCode)
        throw new Error('Set a currency on the associated workspace bank account before connecting Wise.');
      if (balanceCurrency.toUpperCase() !== bank.currencyCode)
        throw new Error('The bank account currency must match the associated bank account.');
      if (Boolean(profile) !== Boolean(balance))
        throw new Error('Enter both existing balance-update identifiers, or leave both empty.');
      const data = await request('/api/integrations/connections', {
        method: 'POST', headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ provider, label, bankId: Number(bankId), environment, setupOnly: true,
          providerAccountRef: profile && balance ? `profile:${profile}:balance:${balance}` : undefined,
          balanceCurrency: balanceCurrency.toUpperCase() }),
      });
      setOneTimeUrl(data.webhookUrl); setGrace(''); setSelectedId(data.id); setProvider('');
      setSubscription(''); setConfirmed(false); setDeliveryPage(1);
      setNotice('Callback created. Finish the Wise subscription setup below before activating.');
    });
  };
  const lifecycle = (action: string) => {
    if (!selected) return;
    void run(async () => {
      const url = `/api/integrations/connections/${encodeURIComponent(selected.id)}`;
      const data = await request(action === 'delete'
        ? `${url}?revision=${encodeURIComponent(selected.revision)}` : `${url}/${action}`,
      { method: action === 'delete' ? 'DELETE' : 'POST' });
      if (action === 'rotate-key') { setOneTimeUrl(data.webhookUrl); setGrace(data.previousKeyExpiresAt); }
      if (action === 'revoke') { setOneTimeUrl(''); setGrace(''); }
      if (action === 'delete') {
        setConnections(current => current.filter(c => c.id !== selected.id));
        setSelectedId(''); setOneTimeUrl(''); setGrace(''); setDeliveries([]); setSetupIdentity(null);
        setNotice('Revoked connection permanently deleted. Imported transactions, delivery history and audit records were retained.');
        return;
      }
      setNotice(action === 'revoke' ? 'Disconnected. Remove the subscription in Wise separately. Existing review items remain.' : 'Connection updated.');
    });
  };
  return <section className="space-y-5">
    <h2 className="text-lg font-bold">Integrations</h2>
    <p className="text-sm text-[var(--text-secondary)]">Connect systems for banking and reconciliation. These connections never initiate payments.</p>
    {error && <p role="alert" className="text-sm text-rose-500">{error}</p>}
    {notice && <p role="status" className="text-sm">{notice}</p>}
    {loading && <p role="status">Loading integrations...</p>}
    {Array.from(new Set(providers.map(p => p.category))).map(category => <section key={category} className="space-y-3">
      <h3 className="font-semibold">{category}</h3>
      <div className="grid gap-3 sm:grid-cols-2">
        {providers.filter(p => p.category === category).map(p => <article key={p.slug} className="rounded-xl border border-[var(--border-subtle)] p-4 space-y-2">
          <h4 className="font-semibold">{p.displayName}</h4>
          <p className="text-sm">{p.description}</p>
          <button type="button" className={button} disabled={!p.isAvailable || p.slug !== 'wise' || busy}
            onClick={() => { setProvider(p.slug); setLabel(p.displayName); setError(''); }}>
            {p.isAvailable ? `Connect ${p.displayName}` : 'Coming soon'}
          </button>
          {!p.isAvailable && <p className="text-xs text-[var(--text-muted)]">{p.comingSoonNotice}</p>}
        </article>)}
      </div>
    </section>)}
    {provider && <form onSubmit={create} className="rounded-xl border border-[var(--border-subtle)] p-4 space-y-3">
      <h3 className="font-semibold">Connect Wise</h3>
      <p className="text-sm">Receive Wise account deposits, transfer updates or transfer issues. No Wise API token or KMS is required. Deposits are staged for review; transfers are activity only, not proof of a bank debit or payment.</p>
      <label className="block text-sm">Connection label<input className={input} required maxLength={200} value={label} onChange={e => setLabel(e.target.value)} /></label>
      <label className="block text-sm">Wise environment<select className={input} value={environment} onChange={e => setEnvironment(e.target.value)}><option value="sandbox">Sandbox</option><option value="production">Production</option></select></label>
      <label className="block text-sm">Associated bank account<select className={input} required value={bankId} onChange={e => setBankId(e.target.value)}>
        <option value="">Select an account</option>{banks.map(b => <option key={b.id} value={b.id}>
          {[b.name, b.accountNumberMasked].filter(Boolean).join(' - ')} ({b.currencyCode || 'Currency not set'})
        </option>)}
      </select></label>
      <p className="text-xs">Select an existing bank account recorded in this workspace. Account numbers are masked when recorded. <Link className="underline" to={`/org/${currentOrg.slug}/banking`}>Edit workspace bank accounts</Link>.</p>
      <label className="block text-sm">Bank account currency<input className={input} required pattern="[A-Za-z]{3}" maxLength={3} value={balanceCurrency} onChange={e => setBalanceCurrency(e.target.value.toUpperCase())} /></label>
      <p className="text-xs">For transfers, the bank association does not verify a funding account or currency. For an existing balance-update subscription only, optionally enter its identifiers. Otherwise leave both empty; confirm the identity from a verified real notification below.</p>
      <label className="block text-sm">Wise profile ID<input className={input} inputMode="numeric" pattern="[1-9][0-9]*" value={profile} onChange={e => setProfile(e.target.value)} /></label>
      <label className="block text-sm">Wise balance ID<input className={input} inputMode="numeric" pattern="[1-9][0-9]*" value={balance} onChange={e => setBalance(e.target.value)} /></label>
      <button className={button} type="submit" disabled={busy || loading}>Generate callback URL</button>
      <button className={button} type="button" disabled={busy} onClick={() => setProvider('')}>Cancel</button>
    </form>}
    <h3 className="font-semibold">Organization connections</h3>
    {!loading && connections.length === 0 && <p className="text-sm">No connections yet.</p>}
    <div className="flex flex-wrap gap-2">{connections.map(c => <button type="button" className={button} key={c.id}
      aria-pressed={selectedId === c.id} disabled={busy} onClick={() => {
        setSelectedId(c.id); setDeliveryPage(1); setOneTimeUrl(''); setGrace(''); setSubscription(''); setConfirmed(false);
      }}>{c.label} ({c.status.replaceAll('_', ' ')})</button>)}</div>
    {selected && <section className="rounded-xl border border-[var(--border-subtle)] p-4 space-y-4">
      <h3 className="font-semibold">{selected.label}</h3>
      <form className="space-y-2" onSubmit={event => {
        event.preventDefault();
        void run(async () => {
          await request(`/api/integrations/connections/${encodeURIComponent(selected.id)}`, {
            method: 'PATCH', headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ label: connectionName.trim(), revision: selected.revision }),
          });
          setNotice('Connection name updated.');
        });
      }}>
        <label className="block text-sm">Connection name<input className={input} required maxLength={200}
          value={connectionName} disabled={busy} onChange={event => setConnectionName(event.target.value)} /></label>
        <button type="submit" className={button}
          disabled={busy || !connectionName.trim() || connectionName.trim() === selected.label}>Save name</button>
      </form>
      <p className="text-sm">{selected.provider} / {selected.environment} / {selected.bankAccountName} ({selected.currency})</p>
      <p className="text-sm">Account: {selected.providerAccountRef}. Connection state: {selected.status.replaceAll('_', ' ')}.</p>
      <p className="text-sm">Last delivery: {selected.lastDeliveryAt ? new Date(selected.lastDeliveryAt).toLocaleString() : 'None yet'}</p>
      {oneTimeUrl && <div className="space-y-2 rounded-xl bg-[var(--bg-subtle)] p-3">
        <label className="block text-sm">One-time callback URL<input aria-label="One-time callback URL" readOnly value={oneTimeUrl} className={input} /></label>
        <p className="text-xs">Copy it now. It cannot be retrieved after leaving this screen. If lost, rotate the URL.</p>
        {grace && <p className="text-xs">The previous URL remains valid until {new Date(grace).toLocaleString()}. Update the Wise subscription before then.</p>}
        <button type="button" className={button} onClick={() => {
          if (!navigator.clipboard) { setError('Select and copy the callback URL manually.'); return; }
          void navigator.clipboard.writeText(oneTimeUrl).then(() => setNotice('Callback URL copied.')).catch(() => setError('Clipboard is unavailable. Select and copy the URL manually.'));
        }}>Copy URL</button>
      </div>}
      {(selected.status === 'setup_required' || selected.status === 'paused') && <form className="space-y-3" onSubmit={event => {
        event.preventDefault();
        if (!confirmed) return;
        void run(async () => {
          await request(`/api/integrations/connections/${selected.id}/activate`, {
            method: 'POST', headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(setupIdentity
              ? { sourceDeliveryId: setupIdentity.deliveryId, revision: selected.revision }
              : { subscriptionId: subscription, revision: selected.revision }),
          });
          setNotice('Connection activated. New matching deposits are staged for review; transfer updates and issues are recorded as activity only. Setup-time notifications are not processed.');
          setSubscription(''); setConfirmed(false); setOneTimeUrl('');
        });
      }}>
        <h4 className="font-semibold">Finish subscription setup</h4>
        <p className="text-sm">In Wise, create a webhook with this URL and select one event stream: Account deposit, Transfer update or Transfer issue. Use a separate AccountGo connection and callback for each stream. Click Test webhook, then Create webhook. After a real notification arrives, refresh and explicitly confirm its profile and subscription. Tests verify connectivity only and cannot activate a connection.</p>
        <p className="text-xs">The first real notification remains setup evidence. Import a setup-time deposit from your statement if needed. Deposits must match the bank currency; transfer notifications supply neither an amount nor a currency and never create bank transactions, bills, payments or postings. Do not overlap deposit and balance-update feeds for the same bank account.</p>
        <a className="text-sm underline" href="https://docs.wise.com/guides/developer/webhooks/subscribe-to-webhooks" target="_blank" rel="noreferrer">Wise subscription instructions</a>
        {setupIdentity ? <p className="text-sm" role="status">Verified notification identity: {setupIdentity.accountReference}; {setupIdentity.currency ? `currency: ${setupIdentity.currency}` : 'transfer activity only; no amount, currency or funding account verified'}. Confirm that this is your intended Wise profile and event stream before activating.</p>
          : selected.providerAccountRef ? <label className="block text-sm">Confirmed Wise subscription UUID<input className={input} required value={subscription} onChange={e => setSubscription(e.target.value)} /></label>
          : <p className="text-sm">Waiting for a signed, non-test notification. Deposits must be in {selected.currency}. Tests verify connectivity only.</p>}
        <label className="flex gap-2 text-sm"><input type="checkbox" required checked={confirmed} onChange={e => setConfirmed(e.target.checked)} />I confirm this Wise profile, subscription and event stream belong to this workspace and the bank association is correct.</label>
        <button type="submit" className={button} disabled={busy || !confirmed || (!setupIdentity && !selected.providerAccountRef)}>Bind subscription and activate</button>
      </form>}
      <div className="flex flex-wrap gap-2">
        {selected.status === 'active' && <button className={button} type="button" disabled={busy} onClick={() => lifecycle('pause')}>Pause</button>}
        {selected.status === 'paused' && selected.providerSubscriptionRef && <button className={button} type="button" disabled={busy} onClick={() => lifecycle('resume')}>Resume</button>}
        {selected.status !== 'revoked' && <>
          <button className={button} type="button" disabled={busy} onClick={() => setConfirm({ action: 'rotate-key', title: 'Rotate callback URL?', message: 'A new URL will be shown once. Update the Wise subscription within the 24-hour overlap. If its UUID changes, pause and bind the replacement subscription before resuming.' })}>Rotate URL</button>
          <button className={button} type="button" disabled={busy} onClick={() => setConfirm({ action: 'revoke', title: 'Disconnect connection?', message: 'Stop accepting and processing this connection immediately. Existing review items remain. Delete the provider-side subscription separately.' })}>Disconnect</button>
        </>}
        {selected.status === 'revoked' && <button className={button} type="button" disabled={busy}
          onClick={() => setConfirm({ action: 'delete', title: 'Permanently delete connection?',
            message: 'Remove this revoked connection permanently. Imported transactions, delivery history and audit records are retained. This cannot be undone. Remove the webhook in Wise separately.' })}>Permanently delete</button>}
        {capabilities.canReview && <Link className={button} to={`/org/${currentOrg.slug}/banking/needs-review?connectionId=${selected.id}`}>Review transactions</Link>}
      </div>
      <h4 className="font-semibold">Delivery history</h4>
      <p className="text-xs">Processed means a deposit was staged for review or transfer activity was recorded, never approved, posted or paid. Transfers are individual notifications, not a latest-status snapshot; use their occurrence times because delivery can be out of order. Diagnostics never reveal raw bodies, headers or credentials.</p>
      <div className="overflow-x-auto"><table className="w-full text-sm"><thead><tr className="text-left"><th>Received</th><th>Event</th><th>Verification</th><th>Processing</th><th>Action</th></tr></thead>
        <tbody>{deliveries.map(d => <tr key={d.id} className="border-t border-[var(--border-subtle)]">
          <td className="p-2">{new Date(d.receivedAt).toLocaleString()}</td>
          <td className="p-2">{d.eventType || 'Unknown event'}{d.isTest && <p className="text-xs">Test notification - not a real transfer or deposit</p>}
            {d.transferActivity && <div className="text-xs">
              <p>Transfer {d.transferActivity.transferId} / Profile {d.transferActivity.profileId}</p>
              {d.transferActivity.currentState && <p>{d.transferActivity.previousState} -&gt; {d.transferActivity.currentState}</p>}
              {d.transferActivity.occurredAt && <p>Occurred: {new Date(d.transferActivity.occurredAt).toLocaleString()}</p>}
              {d.eventType === 'transfers#active-cases' && <p>Active cases: {d.transferActivity.activeCases.length ? d.transferActivity.activeCases.join(', ') : 'None reported'}</p>}
            </div>}
          </td><td className="p-2">{d.verification}</td>
          <td className="p-2">{d.status} ({d.attempts} attempts){d.lastError && <p className="text-xs">{d.lastError}</p>}</td>
          <td>{d.verification === 'verified' && d.status === 'failed' && selected.status === 'active' && <button type="button" className={button} disabled={busy}
            onClick={() => void run(async () => {
              await request(`/api/integrations/connections/${selected.id}/deliveries/${d.id}/replay`, { method: 'POST' });
              setNotice('Delivery queued. Replay does not approve any accounting action.');
            })}>Replay</button>}</td>
        </tr>)}</tbody></table></div>
      {deliveries.length === 0 && <p className="text-sm">No deliveries on this page.</p>}
      <button className={button} type="button" disabled={busy || deliveryPage === 1} onClick={() => setDeliveryPage(p => p - 1)}>Previous deliveries</button>
      <button className={button} type="button" disabled={busy || deliveries.length < 20} onClick={() => setDeliveryPage(p => p + 1)}>Next deliveries</button>
      <button className={button} type="button" disabled={busy} onClick={() => setRevision(r => r + 1)}>Refresh connection</button>
    </section>}
    {confirm && <ConfirmDialog title={confirm.title} message={confirm.message}
      confirmLabel={confirm.action === 'delete' ? 'Delete permanently' : 'Confirm'}
      destructive={confirm.action === 'revoke' || confirm.action === 'delete'} busy={busy}
      onCancel={() => setConfirm(null)} onConfirm={() => lifecycle(confirm.action)} />}
  </section>;
}
