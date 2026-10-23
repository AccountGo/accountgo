import React, { useCallback, useEffect, useState } from 'react';
import { Link, useNavigate, useOutletContext, useParams } from 'react-router-dom';
import { AlertTriangle, Archive, ArchiveRestore, FilePlus, Loader2, Pencil, Receipt, Trash2, X } from 'lucide-react';
import { PageHeader, HeaderButton, HeaderMenu, StatusPill } from '../components/PageHeader';
import { ClientFormDialog } from '../components/ClientFormDialog';
import { ConfirmDialog } from '../components/ConfirmDialog';
import { InvoiceStatusBadge } from '../components/InvoiceStatusBadge';
import { usePermissions } from '../components/Gating';
import { Client, Invoice, PaymentTermOption, TenantContextType } from '../types';
import { useAuth } from '../auth/AuthContext';
import { currencySymbol as getCurrencySymbol } from '../lib/currency';

/** One client: its details, what it owes, and its invoices. */
export const ClientDetail: React.FC = () => {
  const { currentOrg } = useOutletContext<TenantContextType>();
  const { clientId } = useParams<{ clientId: string }>();
  const { authFetch } = useAuth();
  const { hasPermission } = usePermissions();
  const navigate = useNavigate();
  const canManage = hasPermission('sales.customers.manage');
  const canInvoice = hasPermission('sales.invoices.create');
  const listPath = `/org/${currentOrg.slug}/sales/clients`;

  const [client, setClient] = useState<Client | null>(null);
  const [invoices, setInvoices] = useState<Invoice[] | null>(null);
  const [paymentTerms, setPaymentTerms] = useState<PaymentTermOption[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [notFound, setNotFound] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [isEditing, setIsEditing] = useState(false);
  const [isConfirmingDelete, setIsConfirmingDelete] = useState(false);
  const [busy, setBusy] = useState(false);

  const currencySymbol = getCurrencySymbol(currentOrg.currency);
  const formatCurrency = useCallback(
    (val: number) => `${currencySymbol}${val.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`,
    [currencySymbol]
  );
  const formatDate = (value?: string) =>
    value ? new Date(value).toLocaleDateString(undefined, { month: 'short', day: 'numeric', year: 'numeric' }) : '—';

  useEffect(() => {
    let cancelled = false;
    (async () => {
      setIsLoading(true);
      try {
        const [clientRes, invoicesRes, termsRes] = await Promise.all([
          authFetch(`/api/Sales/Client?id=${encodeURIComponent(clientId ?? '')}`),
          authFetch(`/api/Sales/SalesInvoices?customerId=${encodeURIComponent(clientId ?? '')}`),
          authFetch('/api/Common/PaymentTerms'),
        ]);
        if (cancelled) return;
        if (clientRes.status === 404) {
          setNotFound(true);
          return;
        }
        if (!clientRes.ok) throw new Error(`The client could not be loaded (${clientRes.status}).`);
        setClient(await clientRes.json());
        setInvoices(invoicesRes.ok ? await invoicesRes.json() : null);
        if (termsRes.ok) setPaymentTerms(await termsRes.json());
      } catch (err: any) {
        if (!cancelled) setError(err.message || 'The client could not be loaded.');
      } finally {
        if (!cancelled) setIsLoading(false);
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [authFetch, clientId]);

  // Newest first, as the API returns them.
  const clientInvoices = invoices ?? [];

  const setArchived = async (archived: boolean) => {
    if (!client) return;
    setBusy(true);
    setError(null);
    try {
      const res = await authFetch(`/api/Sales/ArchiveClient?id=${client.id}&archived=${archived}`, { method: 'POST' });
      const data = await res.json().catch(() => null);
      if (!res.ok) throw new Error(data?.message || (archived ? 'The client could not be archived.' : 'The client could not be restored.'));
      setClient(data);
    } catch (err: any) {
      setError(err.message);
    } finally {
      setBusy(false);
    }
  };

  const handleDelete = async () => {
    if (!client) return;
    setBusy(true);
    setError(null);
    try {
      const res = await authFetch(`/api/Sales/DeleteClient?id=${client.id}`, { method: 'DELETE' });
      if (!res.ok) {
        const data = await res.json().catch(() => null);
        throw new Error(data?.message || 'The client could not be deleted.');
      }
      navigate(listPath, { replace: true });
    } catch (err: any) {
      setError(err.message);
      setIsConfirmingDelete(false);
      setBusy(false);
    }
  };

  if (isLoading) {
    return (
      <div className="py-24 flex flex-col items-center justify-center text-[var(--text-secondary)] gap-3">
        <Loader2 className="w-8 h-8 animate-spin text-[var(--brand-primary)]" />
        <p className="text-sm">Loading client...</p>
      </div>
    );
  }

  if (notFound || !client) {
    return (
      <div className="space-y-6">
        <PageHeader title="Client not found" back={{ to: listPath, label: 'Back to clients' }} />
        <p className="text-sm text-[var(--text-secondary)]">
          {error || 'This client does not exist in this workspace, or it was deleted.'}{' '}
          <Link to={listPath} className="font-medium text-[var(--brand-primary)] hover:underline">
            See all clients
          </Link>
        </p>
      </div>
    );
  }

  const term = paymentTerms.find((t) => t.id === client.paymentTermId);
  const address = [client.street, [client.city, client.postalCode].filter(Boolean).join(' '), client.country].filter(Boolean);
  const newInvoicePath = `/org/${currentOrg.slug}/sales/new?client=${client.id}`;

  return (
    <div className="space-y-6">
      <PageHeader
        back={{ to: listPath, label: 'Back to clients' }}
        eyebrow="Client"
        title={client.name}
        badges={client.archived && <StatusPill tone="slate" icon={Archive}>Archived</StatusPill>}
        subtitle={client.outstanding ? `Owes ${formatCurrency(client.outstanding)}` : undefined}
        actions={
          <>
            {canManage && <HeaderButton label="Edit" icon={Pencil} compact onClick={() => setIsEditing(true)} />}
            {canManage && (
              <HeaderMenu
                items={[
                  client.archived
                    ? { label: 'Restore client', icon: ArchiveRestore, onSelect: () => setArchived(false) }
                    : { label: 'Archive client', icon: Archive, onSelect: () => setArchived(true) },
                  client.canDelete && 'separator',
                  client.canDelete && { label: 'Delete client', icon: Trash2, danger: true, onSelect: () => setIsConfirmingDelete(true) },
                ]}
              />
            )}
            {client.archived
              ? canManage && <HeaderButton label="Restore client" icon={ArchiveRestore} variant="primary" busy={busy} onClick={() => setArchived(false)} />
              : canInvoice && <HeaderButton label="New invoice" icon={FilePlus} variant="primary" to={newInvoicePath} />}
          </>
        }
      />

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

      {client.archived && (
        <p className="rounded-xl border border-[var(--border-subtle)] bg-[var(--bg-subtle)] px-4 py-3 text-sm text-[var(--text-secondary)]">
          This client is archived: it isn't offered when you pick a client for an invoice. Its invoices are unchanged.
        </p>
      )}

      <div className="grid grid-cols-1 lg:grid-cols-3 gap-4">
        <section aria-label="Client details" className="rounded-xl border border-[var(--border-subtle)] bg-[var(--bg-surface)] p-5 shadow-sm">
          <h2 className="text-xs font-semibold uppercase tracking-wider text-[var(--text-muted)]">Details</h2>
          <dl className="mt-3 space-y-3 text-sm">
            <div>
              <dt className="text-xs text-[var(--text-muted)]">Email</dt>
              <dd className="mt-0.5 break-words text-[var(--text-main)]">
                {client.email ? <a href={`mailto:${client.email}`} className="hover:underline">{client.email}</a> : <span className="text-[var(--text-muted)]">None, so invoices can't be emailed</span>}
              </dd>
            </div>
            <div>
              <dt className="text-xs text-[var(--text-muted)]">Phone</dt>
              <dd className="mt-0.5 text-[var(--text-main)]">{client.phone || <span className="text-[var(--text-muted)]">—</span>}</dd>
            </div>
            <div>
              <dt className="text-xs text-[var(--text-muted)]">Address</dt>
              <dd className="mt-0.5 text-[var(--text-main)]">
                {address.length ? address.map((line) => <div key={line}>{line}</div>) : <span className="text-[var(--text-muted)]">—</span>}
              </dd>
            </div>
            <div>
              <dt className="text-xs text-[var(--text-muted)]">Default payment terms</dt>
              <dd className="mt-0.5 text-[var(--text-main)]">{term?.description ?? 'Workspace default'}</dd>
            </div>
          </dl>
        </section>

        <div className="lg:col-span-2 grid grid-cols-1 sm:grid-cols-3 gap-4 content-start">
          {[
            { label: 'Outstanding', value: client.outstanding, note: 'Unpaid on issued invoices', tone: 'text-[var(--text-primary)]' },
            { label: 'Overdue', value: client.overdue, note: 'Past the due date', tone: client.overdue ? 'text-rose-600 dark:text-rose-400' : 'text-[var(--text-primary)]' },
            { label: 'Paid to date', value: client.paidToDate, note: 'Payments recorded', tone: 'text-emerald-600 dark:text-emerald-400' },
          ].map((tile) => (
            <div key={tile.label} className="p-5 rounded-xl border border-[var(--border-subtle)] bg-[var(--bg-surface)] shadow-sm">
              <div className="text-xs font-semibold uppercase tracking-wider text-[var(--text-secondary)]">{tile.label}</div>
              <div className={`mt-3 text-2xl font-bold ${tile.tone}`}>{formatCurrency(tile.value)}</div>
              <p className="text-xs text-[var(--text-secondary)] mt-1">{tile.note}</p>
            </div>
          ))}
        </div>
      </div>

      {invoices && (
        <section aria-label="Invoices" className="rounded-xl border border-[var(--border-subtle)] bg-[var(--bg-surface)] overflow-hidden shadow-sm">
          <h2 className="px-5 pt-4 pb-3 text-sm font-semibold text-[var(--text-main)]">Invoices</h2>
          {clientInvoices.length === 0 ? (
            <div className="px-5 pb-8 pt-2 text-center">
              <div className="w-10 h-10 rounded-full bg-[var(--bg-muted)] text-[var(--text-secondary)] mx-auto flex items-center justify-center mb-2">
                <Receipt className="w-5 h-5" />
              </div>
              <p className="text-xs text-[var(--text-secondary)]">No invoices for this client yet.</p>
              {canInvoice && !client.archived && (
                <Link to={newInvoicePath} className="mt-3 inline-flex items-center gap-1.5 text-xs font-semibold text-[var(--brand-primary)] hover:underline">
                  <FilePlus className="w-3.5 h-3.5" />
                  Create an invoice
                </Link>
              )}
            </div>
          ) : (
            <div className="overflow-x-auto">
              <table className="w-full text-left text-xs border-collapse">
                <thead>
                  <tr className="border-y border-[var(--border-subtle)] bg-[var(--bg-muted)]/50 text-[var(--text-secondary)] font-medium">
                    <th className="py-3 px-4">Status</th>
                    <th className="py-3 px-4">Invoice #</th>
                    <th className="py-3 px-4">Date</th>
                    <th className="hidden sm:table-cell py-3 px-4">Due date</th>
                    <th className="hidden sm:table-cell py-3 px-4 text-right">Total</th>
                    <th className="py-3 px-4 text-right">Balance due</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-[var(--border-subtle)]">
                  {clientInvoices.map((inv) => {
                    const to = `/org/${currentOrg.slug}/sales/${inv.id}`;
                    return (
                      <tr key={inv.id} className="hover:bg-[var(--bg-muted)]/30 transition-colors cursor-pointer" onClick={() => navigate(to)}>
                        <td className="py-3 px-4 whitespace-nowrap">
                          <InvoiceStatusBadge status={inv.status} />
                        </td>
                        <td className="py-3 px-4 font-semibold text-[var(--brand-primary)] whitespace-nowrap">
                          <Link to={to} onClick={(e) => e.stopPropagation()} className="hover:underline">
                            {inv.no ? `#${inv.no}` : <span className="font-medium italic text-[var(--text-muted)]">Draft</span>}
                          </Link>
                        </td>
                        <td className="py-3 px-4 text-[var(--text-secondary)] whitespace-nowrap">{formatDate(inv.invoiceDate)}</td>
                        <td className="hidden sm:table-cell py-3 px-4 text-[var(--text-secondary)] whitespace-nowrap">{formatDate(inv.dueDate)}</td>
                        <td className="hidden sm:table-cell py-3 px-4 text-right text-[var(--text-primary)] whitespace-nowrap">
                          {formatCurrency(inv.totalAmountAfterTax ?? inv.amount)}
                        </td>
                        <td className="py-3 px-4 text-right font-medium text-[var(--text-primary)] whitespace-nowrap">
                          {inv.status === 'Draft' || inv.status === 'Void' ? '—' : formatCurrency(inv.balanceDue)}
                        </td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            </div>
          )}
        </section>
      )}

      {isEditing && (
        <ClientFormDialog
          client={client}
          paymentTerms={paymentTerms}
          onClose={() => setIsEditing(false)}
          onSaved={(saved) => {
            setClient(saved);
            setIsEditing(false);
          }}
        />
      )}

      {isConfirmingDelete && (
        <ConfirmDialog
          title={`Delete ${client.name}?`}
          message="Nothing has been recorded for this client yet, so it can be deleted. This can't be undone."
          confirmLabel="Delete client"
          destructive
          busy={busy}
          onConfirm={handleDelete}
          onCancel={() => setIsConfirmingDelete(false)}
        />
      )}
    </div>
  );
};
