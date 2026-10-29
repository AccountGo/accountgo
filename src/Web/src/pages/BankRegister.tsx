import React, { useCallback, useEffect, useState } from 'react';
import { Link, useNavigate, useOutletContext, useParams } from 'react-router-dom';
import { AlertTriangle, Archive, FileUp, Loader2, Pencil, Plus, Scale, Star } from 'lucide-react';
import { PageHeader, HeaderButton, HeaderMenu } from '../components/PageHeader';
import { BankAccountFormDialog } from '../components/BankAccountFormDialog';
import { BankTransactionDialog } from '../components/BankTransactionDialog';
import { ConfirmDialog } from '../components/ConfirmDialog';
import { BalanceCheckDialog } from '../components/BalanceCheckDialog';
import { canShowBalanceCheck } from '../lib/balanceCheck';
import { useAuth } from '../auth/AuthContext';
import { useTenantContext } from '../components/Gating';
import { localToday } from '../lib/businessDays';
import type { TenantContextType } from '../types';
import type { BankAccount, BankRegister as BankRegisterDto } from '../types/banking';
import { bankTypeLabel } from '../types/banking';

function sourcePath(orgSlug: string, type: string, id: number): string | null {
  switch (type) {
    case 'SalesReceipt':
    case 'SalesInvoice':
      return `/org/${orgSlug}/sales/${id}`;
    case 'PurchaseInvoice':
    case 'PurchaseInvoicePayment':
      return `/org/${orgSlug}/purchases/${id}`;
    case 'JournalEntry':
      return `/org/${orgSlug}/financials/journals?id=${id}`;
    default:
      return null;
  }
}

