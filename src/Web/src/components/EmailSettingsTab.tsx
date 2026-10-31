import { useCallback, useEffect, useState } from 'react';
import { Copy, Loader2, Mail, RefreshCw } from 'lucide-react';
import { useAuth } from '../auth/AuthContext';
import { useEmailCapabilities } from '../lib/useEmailCapabilities';
import { getPasskeyAssertion, isWebAuthnSupported } from '../lib/webauthn';

interface DnsRecord { purpose: string; type: string; host: string; relativeHost: string; value: string; ttl: string; priority: number | null; status: string }
interface EmailDomain {
  id: string; name: string; state: string; reason: string | null; ownershipHost: string; ownershipValue: string;
  challengeExpiresAt: number; ownershipConsumed: boolean; providerStatus: string; region: string; zoneName: string;
  records: DnsRecord[]; administrativelySuspended: boolean; ownershipCheckedAt: number | null; providerCheckedAt: number | null; provisioningState: string;
}
interface Sender { id: string; domainId: string; address: string; displayName: string; approved: boolean }
interface Settings { domains: EmailDomain[]; senders: Sender[] }
interface Dispatch { id: string; invoiceId: number; fromEmail: string; status: string; attempts: number; createdAt: number }

const inputClass = 'w-full rounded-xl border border-[var(--border-subtle)] bg-[var(--bg-subtle)] px-3 py-2 text-sm text-[var(--text-main)]';
const buttonClass = 'rounded-xl border border-[var(--border-subtle)] px-3 py-2 text-xs font-medium disabled:opacity-40 hover:bg-[var(--bg-subtle)]';
const when = (stamp: number | null) => stamp ? new Date(stamp * 1000).toLocaleString() : 'Not checked';

