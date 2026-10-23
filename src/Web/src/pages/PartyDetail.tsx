import React, { useCallback, useEffect, useState } from 'react';
import { Link, useNavigate, useOutletContext, useParams } from 'react-router-dom';
import { AlertTriangle, Archive, ArchiveRestore, FilePlus, Loader2, Pencil, Receipt, Trash2, X } from 'lucide-react';
import { PageHeader, HeaderButton, HeaderMenu, StatusPill } from '../components/PageHeader';
import { PartyFormDialog } from '../components/PartyFormDialog';
import { ConfirmDialog } from '../components/ConfirmDialog';
import { InvoiceStatusBadge } from '../components/InvoiceStatusBadge';
import { usePermissions } from '../components/Gating';
import { PartyBase, PaymentTermOption, TenantContextType } from '../types';
import { useAuth } from '../auth/AuthContext';
import { currencySymbol as getCurrencySymbol } from '../lib/currency';
import { PartyDocumentRow, PartyRole, partyRoles } from '../lib/partyRoles';

/** One client or vendor: its details, what it owes or is owed, and its invoices or bills. */
export const PartyDetail: React.FC<{ role: PartyRole }> = ({ role }) => {
  const config = partyRoles[role];
  const { currentOrg } = useOutletContext<TenantContextType>();
  const { partyId } = useParams<{ partyId: string }>();
  const { authFetch } = useAuth();
  const { hasPermission } = usePermissions();
  const navigate = useNavigate();
  const canManage = hasPermission(config.managePermission);
  const canCreateDocument = hasPermission(config.createDocumentPermission);
  const listPath = config.listPath(currentOrg.slug);

  const [party, setParty] = useState<PartyBase | null>(null);
  const [documentRows, setDocuments] = useState<PartyDocumentRow[] | null>(null);
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
        const [partyRes, documentsRes, termsRes] = await Promise.all([
          authFetch(config.api.get(partyId ?? '')),
          authFetch(config.api.documents(partyId ?? '')),
          authFetch('/api/Common/PaymentTerms'),
        ]);
        if (cancelled) return;
        if (partyRes.status === 404) {
          setNotFound(true);
          return;
        }
        if (!partyRes.ok) throw new Error(`The ${config.noun} could not be loaded (${partyRes.status}).`);
        setParty(await partyRes.json());
        setDocuments(documentsRes.ok ? ((await documentsRes.json()) as unknown[]).map(config.toDocumentRow) : null);
        if (termsRes.ok) setPaymentTerms(await termsRes.json());
      } catch (err: any) {
        if (!cancelled) setError(err.message || `The ${config.noun} could not be loaded.`);
      } finally {
        if (!cancelled) setIsLoading(false);
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [authFetch, partyId, config]);

  // Newest first, as the API returns them.
  const documents = documentRows ?? [];

  const setArchived = async (archived: boolean) => {
    if (!party) return;
    setBusy(true);
    setError(null);
    try {
      const res = await authFetch(config.api.archive(party.id, archived), { method: 'POST' });
      const data = await res.json().catch(() => null);
      if (!res.ok) throw new Error(data?.message || (archived ? `The ${config.noun} could not be archived.` : `The ${config.noun} could not be restored.`));
      setParty(data);
    } catch (err: any) {
      setError(err.message);
    } finally {
      setBusy(false);
    }
  };

  const handleDelete = async () => {
    if (!party) return;
    setBusy(true);
    setError(null);
    try {
      const res = await authFetch(config.api.remove(party.id), { method: 'DELETE' });
      if (!res.ok) {
        const data = await res.json().catch(() => null);
        throw new Error(data?.message || `The ${config.noun} could not be deleted.`);
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
        <p className="text-sm">Loading {config.noun}...</p>
      </div>
    );
  }

  if (notFound || !party) {
    return (
      <div className="space-y-6">
        <PageHeader title={`${config.Noun} not found`} back={{ to: listPath, label: `Back to ${config.plural}` }} />
        <p className="text-sm text-[var(--text-secondary)]">
          {error || `This ${config.noun} does not exist in this workspace, or it was deleted.`}{' '}
          <Link to={listPath} className="font-medium text-[var(--brand-primary)] hover:underline">
            See all {config.plural}
          </Link>
        </p>
      </div>
    );
  }

  const term = paymentTerms.find((t) => t.id === party.paymentTermId);
  const address = [party.street, [party.city, party.postalCode].filter(Boolean).join(' '), party.country].filter(Boolean);
  const newDocumentPath = config.newDocumentPath?.(currentOrg.slug, party.id);
  const canStartDocument = canCreateDocument && !!newDocumentPath;

  return (
    <div className="space-y-6">
      <PageHeader
        back={{ to: listPath, label: `Back to ${config.plural}` }}
        eyebrow={config.Noun}
        title={party.name}
        badges={party.archived && <StatusPill tone="slate" icon={Archive}>Archived</StatusPill>}
        subtitle={party.outstanding ? config.copy.owes(formatCurrency(party.outstanding)) : undefined}
        actions={
          <>
            {canManage && <HeaderButton label="Edit" icon={Pencil} compact onClick={() => setIsEditing(true)} />}
            {canManage && (
              <HeaderMenu
                items={[
                  party.archived
                    ? { label: `Restore ${config.noun}`, icon: ArchiveRestore, onSelect: () => setArchived(false) }
                    : { label: `Archive ${config.noun}`, icon: Archive, onSelect: () => setArchived(true) },
                  party.canDelete && 'separator',
                  party.canDelete && { label: `Delete ${config.noun}`, icon: Trash2, danger: true, onSelect: () => setIsConfirmingDelete(true) },
                ]}
              />
            )}
            {party.archived
              ? canManage && <HeaderButton label={`Restore ${config.noun}`} icon={ArchiveRestore} variant="primary" busy={busy} onClick={() => setArchived(false)} />
              : canStartDocument && <HeaderButton label={`New ${config.document}`} icon={FilePlus} variant="primary" to={newDocumentPath} />}
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

      {party.archived && (
        <p className="rounded-xl border border-[var(--border-subtle)] bg-[var(--bg-subtle)] px-4 py-3 text-sm text-[var(--text-secondary)]">
          {config.copy.archivedNotice}
        </p>
      )}

      <div className="grid grid-cols-1 lg:grid-cols-3 gap-4">
        <section aria-label={`${config.Noun} details`} className="rounded-xl border border-[var(--border-subtle)] bg-[var(--bg-surface)] p-5 shadow-sm">
          <h2 className="text-xs font-semibold uppercase tracking-wider text-[var(--text-muted)]">Details</h2>
          <dl className="mt-3 space-y-3 text-sm">
            <div>
              <dt className="text-xs text-[var(--text-muted)]">Email</dt>
              <dd className="mt-0.5 break-words text-[var(--text-main)]">
                {party.email ? <a href={`mailto:${party.email}`} className="hover:underline">{party.email}</a> : <span className="text-[var(--text-muted)]">{config.copy.noEmail}</span>}
              </dd>
            </div>
            <div>
              <dt className="text-xs text-[var(--text-muted)]">Phone</dt>
              <dd className="mt-0.5 text-[var(--text-main)]">{party.phone || <span className="text-[var(--text-muted)]">—</span>}</dd>
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
            { label: 'Outstanding', value: party.outstanding, note: config.copy.outstandingNote, tone: 'text-[var(--text-primary)]' },
            { label: 'Overdue', value: party.overdue, note: 'Past the due date', tone: party.overdue ? 'text-rose-600 dark:text-rose-400' : 'text-[var(--text-primary)]' },
            { label: 'Paid to date', value: party.paidToDate, note: config.copy.paidNote, tone: 'text-emerald-600 dark:text-emerald-400' },
          ].map((tile) => (
            <div key={tile.label} className="p-5 rounded-xl border border-[var(--border-subtle)] bg-[var(--bg-surface)] shadow-sm">
              <div className="text-xs font-semibold uppercase tracking-wider text-[var(--text-secondary)]">{tile.label}</div>
              <div className={`mt-3 text-2xl font-bold ${tile.tone}`}>{formatCurrency(tile.value)}</div>
              <p className="text-xs text-[var(--text-secondary)] mt-1">{tile.note}</p>
            </div>
          ))}
        </div>
      </div>

      {documentRows && (
        <section aria-label={`${config.Document}s`} className="rounded-xl border border-[var(--border-subtle)] bg-[var(--bg-surface)] overflow-hidden shadow-sm">
          <h2 className="px-5 pt-4 pb-3 text-sm font-semibold text-[var(--text-main)]">{config.Document}s</h2>
          {documents.length === 0 ? (
            <div className="px-5 pb-8 pt-2 text-center">
              <div className="w-10 h-10 rounded-full bg-[var(--bg-muted)] text-[var(--text-secondary)] mx-auto flex items-center justify-center mb-2">
                <Receipt className="w-5 h-5" />
              </div>
              <p className="text-xs text-[var(--text-secondary)]">No {config.documents} for this {config.noun} yet.</p>
              {canStartDocument && !party.archived && (
                <Link to={newDocumentPath!} className="mt-3 inline-flex items-center gap-1.5 text-xs font-semibold text-[var(--brand-primary)] hover:underline">
                  <FilePlus className="w-3.5 h-3.5" />
                  Create {config.document === 'invoice' ? 'an' : 'a'} {config.document}
                </Link>
              )}
            </div>
          ) : (
            <div className="overflow-x-auto">
              <table className="w-full text-left text-xs border-collapse">
                <thead>
                  <tr className="border-y border-[var(--border-subtle)] bg-[var(--bg-muted)]/50 text-[var(--text-secondary)] font-medium">
                    <th className="py-3 px-4">Status</th>
                    <th className="py-3 px-4">{config.Document} #</th>
                    <th className="py-3 px-4">Date</th>
                    <th className="hidden sm:table-cell py-3 px-4">Due date</th>
                    <th className="hidden sm:table-cell py-3 px-4 text-right">Total</th>
                    <th className="py-3 px-4 text-right">Balance due</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-[var(--border-subtle)]">
                  {documents.map((doc) => {
                    const to = config.documentPath?.(currentOrg.slug, doc.id);
                    const number = doc.number ?? <span className="font-medium italic text-[var(--text-muted)]">Draft</span>;
                    return (
                      <tr
                        key={doc.id}
                        className={`transition-colors ${to ? 'hover:bg-[var(--bg-muted)]/30 cursor-pointer' : ''}`}
                        onClick={to ? () => navigate(to) : undefined}
                      >
                        <td className="py-3 px-4 whitespace-nowrap">
                          <InvoiceStatusBadge status={doc.status} />
                        </td>
                        <td className="py-3 px-4 font-semibold text-[var(--brand-primary)] whitespace-nowrap">
                          {to ? (
                            <Link to={to} onClick={(e) => e.stopPropagation()} className="hover:underline">
                              {number}
                            </Link>
                          ) : (
                            number
                          )}
                        </td>
                        <td className="py-3 px-4 text-[var(--text-secondary)] whitespace-nowrap">{formatDate(doc.date)}</td>
                        <td className="hidden sm:table-cell py-3 px-4 text-[var(--text-secondary)] whitespace-nowrap">{formatDate(doc.dueDate ?? undefined)}</td>
                        <td className="hidden sm:table-cell py-3 px-4 text-right text-[var(--text-primary)] whitespace-nowrap">
                          {formatCurrency(doc.total)}
                        </td>
                        <td className="py-3 px-4 text-right font-medium text-[var(--text-primary)] whitespace-nowrap">
                          {doc.status === 'Draft' || doc.status === 'Void' ? '—' : formatCurrency(doc.balanceDue)}
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
        <PartyFormDialog
          role={role}
          party={party}
          paymentTerms={paymentTerms}
          onClose={() => setIsEditing(false)}
          onSaved={(saved) => {
            setParty(saved);
            setIsEditing(false);
          }}
        />
      )}

      {isConfirmingDelete && (
        <ConfirmDialog
          title={`Delete ${party.name}?`}
          message={`Nothing has been recorded for this ${config.noun} yet, so it can be deleted. This can't be undone.`}
          confirmLabel={`Delete ${config.noun}`}
          destructive
          busy={busy}
          onConfirm={handleDelete}
          onCancel={() => setIsConfirmingDelete(false)}
        />
      )}
    </div>
  );
};
