import React, { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { Link, useNavigate, useOutletContext, useParams } from 'react-router-dom';
import { AlertTriangle, CheckCircle2, Loader2, Plus, Search } from 'lucide-react';
import { PageHeader, HeaderButton } from '../components/PageHeader';
import { BankTransactionDialog } from '../components/BankTransactionDialog';
import { useAuth } from '../auth/AuthContext';
import { useTenantContext } from '../components/Gating';
import { localToday } from '../lib/businessDays';
import {
  RECONCILE_TICK_DEBOUNCE_MS,
  canFinishReconciliation,
  filterVisibleLines,
  nextFocusIndex,
  tickTogglePayload,
  type ReconcileLineRef,
} from '../lib/reconcileUi';
import type { TenantContextType } from '../types';
import type {
  BankAccount,
  BankReconciliation,
  BankReconciliationCandidateLine,
} from '../types/banking';
import { BANK_RECONCILIATION_STATUS } from '../types/banking';

const ReconcilePage: React.FC = () => {
  const { bankId: bankIdParam, reconId: reconIdParam } = useParams<{ bankId: string; reconId?: string }>();
  const bankId = Number(bankIdParam);
  const reconIdFromRoute = reconIdParam ? Number(reconIdParam) : null;
  const navigate = useNavigate();
  const { currentOrg } = useOutletContext<TenantContextType>();
  const { authFetch } = useAuth();
  const { hasPermission } = useTenantContext();
  const canReconcile = hasPermission('banking.reconcile');
  const canCreateTx = hasPermission('banking.transactions.create');

  const [bank, setBank] = useState<BankAccount | null>(null);
  const [banks, setBanks] = useState<BankAccount[]>([]);
  const [recon, setRecon] = useState<BankReconciliation | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const [startEndDate, setStartEndDate] = useState(localToday());
  const [startEndingBalance, setStartEndingBalance] = useState('');
  const [search, setSearch] = useState('');
  const [hideTicked, setHideTicked] = useState(false);
  const [focusIndex, setFocusIndex] = useState(0);
  const [adjustmentOpen, setAdjustmentOpen] = useState(false);
  const searchRef = useRef<HTMLInputElement>(null);
  const pendingTicks = useRef<{ add: Set<number>; remove: Set<number> }>({
    add: new Set(),
    remove: new Set(),
  });
  const tickTimer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const reconRef = useRef<BankReconciliation | null>(null);
  reconRef.current = recon;

  const formatCurrency = useCallback(
    (n: number) =>
      new Intl.NumberFormat(undefined, {
        style: 'currency',
        currency: currentOrg.currency || 'USD',
      }).format(n),
    [currentOrg.currency],
  );

  const loadBank = useCallback(async () => {
    const [accRes, listRes] = await Promise.all([
      authFetch(`/api/Banking/Accounts/${bankId}`),
      authFetch('/api/Banking/Accounts'),
    ]);
    if (!accRes.ok) throw new Error('Bank account not found.');
    setBank(await accRes.json());
    if (listRes.ok) {
      const body = await listRes.json();
      setBanks(body.accounts ?? []);
    }
  }, [authFetch, bankId]);

  const loadRecon = useCallback(
    async (id: number) => {
      const res = await authFetch(`/api/Banking/Reconciliations/${id}`);
      if (!res.ok) {
        const body = await res.json().catch(() => null);
        throw new Error(body?.message || 'Could not load the reconciliation.');
      }
      const data: BankReconciliation = await res.json();
      setRecon(data);
      return data;
    },
    [authFetch],
  );

  const bootstrap = useCallback(async () => {
    if (!Number.isFinite(bankId)) return;
    setLoading(true);
    setError(null);
    try {
      await loadBank();
      if (reconIdFromRoute) {
        await loadRecon(reconIdFromRoute);
      } else {
        const listRes = await authFetch(`/api/Banking/Reconciliations?bankId=${bankId}`);
        if (listRes.ok) {
          const items: { id: number; status: number }[] = await listRes.json();
          const draft = items.find((i) => i.status === BANK_RECONCILIATION_STATUS.Draft);
          if (draft) {
            navigate(`/org/${currentOrg.slug}/banking/${bankId}/reconcile/${draft.id}`, { replace: true });
            await loadRecon(draft.id);
          } else {
            setRecon(null);
          }
        }
      }
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'Could not open reconciliation.');
    } finally {
      setLoading(false);
    }
  }, [authFetch, bankId, reconIdFromRoute, loadBank, loadRecon, navigate, currentOrg.slug]);

  useEffect(() => {
    void bootstrap();
  }, [bootstrap]);

  const flushTicks = useCallback(async () => {
    const current = reconRef.current;
    if (!current || current.status !== BANK_RECONCILIATION_STATUS.Draft) return;
    const add = [...pendingTicks.current.add];
    const remove = [...pendingTicks.current.remove];
    pendingTicks.current = { add: new Set(), remove: new Set() };
    if (add.length === 0 && remove.length === 0) return;

    setBusy(true);
    setError(null);
    try {
      const res = await authFetch(`/api/Banking/Reconciliations/${current.id}/Items`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ add, remove, version: current.version }),
      });
      if (res.status === 409) {
        const body = await res.json().catch(() => null);
        throw new Error(body?.message || 'Someone else changed this reconciliation.');
      }
      if (!res.ok) {
        const body = await res.json().catch(() => null);
        throw new Error(body?.message || 'Could not save ticks.');
      }
      setRecon(await res.json());
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'Could not save ticks.');
      await loadRecon(current.id);
    } finally {
      setBusy(false);
    }
  }, [authFetch, loadRecon]);

  const queueTick = useCallback(
    (lineId: number) => {
      const current = reconRef.current;
      if (!current || !canReconcile) return;
      const ticked = new Set(
        [...current.deposits, ...current.payments].filter((l) => l.ticked).map((l => l.lineId)),
      );
      const { add, remove } = tickTogglePayload(ticked, lineId);

      // Optimistic UI
      setRecon((prev) => {
        if (!prev) return prev;
        const flip = (lines: BankReconciliationCandidateLine[]) =>
          lines.map((l) => (l.lineId === lineId ? { ...l, ticked: !l.ticked } : l));
        return { ...prev, deposits: flip(prev.deposits), payments: flip(prev.payments) };
      });

      for (const id of add) {
        pendingTicks.current.remove.delete(id);
        pendingTicks.current.add.add(id);
      }
      for (const id of remove) {
        pendingTicks.current.add.delete(id);
        pendingTicks.current.remove.add(id);
      }
      if (tickTimer.current) clearTimeout(tickTimer.current);
      tickTimer.current = setTimeout(() => void flushTicks(), RECONCILE_TICK_DEBOUNCE_MS);
    },
    [canReconcile, flushTicks],
  );

  useEffect(
    () => () => {
      if (tickTimer.current) clearTimeout(tickTimer.current);
    },
    [],
  );

  const allLines: ReconcileLineRef[] = useMemo(() => {
    if (!recon) return [];
    return [...recon.deposits, ...recon.payments];
  }, [recon]);

  const visible = useMemo(
    () => filterVisibleLines(allLines, search, hideTicked),
    [allLines, search, hideTicked],
  );

  useEffect(() => {
    if (focusIndex >= visible.length) setFocusIndex(Math.max(0, visible.length - 1));
  }, [visible.length, focusIndex]);

  const onKeyDown = (e: React.KeyboardEvent) => {
    if (!recon || recon.status !== BANK_RECONCILIATION_STATUS.Draft) return;
    if (e.key === '/' && document.activeElement !== searchRef.current) {
      e.preventDefault();
      searchRef.current?.focus();
      return;
    }
    if (document.activeElement === searchRef.current) return;
    if (e.key === 'ArrowDown') {
      e.preventDefault();
      setFocusIndex((i) => nextFocusIndex(visible.length, i, 1));
    } else if (e.key === 'ArrowUp') {
      e.preventDefault();
      setFocusIndex((i) => nextFocusIndex(visible.length, i, -1));
    } else if (e.key === ' ' || e.key === 'Spacebar') {
      e.preventDefault();
      const line = visible[focusIndex];
      if (line && canReconcile) queueTick(line.lineId);
    }
  };

  const start = async () => {
    if (!canReconcile) return;
    setBusy(true);
    setError(null);
    try {
      const ending = Number(startEndingBalance);
      if (!Number.isFinite(ending)) throw new Error('Enter the statement ending balance.');
      const res = await authFetch('/api/Banking/Reconciliations', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          bankId,
          statementEndDate: startEndDate,
          statementEndingBalance: ending,
        }),
      });
      if (res.status === 409) {
        const body = await res.json().catch(() => null);
        if (body?.reconciliationId) {
          navigate(`/org/${currentOrg.slug}/banking/${bankId}/reconcile/${body.reconciliationId}`, {
            replace: true,
          });
          await loadRecon(body.reconciliationId);
          return;
        }
        throw new Error(body?.message || 'A draft already exists.');
      }
      if (!res.ok) {
        const body = await res.json().catch(() => null);
        throw new Error(body?.message || 'Could not start reconciliation.');
      }
      const created: BankReconciliation = await res.json();
      setRecon(created);
      navigate(`/org/${currentOrg.slug}/banking/${bankId}/reconcile/${created.id}`, { replace: true });
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'Could not start reconciliation.');
    } finally {
      setBusy(false);
    }
  };

  const finish = async () => {
    if (!recon || !canReconcile) return;
    if (tickTimer.current) {
      clearTimeout(tickTimer.current);
      await flushTicks();
    }
    const current = reconRef.current;
    if (!current || !canFinishReconciliation(current.difference)) return;
    setBusy(true);
    setError(null);
    try {
      const res = await authFetch(`/api/Banking/Reconciliations/${current.id}/Finish`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ version: current.version }),
      });
      if (!res.ok) {
        const body = await res.json().catch(() => null);
        throw new Error(body?.message || 'Could not finish reconciliation.');
      }
      setRecon(await res.json());
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'Could not finish reconciliation.');
    } finally {
      setBusy(false);
    }
  };

  const discard = async () => {
    if (!recon || !canReconcile) return;
    setBusy(true);
    setError(null);
    try {
      const res = await authFetch(`/api/Banking/Reconciliations/${recon.id}`, { method: 'DELETE' });
      if (!res.ok) {
        const body = await res.json().catch(() => null);
        throw new Error(body?.message || 'Could not discard the draft.');
      }
      navigate(`/org/${currentOrg.slug}/banking/reconcile`);
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'Could not discard the draft.');
    } finally {
      setBusy(false);
    }
  };

  if (loading) {
    return (
      <div className="flex items-center gap-2 text-sm text-[var(--text-muted)]">
        <Loader2 className="w-4 h-4 animate-spin" /> Opening reconciliation…
      </div>
    );
  }

  const isDraft = recon?.status === BANK_RECONCILIATION_STATUS.Draft;

  return (
    <div className="space-y-4" onKeyDown={onKeyDown} tabIndex={0}>
      <PageHeader
        title={bank ? `Reconcile · ${bank.name}` : 'Reconcile'}
        subtitle={
          recon
            ? `Statement through ${String(recon.statementEndDate).slice(0, 10)} · ${recon.statusLabel}`
            : 'Match the bank statement to the books'
        }
        back={{ to: `/org/${currentOrg.slug}/banking/reconcile`, label: 'History' }}
        actions={
          isDraft && canReconcile ? (
            <div className="flex flex-wrap gap-2">
              {canCreateTx && (
                <HeaderButton label="Add fee / interest" icon={Plus} variant="secondary" onClick={() => setAdjustmentOpen(true)} />
              )}
              <HeaderButton label="Save for later" variant="secondary" onClick={() => navigate(`/org/${currentOrg.slug}/banking/reconcile`)} />
              <HeaderButton
                label="Finish"
                variant="primary"
                busy={busy}
                disabled={!canFinishReconciliation(recon.difference)}
                onClick={() => void finish()}
              />
            </div>
          ) : undefined
        }
      />

      <p className="md:hidden text-xs text-amber-700 dark:text-amber-300 bg-amber-500/10 border border-amber-500/20 rounded-xl px-3 py-2">
        Reconciliation works best on a larger screen. You can still tick lines here.
      </p>

      {error && (
        <div className="p-3 rounded-xl bg-rose-500/10 border border-rose-500/20 text-rose-600 text-sm flex gap-2">
          <AlertTriangle className="w-4 h-4 shrink-0 mt-0.5" />
          {error}
        </div>
      )}

      {!recon && bank && (
        <div className="rounded-xl border border-[var(--border-subtle)] bg-[var(--bg-surface)] p-6 space-y-4 max-w-lg">
          <h2 className="text-sm font-bold text-[var(--text-main)]">Start a reconciliation</h2>
          <div>
            <label className="block text-xs font-semibold uppercase tracking-wider text-[var(--text-muted)] mb-1">
              Statement end date
            </label>
            <input
              type="date"
              value={startEndDate}
              onChange={(e) => setStartEndDate(e.target.value)}
              className="w-full h-10 px-3 rounded-xl border border-[var(--border-subtle)] bg-[var(--bg-subtle)] text-sm"
            />
          </div>
          <div>
            <label className="block text-xs font-semibold uppercase tracking-wider text-[var(--text-muted)] mb-1">
              Statement ending balance
            </label>
            <input
              type="number"
              step="0.01"
              value={startEndingBalance}
              onChange={(e) => setStartEndingBalance(e.target.value)}
              className="w-full h-10 px-3 rounded-xl border border-[var(--border-subtle)] bg-[var(--bg-subtle)] text-sm font-mono"
            />
          </div>
          {canReconcile ? (
            <HeaderButton label="Start" variant="primary" busy={busy} onClick={() => void start()} />
          ) : (
            <p className="text-xs text-[var(--text-muted)]">You need the banking.reconcile permission to start.</p>
          )}
        </div>
      )}

      {recon && (
        <>
          <div className="sticky top-0 z-10 rounded-xl border border-[var(--border-subtle)] bg-[var(--bg-surface)] shadow-sm p-4 grid grid-cols-2 md:grid-cols-3 lg:grid-cols-6 gap-3 text-sm">
            <Stat label="Beginning" value={formatCurrency(recon.beginningBalance)} />
            <Stat label="Cleared deposits" value={formatCurrency(recon.clearedDeposits)} />
            <Stat label="Cleared payments" value={formatCurrency(recon.clearedPayments)} />
            <Stat label="Cleared balance" value={formatCurrency(recon.clearedBalance)} />
            <Stat label="Statement ending" value={formatCurrency(recon.statementEndingBalance)} />
            <Stat
              label="Difference"
              value={formatCurrency(recon.difference)}
              emphasize={canFinishReconciliation(recon.difference) ? 'ok' : 'bad'}
            />
          </div>

          {isDraft && (
            <div className="flex flex-wrap items-center gap-3">
              <div className="relative flex-1 min-w-[12rem]">
                <Search className="w-4 h-4 absolute left-3 top-1/2 -translate-y-1/2 text-[var(--text-muted)]" />
                <input
                  ref={searchRef}
                  type="search"
                  value={search}
                  onChange={(e) => setSearch(e.target.value)}
                  placeholder="Search amount, type, reference (/)"
                  className="w-full h-10 pl-9 pr-3 rounded-xl border border-[var(--border-subtle)] bg-[var(--bg-subtle)] text-sm"
                />
              </div>
              <label className="flex items-center gap-2 text-xs font-semibold text-[var(--text-secondary)]">
                <input type="checkbox" checked={hideTicked} onChange={(e) => setHideTicked(e.target.checked)} />
                Hide ticked
              </label>
              {canReconcile && (
                <button
                  type="button"
                  onClick={() => void discard()}
                  className="h-8 px-3 rounded-xl text-xs font-semibold text-rose-600 hover:bg-rose-500/10"
                >
                  Discard draft
                </button>
              )}
            </div>
          )}

          <div className="grid grid-cols-1 lg:grid-cols-2 gap-4">
            <LineColumn
              title="Deposits & credits"
              lines={recon.deposits}
              visibleIds={new Set(visible.map((v) => v.lineId))}
              focusLineId={visible[focusIndex]?.lineId}
              formatCurrency={formatCurrency}
              interactive={!!isDraft && canReconcile}
              onToggle={queueTick}
            />
            <LineColumn
              title="Payments & debits"
              lines={recon.payments}
              visibleIds={new Set(visible.map((v) => v.lineId))}
              focusLineId={visible[focusIndex]?.lineId}
              formatCurrency={formatCurrency}
              interactive={!!isDraft && canReconcile}
              onToggle={queueTick}
            />
          </div>

          {recon.status === BANK_RECONCILIATION_STATUS.Finished && (
            <p className="text-sm text-emerald-600 flex items-center gap-2">
              <CheckCircle2 className="w-4 h-4" />
              Finished{recon.finishedAt ? ` on ${String(recon.finishedAt).slice(0, 10)}` : ''}.
              <Link to={`/org/${currentOrg.slug}/banking/reconcile`} className="underline">
                Back to history
              </Link>
            </p>
          )}
        </>
      )}

      {adjustmentOpen && bank && recon && isDraft && (
        <BankTransactionDialog
          bank={bank}
          banks={banks}
          initialKind={4}
          adjustmentOnly
          reconciliation={{ id: recon.id, version: recon.version }}
          onClose={() => setAdjustmentOpen(false)}
          onSaved={async (updated) => {
            setAdjustmentOpen(false);
            if (updated) setRecon(updated);
            else await loadRecon(recon.id);
          }}
        />
      )}
    </div>
  );
};

