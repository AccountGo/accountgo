import React, { useCallback, useEffect, useState } from 'react';
import { Link, useNavigate, useOutletContext, useParams } from 'react-router-dom';
import { AlertTriangle, Ban, CheckCircle2, Copy, Loader2, Pencil, Trash2, Wallet, X } from 'lucide-react';
import { PageHeader, HeaderButton, HeaderMenu } from '../components/PageHeader';
import { InvoiceStatusBadge } from '../components/InvoiceStatusBadge';
import { PayBillDialog } from '../components/PayBillDialog';
import { ConfirmDialog } from '../components/ConfirmDialog';
import { ReadOnlyModuleBanner, usePermissions } from '../components/Gating';
import { Bill, PaymentTermOption, TenantContextType } from '../types';
import { useAuth } from '../auth/AuthContext';
import { currencySymbol as getCurrencySymbol } from '../lib/currency';
import { BillTitle, billTitle } from '../lib/billTitle';
import { useCapabilities } from '../lib/entitlements';

/** One bill: who it is from, its lines and the accounts they are booked to, what is still owed, and its payments. */
export const BillDetail: React.FC = () => {
  const { currentOrg, subscription, canWrite } = useOutletContext<TenantContextType>();
  const { billId } = useParams<{ billId: string }>();
  const { authFetch } = useAuth();
  const navigate = useNavigate();
  const { hasPermission } = usePermissions();
  const { canCreate: canCreateResource } = useCapabilities();
  const purchasesWritable = canWrite('purchases');
  const canCreate = hasPermission('purchases.bills.create') && purchasesWritable && canCreateResource('bills');
  const canEdit = hasPermission('purchases.bills.create') && purchasesWritable;
  const canApprove = hasPermission('purchases.bills.post') && purchasesWritable;
  const canPay = hasPermission('purchases.payments.create') && purchasesWritable;
  const canSeeVendors = hasPermission('purchases.vendors.view');
  const orgSlug = currentOrg.slug;
  const listPath = `/org/${orgSlug}/purchases`;

  const planKey = (currentOrg.plan || subscription?.planId || 'accounting').toLowerCase();
  const isAccountingPlan = planKey === 'accounting' || planKey === 'enterprise';

  const [bill, setBill] = useState<Bill | null>(null);
  const [paymentTerms, setPaymentTerms] = useState<PaymentTermOption[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [notFound, setNotFound] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [busy, setBusy] = useState<'approve' | 'void' | 'delete' | null>(null);
  const [confirm, setConfirm] = useState<'void' | 'delete' | null>(null);
  const [isPaying, setIsPaying] = useState(false);

  const currencySymbol = getCurrencySymbol(currentOrg.currency);
  const formatCurrency = useCallback(
    (val: number) => `${currencySymbol}${val.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`,
    [currencySymbol]
  );
  const formatDate = (value?: string | null) =>
    value ? new Date(value).toLocaleDateString(undefined, { month: 'short', day: 'numeric', year: 'numeric' }) : '—';

  useEffect(() => {
    let cancelled = false;
    (async () => {
      setIsLoading(true);
      try {
        const [billRes, termsRes] = await Promise.all([
          authFetch(`/api/Purchasing/Bill?id=${encodeURIComponent(billId ?? '')}`),
          authFetch('/api/Common/PaymentTerms'),
        ]);
        if (cancelled) return;
        if (billRes.status === 404) {
          setNotFound(true);
          return;
        }
        if (!billRes.ok) throw new Error(`The bill could not be loaded (${billRes.status}).`);
        setBill(await billRes.json());
        if (termsRes.ok) setPaymentTerms(await termsRes.json());
      } catch (err: any) {
        if (!cancelled) setError(err.message || 'The bill could not be loaded.');
      } finally {
        if (!cancelled) setIsLoading(false);
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [authFetch, billId]);

  const act = async (kind: 'approve' | 'void', path: string, done: string) => {
    if (!bill) return;
    setBusy(kind);
    setError(null);
    try {
      const res = await authFetch(`${path}?id=${bill.id}`, { method: 'POST' });
      const data = await res.json().catch(() => null);
      if (!res.ok) throw new Error(res.status === 403 ? "You don't have permission to do that." : data?.message || `The bill could not be ${kind === 'approve' ? 'approved' : 'voided'}.`);
      setBill(data);
      setNotice(done);
    } catch (err: any) {
      setError(err.message);
    } finally {
      setBusy(null);
      setConfirm(null);
    }
  };

  const handleDelete = async () => {
    if (!bill) return;
    setBusy('delete');
    setError(null);
    try {
      const res = await authFetch(`/api/Purchasing/DeleteBill?id=${bill.id}`, { method: 'DELETE' });
      if (!res.ok) {
        const data = await res.json().catch(() => null);
        throw new Error(data?.message || 'The bill could not be deleted.');
      }
      navigate(listPath, { replace: true });
    } catch (err: any) {
      setError(err.message);
      setBusy(null);
      setConfirm(null);
    }
  };

  if (isLoading) {
    return (
      <div className="py-24 flex flex-col items-center justify-center text-[var(--text-secondary)] gap-3">
        <Loader2 className="w-8 h-8 animate-spin text-[var(--brand-primary)]" />
        <p className="text-sm">Loading bill...</p>
      </div>
    );
  }

  if (notFound || !bill) {
    return (
      <div className="space-y-6">
        <PageHeader title="Bill not found" back={{ to: listPath, label: 'Back to bills' }} />
        <p className="text-sm text-[var(--text-secondary)]">
          {error || 'This bill does not exist in this workspace, or it was deleted.'}{' '}
          <Link to={listPath} className="font-medium text-[var(--brand-primary)] hover:underline">
            See all bills
          </Link>
        </p>
      </div>
    );
  }

  const isDraft = bill.status === 'Draft';
  const isVoid = bill.status === 'Void';
  const canVoid = canApprove && bill.approved && !isVoid && bill.amountPaid === 0;
  const term = paymentTerms.find((t) => t.id === bill.paymentTermId);
  const hasTax = bill.totalTax > 0 || bill.lines.some((l) => l.taxAmount > 0);
  const vendorPath = `/org/${orgSlug}/purchases/vendors/${bill.vendorId}`;
  const duplicatePath = `/org/${orgSlug}/purchases/new?from=${bill.id}`;

  return (
    <div className="space-y-6">
      <ReadOnlyModuleBanner module="purchases" orgSlug={orgSlug} />
      <PageHeader
        back={{ to: listPath, label: 'Back to bills' }}
        eyebrow={bill.no ? `Bill #${bill.no}` : 'Bill'}
        title={bill.vendorInvoiceNo ? `${bill.vendorInvoiceNo} · ${bill.vendorName}` : bill.vendorName ?? 'Bill'}
        badges={<InvoiceStatusBadge status={bill.status} />}
        subtitle={
          isVoid
            ? 'Void: what approving it posted has been reversed.'
            : isDraft
              ? 'Draft: not yet approved, so it is not counted as owed.'
              : bill.balanceDue > 0
                ? `${formatCurrency(bill.balanceDue)} owed, due ${formatDate(bill.dueDate)}`
                : 'Paid in full'
        }
        actions={
          <>
            {isDraft && canEdit && <HeaderButton label="Edit" icon={Pencil} compact to={`/org/${orgSlug}/purchases/${bill.id}/edit`} />}
            {(canCreate || canEdit || canVoid) && (
              <HeaderMenu
                items={[
                  canCreate && { label: 'Duplicate', icon: Copy, onSelect: () => navigate(duplicatePath) },
                  canVoid && 'separator',
                  canVoid && { label: 'Void bill', icon: Ban, danger: true, onSelect: () => setConfirm('void') },
                  isDraft && canEdit && 'separator',
                  isDraft && canEdit && { label: 'Delete draft', icon: Trash2, danger: true, onSelect: () => setConfirm('delete') },
                ]}
              />
            )}
            {isDraft && canApprove && (
              <HeaderButton
                label="Approve"
                icon={CheckCircle2}
                variant="primary"
                busy={busy === 'approve'}
                onClick={() => act('approve', '/api/Purchasing/ApproveBill', `${BillTitle(bill)} is approved and counted as owed.`)}
              />
            )}
            {!isDraft && !isVoid && bill.balanceDue > 0 && canPay && (
              <HeaderButton label="Pay bill" icon={Wallet} variant="primary" onClick={() => setIsPaying(true)} />
            )}
          </>
        }
      />

      {notice && (
        <div role="status" className="p-4 rounded-xl bg-emerald-500/10 border border-emerald-500/20 text-emerald-600 dark:text-emerald-400 text-sm flex items-center justify-between">
          <div className="flex items-center gap-2">
            <CheckCircle2 className="w-4 h-4 shrink-0" />
            <span>{notice}</span>
          </div>
          <button onClick={() => setNotice(null)} aria-label="Dismiss" className="p-1 hover:opacity-75">
            <X className="w-4 h-4" />
          </button>
        </div>
      )}
      {error && (
        <div role="alert" className="p-4 rounded-xl bg-rose-500/10 border border-rose-500/20 text-rose-600 dark:text-rose-400 text-sm flex items-center justify-between">
          <div className="flex items-center gap-2">
            <AlertTriangle className="w-4 h-4 shrink-0" />
            <span>{error}</span>
          </div>
          <button onClick={() => setError(null)} aria-label="Dismiss" className="p-1 hover:opacity-75">
            <X className="w-4 h-4" />
          </button>
        </div>
      )}
      {isDraft && !canApprove && (
        <p className="rounded-xl border border-[var(--border-subtle)] bg-[var(--bg-subtle)] px-4 py-3 text-sm text-[var(--text-secondary)]">
          This draft is waiting for someone with the "Post / Approve Bills" permission to approve it.
        </p>
      )}

      <div className="grid grid-cols-1 lg:grid-cols-3 gap-4">
        <section aria-label="Vendor" className="rounded-xl border border-[var(--border-subtle)] bg-[var(--bg-surface)] p-5 shadow-sm">
          <h2 className="text-xs font-semibold uppercase tracking-wider text-[var(--text-muted)]">From</h2>
          <div className="mt-3 space-y-1 text-sm">
            {canSeeVendors ? (
              <Link to={vendorPath} className="font-semibold text-[var(--text-main)] hover:underline">
                {bill.vendorName}
              </Link>
            ) : (
              <div className="font-semibold text-[var(--text-main)]">{bill.vendorName}</div>
            )}
            {bill.vendorAddress && <div className="text-[var(--text-secondary)]">{bill.vendorAddress}</div>}
            {bill.vendorEmail && <div className="text-[var(--text-secondary)] break-words">{bill.vendorEmail}</div>}
            {bill.vendorPhone && <div className="text-[var(--text-secondary)]">{bill.vendorPhone}</div>}
          </div>
        </section>

        <section aria-label="Bill details" className="lg:col-span-2 rounded-xl border border-[var(--border-subtle)] bg-[var(--bg-surface)] p-5 shadow-sm">
          <dl className="grid grid-cols-2 sm:grid-cols-3 gap-x-4 gap-y-3 text-sm">
            {[
              ["Vendor's bill #", bill.vendorInvoiceNo || '—'],
              ['Our bill #', bill.no ? `#${bill.no}` : 'Given when approved'],
              ['Reference', bill.referenceNo || '—'],
              ['Bill date', formatDate(bill.date)],
              ['Due date', formatDate(bill.dueDate)],
              ['Terms', term?.description ?? 'Custom'],
            ].map(([label, value]) => (
              <div key={label}>
                <dt className="text-xs text-[var(--text-muted)]">{label}</dt>
                <dd className="mt-0.5 text-[var(--text-main)]">{value}</dd>
              </div>
            ))}
          </dl>
        </section>
      </div>

      <section aria-label="Lines" className="rounded-xl border border-[var(--border-subtle)] bg-[var(--bg-surface)] overflow-hidden shadow-sm">
        <div className="overflow-x-auto">
          <table className="w-full text-left text-xs border-collapse">
            <thead>
              <tr className="border-b border-[var(--border-subtle)] bg-[var(--bg-muted)]/50 text-[var(--text-secondary)] font-medium">
                <th className="py-3 px-4">Description</th>
                {isAccountingPlan && <th className="hidden sm:table-cell py-3 px-4">Account</th>}
                <th className="py-3 px-4 text-right">Qty</th>
                <th className="hidden sm:table-cell py-3 px-4 text-right">Unit cost</th>
                {hasTax && <th className="hidden md:table-cell py-3 px-4 text-right">Tax</th>}
                <th className="py-3 px-4 text-right">Amount</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-[var(--border-subtle)]">
              {bill.lines.map((line) => (
                <tr key={line.id}>
                  <td className="py-3 px-4 text-[var(--text-main)]">
                    <div>{line.description}</div>
                    {isAccountingPlan && line.accountName && <div className="sm:hidden text-[10px] text-[var(--text-secondary)]">{line.accountName}</div>}
                    {line.discount ? <div className="text-[10px] text-[var(--text-secondary)]">{line.discount}% off</div> : null}
                  </td>
                  {isAccountingPlan && <td className="hidden sm:table-cell py-3 px-4 text-[var(--text-secondary)]">{line.accountName ?? '—'}</td>}
                  <td className="py-3 px-4 text-right text-[var(--text-secondary)] whitespace-nowrap">{line.quantity}</td>
                  <td className="hidden sm:table-cell py-3 px-4 text-right text-[var(--text-secondary)] whitespace-nowrap">{formatCurrency(line.amount)}</td>
                  {hasTax && <td className="hidden md:table-cell py-3 px-4 text-right text-[var(--text-secondary)] whitespace-nowrap">{formatCurrency(line.taxAmount)}</td>}
                  <td className="py-3 px-4 text-right font-medium text-[var(--text-main)] whitespace-nowrap">{formatCurrency(line.subtotal)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
        <div className="border-t border-[var(--border-subtle)] px-4 py-4 flex justify-end">
          <dl className="w-full max-w-xs space-y-1.5 text-sm">
            <div className="flex justify-between text-[var(--text-secondary)]">
              <dt>Subtotal</dt>
              <dd className="font-mono text-[var(--text-main)]">{formatCurrency(bill.subtotal)}</dd>
            </div>
            <div className="flex justify-between text-[var(--text-secondary)]">
              <dt>Tax</dt>
              <dd className="font-mono text-[var(--text-main)]">{formatCurrency(bill.totalTax)}</dd>
            </div>
            <div className="flex justify-between border-t border-[var(--border-subtle)] pt-1.5 font-semibold text-[var(--text-main)]">
              <dt>Total</dt>
              <dd className="font-mono">{formatCurrency(bill.total)}</dd>
            </div>
            {bill.amountPaid > 0 && (
              <div className="flex justify-between text-emerald-600 dark:text-emerald-400">
                <dt>Paid</dt>
                <dd className="font-mono">−{formatCurrency(bill.amountPaid)}</dd>
              </div>
            )}
            {!isDraft && !isVoid && (
              <div className="flex justify-between text-base font-bold text-[var(--text-main)]">
                <dt>Balance due</dt>
                <dd className="font-mono">{formatCurrency(bill.balanceDue)}</dd>
              </div>
            )}
          </dl>
        </div>
      </section>

      {bill.payments.length > 0 && (
        <section aria-label="Payments" className="rounded-xl border border-[var(--border-subtle)] bg-[var(--bg-surface)] overflow-hidden shadow-sm">
          <h2 className="px-5 pt-4 pb-3 text-sm font-semibold text-[var(--text-main)]">Payments</h2>
          <div className="overflow-x-auto">
            <table className="w-full text-left text-xs border-collapse">
              <thead>
                <tr className="border-y border-[var(--border-subtle)] bg-[var(--bg-muted)]/50 text-[var(--text-secondary)] font-medium">
                  <th className="py-3 px-4">Date</th>
                  <th className="py-3 px-4">Paid from</th>
                  <th className="hidden sm:table-cell py-3 px-4">Method</th>
                  <th className="hidden sm:table-cell py-3 px-4">Reference</th>
                  <th className="py-3 px-4 text-right">Amount</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-[var(--border-subtle)]">
                {bill.payments.map((p) => (
                  <tr key={p.id}>
                    <td className="py-3 px-4 text-[var(--text-secondary)] whitespace-nowrap">{formatDate(p.date)}</td>
                    <td className="py-3 px-4 text-[var(--text-main)]">
                      {p.accountName ?? '—'}
                      {p.memo && <div className="text-[10px] text-[var(--text-secondary)]">{p.memo}</div>}
                    </td>
                    <td className="hidden sm:table-cell py-3 px-4 text-[var(--text-secondary)]">{p.paymentMethod ?? '—'}</td>
                    <td className="hidden sm:table-cell py-3 px-4 text-[var(--text-secondary)]">{p.referenceNo ?? '—'}</td>
                    <td className="py-3 px-4 text-right font-medium text-[var(--text-main)] whitespace-nowrap">{formatCurrency(p.amount)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </section>
      )}

      {bill.notes && (
        <section aria-label="Notes" className="rounded-xl border border-[var(--border-subtle)] bg-[var(--bg-surface)] p-5 shadow-sm">
          <h2 className="text-xs font-semibold uppercase tracking-wider text-[var(--text-muted)]">Notes</h2>
          <p className="mt-2 whitespace-pre-wrap text-sm text-[var(--text-main)]">{bill.notes}</p>
        </section>
      )}

      {isPaying && (
        <PayBillDialog
          bill={bill}
          currencySymbol={currencySymbol}
          formatCurrency={formatCurrency}
          onClose={() => setIsPaying(false)}
          onPaid={(updated) => {
            setBill(updated);
            setNotice(updated.balanceDue > 0 ? `Payment recorded. ${formatCurrency(updated.balanceDue)} is still owed.` : 'Payment recorded. The bill is paid in full.');
            setIsPaying(false);
          }}
        />
      )}

      {confirm === 'void' && (
        <ConfirmDialog
          title={`Void ${billTitle(bill)}?`}
          message="Voiding reverses what approving it posted, so it no longer counts as owed or spent. Stock received on this bill is removed. It stays listed as void. This can't be undone."
          confirmLabel="Void bill"
          destructive
          busy={busy === 'void'}
          onConfirm={() => act('void', '/api/Purchasing/VoidBill', `${BillTitle(bill)} is void.`)}
          onCancel={() => setConfirm(null)}
        />
      )}

      {confirm === 'delete' && (
        <ConfirmDialog
          title="Delete this draft?"
          message="The draft and its lines are deleted. Nothing was posted for it. This can't be undone."
          confirmLabel="Delete draft"
          destructive
          busy={busy === 'delete'}
          onConfirm={handleDelete}
          onCancel={() => setConfirm(null)}
        />
      )}
    </div>
  );
};
