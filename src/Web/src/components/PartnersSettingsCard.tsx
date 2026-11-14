import React, { useCallback, useEffect, useState } from 'react';
import { useAuth } from '../auth/AuthContext';
import { localDateInputValue, percentageTotal, splitPrefill, validatePartnerSetup, type PartnerSetupRow } from '../lib/partners';

type PartnerRow = { id: number; name: string; capitalAccountCode: string; drawingsAccountCode: string; capitalBalance: number; drawingsBalance: number; archived: boolean; rowVersion: string };
type PartnerData = { partnerAccountsFrom: string | null; capitalBalance: number; drawingsBalance: number; partners: PartnerRow[]; openYears: Array<{ fiscalYearCode: string; rowVersion: string; shares: Array<{ partnerId: number; percent: number }> }> };

export const PartnersSettingsCard: React.FC<{ canManage: boolean }> = ({ canManage }) => {
  const { authFetch } = useAuth();
  const [data, setData] = useState<PartnerData | null>(null);
  const [rows, setRows] = useState<PartnerSetupRow[]>([{ name: '', percent: 50, capital: 0, drawings: 0 }, { name: '', percent: 50, capital: 0, drawings: 0 }]);
  const [date, setDate] = useState(localDateInputValue(new Date()));
  const [confirm, setConfirm] = useState(false);
  const [message, setMessage] = useState('');
  const [selectedYear, setSelectedYear] = useState('');
  const [newPartnerName, setNewPartnerName] = useState('');
  const [newPartnerPercent, setNewPartnerPercent] = useState(0);
  const [renameId, setRenameId] = useState<number | null>(null);
  const [renameName, setRenameName] = useState('');

  const reload = useCallback(async () => { const response = await authFetch(`/api/Financials/Partners?asOf=${encodeURIComponent(date)}`); if (response.ok) { const result = await response.json() as PartnerData; setData(result); setSelectedYear(current => current || result.openYears[0]?.fiscalYearCode || ''); } }, [authFetch, date]);
  useEffect(() => { void reload(); }, [reload]);
  useEffect(() => { if (data && !data.partnerAccountsFrom) setRows(current => current.map(row => ({ ...row, capital: splitPrefill(data.capitalBalance, current.length)[current.indexOf(row)] ?? 0, drawings: splitPrefill(data.drawingsBalance, current.length)[current.indexOf(row)] ?? 0 }))); }, [data?.capitalBalance, data?.drawingsBalance]);

  const saveSetup = async () => {
    const validation = data && validatePartnerSetup(rows, data.capitalBalance, data.drawingsBalance);
    if (validation) { setMessage(validation); return; }
    const response = await authFetch('/api/Financials/Partners/Setup', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ date, partners: rows }) });
    if (response.status === 409) { setMessage('Someone else changed this. Reload and try again.'); return; }
    if (!response.ok) { setMessage((await response.json()).message ?? 'Could not set up partners.'); return; }
    setConfirm(false); setMessage('Partner accounts are set up.'); await reload();
  };
  const saveShares = async (shares: Array<{ partnerId: number; percent: number }>) => {
    if (percentageTotal(shares.map(x => x.percent)) !== 100 || shares.some(x => x.percent <= 0)) { setMessage('Profit-sharing ratios must total 100%.'); return; }
    const response = await authFetch(`/api/Financials/Partners/Shares/${encodeURIComponent(selectedYear)}`, { method: 'PUT', headers: { 'Content-Type': 'application/json', 'If-Match': selected?.rowVersion ?? '' }, body: JSON.stringify(shares) });
    if (response.status === 409) { setMessage('Someone else changed this. Reload and try again.'); return; }
    if (!response.ok) { setMessage((await response.json()).message ?? 'Could not save shares.'); return; }
    setMessage('Profit-sharing ratios saved.'); await reload();
  };
  const addPartner = async () => {
    const response = await authFetch('/api/Financials/Partners', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ name: newPartnerName, fiscalYearCode: selectedYear, percent: newPartnerPercent, shares: selectedShares }) });
    if (response.status === 409) { setMessage('Someone else changed this. Reload and try again.'); return; }
    if (!response.ok) { setMessage((await response.json()).message ?? 'Could not add partner.'); return; }
    setNewPartnerName(''); setNewPartnerPercent(0); setMessage('Partner added.'); await reload();
  };
  const renamePartner = async (partner: PartnerRow) => {
    const response = await authFetch(`/api/Financials/Partners/${partner.id}`, { method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ name: renameName, rowVersion: partner.rowVersion }) });
    if (response.status === 409) { setMessage('Someone else changed this. Reload and try again.'); return; }
    if (!response.ok) { setMessage((await response.json()).message ?? 'Could not rename partner.'); return; }
    setRenameId(null); setMessage('Partner renamed.'); await reload();
  };
  const archivePartner = async (partner: PartnerRow) => {
    const response = await authFetch(`/api/Financials/Partners/${partner.id}/Archive`, { method: 'POST' });
    if (response.status === 409) { setMessage('Someone else changed this. Reload and try again.'); return; }
    if (!response.ok) { setMessage((await response.json()).message ?? 'Could not archive partner.'); return; }
    setMessage('Partner archived.'); await reload();
  };

  if (!data) return <section className="rounded-2xl bg-[var(--bg-surface)] p-6 text-sm">Loading partners…</section>;
  const selected = data.openYears.find(y => y.fiscalYearCode === selectedYear);
  const selectedShares = selected ? data.partners.filter(p => !p.archived).map(p => ({ partnerId: p.id, percent: selected.shares.find(s => s.partnerId === p.id)?.percent ?? 0 })) : [];
  const money = (n: number) => Number(n).toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 });
  return <section className="rounded-2xl bg-[var(--bg-surface)] p-4 sm:p-6 shadow-md space-y-5">
    <header><h2 className="text-base font-bold text-[var(--text-main)]">Partners</h2><p className="mt-1 text-xs text-[var(--text-muted)]">Set up partner capital and drawings accounts, then assign each partner a profit-sharing ratio for every open fiscal year.</p></header>
    {!data.partnerAccountsFrom ? <div className="space-y-4 rounded-xl bg-[var(--bg-subtle)] p-4">
      <h3 className="font-semibold">Set up partners</h3><p className="text-xs text-[var(--text-muted)]">Setup creates separate capital and drawings accounts and posts one reclassification journal for the balances you assign.</p>
      {rows.map((row, i) => <div key={i} className="grid grid-cols-2 gap-2 sm:grid-cols-5"><input aria-label={`Partner ${i + 1} name`} disabled={!canManage} value={row.name} placeholder="Partner name" onChange={e => setRows(rows.map((r, n) => n === i ? { ...r, name: e.target.value } : r))} className="rounded-lg p-2 text-sm"/><input aria-label="Share percent" disabled={!canManage} type="number" step="0.0001" value={row.percent} onChange={e => setRows(rows.map((r, n) => n === i ? { ...r, percent: Number(e.target.value) } : r))} className="rounded-lg p-2 text-sm"/><input aria-label="Capital amount" disabled={!canManage} type="number" step="0.01" value={row.capital} onChange={e => setRows(rows.map((r, n) => n === i ? { ...r, capital: Number(e.target.value) } : r))} className="rounded-lg p-2 text-sm"/><input aria-label="Drawings amount" disabled={!canManage} type="number" step="0.01" value={row.drawings} onChange={e => setRows(rows.map((r, n) => n === i ? { ...r, drawings: Number(e.target.value) } : r))} className="rounded-lg p-2 text-sm"/>{i > 1 && <button disabled={!canManage} onClick={() => setRows(rows.filter((_, n) => n !== i))}>Remove</button>}</div>)}
      {canManage && <button className="btn-secondary" onClick={() => setRows([...rows, { name: '', percent: 0, capital: 0, drawings: 0 }])}>Add partner</button>}
      <p className="text-xs">Capital total: {money(rows.reduce((s, x) => s + x.capital, 0))} / {money(data.capitalBalance)} · Drawings total: {money(rows.reduce((s, x) => s + x.drawings, 0))} / {money(data.drawingsBalance)} · Shares: {percentageTotal(rows.map(x => x.percent)).toFixed(4)}%</p>
      <label className="block text-xs">Setup date <input disabled={!canManage} type="date" value={date} onChange={e => setDate(e.target.value)} className="ml-2 rounded-lg p-2"/></label>
      {canManage && <button className="btn-primary" disabled={percentageTotal(rows.map(x => x.percent)) !== 100} onClick={() => setConfirm(true)}>Set up partners</button>}
    </div> : <>
      <div className="overflow-x-auto"><table className="w-full text-sm"><thead><tr className="text-left"><th>Partner</th><th>Capital</th><th>Balance</th><th>Drawings</th><th>Balance</th>{canManage && <th>Actions</th>}</tr></thead><tbody>{data.partners.map(p => <tr key={p.id} className="border-t border-[var(--border-subtle)]"><td className="py-2">{renameId === p.id ? <input aria-label="Partner name" value={renameName} onChange={e => setRenameName(e.target.value)} className="rounded-lg p-2"/> : <>{p.name}{p.archived ? ' (archived)' : ''}</>}</td><td>{p.capitalAccountCode}</td><td>{money(p.capitalBalance)}</td><td>{p.drawingsAccountCode}</td><td>{money(p.drawingsBalance)}</td>{canManage && <td className="space-x-2">{!p.archived && (renameId === p.id ? <><button className="btn-secondary" onClick={() => void renamePartner(p)}>Save</button><button className="btn-secondary" onClick={() => setRenameId(null)}>Cancel</button></> : <button className="btn-secondary" onClick={() => { setRenameId(p.id); setRenameName(p.name); }}>Rename</button>)}{!p.archived && <button className="btn-secondary" title={p.capitalBalance !== 0 || p.drawingsBalance !== 0 ? 'Clear both balances before archiving.' : data.openYears.some(y => y.shares.some(s => s.partnerId === p.id)) ? 'Remove this partner from every open year before archiving.' : 'Archive partner'} disabled={p.capitalBalance !== 0 || p.drawingsBalance !== 0 || data.openYears.some(y => y.shares.some(s => s.partnerId === p.id))} onClick={() => void archivePartner(p)}>Archive</button>}</td>}</tr>)}</tbody></table></div>
      {data.openYears.length > 0 && <div className="space-y-3"><label className="text-sm">Fiscal year <select value={selectedYear} onChange={e => setSelectedYear(e.target.value)} className="ml-2 rounded-lg p-2">{data.openYears.map(y => <option key={y.fiscalYearCode}>{y.fiscalYearCode}</option>)}</select></label>
        {selected && selectedShares.map((share, i) => <label key={share.partnerId} className="flex items-center justify-between text-sm">{data.partners.find(p => p.id === share.partnerId)?.name}<input aria-label={`Share for partner ${share.partnerId}`} disabled={!canManage} type="number" step="0.0001" value={share.percent} onChange={e => { const updated = selectedShares.map((s, n) => n === i ? { ...s, percent: Number(e.target.value) } : s); setData({ ...data, openYears: data.openYears.map(y => y.fiscalYearCode === selectedYear ? { ...y, shares: updated } : y) }); }} className="w-28 rounded-lg p-2" /></label>)}
        {canManage && <><button className="btn-primary" onClick={() => void saveShares(selectedShares)}>Save shares ({percentageTotal(selectedShares.map(x => x.percent)).toFixed(4)}%)</button>
          <div className="flex flex-wrap gap-2"><input aria-label="New partner name" placeholder="Partner name" value={newPartnerName} onChange={e => setNewPartnerName(e.target.value)} className="rounded-lg p-2 text-sm"/><input aria-label="New partner share" type="number" step="0.0001" value={newPartnerPercent} onChange={e => setNewPartnerPercent(Number(e.target.value))} className="w-28 rounded-lg p-2 text-sm"/><button className="btn-secondary" disabled={!newPartnerName.trim()} onClick={() => void addPartner()}>Add partner with shares</button></div></>}</div>}
    </>}
    {message && <p role="status" className="text-sm">{message}</p>}
    {confirm && <div role="dialog" aria-modal="true" className="fixed inset-0 z-50 grid place-items-center bg-black/50 p-4"><div className="max-w-lg space-y-4 rounded-2xl bg-[var(--bg-surface)] p-6"><p className="text-sm">This posts a journal that moves your current partners' capital and drawings into each partner's own accounts. It can't be undone automatically; to correct it later, post a journal between partner accounts.</p><div className="flex justify-end gap-2"><button className="btn-secondary" onClick={() => setConfirm(false)}>Cancel</button><button className="btn-primary" onClick={() => void saveSetup()}>Confirm setup</button></div></div></div>}
  </section>;
};
