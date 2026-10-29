import React, { useEffect, useId, useState } from 'react';
import { Landmark, Loader2, X } from 'lucide-react';
import { useAuth } from '../auth/AuthContext';
import { ModalShell } from './ModalShell';
import { buildCreatePayload } from '../lib/bankingForm';
import { localToday } from '../lib/businessDays';
import type { BankAccount, CreateBankAccountPayload, EligiblePostingAccount, UpdateBankAccountPayload } from '../types/banking';

const inputClass =
  'w-full px-3.5 py-2.5 text-sm bg-[var(--bg-subtle)] border border-[var(--border-subtle)] rounded-xl focus:ring-2 focus:ring-[var(--brand-ring)] focus:outline-none text-[var(--text-main)] placeholder-[var(--text-muted)] transition';
const labelClass = 'block text-xs font-semibold uppercase tracking-wider text-[var(--text-muted)] mb-1';

export const BankAccountFormDialog: React.FC<{
  account?: BankAccount;
  hasFinancials: boolean;
  /** Pre-selected adopt when taking over a single cash-history group. */
  defaultAdoptAccountId?: number | null;
  onSaved: (account: BankAccount, created: boolean) => void;
  onClose: () => void;
}> = ({ account, hasFinancials, defaultAdoptAccountId = null, onSaved, onClose }) => {
  const { authFetch } = useAuth();
  const titleId = useId();
  const editing = !!account;

  const [name, setName] = useState(account?.name ?? '');
  const [type, setType] = useState(account?.type ?? 1);
  const [bankName, setBankName] = useState(account?.bankName ?? '');
  const [accountNumber, setAccountNumber] = useState(account?.accountNumber ?? '');
  const [routingNumber, setRoutingNumber] = useState(account?.routingNumber ?? '');
  const [iban, setIban] = useState(account?.iban ?? '');
  const [swift, setSwift] = useState(account?.swift ?? '');
  const [isDefaultForReceipts, setIsDefaultForReceipts] = useState(account?.isDefaultForReceipts ?? false);
  const [isDefaultForPayments, setIsDefaultForPayments] = useState(account?.isDefaultForPayments ?? false);
  const [openingBalance, setOpeningBalance] = useState(
    account?.openingBalance != null ? String(account.openingBalance) : '',
  );
  const [openingBalanceDate, setOpeningBalanceDate] = useState(
    account?.openingBalanceDate ? account.openingBalanceDate.slice(0, 10) : localToday(),
  );
  const [postingMode, setPostingMode] = useState<'new' | 'link' | 'adopt'>(
    defaultAdoptAccountId ? 'adopt' : 'new',
  );
  const [postingAccountId, setPostingAccountId] = useState<number | ''>('');
  const [eligible, setEligible] = useState<EligiblePostingAccount[] | null>(null);
  const [isSaving, setIsSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!hasFinancials || editing) return;
    let cancelled = false;
    (async () => {
      const res = await authFetch('/api/Banking/Accounts/EligiblePostingAccounts');
      if (cancelled) return;
      if (!res.ok) {
        setEligible([]);
        return;
      }
      const data = await res.json();
      setEligible(Array.isArray(data) ? data : []);
    })();
    return () => {
      cancelled = true;
    };
  }, [authFetch, hasFinancials, editing]);

  // Taken-over (adopt) accounts must use books start as the OB as-of default — the server requires
  // as-of ≤ the first existing line. Brand-new accounts keep today's date.
  useEffect(() => {
    if (editing || !defaultAdoptAccountId) return;
    let cancelled = false;
    (async () => {
      const res = await authFetch('/api/organizations/current/accounting-policy');
      if (!res.ok || cancelled) return;
      const policy = await res.json();
      const booksStart = typeof policy?.booksStartDate === 'string' ? policy.booksStartDate.slice(0, 10) : null;
      if (booksStart) setOpeningBalanceDate(booksStart);
    })();
    return () => {
      cancelled = true;
    };
  }, [authFetch, editing, defaultAdoptAccountId]);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setIsSaving(true);
    setError(null);
    try {
      if (editing && account) {
        const body: UpdateBankAccountPayload = {
          name: name.trim(),
          type,
          bankName: bankName.trim() || null,
          accountNumber: accountNumber.trim() || null,
          routingNumber: routingNumber.trim() || null,
          iban: iban.trim() || null,
          swift: swift.trim() || null,
          isDefaultForReceipts,
          isDefaultForPayments,
          openingBalance: openingBalance.trim() === '' ? null : Number(openingBalance),
          openingBalanceDate: openingBalance.trim() === '' ? null : openingBalanceDate,
        };
        if (Number.isNaN(body.openingBalance as number) && openingBalance.trim() !== '') {
          throw new Error('Opening balance must be a number.');
        }
        const res = await authFetch(`/api/Banking/Accounts/${account.id}`, {
          method: 'PUT',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify(body),
        });
        const data = await res.json().catch(() => null);
        if (!res.ok) throw new Error(data?.message || 'Could not update the bank account.');
        onSaved(data as BankAccount, false);
        return;
      }

      const payload = buildCreatePayload({
        name,
        type,
        bankName,
        accountNumber,
        routingNumber,
        iban,
        swift,
        isDefaultForReceipts,
        isDefaultForPayments,
        openingBalance,
        openingBalanceDate,
        adoptAccountId:
          postingMode === 'adopt'
            ? defaultAdoptAccountId
            : postingMode === 'link' && postingAccountId
              ? Number(postingAccountId)
              : null,
        postingAccountId: postingMode === 'link' && postingAccountId ? Number(postingAccountId) : null,
      });

      // Link existing uses postingAccountId; adopt uses adoptAccountId only.
      if (postingMode === 'link') {
        delete (payload as CreateBankAccountPayload).adoptAccountId;
      } else if (postingMode === 'adopt') {
        delete (payload as CreateBankAccountPayload).postingAccountId;
      } else {
        delete (payload as CreateBankAccountPayload).adoptAccountId;
        delete (payload as CreateBankAccountPayload).postingAccountId;
      }

      const res = await authFetch('/api/Banking/Accounts', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(payload),
      });
      const data = await res.json().catch(() => null);
      if (!res.ok) throw new Error(data?.message || 'Could not create the bank account.');
      onSaved(data as BankAccount, true);
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'Something went wrong.');
      setIsSaving(false);
    }
  };

  return (
    <ModalShell
      open
      onClose={() => {
        if (!isSaving) onClose();
      }}
      closeOnEscape={!isSaving}
      closeOnBackdrop={!isSaving}
      bare
      aria-labelledby={titleId}
      panelClassName="bg-[var(--bg-surface)] rounded-2xl max-w-lg w-full max-h-[min(90vh,calc(100dvh-2rem))] overflow-y-auto border border-[var(--border-subtle)] shadow-2xl"
    >
      <div className="px-6 py-4 border-b border-[var(--border-subtle)] flex items-center justify-between">
        <div className="flex items-center gap-2">
          <Landmark className="w-5 h-5 text-[var(--brand-primary)]" />
          <h3 id={titleId} className="font-bold text-[var(--text-main)] text-base">
            {editing ? 'Edit bank account' : 'Add bank account'}
          </h3>
        </div>
        <button
          type="button"
          onClick={onClose}
          disabled={isSaving}
          aria-label="Close"
          className="text-[var(--text-muted)] hover:text-[var(--text-main)] p-1 rounded-lg"
        >
          <X className="w-5 h-5" />
        </button>
      </div>

      <form onSubmit={handleSubmit} className="p-6 space-y-4">
        {error && (
          <div role="alert" className="p-3 bg-rose-500/10 border border-rose-500/20 rounded-xl text-xs text-rose-500">
            {error}
          </div>
        )}

        <div>
          <label htmlFor={`${titleId}-name`} className={labelClass}>
            Display name <span className="text-rose-500">*</span>
          </label>
          <input id={`${titleId}-name`} required value={name} onChange={(e) => setName(e.target.value)} className={inputClass} placeholder="Operating checking" />
        </div>

        <div>
          <label htmlFor={`${titleId}-type`} className={labelClass}>
            Type
          </label>
          <select id={`${titleId}-type`} value={type} onChange={(e) => setType(Number(e.target.value))} className={inputClass}>
            <option value={1}>Checking</option>
            <option value={2}>Savings</option>
            <option value={3}>Cash on hand</option>
          </select>
        </div>

        <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
          <div>
            <label htmlFor={`${titleId}-bank`} className={labelClass}>
              Bank / institution
            </label>
            <input id={`${titleId}-bank`} value={bankName} onChange={(e) => setBankName(e.target.value)} className={inputClass} />
          </div>
          <div>
            <label htmlFor={`${titleId}-number`} className={labelClass}>
              Account number
            </label>
            <input id={`${titleId}-number`} value={accountNumber} onChange={(e) => setAccountNumber(e.target.value)} className={inputClass} autoComplete="off" />
          </div>
        </div>

        <div className="grid grid-cols-1 sm:grid-cols-3 gap-3">
          <div>
            <label htmlFor={`${titleId}-routing`} className={labelClass}>
              Routing / sort
            </label>
            <input id={`${titleId}-routing`} value={routingNumber} onChange={(e) => setRoutingNumber(e.target.value)} className={inputClass} />
          </div>
          <div>
            <label htmlFor={`${titleId}-iban`} className={labelClass}>
              IBAN
            </label>
            <input id={`${titleId}-iban`} value={iban} onChange={(e) => setIban(e.target.value)} className={inputClass} />
          </div>
          <div>
            <label htmlFor={`${titleId}-swift`} className={labelClass}>
              SWIFT
            </label>
            <input id={`${titleId}-swift`} value={swift} onChange={(e) => setSwift(e.target.value)} className={inputClass} />
          </div>
        </div>

        <div className="flex flex-col gap-2">
          <label className="inline-flex items-center gap-2 text-sm text-[var(--text-main)]">
            <input type="checkbox" checked={isDefaultForReceipts} onChange={(e) => setIsDefaultForReceipts(e.target.checked)} />
            Default for receiving payments
          </label>
          <label className="inline-flex items-center gap-2 text-sm text-[var(--text-main)]">
            <input type="checkbox" checked={isDefaultForPayments} onChange={(e) => setIsDefaultForPayments(e.target.checked)} />
            Default for paying bills
          </label>
        </div>

        <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
          <div>
            <label htmlFor={`${titleId}-ob`} className={labelClass}>
              Opening balance
            </label>
            <input
              id={`${titleId}-ob`}
              type="number"
              step="0.01"
              value={openingBalance}
              onChange={(e) => setOpeningBalance(e.target.value)}
              className={`${inputClass} font-mono`}
              placeholder="Leave empty if none"
            />
          </div>
          <div>
            <label htmlFor={`${titleId}-obd`} className={labelClass}>
              As of
            </label>
            <input
              id={`${titleId}-obd`}
              type="date"
              value={openingBalanceDate}
              onChange={(e) => setOpeningBalanceDate(e.target.value)}
              className={inputClass}
              disabled={!openingBalance.trim()}
            />
          </div>
        </div>
        {defaultAdoptAccountId && !editing && (
          <p className="text-xs text-amber-600 dark:text-amber-400">
            This account already has ledger history. Opening balance is optional — leave it empty unless you need a starting balance before that history.
          </p>
        )}

        {hasFinancials && !editing && (
          <div className="space-y-2">
            <p className={labelClass}>Posting account</p>
            <div className="flex flex-col gap-2 text-sm text-[var(--text-main)]">
              <label className="inline-flex items-center gap-2">
                <input type="radio" name="postingMode" checked={postingMode === 'new'} onChange={() => setPostingMode('new')} />
                New cash account (recommended)
              </label>
              {defaultAdoptAccountId != null && (
                <label className="inline-flex items-center gap-2">
                  <input type="radio" name="postingMode" checked={postingMode === 'adopt'} onChange={() => setPostingMode('adopt')} />
                  Take over suggested cash account
                </label>
              )}
              <label className="inline-flex items-center gap-2">
                <input type="radio" name="postingMode" checked={postingMode === 'link'} onChange={() => setPostingMode('link')} />
                Link an existing unused cash account
              </label>
            </div>
            {postingMode === 'link' && (
              eligible === null ? (
                <div className="flex items-center gap-2 text-xs text-[var(--text-muted)]">
                  <Loader2 className="w-3.5 h-3.5 animate-spin" /> Loading accounts…
                </div>
              ) : (
                <select
                  value={postingAccountId}
                  onChange={(e) => setPostingAccountId(e.target.value ? Number(e.target.value) : '')}
                  className={inputClass}
                  required
                >
                  <option value="">Select account…</option>
                  {eligible.map((a) => {
                    const id = a.id;
                    const code = a.code ?? a.accountCode ?? '';
                    const nm = a.name ?? a.accountName ?? '';
                    return (
                      <option key={id} value={id}>
                        {code} · {nm}
                      </option>
                    );
                  })}
                </select>
              )
            )}
          </div>
        )}

        <div className="pt-2 flex items-center justify-end gap-3">
          <button
            type="button"
            onClick={onClose}
            disabled={isSaving}
            className="px-4 py-2.5 text-sm font-medium text-[var(--text-secondary)] bg-[var(--bg-subtle)] border border-[var(--border-subtle)] rounded-xl hover:bg-[var(--bg-surface-hover)]"
          >
            Cancel
          </button>
          <button
            type="submit"
            disabled={isSaving}
            className="inline-flex items-center gap-2 px-5 py-2.5 text-sm font-semibold text-white bg-[var(--brand-primary)] rounded-xl hover:opacity-90 disabled:opacity-50"
          >
            {isSaving && <Loader2 className="w-4 h-4 animate-spin" />}
            {editing ? 'Save' : 'Add account'}
          </button>
        </div>
      </form>
    </ModalShell>
  );
};
