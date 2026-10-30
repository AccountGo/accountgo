import React, { useEffect, useState } from 'react';
import { AlertCircle, CheckCircle2, Lock, Save } from 'lucide-react';
import { useAuth } from '../auth/AuthContext';

interface Policy {
  reportingFramework: string;
  accountingBasis: string;
  fiscalYearPattern: string;
  fiscalYearStartMonth: number | null;
  booksStartDate: string;
  taxRegime: string;
  entityType?: string;
  inventoryCostingMethod: string;
  isLedgerFrozen: boolean;
}

const FRAMEWORKS: [string, string][] = [
  ['IfrsForSmes', 'IFRS for SMEs'], ['UsGaapPrivate', 'US GAAP (private company)'], ['Ifrs', 'Full IFRS'], ['CashBasis', 'Cash basis'],
];
const ENTITY_TYPES: [string, string][] = [
  ['SoleProprietor', 'Sole Proprietor'],
  ['Partnership', 'Partnership'],
  ['Corporation', 'Corporation'],
  ['Cooperative', 'Cooperative'],
  ['NonProfit', 'Non-Profit'],
];
const PATTERNS: [string, string][] = [['Calendar', 'Calendar year (Jan–Dec)'], ['AprMar', 'April – March'], ['JulJun', 'July – June'], ['Custom', 'Custom start month']];
const TAX: [string, string][] = [['None', 'No tax'], ['SalesTax', 'Sales tax'], ['VatGst', 'VAT / GST']];
const INVENTORY: [string, string][] = [['None', 'Not tracked'], ['WeightedAverage', 'Weighted average'], ['Fifo', 'FIFO']];
const MONTHS = ['January', 'February', 'March', 'April', 'May', 'June', 'July', 'August', 'September', 'October', 'November', 'December'];

const selectClass =
  'w-full h-11 px-3.5 rounded-xl bg-[var(--bg-subtle)] shadow-inner text-sm text-[var(--text-main)] focus:outline-none focus:ring-2 focus:ring-[var(--brand-ring)] disabled:opacity-60';
const labelClass = 'block text-xs font-semibold text-[var(--text-secondary)] mb-1.5';