export function EmailSettingsTab({ companyId }: { companyId: string }) {
  const { authFetch, completeStepUp } = useAuth();
  const [revision, setRevision] = useState(0);
  const { capabilities, error: capabilityError } = useEmailCapabilities(companyId, revision);
  const [settings, setSettings] = useState<Settings | null>(null);
  const [dispatches, setDispatches] = useState<Dispatch[]>([]);
  const [domainInput, setDomainInput] = useState('');
  const [senderDomain, setSenderDomain] = useState('');
  const [address, setAddress] = useState('');
  const [displayName, setDisplayName] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [needsStepUp, setNeedsStepUp] = useState(false);
  const [code, setCode] = useState('');

  const load = useCallback(async () => {
    const [res, history] = await Promise.all([authFetch('/api/email/settings'), authFetch('/api/email/settings/dispatches')]);
    if (!res.ok) throw new Error('Organization email settings could not be loaded.');
    setSettings(await res.json());
    if (history.ok) setDispatches(await history.json());
  }, [authFetch]);

  useEffect(() => {
    if (capabilities?.canManageDomains) void load().catch(err => setError(err.message));
  }, [capabilities?.canManageDomains, load]);

  const act = async (path: string, method = 'POST', body?: unknown) => {
    setBusy(true); setError(null); setNotice(null);
    try {
      const res = await authFetch(`/api/email/settings/${path}`, { method,
        ...(body === undefined ? {} : { headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) }) });
      if (!res.ok) {
        const data = await res.json().catch(() => null);
        if (res.status === 403 && (!data || data.code === 'mfa_step_up_required')) {
          setNeedsStepUp(true);
          throw new Error('Verify your identity below, then retry this action.');
        }
        throw new Error(data?.message || data?.detail || 'The email setting could not be changed.');
      }
      await load(); setRevision(v => v + 1);
      setNotice('Email settings updated. DNS verification may still be pending.');
    } catch (err) { setError(err instanceof Error ? err.message : 'The operation failed.'); }
    finally { setBusy(false); }
  };

  const verify = async (passkey: boolean) => {
    setBusy(true); setError(null);
    try {
      let endpoint = '/api/auth/mfa/step-up/totp';
      let body: unknown = { code };
      if (passkey) {
        const begin = await authFetch('/api/auth/mfa/step-up/webauthn/options', { method: 'POST' });
        if (!begin.ok) throw new Error('Passkey verification could not be started.');
        body = { assertion: await getPasskeyAssertion(await begin.json()) };
        endpoint = '/api/auth/mfa/step-up/webauthn/complete';
      }
      const res = await authFetch(endpoint, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) });
      const data = await res.json().catch(() => null);
      if (!res.ok || !data?.accessToken) throw new Error(data?.detail || 'Verification failed.');
      completeStepUp(data.accessToken); setNeedsStepUp(false); setCode('');
      setNotice('Identity verified. Retry the email settings action.');
    } catch (err) { setError(err instanceof Error ? err.message : 'Verification failed.'); }
    finally { setBusy(false); }
  };

  const copy = (value: string) => {
    void navigator.clipboard.writeText(value).then(() => setNotice('DNS value copied.'))
      .catch(() => setError('Copy is unavailable. Select and copy the displayed value.'));
  };

  return <div className="space-y-5 text-[var(--text-main)]">
    <div className="rounded-2xl bg-[var(--bg-surface)] p-5 space-y-3">
      <h2 className="flex items-center gap-2 font-bold"><Mail className="h-5 w-5" /> Organization email</h2>
      <p className="text-sm text-[var(--text-secondary)]">Send invoices and follow-ups from an approved organization address. Sending verification does not create a mailbox; replies go to your existing business email.</p>
      <p className="text-xs text-[var(--text-muted)]">Use an unused sending subdomain, such as invoices.example.com, with a dedicated return path. Preserve your root MX and existing DMARC policy, and never add a second SPF policy at an occupied hostname.</p>
      {capabilities?.unavailableReason && <p className="text-sm">{capabilities.unavailableReason}</p>}
      {capabilities && !capabilities.canManageDomains && <p className="text-sm">An organization owner or admin manages domains and sender approvals.</p>}
      {capabilities?.canManageDomains && <button type="button" className={buttonClass} disabled={busy} onClick={() => { void load().catch(err => setError(err.message)); setRevision(v => v + 1); }}><RefreshCw className="inline h-3 w-3 mr-1" /> Refresh settings</button>}
    </div>
    {(error || capabilityError) && <p role="alert" className="rounded-xl bg-rose-500/10 p-3 text-sm text-rose-600">{error || capabilityError}</p>}
    {notice && <p role="status" className="text-sm">{notice}</p>}
    {needsStepUp && <form className="rounded-2xl bg-[var(--bg-surface)] p-5 space-y-3" onSubmit={e => { e.preventDefault(); void verify(false); }}>
      <h3 className="font-semibold">Verify this sensitive action</h3>
      <label className="block text-sm">Authenticator code<input className={inputClass} value={code} onChange={e => setCode(e.target.value.replace(/\D/g, '').slice(0, 6))} inputMode="numeric" autoComplete="one-time-code" /></label>
      <button className={buttonClass} disabled={busy || code.length !== 6}>Verify code</button>
      {isWebAuthnSupported() && <button type="button" className={buttonClass + ' ml-2'} disabled={busy} onClick={() => void verify(true)}>Use passkey</button>}
    </form>}
    {capabilities?.canManageDomains && <>
      <form className="rounded-2xl bg-[var(--bg-surface)] p-5 space-y-3" onSubmit={e => { e.preventDefault(); void act('domains', 'POST', { domain: domainInput }); }}>
        <label className="block text-sm font-medium">Sending domain<input className={inputClass + ' mt-1'} placeholder="invoices.example.com" value={domainInput} onChange={e => setDomainInput(e.target.value)} required disabled={busy || !capabilities.customEmailAvailable} /></label>
        <button className={buttonClass} disabled={busy || !capabilities.customEmailAvailable}>{busy && <Loader2 className="inline h-3 w-3 animate-spin mr-1" />} Add domain</button>
      </form>
      {settings?.domains.map(domain => <section key={domain.id} className="rounded-2xl bg-[var(--bg-surface)] p-5 space-y-4" aria-label={`Email domain ${domain.name}`}>
        <div><h3 className="font-semibold break-all">{domain.name}</h3><p className="text-sm">{domain.state} · Provider: {domain.providerStatus} · {domain.region}</p></div>
        {domain.reason && <p className="text-sm text-amber-600">{domain.reason}</p>}
        <div className="space-y-2 text-xs">
          <p className="font-semibold">AccountGo ownership TXT record — keep this record after verification</p>
          <div className="break-all font-mono">{domain.ownershipHost} <button type="button" className={buttonClass} aria-label={`Copy ownership hostname for ${domain.name}`} onClick={() => copy(domain.ownershipHost)}><Copy className="h-3 w-3" /></button></div>
          <div className="break-all font-mono">{domain.ownershipValue} <button type="button" className={buttonClass} aria-label={`Copy ownership value for ${domain.name}`} onClick={() => copy(domain.ownershipValue)}><Copy className="h-3 w-3" /></button></div>
          {!domain.ownershipConsumed && <p>Complete this challenge before {when(domain.challengeExpiresAt)}.</p>}
        </div>
        {domain.records.length > 0 && <div className="overflow-x-auto"><table className="w-full text-xs text-left"><caption className="text-left py-2">Provider DNS records. Relative hosts below use the {domain.zoneName} zone.</caption><thead><tr>{['Purpose / type', 'Hostname', 'Value', 'TTL / priority', 'Status'].map(h => <th key={h} scope="col" className="p-2">{h}</th>)}</tr></thead><tbody>{domain.records.map((record, index) => <tr key={index} className="border-t border-[var(--border-subtle)]">
          <td className="p-2">{record.purpose}<br />{record.type}</td><td className="p-2"><div className="break-all font-mono">{record.host}</div><div>Relative: {record.relativeHost}</div><button type="button" className={buttonClass} onClick={() => copy(record.host)} aria-label={`Copy hostname ${record.host}`}>Copy host</button></td>
          <td className="p-2 max-w-sm"><div className="break-all font-mono">{record.value}</div><button type="button" className={buttonClass} onClick={() => copy(record.value)} aria-label={`Copy ${record.purpose} value`}>Copy value</button></td><td className="p-2">{record.ttl}{record.priority !== null && <div>Priority {record.priority}</div>}</td><td className="p-2">{record.status}</td>
        </tr>)}</tbody></table></div>}
        <p className="text-xs text-[var(--text-muted)]">Ownership last checked: {when(domain.ownershipCheckedAt)}. Provider last checked: {when(domain.providerCheckedAt)}.</p>
        <div className="flex flex-wrap gap-2">
          {domain.state === 'AwaitingOwnership' && <button className={buttonClass} disabled={busy || !capabilities.customEmailAvailable} onClick={() => void act(`domains/${domain.id}/ownership`)}>Check ownership TXT</button>}
          {domain.ownershipConsumed && <button className={buttonClass} disabled={busy || !capabilities.customEmailAvailable} onClick={() => void act(`domains/${domain.id}/verify`)}>{domain.provisioningState === 'Rejected' ? 'Retry provider setup' : 'Request provider verification'}</button>}
          <button className={buttonClass} disabled={busy} onClick={() => void act(`domains/${domain.id}/refresh`)}>Check status</button>
          <button className={buttonClass} disabled={busy || !capabilities.customEmailAvailable} onClick={() => void act(`domains/${domain.id}/challenge`)}>Get fresh ownership challenge</button>
          <button className={buttonClass} disabled={busy || (domain.administrativelySuspended && !capabilities.customEmailAvailable)} onClick={() => void act(`domains/${domain.id}/${domain.administrativelySuspended ? 'resume' : 'suspend'}`)}>{domain.administrativelySuspended ? 'Resume' : 'Suspend'}</button>
          <button className={buttonClass + ' text-rose-600'} disabled={busy} onClick={() => void act(`domains/${domain.id}`, 'DELETE')}>Delete domain</button>
        </div>
      </section>)}
      <form className="rounded-2xl bg-[var(--bg-surface)] p-5 space-y-3" onSubmit={e => { e.preventDefault(); void act('senders', 'POST', { domainId: senderDomain, address, displayName }); }}>
        <h3 className="font-semibold">Approved sender addresses</h3>
        <p className="text-xs text-[var(--text-muted)]">Approve an exact address on the registered sending domain. Use an existing mailbox in your business details for replies.</p>
        <label className="block text-sm">Domain<select className={inputClass} value={senderDomain} onChange={e => setSenderDomain(e.target.value)} required disabled={busy || !capabilities.customEmailAvailable}><option value="">Choose a verified ownership binding</option>{settings?.domains.filter(d => d.ownershipConsumed && d.state !== 'Deleting').map(d => <option key={d.id} value={d.id}>{d.name}</option>)}</select></label>
        <label className="block text-sm">Sender address<input type="email" required className={inputClass} value={address} onChange={e => setAddress(e.target.value)} disabled={busy || !capabilities.customEmailAvailable} /></label>
        <label className="block text-sm">Display name<input required maxLength={70} className={inputClass} value={displayName} onChange={e => setDisplayName(e.target.value)} disabled={busy || !capabilities.customEmailAvailable} /></label>
        <button className={buttonClass} disabled={busy || !capabilities.customEmailAvailable}>Approve sender</button>
        {settings?.senders.map(sender => <div key={sender.id} className="flex flex-wrap items-center justify-between gap-2 border-t border-[var(--border-subtle)] pt-3 text-sm"><span>{sender.displayName} &lt;{sender.address}&gt; — {sender.approved ? 'Approved' : 'Revoked'}</span>{sender.approved && <button type="button" className={buttonClass} disabled={busy} onClick={() => void act(`senders/${sender.id}`, 'DELETE')}>Revoke</button>}</div>)}
      </form>
      {dispatches.length > 0 && <section className="rounded-2xl bg-[var(--bg-surface)] p-5 space-y-3"><h3 className="font-semibold">Recent custom sends</h3><p className="text-xs">Provider acceptance is separate from delivery. An ambiguous outcome requires retrying the same dispatch or operator investigation.</p><div className="overflow-x-auto"><table className="w-full text-xs text-left"><thead><tr>{['Invoice', 'From', 'Outcome', 'Attempts', 'Created'].map(h => <th scope="col" className="p-2" key={h}>{h}</th>)}</tr></thead><tbody>{dispatches.map(d => <tr key={d.id} className="border-t border-[var(--border-subtle)]"><td className="p-2">{d.invoiceId}</td><td className="p-2">{d.fromEmail}</td><td className="p-2">{d.status}<div className="font-mono break-all">{d.id}</div></td><td className="p-2">{d.attempts}</td><td className="p-2">{when(d.createdAt)}</td></tr>)}</tbody></table></div></section>}
    </>}
  </div>;
}