const BankRegisterPage: React.FC = () => {
  const { bankId } = useParams<{ bankId: string }>();
  const id = Number(bankId);
  const navigate = useNavigate();
  const { currentOrg } = useOutletContext<TenantContextType>();
  const { authFetch } = useAuth();
  const { hasModule, hasPermission } = useTenantContext();
  const hasFinancials = hasModule('financials');
  const canManage = hasPermission('banking.accounts.manage');
  const canCreateTx = hasPermission('banking.transactions.create');
  const canReconcile = hasPermission('banking.reconcile');
  const canViewAccounts = hasPermission('banking.accounts.view');

  const [account, setAccount] = useState<BankAccount | null>(null);
  const [banks, setBanks] = useState<BankAccount[]>([]);
  const [register, setRegister] = useState<BankRegisterDto | null>(null);
  const [from, setFrom] = useState('');
  const [to, setTo] = useState(localToday());
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [editOpen, setEditOpen] = useState(false);
  const [txOpen, setTxOpen] = useState(false);
  const [balanceCheckOpen, setBalanceCheckOpen] = useState(false);
  const [busy, setBusy] = useState(false);
  const [archiveConfirmMessage, setArchiveConfirmMessage] = useState<string | null>(null);

  const formatCurrency = useCallback(
    (n: number) =>
      new Intl.NumberFormat(undefined, {
        style: 'currency',
        currency: currentOrg.currency || 'USD',
      }).format(n),
    [currentOrg.currency],
  );

  const load = useCallback(async () => {
    if (!Number.isFinite(id)) return;
    setLoading(true);
    setError(null);
    try {
      const q = new URLSearchParams({ page: '1', pageSize: '200' });
      if (from) q.set('from', from);
      if (to) q.set('to', to);
      const [accRes, regRes, listRes] = await Promise.all([
        authFetch(`/api/Banking/Accounts/${id}`),
        authFetch(`/api/Banking/Accounts/${id}/Register?${q}`),
        authFetch('/api/Banking/Accounts'),
      ]);
      if (!accRes.ok) throw new Error('Bank account not found.');
      if (!regRes.ok) {
        const body = await regRes.json().catch(() => null);
        throw new Error(body?.message || 'Could not load the register.');
      }
      setAccount(await accRes.json());
      setRegister(await regRes.json());
      if (listRes.ok) {
        const body = await listRes.json();
        setBanks(body.accounts ?? []);
      }
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'Could not load the register.');
      setAccount(null);
      setRegister(null);
    } finally {
      setLoading(false);
    }
  }, [authFetch, id, from, to]);

  useEffect(() => {
    void load();
  }, [load]);

  const archive = async (confirm = false) => {
    if (!account || !canManage) return;
    setBusy(true);
    setError(null);
    try {
      const url = confirm
        ? `/api/Banking/Accounts/${account.id}/Archive?confirm=true`
        : `/api/Banking/Accounts/${account.id}/Archive`;
      const res = await authFetch(url, { method: 'POST' });
      if (res.status === 409) {
        const body = await res.json().catch(() => null);
        if (body?.code === 'ARCHIVE_CONFIRM_REQUIRED' && !confirm) {
          setArchiveConfirmMessage(body.message || 'This account still has a balance. Archive anyway?');
          return;
        }
        throw new Error(body?.message || 'Could not archive.');
      }
      if (!res.ok) {
        const body = await res.json().catch(() => null);
        throw new Error(body?.message || 'Could not archive.');
      }
      setArchiveConfirmMessage(null);
      await load();
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'Could not archive.');
    } finally {
      setBusy(false);
    }
  };

  const unarchive = async () => {
    if (!account || !canManage) return;
    setBusy(true);
    setError(null);
    try {
      const res = await authFetch(`/api/Banking/Accounts/${account.id}/Unarchive`, { method: 'POST' });
      if (!res.ok) {
        const body = await res.json().catch(() => null);
        throw new Error(body?.message || 'Could not unarchive.');
      }
      await load();
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'Could not unarchive.');
    } finally {
      setBusy(false);
    }
  };

  const setDefault = async (forKind: 'receipts' | 'payments') => {
    if (!account || !canManage) return;
    setBusy(true);
    setError(null);
    try {
      const res = await authFetch(`/api/Banking/Accounts/${account.id}/SetDefault?for=${forKind}`, { method: 'POST' });
      if (!res.ok) {
        const body = await res.json().catch(() => null);
        throw new Error(body?.message || 'Could not set default.');
      }
      await load();
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'Could not set default.');
    } finally {
      setBusy(false);
    }
  };

  if (loading && !account) {
    return (
      <div className="flex items-center gap-2 text-sm text-[var(--text-muted)]">
        <Loader2 className="w-4 h-4 animate-spin" /> Loading register…
      </div>
    );
  }

  if (!account) {
    return (
      <div className="space-y-4">
        <PageHeader title="Bank register" back={{ to: `/org/${currentOrg.slug}/banking`, label: 'Banking' }} />
        {error && (
          <div className="p-4 rounded-xl bg-rose-500/10 border border-rose-500/20 text-rose-600 text-sm">{error}</div>
        )}
      </div>
    );
  }

  return (
    <div className="space-y-6">
      <PageHeader
        title={account.name}
        subtitle={`${bankTypeLabel(account.type)}${account.accountNumberMasked ? ` · ${account.accountNumberMasked}` : ''}`}
        back={{ to: `/org/${currentOrg.slug}/banking`, label: 'Banking' }}
        badges={
          <span className="font-mono text-sm font-bold text-[var(--text-main)]">{formatCurrency(account.bookBalance)}</span>
        }
        actions={
          <div className="flex flex-wrap items-center gap-2">
            {hasFinancials && canCreateTx && !account.archivedAt && (
              <HeaderButton label="Record transaction" icon={Plus} variant="secondary" onClick={() => setTxOpen(true)} />
            )}
            {hasFinancials && canReconcile && !account.archivedAt && (
              <HeaderButton
                label="Reconcile"
                icon={Scale}
                variant="secondary"
                onClick={() => navigate(`/org/${currentOrg.slug}/banking/${account.id}/reconcile`)}
              />
            )}
            {hasFinancials && canReconcile && !account.archivedAt && (
              <HeaderButton
                label="Import statement"
                icon={FileUp}
                variant="secondary"
                onClick={() => navigate(`/org/${currentOrg.slug}/banking/${account.id}/statement-import`)}
              />
            )}
            {canShowBalanceCheck({ hasFinancials, canView: canViewAccounts, archived: !!account.archivedAt }) && (
              <HeaderButton
                label="Check balance"
                icon={Scale}
                variant="secondary"
                onClick={() => setBalanceCheckOpen(true)}
              />
            )}
            {canManage ? (
              <HeaderMenu
                label="Actions"
                items={[
                  { label: 'Edit', icon: Pencil, onSelect: () => setEditOpen(true) },
                  { label: 'Default for receipts', icon: Star, onSelect: () => void setDefault('receipts') },
                  { label: 'Default for payments', icon: Star, onSelect: () => void setDefault('payments') },
                  {
                    label: account.archivedAt ? 'Unarchive' : 'Archive',
                    icon: Archive,
                    onSelect: () => {
                      if (account.archivedAt) void unarchive();
                      else void archive(false);
                    },
                  },
                ]}
              />
            ) : null}
          </div>
        }
      />

      {error && (
        <div className="p-4 rounded-xl bg-rose-500/10 border border-rose-500/20 text-rose-600 text-sm flex gap-2">
          <AlertTriangle className="w-4 h-4 shrink-0 mt-0.5" />
          {error}
        </div>
      )}

      <div className="flex flex-wrap items-end gap-3">
        <div>
          <label className="block text-xs font-semibold uppercase tracking-wider text-[var(--text-muted)] mb-1">From</label>
          <input type="date" value={from} onChange={(e) => setFrom(e.target.value)} className="h-10 px-3 rounded-xl border border-[var(--border-subtle)] bg-[var(--bg-subtle)] text-sm" />
        </div>
        <div>
          <label className="block text-xs font-semibold uppercase tracking-wider text-[var(--text-muted)] mb-1">To</label>
          <input type="date" value={to} onChange={(e) => setTo(e.target.value)} className="h-10 px-3 rounded-xl border border-[var(--border-subtle)] bg-[var(--bg-subtle)] text-sm" />
        </div>
        <HeaderButton label="Apply" variant="secondary" onClick={() => void load()} busy={loading || busy} />
      </div>

      <div className="rounded-xl border border-[var(--border-subtle)] bg-[var(--bg-surface)] overflow-x-auto shadow-sm">
        <table className="w-full text-sm min-w-[640px]">
          <thead>
            <tr className="text-left text-xs uppercase tracking-wider text-[var(--text-muted)] border-b border-[var(--border-subtle)]">
              <th className="px-4 py-3 font-semibold">Date</th>
              <th className="px-4 py-3 font-semibold">Type</th>
              <th className="px-4 py-3 font-semibold">Payee</th>
              <th className="px-4 py-3 font-semibold">Reference</th>
              <th className="px-4 py-3 font-semibold text-right">In</th>
              <th className="px-4 py-3 font-semibold text-right">Out</th>
              <th className="px-4 py-3 font-semibold text-right">Balance</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-[var(--border-subtle)]">
            {from && register && (
              <tr className="bg-[var(--bg-subtle)]/60 text-[var(--text-secondary)]">
                <td className="px-4 py-2" colSpan={4}>
                  Brought forward
                </td>
                <td className="px-4 py-2" />
                <td className="px-4 py-2" />
                <td className="px-4 py-2 text-right font-mono">{formatCurrency(register.broughtForward)}</td>
              </tr>
            )}
            {(register?.lines ?? []).map((line) => {
              const href =
                line.sourceDoc && sourcePath(currentOrg.slug, line.sourceDoc.type, line.sourceDoc.id);
              return (
                <tr key={line.lineId} className="text-[var(--text-main)]">
                  <td className="px-4 py-2.5 whitespace-nowrap">{String(line.date).slice(0, 10)}</td>
                  <td className="px-4 py-2.5">{line.type}</td>
                  <td className="px-4 py-2.5">{line.payee || '—'}</td>
                  <td className="px-4 py-2.5">
                    {href ? (
                      <Link to={href} className="text-[var(--brand-primary)] hover:underline">
                        {line.reference || line.sourceDoc?.no || `#${line.sourceDoc?.id}`}
                      </Link>
                    ) : (
                      line.reference || '—'
                    )}
                  </td>
                  <td className="px-4 py-2.5 text-right font-mono">{line.in ? formatCurrency(line.in) : ''}</td>
                  <td className="px-4 py-2.5 text-right font-mono">{line.out ? formatCurrency(line.out) : ''}</td>
                  <td className="px-4 py-2.5 text-right font-mono font-semibold">{formatCurrency(line.balance)}</td>
                </tr>
              );
            })}
            {register && register.lines.length === 0 && (
              <tr>
                <td colSpan={7} className="px-4 py-8 text-center text-[var(--text-muted)]">
                  No lines in this date range.
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </div>

      {editOpen && (
        <BankAccountFormDialog
          account={account}
          hasFinancials={hasFinancials}
          onSaved={async () => {
            setEditOpen(false);
            await load();
          }}
          onClose={() => setEditOpen(false)}
        />
      )}

      {txOpen && (
        <BankTransactionDialog
          bank={account}
          banks={banks}
          onClose={() => setTxOpen(false)}
          onSaved={async () => {
            setTxOpen(false);
            await load();
          }}
        />
      )}

      {balanceCheckOpen && (
        <BalanceCheckDialog bank={account} formatCurrency={formatCurrency} onClose={() => setBalanceCheckOpen(false)} />
      )}

      {archiveConfirmMessage && (
        <ConfirmDialog
          title="Archive with a balance?"
          message={archiveConfirmMessage}
          confirmLabel="Archive anyway"
          destructive
          busy={busy}
          onConfirm={() => void archive(true)}
          onCancel={() => setArchiveConfirmMessage(null)}
        />
      )}
    </div>
  );
};

export default BankRegisterPage;
