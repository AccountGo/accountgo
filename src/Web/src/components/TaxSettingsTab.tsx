import React, { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { AlertCircle, Archive, ArchiveRestore, Pencil, Percent, Plus, RefreshCw, Trash2 } from 'lucide-react';
import { useAuth } from '../auth/AuthContext';

interface TaxRow {
  id: number;
  taxCode: string;
  taxName: string;
  rate: number;
  isActive: boolean;
  isSystem: boolean;
  salesAccountId: number | null;
  salesAccountCode: string | null;
  salesAccountName: string | null;
  purchaseAccountId: number | null;
  purchaseAccountCode: string | null;
  purchaseAccountName: string | null;
  inUse: boolean;
}

interface AccountNode {
  id: number;
  accountCode: string;
  accountName: string;
  accountClassId: number;
  canPost: boolean;
  archived?: boolean;
  childAccounts?: AccountNode[];
}

interface Draft {
  id: number | null;
  taxName: string;
  taxCode: string;
  rate: string;
  isActive: boolean;
  salesAccountId: number | '';
  purchaseAccountId: number | '';
}

const emptyDraft = (): Draft => ({
  id: null,
  taxName: '',
  taxCode: '',
  rate: '',
  isActive: true,
  salesAccountId: '',
  purchaseAccountId: '',
});

const fieldClass =
  'w-full px-3.5 py-2.5 rounded-xl bg-[var(--bg-subtle)] text-sm text-[var(--text-main)] focus:outline-none focus:ring-2 focus:ring-[var(--brand-ring)]';

function flatten(nodes: AccountNode[], out: AccountNode[] = []): AccountNode[] {
  for (const node of nodes) {
    out.push(node);
    if (node.childAccounts?.length) flatten(node.childAccounts, out);
  }
  return out;
}

function accountLabel(code: string | null, name: string | null) {
  if (!code && !name) return '—';
  return [code, name].filter(Boolean).join(' · ');
}

async function readError(res: Response) {
  const data = await res.json().catch(() => null) as { message?: string; detail?: string; title?: string } | null;
  return data?.message || data?.detail || data?.title || 'The tax could not be saved.';
}

interface TaxSettingsTabProps {
  canManage: boolean;
  hasFinancials: boolean;
  currency: string;
}

/** Workspace tax rates. Issued invoices and bills keep the amounts they were saved with. */
export const TaxSettingsTab: React.FC<TaxSettingsTabProps> = ({ canManage, hasFinancials, currency }) => {
  const { authFetch } = useAuth();
  const [taxes, setTaxes] = useState<TaxRow[]>([]);
  const [accounts, setAccounts] = useState<AccountNode[]>([]);
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [draft, setDraft] = useState<Draft | null>(null);
  const [formError, setFormError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);
  const [pendingDelete, setPendingDelete] = useState<TaxRow | null>(null);
  const [deleteError, setDeleteError] = useState<string | null>(null);
  const [deleting, setDeleting] = useState(false);
  const nameInputRef = useRef<HTMLInputElement>(null);
  const dialogOpen = draft != null;
  const dialogTaxId = draft?.id ?? null;

  const load = useCallback(async () => {
    setLoading(true);
    setLoadError(null);
    try {
      const requests = [authFetch('/api/Tax')];
      if (hasFinancials) requests.push(authFetch('/api/Financials/Accounts'));
      const [taxRes, accountRes] = await Promise.all(requests);
      if (!taxRes.ok) throw new Error('Taxes could not be loaded.');
      setTaxes(await taxRes.json() as TaxRow[]);
      if (accountRes?.ok) setAccounts(flatten(await accountRes.json() as AccountNode[]));
    } catch (err) {
      setLoadError(err instanceof Error ? err.message : 'Taxes could not be loaded.');
    } finally {
      setLoading(false);
    }
  }, [authFetch, hasFinancials]);

  useEffect(() => { void load(); }, [load]);

  useEffect(() => {
    if (dialogOpen) nameInputRef.current?.focus();
  }, [dialogOpen, dialogTaxId]);

  const salesAccounts = useMemo(
    () => accounts.filter((account) => account.canPost && account.accountClassId === 2 && (!account.archived || account.id === draft?.salesAccountId)),
    [accounts, draft?.salesAccountId]
  );
  const purchaseAccounts = useMemo(
    () => accounts.filter((account) => account.canPost && (account.accountClassId === 1 || account.accountClassId === 5) && (!account.archived || account.id === draft?.purchaseAccountId)),
    [accounts, draft?.purchaseAccountId]
  );
  const rows = useMemo(
    () => [...taxes].sort((a, b) => Number(a.isSystem) - Number(b.isSystem) || a.taxName.localeCompare(b.taxName)),
    [taxes]
  );
  const customTaxes = taxes.filter((tax) => !tax.isSystem);
  const currencyCode = currency.trim().toUpperCase();
  const quickFill = currencyCode === 'THB'
    ? { taxName: 'VAT 7%', taxCode: 'VAT7', rate: '7' }
    : currencyCode === 'PHP'
      ? { taxName: 'VAT 12%', taxCode: 'VAT12', rate: '12' }
      : null;

  const openDraft = (next: Draft) => {
    setFormError(null);
    setDraft({
      ...next,
      salesAccountId: next.salesAccountId || salesAccounts.find((account) => account.accountCode === '20300')?.id || '',
      purchaseAccountId: next.purchaseAccountId || purchaseAccounts.find((account) => account.accountCode === '10160')?.id || '',
    });
  };

  const save = async (event: React.FormEvent) => {
    event.preventDefault();
    if (!draft) return;
    setSaving(true);
    setFormError(null);
    const body: Record<string, unknown> = {
      taxName: draft.taxName.trim(),
      taxCode: draft.taxCode.trim(),
      rate: Number(draft.rate),
    };
    if (draft.id) body.isActive = draft.isActive;
    if (hasFinancials) {
      const salesAccountId = draft.salesAccountId !== '' ? draft.salesAccountId : accounts.find((account) => account.accountCode === '20300')?.id;
      const purchaseAccountId = draft.purchaseAccountId !== '' ? draft.purchaseAccountId : accounts.find((account) => account.accountCode === '10160')?.id;
      if (salesAccountId) body.salesAccountId = salesAccountId;
      if (purchaseAccountId) body.purchaseAccountId = purchaseAccountId;
    }
    try {
      const res = await authFetch(draft.id ? `/api/Tax/${draft.id}` : '/api/Tax', {
        method: draft.id ? 'PUT' : 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(body),
      });
      if (!res.ok) throw new Error(await readError(res));
      setDraft(null);
      await load();
    } catch (err) {
      setFormError(err instanceof Error ? err.message : 'The tax could not be saved.');
    } finally {
      setSaving(false);
    }
  };

  const setActive = async (tax: TaxRow, isActive: boolean) => {
    setLoadError(null);
    const res = await authFetch(`/api/Tax/${tax.id}`, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        taxName: tax.taxName,
        taxCode: tax.taxCode,
        rate: tax.rate,
        isActive,
        ...(hasFinancials ? { salesAccountId: tax.salesAccountId, purchaseAccountId: tax.purchaseAccountId } : {}),
      }),
    });
    if (!res.ok) {
      setLoadError(await readError(res));
      return;
    }
    await load();
  };

  const remove = async () => {
    if (!pendingDelete) return;
    setDeleting(true);
    setDeleteError(null);
    try {
      const res = await authFetch(`/api/Tax/${pendingDelete.id}`, { method: 'DELETE' });
      if (!res.ok) throw new Error(await readError(res));
      setPendingDelete(null);
      await load();
    } catch (err) {
      setDeleteError(err instanceof Error ? err.message : 'The tax could not be deleted.');
    } finally {
      setDeleting(false);
    }
  };

  return (
    <div className="space-y-6">
      <div className="p-4 sm:p-6 rounded-2xl bg-[var(--bg-surface)] backdrop-blur-md shadow-md space-y-4">
        <div className="flex items-center justify-between gap-3 pb-2 border-b border-[var(--border-subtle)]">
          <div className="flex items-center gap-2.5">
            <Percent className="w-5 h-5 text-[var(--brand-primary)]" />
            <div>
              <h2 className="text-sm sm:text-base font-bold text-[var(--text-main)]">Taxes</h2>
              <p className="text-xs text-[var(--text-muted)]">Rates added on top of invoice and bill lines. Changing a rate later leaves issued documents as they were.</p>
            </div>
          </div>
          <div className="flex items-center gap-2">
            {loading && <RefreshCw className="w-4 h-4 text-[var(--brand-primary)] animate-spin" />}
            {canManage && (
              <button type="button" onClick={() => openDraft(emptyDraft())} className="btn-primary !h-10 !px-4 text-xs inline-flex items-center gap-1.5">
                <Plus className="w-4 h-4" /> Add tax
              </button>
            )}
          </div>
        </div>

        {loadError && (
          <p role="alert" className="text-sm text-rose-400 flex items-center gap-2"><AlertCircle className="w-4 h-4" />{loadError}</p>
        )}

        {!loading && customTaxes.length === 0 && (
          <div className="p-4 rounded-xl bg-[var(--bg-subtle)] text-sm text-[var(--text-secondary)] space-y-3">
            <p>No tax rates yet, other than the system “No tax” rate. Add the rate you charge on invoices and bills.</p>
            {canManage && quickFill && (
              <button
                type="button"
                onClick={() => openDraft({ ...emptyDraft(), ...quickFill })}
                className="h-10 px-4 rounded-xl bg-[var(--bg-surface)] text-xs font-semibold text-[var(--text-main)] shadow-sm"
              >
                Start with {quickFill.taxName}
              </button>
            )}
          </div>
        )}

        <div className="overflow-x-auto">
          <table className="w-full text-left text-sm">
            <thead>
              <tr className="text-[10px] uppercase tracking-wider text-[var(--text-muted)]">
                <th className="py-2 pr-3 font-semibold">Name</th>
                <th className="py-2 pr-3 font-semibold">Code</th>
                <th className="py-2 pr-3 font-semibold">Rate</th>
                <th className="py-2 pr-3 font-semibold">Sales account</th>
                <th className="py-2 pr-3 font-semibold">Purchase account</th>
                <th className="py-2 pr-3 font-semibold">Status</th>
                {canManage && <th className="py-2 font-semibold"><span className="sr-only">Actions</span></th>}
              </tr>
            </thead>
            <tbody>
              {rows.map((tax) => (
                <tr key={tax.id} className={tax.isSystem ? 'text-[var(--text-muted)]' : 'text-[var(--text-main)]'}>
                  <td className="py-2.5 pr-3">
                    {tax.taxName}
                    {tax.isSystem && <span className="ml-2 px-1.5 py-0.5 rounded text-[10px] font-bold bg-[var(--bg-subtle)]">System</span>}
                  </td>
                  <td className="py-2.5 pr-3 font-mono text-xs">{tax.taxCode}</td>
                  <td className="py-2.5 pr-3">{tax.rate}%</td>
                  <td className="py-2.5 pr-3 text-xs">{accountLabel(tax.salesAccountCode, tax.salesAccountName)}</td>
                  <td className="py-2.5 pr-3 text-xs">{accountLabel(tax.purchaseAccountCode, tax.purchaseAccountName)}</td>
                  <td className="py-2.5 pr-3">
                    <span className={`px-2 py-0.5 rounded text-[10px] font-bold ${tax.isActive ? 'bg-emerald-500/10 text-emerald-400' : 'bg-[var(--bg-subtle)] text-[var(--text-muted)]'}`}>
                      {tax.isActive ? 'Active' : 'Archived'}
                    </span>
                  </td>
                  {canManage && (
                    <td className="py-2.5 text-right whitespace-nowrap">
                      {!tax.isSystem && (
                        <span className="inline-flex items-center gap-1">
                          <button type="button" title="Edit" aria-label={`Edit ${tax.taxName}`} onClick={() => openDraft({
                            id: tax.id, taxName: tax.taxName, taxCode: tax.taxCode, rate: String(tax.rate), isActive: tax.isActive,
                            salesAccountId: tax.salesAccountId ?? '', purchaseAccountId: tax.purchaseAccountId ?? '',
                          })} className="p-1.5 rounded-lg text-[var(--text-muted)] hover:text-[var(--text-main)]">
                            <Pencil className="w-4 h-4" />
                          </button>
                          <button type="button" title={tax.isActive ? 'Archive' : 'Restore'} aria-label={`${tax.isActive ? 'Archive' : 'Restore'} ${tax.taxName}`} onClick={() => void setActive(tax, !tax.isActive)} className="p-1.5 rounded-lg text-[var(--text-muted)] hover:text-[var(--text-main)]">
                            {tax.isActive ? <Archive className="w-4 h-4" /> : <ArchiveRestore className="w-4 h-4" />}
                          </button>
                          <button
                            type="button"
                            title={tax.inUse ? 'Used on invoices or bills — archive instead' : 'Delete'}
                            aria-label={`Delete ${tax.taxName}`}
                            disabled={tax.inUse}
                            onClick={() => { setDeleteError(null); setPendingDelete(tax); }}
                            className="p-1.5 rounded-lg text-[var(--text-muted)] hover:text-rose-400 disabled:opacity-30 disabled:hover:text-[var(--text-muted)]"
                          >
                            <Trash2 className="w-4 h-4" />
                          </button>
                        </span>
                      )}
                    </td>
                  )}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
        {!canManage && <p className="text-[11px] text-[var(--text-muted)]">You can view tax rates. Managing them takes the Manage Taxes permission.</p>}
      </div>

      {draft && (
        <div className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-black/50" onKeyDown={(event) => { if (event.key === 'Escape') setDraft(null); }}>
          <form role="dialog" aria-modal="true" aria-labelledby="tax-dialog-title" onSubmit={save} className="w-full max-w-lg p-5 rounded-2xl bg-[var(--bg-surface)] shadow-xl space-y-4">
            <h3 id="tax-dialog-title" className="text-base font-bold text-[var(--text-main)]">{draft.id ? 'Edit tax' : 'Add tax'}</h3>
            <label className="block text-xs font-semibold text-[var(--text-secondary)]">Name
              <input ref={nameInputRef} id="tax-name" autoFocus value={draft.taxName} onChange={(event) => setDraft({ ...draft, taxName: event.target.value })} maxLength={50} className={`${fieldClass} mt-1`} required />
            </label>
            <div className="grid grid-cols-2 gap-3">
              <label className="block text-xs font-semibold text-[var(--text-secondary)]">Code
                <input value={draft.taxCode} onChange={(event) => setDraft({ ...draft, taxCode: event.target.value })} maxLength={16} className={`${fieldClass} mt-1 font-mono`} required />
              </label>
              <label className="block text-xs font-semibold text-[var(--text-secondary)]">Rate (%)
                <input type="number" min="0" max="100" step="0.01" value={draft.rate} onChange={(event) => setDraft({ ...draft, rate: event.target.value })} className={`${fieldClass} mt-1`} required />
              </label>
            </div>
            {hasFinancials && (
              <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
                <label className="block text-xs font-semibold text-[var(--text-secondary)]">Sales tax account
                  <select value={draft.salesAccountId} onChange={(event) => setDraft({ ...draft, salesAccountId: event.target.value ? Number(event.target.value) : '' })} className={`${fieldClass} mt-1`}>
                    <option value="">Default (20300)</option>
                    {salesAccounts.map((account) => <option key={account.id} value={account.id}>{account.accountCode} · {account.accountName}</option>)}
                  </select>
                </label>
                <label className="block text-xs font-semibold text-[var(--text-secondary)]">Purchase tax account
                  <select value={draft.purchaseAccountId} onChange={(event) => setDraft({ ...draft, purchaseAccountId: event.target.value ? Number(event.target.value) : '' })} className={`${fieldClass} mt-1`}>
                    <option value="">Default (10160)</option>
                    {purchaseAccounts.map((account) => <option key={account.id} value={account.id}>{account.accountCode} · {account.accountName}</option>)}
                  </select>
                </label>
              </div>
            )}
            {formError && <p role="alert" className="text-sm text-rose-400">{formError}</p>}
            <div className="flex justify-end gap-2">
              <button type="button" onClick={() => setDraft(null)} className="h-10 px-4 rounded-xl text-xs font-semibold text-[var(--text-secondary)]">Cancel</button>
              <button type="submit" disabled={saving} className="btn-primary !h-10 !px-4 text-xs">{saving ? 'Saving…' : 'Save tax'}</button>
            </div>
          </form>
        </div>
      )}

      {pendingDelete && (
        <div className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-black/50">
          <div role="alertdialog" aria-modal="true" aria-labelledby="delete-tax-title" className="w-full max-w-md p-5 rounded-2xl bg-[var(--bg-surface)] shadow-xl space-y-4">
            <h3 id="delete-tax-title" className="text-base font-bold text-[var(--text-main)]">Delete {pendingDelete.taxName}?</h3>
            <p className="text-sm text-[var(--text-secondary)]">This removes the rate. It can’t be deleted once an invoice or bill uses it.</p>
            {deleteError && <p role="alert" className="text-sm text-rose-400">{deleteError}</p>}
            <div className="flex justify-end gap-2">
              <button type="button" onClick={() => setPendingDelete(null)} className="h-10 px-4 rounded-xl text-xs font-semibold text-[var(--text-secondary)]">Cancel</button>
              <button type="button" onClick={() => void remove()} disabled={deleting} className="h-10 px-4 rounded-xl bg-rose-600 text-white text-xs font-semibold disabled:opacity-50">
                {deleting ? 'Deleting…' : 'Delete tax'}
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
};
