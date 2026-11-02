import React, { useEffect, useId, useMemo, useState } from 'react';
import { Receipt, X } from 'lucide-react';
import { ModalShell } from './ModalShell';
import { useAuth } from '../auth/AuthContext';
import { useCapabilities } from '../lib/entitlements';
import { useTenantContext } from './Gating';
import { matchVendorIdByPayee } from '../lib/statementImport';
import type { Bill, Vendor } from '../types/purchasing';
import type {
  BankStatementLine,
  StatementExpenseCategories,
  StatementExpenseCategory,
} from '../types/banking';

const labelClass = 'block text-xs font-semibold uppercase tracking-wider text-[var(--text-muted)] mb-1';
const inputClass =
  'w-full h-10 px-3 rounded-xl border border-[var(--border-subtle)] bg-[var(--bg-subtle)] text-sm text-[var(--text-main)]';

type Mode = 'expense' | 'payBill' | 'draft';

interface WorkspaceTax {
  id: number;
  name: string;
  rate: number;
}

interface StatementLineExpenseDialogProps {
  line: BankStatementLine;
  /** Extra money-out lines to record with the same vendor + category (#436 I4). */
  additionalLineIds?: number[];
  formatCurrency: (n: number) => string;
  initialMode?: Mode;
  bankAccountName?: string;
  onSaved: () => void | Promise<void>;
  onClose: () => void;
}

