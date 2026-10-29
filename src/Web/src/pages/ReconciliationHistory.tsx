import React, { useCallback, useEffect, useState } from 'react';
import { Link, useNavigate, useOutletContext } from 'react-router-dom';
import { AlertTriangle, Loader2, RotateCcw } from 'lucide-react';
import { PageHeader, HeaderButton } from '../components/PageHeader';
import { ModalShell } from '../components/ModalShell';
import { useAuth } from '../auth/AuthContext';
import { useTenantContext } from '../components/Gating';
import type { TenantContextType } from '../types';
import type { BankAccount, BankReconciliationListItem } from '../types/banking';
import { BANK_RECONCILIATION_STATUS } from '../types/banking';
import { BankingTabs } from '../components/BankingTabs';

const ReconciliationHistoryPage: React.FC = () => {
  const { currentOrg } = useOutletContext<TenantContextType>();
  const navigate = useNavigate();
  const { authFetch } = useAuth();
  const { hasPermission } = useTenantContext();
  const canReconcile = hasPermission('banking.reconcile');
  const canUndo = hasPermission('banking.reconcile.undo');

  const [banks, setBanks] = useState<BankAccount[]>([]);
  const [bankId, setBankId] = useState<number | ''>('');
  const [items, setItems] = useState<BankReconciliationListItem[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [undoTarget, setUndoTarget] = useState<BankReconciliationListItem | null>(null);
  const [undoReason, setUndoReason] = useState('');
  const [busy, setBusy] = useState(false);

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

  const loadHistory = useCallback(async () => {
    if (bankId === '') {
      setItems([]);
      return;
    }
    const res = await authFetch(`/api/Banking/Reconciliations?bankId=${bankId}`);
    if (!res.ok) {
      const body = await res.json().catch(() => null);
      throw new Error(body?.message || 'Could not load reconciliations.');
    }
    setItems(await res.json());
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
        await loadHistory();
      } catch (err: unknown) {
        setError(err instanceof Error ? err.message : 'Could not load history.');
        setItems([]);
      }
    })();
  }, [bankId, loadHistory]);

  const latestFinishedId = items.find((i) => i.status === BANK_RECONCILIATION_STATUS.Finished)?.id;
  const draft = items.find((i) => i.status === BANK_RECONCILIATION_STATUS.Draft);

  const undo = async () => {
    if (!undoTarget || !undoReason.trim()) return;
    setBusy(true);
    setError(null);
    try {
      const res = await authFetch(`/api/Banking/Reconciliations/${undoTarget.id}/Undo`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ reason: undoReason.trim() }),
      });
      if (!res.ok) {
        const body = await res.json().catch(() => null);
        throw new Error(body?.message || 'Could not undo.');
      }
      setUndoTarget(null);
      setUndoReason('');
      await loadHistory();
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'Could not undo.');
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="space-y-6">
      <PageHeader
        title="Banking"
        subtitle="Reconcile statements to the books."
        actions={
          canReconcile && bankId !== '' ? (
            <HeaderButton
              label={draft ? 'Resume draft' : 'Start reconciliation'}
              variant="primary"
              onClick={() =>
                navigate(
                  draft
                    ? `/org/${currentOrg.slug}/banking/${bankId}/reconcile/${draft.id}`
                    : `/org/${currentOrg.slug}/banking/${bankId}/reconcile`,
                )
              }
            />
          ) : undefined
        }
      />

      <BankingTabs active="reconcile" orgSlug={currentOrg.slug} />

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
        <div className="rounded-xl border border-[var(--border-subtle)] bg-[var(--bg-surface)] overflow-hidden shadow-sm">
          <table className="w-full text-sm">
            <thead>
              <tr className="text-left text-xs uppercase tracking-wider text-[var(--text-muted)] border-b border-[var(--border-subtle)]">
                <th className="px-4 py-3 font-semibold">Statement end</th>
                <th className="px-4 py-3 font-semibold">Ending balance</th>
                <th className="px-4 py-3 font-semibold">Status</th>
                <th className="px-4 py-3 font-semibold">Finished</th>
                <th className="px-4 py-3 font-semibold text-right">Actions</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-[var(--border-subtle)]">
              {items.map((item) => (
                <tr key={item.id}>
                  <td className="px-4 py-3 whitespace-nowrap">{String(item.statementEndDate).slice(0, 10)}</td>
                  <td className="px-4 py-3 font-mono">{formatCurrency(item.statementEndingBalance)}</td>
                  <td className="px-4 py-3">{item.statusLabel}</td>
                  <td className="px-4 py-3 text-[var(--text-muted)] text-xs">
                    {item.finishedAt
                      ? `${String(item.finishedAt).slice(0, 10)}${item.finishedBy ? ` · ${item.finishedBy}` : ''}`
                      : '—'}
                  </td>
                  <td className="px-4 py-3 text-right space-x-2">
                    <Link
                      to={`/org/${currentOrg.slug}/banking/${item.bankId}/reconcile/${item.id}`}
                      className="text-xs font-semibold text-[var(--brand-primary)] hover:underline"
                    >
                      Open
                    </Link>
                    {item.status === BANK_RECONCILIATION_STATUS.Finished && (
                      <a
                        href={`/api/Banking/Reconciliations/${item.id}/Report.pdf`}
                        className="text-xs font-semibold text-[var(--brand-primary)] hover:underline"
                        onClick={(e) => {
                          e.preventDefault();
                          void (async () => {
                            const res = await authFetch(`/api/Banking/Reconciliations/${item.id}/Report.pdf`);
                            if (!res.ok) {
                              const body = await res.json().catch(() => null);
                              setError(body?.message || 'Could not download the report.');
                              return;
                            }
                            const blob = await res.blob();
                            const url = URL.createObjectURL(blob);
                            const anchor = document.createElement('a');
                            anchor.href = url;
                            anchor.download = `BankReconciliation-${String(item.statementEndDate).slice(0, 10)}.pdf`;
                            anchor.click();
                            URL.revokeObjectURL(url);
                          })();
                        }}
                      >
                        Report PDF
                      </a>
                    )}
                    {item.status === BANK_RECONCILIATION_STATUS.Finished &&
                      item.id === latestFinishedId &&
                      canUndo && (
                        <button
                          type="button"
                          onClick={() => {
                            setUndoTarget(item);
                            setUndoReason('');
                          }}
                          className="text-xs font-semibold text-rose-600 hover:underline inline-flex items-center gap-1"
                        >
                          <RotateCcw className="w-3 h-3" /> Undo
                        </button>
                      )}
                  </td>
                </tr>
              ))}
              {items.length === 0 && (
                <tr>
                  <td colSpan={5} className="px-4 py-8 text-center text-[var(--text-muted)]">
                    {bankId === '' ? 'Pick a bank account.' : 'No reconciliations yet for this account.'}
                  </td>
                </tr>
              )}
            </tbody>
          </table>
        </div>
      )}

      {undoTarget && (
        <ModalShell
          open
          onClose={() => {
            if (!busy) setUndoTarget(null);
          }}
          closeOnEscape={!busy}
          closeOnBackdrop={!busy}
          maxWidthClassName="max-w-md"
          role="alertdialog"
          aria-labelledby="undo-recon-title"
          panelClassName="p-6 space-y-4"
        >
          <h2 id="undo-recon-title" className="text-base font-bold text-[var(--text-main)]">
            Undo reconciliation?
          </h2>
          <p className="text-sm text-[var(--text-secondary)]">
            Clears cleared marks through {String(undoTarget.statementEndDate).slice(0, 10)}. A reason is required.
          </p>
          <textarea
            required
            value={undoReason}
            onChange={(e) => setUndoReason(e.target.value)}
            rows={3}
            className="w-full px-3 py-2 rounded-xl border border-[var(--border-subtle)] bg-[var(--bg-subtle)] text-sm"
            placeholder="Why are you undoing this?"
          />
          <div className="flex justify-end gap-2">
            <button
              type="button"
              disabled={busy}
              onClick={() => setUndoTarget(null)}
              className="h-10 px-4 rounded-xl text-xs font-semibold text-[var(--text-secondary)]"
            >
              Cancel
            </button>
            <button
              type="button"
              disabled={busy || !undoReason.trim()}
              onClick={() => void undo()}
              className="h-10 px-4 rounded-xl text-xs font-semibold text-white bg-rose-600 disabled:opacity-50"
            >
              {busy ? 'Working…' : 'Undo'}
            </button>
          </div>
        </ModalShell>
      )}
    </div>
  );
};

export default ReconciliationHistoryPage;
