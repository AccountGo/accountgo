import React, { useCallback, useEffect, useMemo, useState } from 'react';
import { useOutletContext } from 'react-router-dom';
import { AlertTriangle, Loader2, Package, Plus, Search, X } from 'lucide-react';
import { PageHeader, HeaderButton } from '../components/PageHeader';
import { ItemFormDialog } from '../components/ItemFormDialog';
import { ItemsTabs } from '../components/ItemsTabs';
import { usePermissions } from '../components/Gating';
import { CatalogItem, TenantContextType } from '../types';
import { useAuth } from '../auth/AuthContext';
import { currencySymbol as getCurrencySymbol } from '../lib/currency';

/**
 * The products and services a workspace sells or buys. Shared by Sales and Purchases: the selling price shows when
 * the workspace sells, the purchase cost when it buys.
 */
export const ProductsServices: React.FC = () => {
  const { currentOrg, hasModule } = useOutletContext<TenantContextType>();
  const { authFetch } = useAuth();
  const { hasPermission } = usePermissions();
  const canManage = hasPermission('inventory.items.manage');
  const showPrice = hasModule('sales');
  const showCost = hasModule('purchases');
  const showStock = hasModule('inventory');

  const [items, setItems] = useState<CatalogItem[]>([]);
  const [salesAccounts, setSalesAccounts] = useState<{ id: number; code: string; name: string }[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [query, setQuery] = useState('');
  const [view, setView] = useState<'active' | 'archived'>('active');
  // The dialog: {} to add an item, or the item being edited.
  const [dialog, setDialog] = useState<{ item?: CatalogItem } | null>(null);
  const [openingId, setOpeningId] = useState<number | null>(null);

  const currencySymbol = getCurrencySymbol(currentOrg.currency);
  const formatCurrency = useCallback(
    (val: number) => `${currencySymbol}${val.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`,
    [currencySymbol]
  );

  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        const promises: Promise<Response>[] = [authFetch('/api/Catalog/Items')];
        if (showPrice) promises.push(authFetch('/api/Catalog/SalesAccounts'));
        const [itemsRes, accountsRes] = await Promise.all(promises);
        if (!itemsRes.ok) throw new Error(`Products and services could not be loaded (${itemsRes.status}).`);
        const data = await itemsRes.json();
        if (!cancelled) setItems(data);
        if (accountsRes?.ok) {
          const accs = await accountsRes.json();
          if (!cancelled && Array.isArray(accs)) setSalesAccounts(accs);
        }
      } catch (err: any) {
        if (!cancelled) setError(err.message);
      } finally {
        if (!cancelled) setIsLoading(false);
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [authFetch, showPrice]);

  const counts = useMemo(
    () => ({ active: items.filter((i) => !i.archived).length, archived: items.filter((i) => i.archived).length }),
    [items]
  );
  const shown = useMemo(() => {
    const terms = query.trim().toLowerCase().split(/\s+/).filter(Boolean);
    return items.filter((i) => {
      if (i.archived !== (view === 'archived')) return false;
      const haystack = `${i.name} ${i.code ?? ''}`.toLowerCase();
      return terms.every((t) => haystack.includes(t));
    });
  }, [items, query, view]);

  // Whether an item can be deleted is only known per item, so editing loads it first.
  const openItem = async (id: number) => {
    setOpeningId(id);
    setError(null);
    try {
      const res = await authFetch(`/api/Catalog/Item?id=${id}`);
      if (!res.ok) throw new Error('The item could not be loaded.');
      setDialog({ item: await res.json() });
    } catch (err: any) {
      setError(err.message);
    } finally {
      setOpeningId(null);
    }
  };

  const handleSaved = (saved: CatalogItem) => {
    setItems((prev) => {
      const next = prev.some((i) => i.id === saved.id) ? prev.map((i) => (i.id === saved.id ? saved : i)) : [...prev, saved];
      return next.sort((a, b) => a.name.localeCompare(b.name));
    });
    setDialog(null);
  };

  return (
    <div className="space-y-6">
      <PageHeader
        title="Products & services"
        subtitle={
          showPrice && showCost
            ? 'What you sell and buy, with default prices and costs.'
            : showCost
              ? 'What you buy, with default costs.'
              : 'What you sell, with default prices. Pick them when you write an invoice.'
        }
        actions={canManage && <HeaderButton label="New item" icon={Plus} variant="primary" onClick={() => setDialog({})} />}
      />
      <ItemsTabs orgSlug={currentOrg.slug} />

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
            aria-label="Search products and services"
            placeholder="Search by name or code"
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
            <p className="text-sm">Loading products and services...</p>
          </div>
        ) : shown.length === 0 ? (
          <div className="py-16 px-4 text-center">
            <div className="w-12 h-12 rounded-full bg-[var(--bg-muted)] text-[var(--text-secondary)] mx-auto flex items-center justify-center mb-3">
              <Package className="w-6 h-6" />
            </div>
            <h3 className="text-base font-semibold text-[var(--text-primary)]">
              {query ? 'No matching items' : view === 'archived' ? 'No archived items' : 'No products or services yet'}
            </h3>
            <p className="text-xs text-[var(--text-secondary)] max-w-sm mx-auto mt-1">
              {query
                ? `No ${view} items match "${query}".`
                : view === 'archived'
                  ? 'Items you archive are kept here, out of the way when you pick an item for a line.'
                  : 'Add what you sell so you can pick it on an invoice with its price filled in.'}
            </p>
            {!query && view === 'active' && canManage && (
              <button
                onClick={() => setDialog({})}
                className="mt-4 inline-flex items-center gap-1.5 px-4 py-2 rounded-lg bg-[var(--brand-primary)] text-white text-xs font-semibold hover:opacity-90"
              >
                <Plus className="w-3.5 h-3.5" />
                Add an item
              </button>
            )}
          </div>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full text-left text-xs border-collapse">
              <thead>
                <tr className="border-b border-[var(--border-subtle)] bg-[var(--bg-muted)]/50 text-[var(--text-secondary)] font-medium">
                  <th className="py-3 px-4">Name</th>
                  <th className="hidden sm:table-cell py-3 px-4">Code</th>
                  {showPrice && <th className="py-3 px-4 text-right">Selling price</th>}
                  {showCost && <th className="py-3 px-4 text-right">Purchase cost</th>}
                  {showStock && <th className="py-3 px-4 text-right">On hand</th>}
                  {showStock && <th className="hidden sm:table-cell py-3 px-4 text-right">Avg cost</th>}
                </tr>
              </thead>
              <tbody className="divide-y divide-[var(--border-subtle)]">
                {shown.map((item) => (
                  <tr
                    key={item.id}
                    className={`transition-colors ${canManage ? 'cursor-pointer hover:bg-[var(--bg-muted)]/30' : ''}`}
                    onClick={canManage ? () => openItem(item.id) : undefined}
                  >
                    <td className="py-3.5 px-4 max-w-[320px]">
                      {canManage ? (
                        <button
                          type="button"
                          onClick={(e) => {
                            e.stopPropagation();
                            openItem(item.id);
                          }}
                          className="inline-flex max-w-full items-center gap-2 text-left font-medium text-[var(--text-primary)] hover:underline"
                        >
                          <span className="truncate">{item.name}</span>
                          {openingId === item.id && <Loader2 className="w-3.5 h-3.5 shrink-0 animate-spin text-[var(--brand-primary)]" />}
                        </button>
                      ) : (
                        <span className="block truncate font-medium text-[var(--text-primary)]">{item.name}</span>
                      )}
                      {showPrice && item.salesAccountCode && (
                        <span className="block text-xs text-[var(--text-muted)] font-mono">
                          {item.salesAccountCode}{item.salesAccountName ? ` · ${item.salesAccountName}` : ''}
                        </span>
                      )}
                    </td>
                    <td className="hidden sm:table-cell py-3.5 px-4 font-mono text-[var(--text-secondary)] whitespace-nowrap">{item.code || '—'}</td>
                    {showPrice && (
                      <td className="py-3.5 px-4 text-right font-mono text-[var(--text-primary)] whitespace-nowrap">
                        {item.price != null ? formatCurrency(item.price) : '—'}
                      </td>
                    )}
                    {showCost && (
                      <td className="py-3.5 px-4 text-right font-mono text-[var(--text-primary)] whitespace-nowrap">
                        {item.cost != null ? formatCurrency(item.cost) : '—'}
                      </td>
                    )}
                    {showStock && (
                      <>
                        <td className={`py-3.5 px-4 text-right font-mono whitespace-nowrap ${item.trackStock && item.quantityOnHand < 0 ? 'text-rose-600 dark:text-rose-400' : 'text-[var(--text-primary)]'}`}>
                          {item.trackStock ? item.quantityOnHand.toLocaleString(undefined, { maximumFractionDigits: 4 }) : '—'}
                        </td>
                        <td className="hidden sm:table-cell py-3.5 px-4 text-right font-mono text-[var(--text-primary)] whitespace-nowrap">
                          {item.trackStock ? formatCurrency(item.averageCost) : '—'}
                        </td>
                      </>
                    )}
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>

      {dialog && (
        <ItemFormDialog
          item={dialog.item}
          showPrice={showPrice}
          showCost={showCost}
          hasInventoryModule={showStock}
          currencySymbol={currencySymbol}
          salesAccounts={salesAccounts}
          onSaved={handleSaved}
          onDeleted={(id) => {
            setItems((prev) => prev.filter((i) => i.id !== id));
            setDialog(null);
          }}
          onClose={() => setDialog(null)}
        />
      )}
    </div>
  );
};