/** Record expense or pay an open bill from an unmatched money-out statement line (#436 I2). */
export const StatementLineExpenseDialog: React.FC<StatementLineExpenseDialogProps> = ({
  line,
  additionalLineIds = [],
  formatCurrency,
  initialMode = 'expense',
  bankAccountName,
  onSaved,
  onClose,
}) => {
  const { authFetch } = useAuth();
  const { hasPermission } = useTenantContext();
  const { canModule, canWrite, canCreate } = useCapabilities();
  const purchasesWritable = canModule('purchases') && canWrite('purchases');
  const canRecordExpense =
    purchasesWritable &&
    hasPermission('banking.accounts.manage') &&
    hasPermission('purchases.bills.create') &&
    hasPermission('purchases.bills.post') &&
    hasPermission('purchases.payments.create');
  const canPayOpenBill =
    purchasesWritable &&
    hasPermission('banking.accounts.manage') &&
    hasPermission('purchases.payments.create');
  const canCreateVendor = hasPermission('purchases.vendors.manage');
  const canDraft = purchasesWritable && hasPermission('banking.accounts.manage') &&
    hasPermission('purchases.bills.create') && canCreate('bills') && !line.draftBillId;
  const titleId = useId();
  const amount = Math.abs(line.amount);
  const bulkLineIds = useMemo(
    () => Array.from(new Set([line.id, ...additionalLineIds])),
    [line.id, additionalLineIds],
  );
  const [mode, setMode] = useState<Mode>(() => {
    if (initialMode === 'draft' && canDraft) return 'draft';
    if (initialMode === 'payBill' && canPayOpenBill) return 'payBill';
    if (canRecordExpense && !line.draftBillId) return 'expense';
    return canPayOpenBill ? 'payBill' : canDraft ? 'draft' : 'expense';
  });
  const isBulk = mode === 'expense' && bulkLineIds.length > 1;
  const [categories, setCategories] = useState<StatementExpenseCategories | null>(null);
  const [vendors, setVendors] = useState<Vendor[]>([]);
  const [bills, setBills] = useState<Bill[]>([]);
  const [taxes, setTaxes] = useState<WorkspaceTax[]>([]);
  const [vendorChoice, setVendorChoice] = useState('');
  const [expenseAccountId, setExpenseAccountId] = useState('');
  const [taxId, setTaxId] = useState('');
  const [billId, setBillId] = useState('');
  const [memo, setMemo] = useState(line.memo || '');
  const [description, setDescription] = useState(line.payee || line.memo || '');
  const [vendorInvoiceNo, setVendorInvoiceNo] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [loading, setLoading] = useState(true);

  const createVendorValue = useMemo(() => {
    const name = (line.payee || line.memo || '').trim();
    return name ? `__new__:${name}` : '';
  }, [line.payee, line.memo]);

  useEffect(() => {
    let cancelled = false;
    void (async () => {
      setLoading(true);
      try {
        const descQ = encodeURIComponent(line.payee || line.memo || '');
        const [catRes, vendorRes, billRes, taxRes, ruleRes] = await Promise.all([
          authFetch('/api/Banking/StatementExpenseCategories'),
          authFetch('/api/Purchasing/Vendors'),
          authFetch('/api/Purchasing/Bills'),
          authFetch('/api/Tax/Taxes'),
          authFetch(`/api/Banking/StatementDescriptionRule?description=${descQ}`),
        ]);
        if (cancelled) return;
        if (catRes.ok) setCategories((await catRes.json()) as StatementExpenseCategories);
        let ruleVendorId: number | null = null;
        let ruleAccountId: number | null = null;
        if (ruleRes.status === 200) {
          const rule = await ruleRes.json();
          if (rule?.vendorId) ruleVendorId = Number(rule.vendorId);
          if (rule?.expenseAccountId) ruleAccountId = Number(rule.expenseAccountId);
        }
        if (vendorRes.ok) {
          const list = (await vendorRes.json()) as Vendor[];
          setVendors(list);
          const matched = ruleVendorId ?? matchVendorIdByPayee(line.payee || line.memo, list);
          if (matched != null) setVendorChoice(String(matched));
          else if (createVendorValue) setVendorChoice(createVendorValue);
        }
        if (ruleAccountId != null) setExpenseAccountId(String(ruleAccountId));
        if (billRes.ok) {
          const list = (await billRes.json()) as Bill[];
          const open = list.filter((b) => !b.voidedAt && b.approved && b.balanceDue > 0.005);
          const eligible = line.draftBillId ? open.filter(b => b.id === line.draftBillId) : open;
          setBills(eligible);
          const exact = eligible.find((b) => Math.abs(b.balanceDue - amount) < 0.005);
          if (exact) setBillId(String(exact.id));
        }
        if (taxRes.ok) {
          const data = await taxRes.json();
          setTaxes((data?.taxes ?? []) as WorkspaceTax[]);
        }
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [authFetch, amount, createVendorValue, line.memo, line.payee, line.draftBillId]);

  useEffect(() => {
    if (!categories || expenseAccountId) return;
    const first = categories.curated[0] ?? categories.more[0];
    if (first) setExpenseAccountId(String(first.accountId));
  }, [categories, expenseAccountId]);

  const categoryOptions: StatementExpenseCategory[] = useMemo(() => {
    if (!categories) return [];
    return [...categories.curated, ...categories.more];
  }, [categories]);

  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    setBusy(true);
    setError(null);
    try {
      if (mode === 'expense' || mode === 'draft') {
        if (mode === 'draft' && !canDraft) throw new Error('You cannot create a draft for this line.');
        if (mode === 'expense' && (!canRecordExpense || line.draftBillId)) throw new Error('Review the linked draft before recording a payment.');
        if (!expenseAccountId) throw new Error('Pick a category.');
        if (!vendorChoice) throw new Error('Pick a vendor or create one from the bank description.');
        const body: Record<string, unknown> = {
          expenseAccountId: Number(expenseAccountId),
          taxId: taxId ? Number(taxId) : null,
          description: description.trim() || null,
          vendorInvoiceNo: vendorInvoiceNo.trim() || null,
          memo: memo.trim() || null,
          rememberRule: true,
        };
        if (vendorChoice.startsWith('__new__:')) {
          body.newVendor = { name: vendorChoice.slice('__new__:'.length) };
        } else {
          body.vendorId = Number(vendorChoice);
        }
        if (isBulk) {
          const res = await authFetch('/api/Banking/StatementLines/BulkRecordExpense', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
              lineIds: bulkLineIds,
              expenseAccountId: body.expenseAccountId,
              taxId: body.taxId,
              memo: body.memo,
              rememberRule: true,
              vendorId: body.vendorId ?? null,
              newVendor: body.newVendor ?? null,
            }),
          });
          if (!res.ok) {
            const errBody = await res.json().catch(() => null);
            throw new Error(errBody?.message || 'Could not record the expenses.');
          }
        } else {
          const res = await authFetch(`/api/Banking/StatementLines/${line.id}/${mode === 'draft' ? 'DraftExpense' : 'RecordExpense'}`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(body),
          });
          if (!res.ok) {
            const errBody = await res.json().catch(() => null);
            throw new Error(errBody?.message || 'Could not record the expense.');
          }
        }
      } else {
        if (!billId) throw new Error('Pick an open bill to pay.');
        const res = await authFetch(`/api/Banking/StatementLines/${line.id}/PayOpenBill`, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({
            billId: Number(billId),
            memo: memo.trim() || null,
          }),
        });
        if (!res.ok) {
          const errBody = await res.json().catch(() => null);
          throw new Error(errBody?.message || 'Could not pay the bill.');
        }
      }
      await onSaved();
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'Could not save.');
    } finally {
      setBusy(false);
    }
  };

  return (
    <ModalShell
      open
      onClose={() => {
        if (!busy) onClose();
      }}
      closeOnEscape={!busy}
      closeOnBackdrop={!busy}
      bare
      aria-labelledby={titleId}
      panelClassName="bg-[var(--bg-surface)] rounded-2xl max-w-lg w-full max-h-[min(90vh,calc(100dvh-2rem))] overflow-y-auto border border-[var(--border-subtle)] shadow-2xl"
    >
      <div className="px-6 py-4 border-b border-[var(--border-subtle)] flex items-center justify-between">
        <div className="flex items-center gap-2">
          <Receipt className="w-5 h-5 text-[var(--brand-primary)]" />
          <h3 id={titleId} className="font-bold text-[var(--text-main)] text-base">
            {mode === 'draft' ? 'Save bill draft' : mode === 'expense'
              ? isBulk
                ? `Record expense (${bulkLineIds.length} lines)`
                : 'Record expense'
              : 'Pay open bill'}
          </h3>
        </div>
        <button
          type="button"
          onClick={onClose}
          disabled={busy}
          aria-label="Close"
          className="text-[var(--text-muted)] hover:text-[var(--text-main)] p-1 rounded-lg"
        >
          <X className="w-5 h-5" />
        </button>
      </div>

      <form onSubmit={(e) => void submit(e)} className="p-6 space-y-4">
        <p className="text-sm text-[var(--text-secondary)]">
          {String(line.postedDate).slice(0, 10)} · {line.payee || line.memo || 'Statement line'} ·{' '}
          <span className="font-mono font-semibold text-[var(--text-main)]">{formatCurrency(line.amount)}</span>
        </p>

        {!isBulk && (
          <div className="flex gap-1 p-1 rounded-xl bg-[var(--bg-subtle)]">
            {(
              [
                canRecordExpense && !line.draftBillId ? (['expense', 'Record expense'] as const) : null,
                canDraft ? (['draft', 'Save draft'] as const) : null,
                canPayOpenBill ? (['payBill', 'Pay open bill'] as const) : null,
              ] as Array<readonly [Mode, string] | null>
            )
              .filter((entry): entry is readonly [Mode, string] => entry != null)
              .map(([value, label]) => (
              <button
                key={value}
                type="button"
                disabled={busy}
                onClick={() => setMode(value)}
                className={`flex-1 h-9 rounded-lg text-xs font-semibold ${
                  mode === value
                    ? 'bg-[var(--brand-primary)] text-white'
                    : 'text-[var(--text-secondary)] hover:bg-[var(--bg-surface-hover)]'
                }`}
              >
                {label}
              </button>
            ))}
          </div>
        )}

        {error && (
          <div role="alert" className="p-3 bg-rose-500/10 border border-rose-500/20 rounded-xl text-xs text-rose-500">
            {error}
          </div>
        )}

        {loading ? (
          <p className="text-sm text-[var(--text-muted)]">Loading…</p>
        ) : mode === 'expense' || mode === 'draft' ? (
          <>
            <div>
              <label htmlFor={`${titleId}-vendor`} className={labelClass}>
                Vendor
              </label>
              <select
                id={`${titleId}-vendor`}
                required
                value={vendorChoice}
                onChange={(e) => setVendorChoice(e.target.value)}
                className={inputClass}
              >
                <option value="">Select vendor…</option>
                {canCreateVendor && createVendorValue && (
                  <option value={createVendorValue}>
                    Create vendor '{createVendorValue.slice('__new__:'.length)}'
                  </option>
                )}
                {vendors.map((v) => (
                  <option key={v.id} value={v.id}>
                    {v.name}
                  </option>
                ))}
              </select>
            </div>

            <div>
              <label htmlFor={`${titleId}-description`} className={labelClass}>Description</label>
              <input id={`${titleId}-description`} className={inputClass} required value={description} onChange={e => setDescription(e.target.value)} />
            </div>
            <div>
              <label htmlFor={`${titleId}-invoice`} className={labelClass}>Vendor invoice / reference (if supplied)</label>
              <input id={`${titleId}-invoice`} className={inputClass} value={vendorInvoiceNo} onChange={e => setVendorInvoiceNo(e.target.value)} />
            </div>
            <div>
              <label htmlFor={`${titleId}-category`} className={labelClass}>
                Category
              </label>
              <select
                id={`${titleId}-category`}
                required
                value={expenseAccountId}
                onChange={(e) => setExpenseAccountId(e.target.value)}
                className={inputClass}
              >
                <option value="">Select category…</option>
                {categories && categories.curated.length > 0 && (
                  <optgroup label="Common">
                    {categories.curated.map((c) => (
                      <option key={c.accountId} value={c.accountId}>
                        {c.name}
                      </option>
                    ))}
                  </optgroup>
                )}
                {categories && categories.more.length > 0 && (
                  <optgroup label="More…">
                    {categories.more.map((c) => (
                      <option key={c.accountId} value={c.accountId}>
                        {c.name}
                      </option>
                    ))}
                  </optgroup>
                )}
                {!categories &&
                  categoryOptions.map((c) => (
                    <option key={c.accountId} value={c.accountId}>
                      {c.name}
                    </option>
                  ))}
              </select>
            </div>

            {taxes.length > 0 && (
              <div>
                <label htmlFor={`${titleId}-tax`} className={labelClass}>
                  Tax (optional)
                </label>
                <select
                  id={`${titleId}-tax`}
                  value={taxId}
                  onChange={(e) => setTaxId(e.target.value)}
                  className={inputClass}
                >
                  <option value="">No tax</option>
                  {taxes.map((t) => (
                    <option key={t.id} value={t.id}>
                      {t.name} ({t.rate}%)
                    </option>
                  ))}
                </select>
              </div>
            )}
          </>
        ) : (
          <div>
            <label htmlFor={`${titleId}-bill`} className={labelClass}>
              Open bill
            </label>
            <select
              id={`${titleId}-bill`}
              required
              value={billId}
              onChange={(e) => setBillId(e.target.value)}
              className={inputClass}
            >
              <option value="">Select bill…</option>
              {bills.map((b) => (
                <option key={b.id} value={b.id}>
                  {b.no || `#${b.id}`} · {b.vendorName || 'Vendor'} · due {formatCurrency(b.balanceDue)}
                </option>
              ))}
            </select>
            {bills.length === 0 && (
              <p className="mt-1 text-xs text-[var(--text-muted)]">No open bills with a balance due.</p>
            )}
          </div>
        )}

        <div>
          <label htmlFor={`${titleId}-memo`} className={labelClass}>
            Memo
          </label>
          <input id={`${titleId}-memo`} value={memo} onChange={(e) => setMemo(e.target.value)} className={inputClass} />
        </div>

        <p role="note" className="rounded-xl bg-[var(--bg-subtle)] p-3 text-xs">
          {mode === 'draft'
            ? 'Save one bill draft only. No approval, payment, posting or statement match. This line stays in Needs review.'
            : mode === 'expense'
              ? `Record and match explicitly creates and approves a bill, records the already-made ${formatCurrency(amount)} payment, and matches this line.`
              : `Record the already-made ${formatCurrency(amount)} payment against ${bills.find(b => b.id === Number(billId))?.no || 'the selected bill'} and match this line.`}
          {' '}Date: {line.postedDate.slice(0, 10)}. Bank: {bankAccountName || `Account ${line.bankId}`}.
          {' '}No payment instruction is sent to any provider. {taxId ? 'The selected tax is included in the bank gross amount.' : 'No tax selected.'}
        </p>
        <div className="flex justify-end gap-2 pt-2">
          <button
            type="button"
            disabled={busy}
            onClick={onClose}
            className="h-10 px-4 rounded-xl text-xs font-semibold text-[var(--text-secondary)] hover:bg-[var(--bg-surface-hover)]"
          >
            Cancel
          </button>
          <button
            type="submit"
            disabled={busy || loading || (mode === 'draft' ? !canDraft : mode === 'expense' ? !canRecordExpense || !!line.draftBillId : !canPayOpenBill)}
            className="h-10 px-4 rounded-xl text-xs font-semibold text-white bg-[var(--brand-primary)] disabled:opacity-50"
          >
            {busy ? 'Saving…' : mode === 'draft' ? 'Save draft' : mode === 'expense' ? 'Record and match' : 'Record payment and match'}
          </button>
        </div>
      </form>
    </ModalShell>
  );
};

export default StatementLineExpenseDialog;
