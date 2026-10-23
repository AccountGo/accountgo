import React, { useCallback, useEffect, useMemo, useState } from 'react';
import { Link, useNavigate, useOutletContext } from 'react-router-dom';
import { AlertTriangle, Loader2, Mail, Search, UserPlus, Users, X } from 'lucide-react';
import { PageHeader, HeaderButton } from '../components/PageHeader';
import { SalesTabs } from '../components/SalesTabs';
import { ClientFormDialog } from '../components/ClientFormDialog';
import { usePermissions } from '../components/Gating';
import { Client, PaymentTermOption, TenantContextType } from '../types';
import { useAuth } from '../auth/AuthContext';
import { currencySymbol as getCurrencySymbol } from '../lib/currency';

/** The clients a workspace invoices, with what each one owes. */
export const Clients: React.FC = () => {
  const { currentOrg } = useOutletContext<TenantContextType>();
  const { authFetch } = useAuth();
  const { hasPermission } = usePermissions();
  const navigate = useNavigate();
  const canManage = hasPermission('sales.customers.manage');

  const [clients, setClients] = useState<Client[]>([]);
  const [paymentTerms, setPaymentTerms] = useState<PaymentTermOption[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [query, setQuery] = useState('');
  const [view, setView] = useState<'active' | 'archived'>('active');
  const [isAdding, setIsAdding] = useState(false);

  const currencySymbol = getCurrencySymbol(currentOrg.currency);
  const formatCurrency = useCallback(
    (val: number) => `${currencySymbol}${val.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`,
    [currencySymbol]
  );

  const load = useCallback(async () => {
    setIsLoading(true);
    setError(null);
    try {
      const [clientsRes, termsRes] = await Promise.all([authFetch('/api/Sales/Clients'), authFetch('/api/Common/PaymentTerms')]);
      if (!clientsRes.ok) throw new Error(`Clients could not be loaded (${clientsRes.status}).`);
      setClients(await clientsRes.json());
      if (termsRes.ok) setPaymentTerms(await termsRes.json());
    } catch (err: any) {
      setError(err.message || 'Clients could not be loaded.');
    } finally {
      setIsLoading(false);
    }
  }, [authFetch]);

  useEffect(() => {
    load();
  }, [load]);

  const counts = useMemo(
    () => ({ active: clients.filter((c) => !c.archived).length, archived: clients.filter((c) => c.archived).length }),
    [clients]
  );
  const shown = useMemo(() => {
    const terms = query.trim().toLowerCase().split(/\s+/).filter(Boolean);
    return clients.filter((c) => {
      if (c.archived !== (view === 'archived')) return false;
      const haystack = `${c.name} ${c.email ?? ''} ${c.phone ?? ''} ${c.city ?? ''}`.toLowerCase();
      return terms.every((t) => haystack.includes(t));
    });
  }, [clients, query, view]);

  const formatDate = (value?: string) =>
    value ? new Date(value).toLocaleDateString(undefined, { month: 'short', day: 'numeric', year: 'numeric' }) : '—';

  return (
    <div className="space-y-6">
      <PageHeader
        title="Clients"
        subtitle="The people and businesses you invoice, and what each one owes you."
        actions={canManage && <HeaderButton label="New client" icon={UserPlus} variant="primary" onClick={() => setIsAdding(true)} />}
      />
      <SalesTabs orgSlug={currentOrg.slug} />

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

      <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-4">
        <div className="flex items-center gap-1">
          {([
            ['active', 'Active', counts.active],
            ['archived', 'Archived', counts.archived],
          ] as const).map(([key, label, count]) => {
            const isActive = view === key;
            return (
              <button
                key={key}
                onClick={() => setView(key)}
                aria-pressed={isActive}
                className={`px-3 py-1.5 rounded-lg text-xs font-medium transition-colors whitespace-nowrap flex items-center gap-1.5 ${
                  isActive
                    ? 'bg-[var(--brand-primary)] text-white'
                    : 'text-[var(--text-secondary)] hover:text-[var(--text-primary)] hover:bg-[var(--bg-surface)]'
                }`}
              >
                <span>{label}</span>
                <span className={`px-1.5 rounded-full text-[10px] ${isActive ? 'bg-white/20 text-white' : 'bg-[var(--bg-surface)] text-[var(--text-secondary)] border border-[var(--border-subtle)]'}`}>
                  {count}
                </span>
              </button>
            );
          })}
        </div>
        <div className="relative w-full sm:w-64">
          <Search className="w-4 h-4 absolute left-3 top-1/2 -translate-y-1/2 text-[var(--text-secondary)] pointer-events-none" />
          <input
            type="search"
            aria-label="Search clients"
            placeholder="Search by name, email or phone"
            value={query}
            onChange={(e) => setQuery(e.target.value)}
            className="w-full pl-9 pr-3 py-1.5 text-xs rounded-lg border border-[var(--border-subtle)] bg-[var(--bg-surface)] text-[var(--text-primary)] placeholder-[var(--text-secondary)] focus:outline-none focus:ring-2 focus:ring-[var(--brand-primary)]"
          />
        </div>
      </div>

      <div className="rounded-xl border border-[var(--border-subtle)] bg-[var(--bg-surface)] overflow-hidden shadow-sm">
        {isLoading ? (
          <div className="py-20 flex flex-col items-center justify-center text-[var(--text-secondary)] gap-3">
            <Loader2 className="w-8 h-8 animate-spin text-[var(--brand-primary)]" />
            <p className="text-sm">Loading clients...</p>
          </div>
        ) : shown.length === 0 ? (
          <div className="py-16 px-4 text-center">
            <div className="w-12 h-12 rounded-full bg-[var(--bg-muted)] text-[var(--text-secondary)] mx-auto flex items-center justify-center mb-3">
              <Users className="w-6 h-6" />
            </div>
            <h3 className="text-base font-semibold text-[var(--text-primary)]">
              {query ? 'No matching clients' : view === 'archived' ? 'No archived clients' : 'No clients yet'}
            </h3>
            <p className="text-xs text-[var(--text-secondary)] max-w-sm mx-auto mt-1">
              {query
                ? `No ${view} clients match "${query}".`
                : view === 'archived'
                  ? 'Clients you archive are kept here with their invoices, out of the way when you pick a client.'
                  : 'Add the people and businesses you invoice. You can also add one while writing an invoice.'}
            </p>
            {!query && view === 'active' && canManage && (
              <button
                onClick={() => setIsAdding(true)}
                className="mt-4 inline-flex items-center gap-1.5 px-4 py-2 rounded-lg bg-[var(--brand-primary)] text-white text-xs font-semibold hover:opacity-90"
              >
                <UserPlus className="w-3.5 h-3.5" />
                Add a client
              </button>
            )}
          </div>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full text-left text-xs border-collapse">
              <thead>
                <tr className="border-b border-[var(--border-subtle)] bg-[var(--bg-muted)]/50 text-[var(--text-secondary)] font-medium">
                  <th className="py-3 px-4">Client</th>
                  <th className="hidden sm:table-cell py-3 px-4">Phone</th>
                  <th className="hidden sm:table-cell py-3 px-4">Last invoice</th>
                  <th className="py-3 px-4 text-right">Outstanding</th>
                  <th className="py-3 px-4 text-right">Overdue</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-[var(--border-subtle)]">
                {shown.map((c) => {
                  const to = `/org/${currentOrg.slug}/sales/clients/${c.id}`;
                  return (
                    <tr key={c.id} className="hover:bg-[var(--bg-muted)]/30 transition-colors cursor-pointer" onClick={() => navigate(to)}>
                      <td className="py-3.5 px-4 max-w-[260px]">
                        <Link to={to} onClick={(e) => e.stopPropagation()} className="block truncate font-medium text-[var(--text-primary)] hover:underline">
                          {c.name}
                        </Link>
                        {c.email && (
                          <div className="text-[10px] text-[var(--text-secondary)] truncate flex items-center gap-1 mt-0.5">
                            <Mail className="w-3 h-3 shrink-0" />
                            <span className="truncate">{c.email}</span>
                          </div>
                        )}
                      </td>
                      <td className="hidden sm:table-cell py-3.5 px-4 text-[var(--text-secondary)] whitespace-nowrap">{c.phone || '—'}</td>
                      <td className="hidden sm:table-cell py-3.5 px-4 text-[var(--text-secondary)] whitespace-nowrap">{formatDate(c.lastInvoiceDate ?? undefined)}</td>
                      <td className="py-3.5 px-4 text-right font-medium text-[var(--text-primary)] whitespace-nowrap">
                        {formatCurrency(c.outstanding)}
                      </td>
                      <td className={`py-3.5 px-4 text-right whitespace-nowrap ${c.overdue ? 'font-semibold text-rose-600 dark:text-rose-400' : 'text-[var(--text-secondary)]'}`}>
                        {formatCurrency(c.overdue)}
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        )}
      </div>

      {isAdding && (
        <ClientFormDialog
          paymentTerms={paymentTerms}
          onClose={() => setIsAdding(false)}
          onSaved={(client) => navigate(`/org/${currentOrg.slug}/sales/clients/${client.id}`)}
        />
      )}
    </div>
  );
};
