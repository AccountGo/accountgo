import React, { useCallback, useEffect, useState } from 'react';
import { Link, useOutletContext } from 'react-router-dom';
import { AlertTriangle, Landmark, Loader2, Plus } from 'lucide-react';
import { PageHeader, HeaderButton } from '../components/PageHeader';
import { BankAccountFormDialog } from '../components/BankAccountFormDialog';
import { CashHistoryStep } from '../components/CashHistoryStep';
import { useAuth } from '../auth/AuthContext';
import { useTenantContext } from '../components/Gating';
import { needsCashHistoryStep } from '../lib/bankingForm';
import type { TenantContextType } from '../types';
import type { BankAccount, BankAccountsList, UnassignedCash } from '../types/banking';
import { bankTypeLabel } from '../types/banking';

const Banking: React.FC = () => {
  const { currentOrg } = useOutletContext<TenantContextType>();
  const { authFetch } = useAuth();
  const { hasModule, hasPermission } = useTenantContext();
  const hasFinancials = hasModule('financials');
  const canManage = hasPermission('banking.accounts.manage');

  const [data, setData] = useState<BankAccountsList | null>(null);
  const [includeArchived, setIncludeArchived] = useState(false);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [formOpen, setFormOpen] = useState(false);
  const [editAccount, setEditAccount] = useState<BankAccount | undefined>();
  const [cashStep, setCashStep] = useState<{
    groups: UnassignedCash[];
    pendingName: string;
  } | null>(null);
  const [adoptHint, setAdoptHint] = useState<number | null>(null);

  const formatCurrency = useCallback(
    (n: number) =>
      new Intl.NumberFormat(undefined, {
        style: 'currency',
        currency: currentOrg.currencyCode || 'USD',
      }).format(n),
    [currentOrg.currencyCode],
  );

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const res = await authFetch(`/api/Banking/Accounts?includeArchived=${includeArchived}`);
      if (!res.ok) {
        const body = await res.json().catch(() => null);
        throw new Error(body?.message || 'Could not load bank accounts.');
      }
      setData(await res.json());
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'Could not load bank accounts.');
      setData(null);
    } finally {
      setLoading(false);
    }
  }, [authFetch, includeArchived]);

  useEffect(() => {
    void load();
  }, [load]);

  const startAdd = async () => {
    setEditAccount(undefined);
    setAdoptHint(null);
    if (canManage) {
      const hist = await authFetch('/api/Banking/Accounts/CashHistory');
      if (hist.ok) {
        const groups: UnassignedCash[] = await hist.json();
        const activeCount = data?.accounts.filter((a) => !a.archivedAt).length ?? 0;
        if (activeCount === 0 && needsCashHistoryStep(groups)) {
          setCashStep({ groups, pendingName: 'New account' });
          return;
        }
        if (activeCount === 0 && groups.length === 1) {
          setAdoptHint(groups[0].accountId);
        }
      }
    }
    setFormOpen(true);
  };

  const accounts = data?.accounts ?? [];
  const unassigned = data?.unassignedCash ?? [];

  return (
    <div className="space-y-6">
      <PageHeader
        title="Banking"
        subtitle="Bank and cash accounts for deposits, payments, and reconciliation."
        actions={
          canManage ? (
            <HeaderButton label="Add bank account" icon={Plus} variant="primary" onClick={() => void startAdd()} />
          ) : undefined
        }
      />

      {error && (
        <div className="p-4 rounded-xl bg-rose-500/10 border border-rose-500/20 text-rose-600 dark:text-rose-400 text-sm flex items-start gap-2">
          <AlertTriangle className="w-4 h-4 mt-0.5 shrink-0" />
          <span>{error}</span>
        </div>
      )}

      <div className="flex items-center gap-2">
        <button
          type="button"
          onClick={() => setIncludeArchived(false)}
          className={`h-8 px-3 rounded-full text-xs font-semibold ${!includeArchived ? 'bg-[var(--brand-primary)] text-white' : 'bg-[var(--bg-subtle)] text-[var(--text-secondary)]'}`}
        >
          Active
        </button>
        <button
          type="button"
          onClick={() => setIncludeArchived(true)}
          className={`h-8 px-3 rounded-full text-xs font-semibold ${includeArchived ? 'bg-[var(--brand-primary)] text-white' : 'bg-[var(--bg-subtle)] text-[var(--text-secondary)]'}`}
        >
          Include archived
        </button>
      </div>

      {loading ? (
        <div className="flex items-center gap-2 text-sm text-[var(--text-muted)]">
          <Loader2 className="w-4 h-4 animate-spin" /> Loading accounts…
        </div>
      ) : accounts.length === 0 && unassigned.length === 0 ? (
        <div className="rounded-xl border border-dashed border-[var(--border-subtle)] bg-[var(--bg-surface)] p-10 text-center space-y-3">
          <Landmark className="w-8 h-8 mx-auto text-[var(--text-muted)]" />
          <p className="text-sm font-semibold text-[var(--text-main)]">No bank accounts yet</p>
          <p className="text-xs text-[var(--text-muted)] max-w-sm mx-auto">
            Add the accounts you deposit into and pay from so each payment lands in the right place.
          </p>
          {canManage && (
            <button
              type="button"
              onClick={() => void startAdd()}
              className="inline-flex items-center gap-2 h-10 px-4 rounded-xl text-sm font-semibold text-white bg-[var(--brand-primary)]"
            >
              <Plus className="w-4 h-4" />
              Add bank account
            </button>
          )}
        </div>
      ) : (
        <div className="rounded-xl border border-[var(--border-subtle)] bg-[var(--bg-surface)] overflow-hidden shadow-sm">
          <ul className="divide-y divide-[var(--border-subtle)]">
            {accounts.map((a) => (
              <li key={a.id}>
                <Link
                  to={`/org/${currentOrg.slug}/banking/${a.id}`}
                  className="flex flex-col sm:flex-row sm:items-center gap-2 sm:gap-4 px-4 py-4 hover:bg-[var(--bg-subtle)] transition"
                >
                  <div className="min-w-0 flex-1">
                    <div className="flex flex-wrap items-center gap-2">
                      <span className="font-semibold text-[var(--text-main)]">{a.name}</span>
                      <span className="text-[10px] uppercase tracking-wider px-2 py-0.5 rounded-full bg-[var(--bg-canvas)] text-[var(--text-muted)]">
                        {bankTypeLabel(a.type)}
                      </span>
                      {a.isDefaultForReceipts && (
                        <span className="text-[10px] font-semibold px-2 py-0.5 rounded-full bg-emerald-500/15 text-emerald-600">Receipts</span>
                      )}
                      {a.isDefaultForPayments && (
                        <span className="text-[10px] font-semibold px-2 py-0.5 rounded-full bg-sky-500/15 text-sky-600">Payments</span>
                      )}
                      {a.archivedAt && (
                        <span className="text-[10px] font-semibold px-2 py-0.5 rounded-full bg-rose-500/15 text-rose-600">Archived</span>
                      )}
                    </div>
                    <p className="text-xs text-[var(--text-muted)] mt-0.5">
                      {[a.bankName, a.accountNumberMasked].filter(Boolean).join(' · ') || 'No number on file'}
                      {hasFinancials && a.reconciledThrough
                        ? ` · Reconciled through ${a.reconciledThrough.slice(0, 10)}`
                        : ''}
                    </p>
                  </div>
                  <div className="font-mono font-bold text-[var(--text-main)] sm:text-right">
                    {formatCurrency(a.bookBalance)}
                  </div>
                </Link>
              </li>
            ))}
            {unassigned.map((u) => (
              <li key={`u-${u.accountId}`} className="px-4 py-4 bg-[var(--bg-subtle)]/50">
                <div className="flex flex-col sm:flex-row sm:items-center gap-2 sm:gap-4">
                  <div className="min-w-0 flex-1">
                    <p className="font-semibold text-[var(--text-main)]">Unassigned cash</p>
                    <p className="text-xs text-[var(--text-muted)] mt-0.5">
                      {hasFinancials && u.accountCode ? `${u.accountCode} · ` : ''}
                      {u.description}
                    </p>
                  </div>
                  <div className="flex items-center gap-3">
                    <span className="font-mono font-bold text-[var(--text-main)]">{formatCurrency(u.balance)}</span>
                    {canManage && (
                      <button
                        type="button"
                        onClick={() => {
                          setAdoptHint(u.accountId);
                          setEditAccount(undefined);
                          setFormOpen(true);
                        }}
                        className="h-8 px-3 rounded-xl text-xs font-semibold border border-[var(--border-subtle)] bg-[var(--bg-surface)]"
                      >
                        Assign to a bank
                      </button>
                    )}
                  </div>
                </div>
              </li>
            ))}
          </ul>
        </div>
      )}

      {formOpen && (
        <BankAccountFormDialog
          account={editAccount}
          hasFinancials={hasFinancials}
          defaultAdoptAccountId={adoptHint}
          onSaved={async () => {
            setFormOpen(false);
            setAdoptHint(null);
            await load();
          }}
          onClose={() => {
            setFormOpen(false);
            setAdoptHint(null);
          }}
        />
      )}

      {cashStep && (
        <CashHistoryStep
          groups={cashStep.groups}
          bankName={cashStep.pendingName}
          showCodes={hasFinancials}
          formatCurrency={formatCurrency}
          onAssign={(accountId) => {
            setCashStep(null);
            setAdoptHint(accountId);
            setFormOpen(true);
          }}
          onSkipAll={() => {
            setCashStep(null);
            setFormOpen(true);
          }}
          onClose={() => setCashStep(null)}
        />
      )}
    </div>
  );
};

export default Banking;