/** Accounting policy chosen at creation. Framework, fiscal year and books start lock after the first journal entry. */
export const AccountingPolicyCard: React.FC<{ canEdit: boolean }> = ({ canEdit }) => {
  const { authFetch } = useAuth();
  const [policy, setPolicy] = useState<Policy | null>(null);
  const [saved, setSaved] = useState<Policy | null>(null);
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState<{ kind: 'ok' | 'error'; text: string } | null>(null);

  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        const res = await authFetch('/api/organizations/current/accounting-policy');
        if (res.ok && !cancelled) {
          const data = await res.json() as Policy;
          setPolicy(data);
          setSaved(data);
        }
      } catch (err) {
        console.error('Failed to load accounting policy:', err);
      }
    })();
    return () => { cancelled = true; };
  }, [authFetch]);

  if (!policy || !saved) return null;

  const frozen = policy.isLedgerFrozen;
  const lockedDisabled = !canEdit || frozen;
  const set = (patch: Partial<Policy>) => setPolicy({ ...policy, ...patch });
  const lockedChanged = policy.reportingFramework !== saved.reportingFramework
    || policy.fiscalYearPattern !== saved.fiscalYearPattern
    || policy.fiscalYearStartMonth !== saved.fiscalYearStartMonth
    || policy.booksStartDate !== saved.booksStartDate;

  const save = async (e: React.FormEvent) => {
    e.preventDefault();
    if (lockedChanged && !window.confirm('Changing the framework, fiscal year or books start date rebuilds your fiscal year. This is only possible while you have no journal entries. Continue?')) return;
    setBusy(true);
    setMessage(null);
    try {
      const body: Record<string, unknown> = {
        taxRegime: policy.taxRegime,
        entityType: policy.entityType,
        inventoryCostingMethod: policy.inventoryCostingMethod
      };
      if (!frozen) {
        Object.assign(body, {
          reportingFramework: policy.reportingFramework,
          fiscalYearPattern: policy.fiscalYearPattern,
          fiscalYearStartMonth: policy.fiscalYearPattern === 'Custom' ? policy.fiscalYearStartMonth : undefined,
          booksStartDate: policy.booksStartDate,
        });
      }
      const res = await authFetch('/api/organizations/current/accounting-policy', {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(body),
      });
      if (!res.ok) {
        const problem = await res.json().catch(() => null);
        throw new Error(problem?.detail || problem?.title || 'Could not save accounting policy.');
      }
      const data = await res.json() as Policy;
      setPolicy(data);
      setSaved(data);
      setMessage({ kind: 'ok', text: 'Accounting policy saved.' });
    } catch (err) {
      setMessage({ kind: 'error', text: err instanceof Error ? err.message : 'Could not save accounting policy.' });
    } finally {
      setBusy(false);
    }
  };

  return (
    <form onSubmit={save} className="p-4 sm:p-6 rounded-2xl bg-[var(--bg-surface)] shadow-md space-y-4">
      <div className="pb-2 border-b border-[var(--border-subtle)]">
        <h3 className="text-sm font-bold text-[var(--text-main)]">Accounting policy</h3>
        <p className="text-xs text-[var(--text-muted)]">Chosen when the workspace was created.</p>
      </div>

      {frozen && (
        <div role="note" className="flex items-start gap-2 p-3 rounded-xl bg-[var(--bg-subtle)] text-xs text-[var(--text-secondary)]">
          <Lock className="w-4 h-4 flex-shrink-0 mt-0.5" aria-hidden="true" />
          <span>Framework, accounting basis, fiscal year and books start date are locked because journal entries exist. Tax regime and inventory method can still change.</span>
        </div>
      )}

      <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
        <div>
          <label htmlFor="policy-framework" className={labelClass}>Reporting framework</label>
          <select id="policy-framework" className={selectClass} disabled={lockedDisabled} value={policy.reportingFramework}
            onChange={(e) => set({ reportingFramework: e.target.value, accountingBasis: e.target.value === 'CashBasis' ? 'Cash' : 'Accrual' })}>
            {FRAMEWORKS.map(([v, l]) => <option key={v} value={v}>{l}</option>)}
          </select>
          <p className="text-[11px] text-[var(--text-muted)] mt-1">Basis: {policy.accountingBasis}</p>
        </div>
        <div>
          <label htmlFor="policy-books-start" className={labelClass}>Books start date</label>
          <input id="policy-books-start" type="date" className={selectClass} disabled={lockedDisabled} value={policy.booksStartDate}
            onChange={(e) => set({ booksStartDate: e.target.value })} />
        </div>
        <div>
          <label htmlFor="policy-fy" className={labelClass}>Fiscal year</label>
          <select id="policy-fy" className={selectClass} disabled={lockedDisabled} value={policy.fiscalYearPattern}
            onChange={(e) => set({ fiscalYearPattern: e.target.value, fiscalYearStartMonth: e.target.value === 'Custom' ? (policy.fiscalYearStartMonth ?? 1) : null })}>
            {PATTERNS.map(([v, l]) => <option key={v} value={v}>{l}</option>)}
          </select>
        </div>
        {policy.fiscalYearPattern === 'Custom' && (
          <div>
            <label htmlFor="policy-fy-month" className={labelClass}>Start month</label>
            <select id="policy-fy-month" className={selectClass} disabled={lockedDisabled} value={policy.fiscalYearStartMonth ?? 1}
              onChange={(e) => set({ fiscalYearStartMonth: Number(e.target.value) })}>
              {MONTHS.map((m, i) => <option key={m} value={i + 1}>{m}</option>)}
            </select>
          </div>
        )}
        <div>
          <label htmlFor="policy-tax" className={labelClass}>Tax regime</label>
          <select id="policy-tax" className={selectClass} disabled={!canEdit} value={policy.taxRegime} onChange={(e) => set({ taxRegime: e.target.value })}>
            {TAX.map(([v, l]) => <option key={v} value={v}>{l}</option>)}
          </select>
        </div>
        <div>
          <label htmlFor="policy-entity-type" className={labelClass}>Entity type</label>
          <select id="policy-entity-type" className={selectClass} disabled={!canEdit} value={policy.entityType ?? 'SoleProprietor'} onChange={(e) => set({ entityType: e.target.value })}>
            {ENTITY_TYPES.map(([v, l]) => <option key={v} value={v}>{l}</option>)}
          </select>
        </div>
        <div>
          <label htmlFor="policy-inventory" className={labelClass}>Inventory costing</label>
          <select id="policy-inventory" className={selectClass} disabled={!canEdit} value={policy.inventoryCostingMethod} onChange={(e) => set({ inventoryCostingMethod: e.target.value })}>
            {INVENTORY.map(([v, l]) => <option key={v} value={v}>{l}</option>)}
          </select>
        </div>
      </div>

      {message && (
        <div role={message.kind === 'error' ? 'alert' : 'status'} className={`flex items-start gap-2 p-3 rounded-xl text-xs font-medium ${message.kind === 'error' ? 'bg-rose-500/10 text-rose-400' : 'bg-emerald-500/10 text-emerald-400'}`}>
          {message.kind === 'error' ? <AlertCircle className="w-4 h-4 mt-0.5" aria-hidden="true" /> : <CheckCircle2 className="w-4 h-4 mt-0.5" aria-hidden="true" />}
          <span>{message.text}</span>
        </div>
      )}

      {canEdit && (
        <div className="flex justify-end">
          <button type="submit" disabled={busy} className="btn-primary !h-11">
            <Save className="w-4 h-4" aria-hidden="true" /> {busy ? 'Saving…' : 'Save policy'}
          </button>
        </div>
      )}
    </form>
  );
};