function Stat({
  label,
  value,
  emphasize,
}: {
  label: string;
  value: string;
  emphasize?: 'ok' | 'bad';
}) {
  const color =
    emphasize === 'ok'
      ? 'text-emerald-600'
      : emphasize === 'bad'
        ? 'text-rose-600'
        : 'text-[var(--text-main)]';
  return (
    <div>
      <div className="text-[10px] uppercase tracking-wider font-semibold text-[var(--text-muted)]">{label}</div>
      <div className={`font-mono font-bold ${color}`}>{value}</div>
    </div>
  );
}

function LineColumn({
  title,
  lines,
  visibleIds,
  focusLineId,
  formatCurrency,
  interactive,
  onToggle,
}: {
  title: string;
  lines: BankReconciliationCandidateLine[];
  visibleIds: Set<number>;
  focusLineId?: number;
  formatCurrency: (n: number) => string;
  interactive: boolean;
  onToggle: (lineId: number) => void;
}) {
  const shown = lines.filter((l) => visibleIds.has(l.lineId));
  return (
    <div className="rounded-xl border border-[var(--border-subtle)] bg-[var(--bg-surface)] overflow-hidden">
      <div className="px-4 py-3 border-b border-[var(--border-subtle)] text-xs font-bold uppercase tracking-wider text-[var(--text-muted)]">
        {title}
      </div>
      <ul className="divide-y divide-[var(--border-subtle)] max-h-[28rem] overflow-y-auto">
        {shown.map((line) => {
          const amount = line.in || line.out;
          const focused = line.lineId === focusLineId;
          return (
            <li key={line.lineId}>
              <button
                type="button"
                disabled={!interactive}
                onClick={() => interactive && onToggle(line.lineId)}
                className={`w-full text-left px-4 py-2.5 flex items-start gap-3 transition ${
                  line.ticked ? 'opacity-50' : ''
                } ${focused ? 'bg-[var(--brand-primary)]/10' : 'hover:bg-[var(--bg-subtle)]'} disabled:cursor-default`}
              >
                <span
                  className={`mt-0.5 w-4 h-4 rounded border flex items-center justify-center shrink-0 ${
                    line.ticked
                      ? 'bg-[var(--brand-primary)] border-[var(--brand-primary)] text-white'
                      : 'border-[var(--border-subtle)]'
                  }`}
                  aria-hidden
                >
                  {line.ticked ? '✓' : ''}
                </span>
                <span className="min-w-0 flex-1">
                  <span className="block text-sm text-[var(--text-main)]">
                    {String(line.date).slice(0, 10)} · {line.type}
                  </span>
                  <span className="block text-xs text-[var(--text-muted)] truncate">
                    {line.reference || line.payee || '—'}
                  </span>
                </span>
                <span className="font-mono text-sm font-semibold text-[var(--text-main)] shrink-0">
                  {formatCurrency(amount)}
                </span>
              </button>
            </li>
          );
        })}
        {shown.length === 0 && (
          <li className="px-4 py-8 text-center text-xs text-[var(--text-muted)]">No lines to show.</li>
        )}
      </ul>
    </div>
  );
}

export default ReconcilePage;
