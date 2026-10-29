import React, { useCallback, useEffect, useState } from 'react';
import { useOutletContext } from 'react-router-dom';
import { AlertTriangle, Loader2, Plus } from 'lucide-react';
import { PageHeader, HeaderButton } from '../components/PageHeader';
import { BankingTabs } from '../components/BankingTabs';
import { BankTransactionDialog } from '../components/BankTransactionDialog';
import { useAuth } from '../auth/AuthContext';
import { useTenantContext } from '../components/Gating';
import type { TenantContextType } from '../types';
import type { BankAccount, BankTransaction } from '../types/banking';
import { BANK_TRANSACTION_KIND_LABELS } from '../types/banking';

const BankingTransactionsPage: React.FC = () => {
  const { currentOrg } = useOutletContext<TenantContextType>();
  const { authFetch } = useAuth();
  const { hasPermission } = useTenantContext();
  const canCreate = hasPermission('banking.transactions.create');

  const [banks, setBanks] = useState<BankAccount[]>([]);
  const [bankId, setBankId] = useState<number | ''>('');
  const [rows, setRows] = useState<BankTransaction[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [dialogOpen, setDialogOpen] = useState(false);

  const formatCurrency = useCallback(
    (n: number) =>
      new Intl.NumberFormat(undefined, {
        style: 'currency',
        currency: currentOrg.currency || 'USD',
      }).format(n),
    [currentOrg.currency],
  );

  const loadBanks = useCallback(async () => {
    const res = await authFetch('/api/Banking/Accounts');
    if (!res.ok) throw new Error('Could not load bank accounts.');
    const body = await res.json();
    const accounts: BankAccount[] = (body.accounts ?? []).filter((a: BankAccount) => !a.archivedAt);
    setBanks(accounts);
    setBankId((prev) => (prev === '' && accounts[0] ? accounts[0].id : prev));
  }, [authFetch]);

  const loadTx = useCallback(async () => {
    if (bankId === '') {
      setRows([]);
      return;
    }
    const res = await authFetch(`/api/Banking/Transactions?bankId=${bankId}`);
    if (!res.ok) {
      const body = await res.json().catch(() => null);
      throw new Error(body?.message || 'Could not load transactions.');
    }
    const body = await res.json();
    setRows(body.transactions ?? []);
  }, [authFetch, bankId]);

  useEffect(() => {
    void (async () => {
      setLoading(true);
      setError(null);
      try {
        await loadBanks();
      } catch (err: unknown) {
        setError(err instanceof Error ? err.message : 'Could not load.');
      } finally {
        setLoading(false);
      }
    })();
  }, [loadBanks]);

  useEffect(() => {
    if (bankId === '') return;
    void (async () => {
      setError(null);
      try {
        await loadTx();
      } catch (err: unknown) {
        setError(err instanceof Error ? err.message : 'Could not load transactions.');
        setRows([]);
      }
    })();
  }, [bankId, loadTx]);

  const selectedBank = banks.find((b) => b.id === bankId) ?? null;

  return (
    <div className="space-y-6">
      <PageHeader
        title="Banking"
        subtitle="Manual money in, money out, transfers, fees, and interest."
        actions={
          canCreate && selectedBank ? (
            <HeaderButton
              label="Record transaction"
              icon={Plus}
              variant="primary"
              onClick={() => setDialogOpen(true)}
            />
          ) : undefined
        }
      />

      <BankingTabs active="transactions" orgSlug={currentOrg.slug} />

      {error && (
        <div className="p-4 rounded-xl bg-rose-500/10 border border-rose-500/20 text-rose-600 text-sm flex gap-2">
          <AlertTriangle className="w-4 h-4 shrink-0 mt-0.5" />
          {error}
        </div>
      )}

      <div>
        <label className="block text-xs font-semibold uppercase tracking-wider text-[var(--text-muted)] mb-1">
          Bank account
        </label>
        <select
          value={bankId}
          onChange={(e) => setBankId(e.target.value ? Number(e.target.value) : '')}
          className="h-10 px-3 rounded-xl border border-[var(--border-subtle)] bg-[var(--bg-subtle)] text-sm min-w-[16rem]"
        >
          <option value="">Select…</option>
          {banks.map((b) => (
            <option key={b.id} value={b.id}>
              {b.name}
            </option>
          ))}
        </select>
      </div>

      {loading ? (
        <div className="flex items-center gap-2 text-sm text-[var(--text-muted)]">
          <Loader2 className="w-4 h-4 animate-spin" /> Loading…
        </div>
      ) : (
        <div className="rounded-xl border border-[var(--border-subtle)] bg-[var(--bg-surface)] overflow-x-auto shadow-sm">
          <table className="w-full text-sm min-w-[640px]">
            <thead>
              <tr className="text-left text-xs uppercase tracking-wider text-[var(--text-muted)] border-b border-[var(--border-subtle)]">
                <th className="px-4 py-3 font-semibold">Date</th>
                <th className="px-4 py-3 font-semibold">No</th>
                <th className="px-4 py-3 font-semibold">Type</th>
                <th className="px-4 py-3 font-semibold">Memo</th>
                <th className="px-4 py-3 font-semibold text-right">Amount</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-[var(--border-subtle)]">
              {rows.map((tx) => (
                <tr key={tx.id} className={tx.isVoid ? 'opacity-50 line-through' : ''}>
                  <td className="px-4 py-2.5 whitespace-nowrap">{String(tx.date).slice(0, 10)}</td>
                  <td className="px-4 py-2.5 font-mono text-xs">{tx.no}</td>
                  <td className="px-4 py-2.5">{tx.kindLabel || BANK_TRANSACTION_KIND_LABELS[tx.kind] || tx.kind}</td>
                  <td className="px-4 py-2.5 text-[var(--text-secondary)]">{tx.memo || tx.referenceNo || '—'}</td>
                  <td className="px-4 py-2.5 text-right font-mono font-semibold">{formatCurrency(tx.amount)}</td>
                </tr>
              ))}
              {rows.length === 0 && (
                <tr>
                  <td colSpan={5} className="px-4 py-8 text-center text-[var(--text-muted)]">
                    {bankId === '' ? 'Pick a bank account.' : 'No manual transactions yet.'}
                  </td>
                </tr>
              )}
            </tbody>
          </table>
        </div>
      )}

      {dialogOpen && selectedBank && (
        <BankTransactionDialog
          bank={selectedBank}
          banks={banks}
          onClose={() => setDialogOpen(false)}
          onSaved={async () => {
            setDialogOpen(false);
            await loadTx();
          }}
        />
      )}
    </div>
  );
};

export default BankingTransactionsPage;
