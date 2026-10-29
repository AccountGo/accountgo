import React, { FormEvent, useCallback, useEffect, useMemo, useState } from 'react';
import { Link, useNavigate, useParams } from 'react-router-dom';
import { ArrowDown, ArrowUp, Loader2, Package, Plus, X } from 'lucide-react';
import { HeaderButton, PageHeader } from '../components/PageHeader';
import { PermissionDeniedCard, useTenantContext } from '../components/Gating';
import { ItemsTabs } from '../components/ItemsTabs';
import { useAuth } from '../auth/AuthContext';
import { localToday } from '../lib/businessDays';
import { currencySymbol } from '../lib/currency';
import { ModalShell } from '../components/ModalShell';

interface StockItem {
  id: number;
  code: string | null;
  name: string;
  quantityOnHand: number;
  averageCost: number;
  value: number;
  archived: boolean;
  hasMovements: boolean;
}

interface StockItemsResponse {
  items: StockItem[];
  totalValue: number;
  inventoryAccountBalance: number;
}

interface StockMovement {
  id: number;
  itemId: number;
  itemName: string;
  date: string;
  type: string;
  documentId: number | null;
  documentNo: string | null;
  linkType: 'bill' | 'invoice' | null;
  in: number;
  out: number;
  unitCost: number | null;
  totalCost: number | null;
  runningQuantity: number;
  memo: string | null;
}

interface AdjustmentPayload {
  itemId: number;
  quantityChange: number;
  unitCost: number | null;
  reason: string;
  date: string;
  memo: string | null;
}

const responseMessage = async (response: Response, fallback: string) => {
  try {
    const body = await response.json() as { message?: string; detail?: string };
    return body.message || body.detail || fallback;
  } catch {
    return fallback;
  }
};

export const Inventory: React.FC = () => {
  const { currentOrg, hasPermission } = useTenantContext();
  const { authFetch } = useAuth();
  const { itemId: routeItemId } = useParams();
  const navigate = useNavigate();
  const canView = hasPermission('inventory.items.view');
  const canAdjust = hasPermission('inventory.adjustments.post');
  const [data, setData] = useState<StockItemsResponse | null>(null);
  const [movements, setMovements] = useState<StockMovement[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [success, setSuccess] = useState('');
  const [showAdjust, setShowAdjust] = useState(false);
  const activeItemId = routeItemId ? Number(routeItemId) : null;

  const currency = useMemo(() => new Intl.NumberFormat(undefined, {
    style: 'currency',
    currency: currentOrg.currency || 'USD',
  }), [currentOrg.currency]);
  const money = (amount: number) => currency.format(amount);
  const quantity = (amount: number) => Number(amount).toLocaleString(undefined, { maximumFractionDigits: 4 });

  const load = useCallback(async () => {
    setLoading(true);
    setError('');
    try {
      const itemsResponse = await authFetch('/api/Stock/Items');
      if (!itemsResponse.ok) throw new Error(await responseMessage(itemsResponse, 'Could not load stock items.'));
      const items = await itemsResponse.json() as StockItemsResponse;
      setData(items);

      if (activeItemId !== null) {
        if (!Number.isInteger(activeItemId) || !items.items.some(item => item.id === activeItemId)) {
          setMovements([]);
          throw new Error('Stock item not found.');
        }
        const movementResponse = await authFetch(`/api/Stock/Movements?itemId=${activeItemId}`);
        if (!movementResponse.ok) throw new Error(await responseMessage(movementResponse, 'Could not load stock movements.'));
        setMovements(await movementResponse.json() as StockMovement[]);
      } else {
        setMovements([]);
      }
    } catch (loadError) {
      setError(loadError instanceof Error ? loadError.message : 'Could not load inventory.');
    } finally {
      setLoading(false);
    }
  }, [activeItemId, authFetch]);

  useEffect(() => {
    void load();
  }, [load]);

  if (!canView) return <PermissionDeniedCard permission="inventory.items.view" />;

  const selectedItem = data?.items.find(item => item.id === activeItemId);
  const difference = data ? (Math.round(data.totalValue * 100) - Math.round(data.inventoryAccountBalance * 100)) / 100 : 0;
  const adjustmentItems = data?.items.filter(item => !item.archived) ?? [];

  const postAdjustment = async (payload: AdjustmentPayload) => {
    const response = await authFetch('/api/Stock/Adjust', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload),
    });
    if (!response.ok) throw new Error(await responseMessage(response, 'Could not post the stock adjustment.'));
    setShowAdjust(false);
    setSuccess('Stock adjustment posted.');
    await load();
  };

  return (
    <div className="space-y-6 pb-10">
      <PageHeader
        title="Inventory"
        back={selectedItem ? { to: `/org/${currentOrg.slug}/items/inventory`, label: 'Back to inventory' } : undefined}
        subtitle="Stock on hand, moving-average cost, and the inventory ledger."
        actions={canAdjust ? (
          <HeaderButton label="Adjust stock" icon={Plus} variant="primary" onClick={() => { setSuccess(''); setShowAdjust(true); }} />
        ) : undefined}
      />
      <ItemsTabs orgSlug={currentOrg.slug} />

      {error && (
        <div role="alert" className="rounded-xl border border-rose-500/20 bg-rose-500/10 p-3 text-sm text-rose-600 dark:text-rose-400">
          {error}
        </div>
      )}
      {success && (
        <div role="status" className="rounded-xl border border-emerald-500/20 bg-emerald-500/10 p-3 text-sm text-emerald-600 dark:text-emerald-400">
          {success}
        </div>
      )}

      {loading && (
        <div className="flex items-center justify-center gap-2 rounded-2xl bg-[var(--bg-surface)] p-10 text-sm text-[var(--text-muted)]">
          <Loader2 className="h-4 w-4 animate-spin" /> Loading inventory…
        </div>
      )}

      {!loading && data && (
        <>
          <section className="overflow-hidden rounded-2xl border border-[var(--border-subtle)] bg-[var(--bg-surface)]">
            <div className="overflow-x-auto">
              <table className="w-full min-w-[620px] text-left text-sm">
                <thead className="border-b border-[var(--border-subtle)] text-xs uppercase tracking-wide text-[var(--text-muted)]">
                  <tr>
                    <th className="px-5 py-3 font-semibold">Item</th>
                    <th className="px-5 py-3 text-right font-semibold">On hand</th>
                    <th className="px-5 py-3 text-right font-semibold">Average cost</th>
                    <th className="px-5 py-3 text-right font-semibold">Value</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-[var(--border-subtle)]">
                  {data.items.map(item => (
                    <tr key={item.id} className="hover:bg-[var(--bg-surface-hover)]">
                      <td className="px-5 py-3">
                        <button
                          type="button"
                          onClick={() => navigate(`/org/${currentOrg.slug}/items/inventory/${item.id}`)}
                          className="text-left font-medium text-[var(--text-main)] hover:text-[var(--brand-primary)]"
                        >
                          {item.name}
                        </button>
                        <div className="mt-0.5 flex items-center gap-2 text-xs text-[var(--text-muted)]">
                          {item.code || 'No code'}
                          {item.archived && <span className="rounded-full bg-slate-500/10 px-2 py-0.5">Archived</span>}
                        </div>
                      </td>
                      <td className={`px-5 py-3 text-right font-mono ${item.quantityOnHand < 0 ? 'text-rose-600 dark:text-rose-400' : 'text-[var(--text-main)]'}`}>
                        {quantity(item.quantityOnHand)}
                      </td>
                      <td className="px-5 py-3 text-right font-mono text-[var(--text-secondary)]">{money(item.averageCost)}</td>
                      <td className="px-5 py-3 text-right font-mono text-[var(--text-main)]">{money(item.value)}</td>
                    </tr>
                  ))}
                  {data.items.length === 0 && (
                    <tr>
                      <td colSpan={4} className="px-5 py-12 text-center text-sm text-[var(--text-muted)]">
                        No stock-tracked items yet. Turn on stock tracking from Products &amp; services.
                      </td>
                    </tr>
                  )}
                </tbody>
                <tfoot className="border-t border-[var(--border-subtle)] bg-[var(--bg-subtle)]">
                  <tr>
                    <th colSpan={3} className="px-5 py-3 text-right font-semibold text-[var(--text-main)]">Total stock value</th>
                    <td className="px-5 py-3 text-right font-mono font-semibold text-[var(--text-main)]">{money(data.totalValue)}</td>
                  </tr>
                </tfoot>
              </table>
            </div>
            <div className={`border-t border-[var(--border-subtle)] px-5 py-3 text-sm ${difference === 0 ? 'text-emerald-600 dark:text-emerald-400' : 'text-amber-600 dark:text-amber-400'}`}>
              {difference === 0
                ? `Matches your Inventory account (${money(data.inventoryAccountBalance)})`
                : `Differs from your Inventory account by ${money(Math.abs(difference))} (stock value ${difference > 0 ? 'is higher' : 'is lower'}).`}
            </div>
          </section>

          {activeItemId !== null && (
            <section className="space-y-4">
              <div>
                <h2 className="text-lg font-semibold text-[var(--text-main)]">{selectedItem?.name ?? 'Stock movements'}</h2>
                <p className="text-sm text-[var(--text-muted)]">Movement history, newest first.</p>
              </div>
              <div className="overflow-hidden rounded-2xl border border-[var(--border-subtle)] bg-[var(--bg-surface)]">
                <div className="overflow-x-auto">
                  <table className="w-full min-w-[760px] text-left text-sm">
                    <thead className="border-b border-[var(--border-subtle)] text-xs uppercase tracking-wide text-[var(--text-muted)]">
                      <tr>
                        <th className="px-4 py-3 font-semibold">Date</th>
                        <th className="px-4 py-3 font-semibold">Movement</th>
                        <th className="px-4 py-3 font-semibold">Document</th>
                        <th className="px-4 py-3 text-right font-semibold">In</th>
                        <th className="px-4 py-3 text-right font-semibold">Out</th>
                        <th className="px-4 py-3 text-right font-semibold">Unit cost</th>
                        <th className="px-4 py-3 text-right font-semibold">On hand</th>
                      </tr>
                    </thead>
                    <tbody className="divide-y divide-[var(--border-subtle)]">
                      {movements.map(movement => (
                        <tr key={movement.id}>
                          <td className="whitespace-nowrap px-4 py-3 text-[var(--text-secondary)]">{new Date(movement.date).toLocaleDateString(undefined, { month: 'short', day: 'numeric', year: 'numeric' })}</td>
                          <td className="px-4 py-3">
                            <span className="flex items-center gap-2 text-[var(--text-main)]">
                              {movement.in > 0 ? <ArrowDown className="h-3.5 w-3.5 text-emerald-500" /> : movement.out > 0 ? <ArrowUp className="h-3.5 w-3.5 text-rose-500" /> : null}
                              {movement.type}
                            </span>
                            {movement.memo && <span className="mt-0.5 block max-w-48 truncate text-xs text-[var(--text-muted)]">{movement.memo}</span>}
                          </td>
                          <td className="px-4 py-3">
                            {movement.linkType && movement.documentId ? (
                              <Link
                                className="font-medium text-[var(--brand-primary)] hover:underline"
                                to={`/org/${currentOrg.slug}/${movement.linkType === 'bill' ? 'purchases' : 'sales'}/${movement.documentId}`}
                              >
                                {movement.linkType === 'bill' ? 'Bill' : 'Invoice'} #{movement.documentNo || movement.documentId}
                              </Link>
                            ) : movement.documentNo ? `${movement.type.replace('Void of ', '')} #${movement.documentNo}` : '—'}
                          </td>
                          <td className="px-4 py-3 text-right font-mono text-emerald-600 dark:text-emerald-400">{movement.in ? quantity(movement.in) : '—'}</td>
                          <td className="px-4 py-3 text-right font-mono text-rose-600 dark:text-rose-400">{movement.out ? quantity(movement.out) : '—'}</td>
                          <td className="px-4 py-3 text-right font-mono text-[var(--text-secondary)]">{movement.unitCost == null ? '—' : money(movement.unitCost)}</td>
                          <td className="px-4 py-3 text-right font-mono font-medium text-[var(--text-main)]">{quantity(movement.runningQuantity)}</td>
                        </tr>
                      ))}
                      {movements.length === 0 && (
                        <tr><td colSpan={7} className="px-4 py-10 text-center text-[var(--text-muted)]">No movements for this item.</td></tr>
                      )}
                    </tbody>
                  </table>
                </div>
              </div>
            </section>
          )}
        </>
      )}

      <AdjustStockDialog
        open={showAdjust}
        items={adjustmentItems}
        initialItemId={selectedItem && !selectedItem.archived ? selectedItem.id : adjustmentItems[0]?.id}
        currencySymbol={currencySymbol(currentOrg.currency)}
        formatMoney={money}
        onClose={() => setShowAdjust(false)}
        onSubmit={postAdjustment}
      />
    </div>
  );
};

const AdjustStockDialog: React.FC<{
  open: boolean;
  items: StockItem[];
  initialItemId?: number;
  currencySymbol: string;
  formatMoney: (amount: number) => string;
  onClose: () => void;
  onSubmit: (payload: AdjustmentPayload) => Promise<void>;
}> = ({ open, items, initialItemId, currencySymbol, formatMoney, onClose, onSubmit }) => {
  const [itemId, setItemId] = useState(initialItemId ? String(initialItemId) : '');
  const [direction, setDirection] = useState<'add' | 'remove'>('add');
  const [quantityValue, setQuantityValue] = useState('');
  const [unitCost, setUnitCost] = useState('');
  const [reason, setReason] = useState('Count');
  const [date, setDate] = useState(localToday);
  const [memo, setMemo] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const selectedItem = items.find(item => item.id === Number(itemId));
  const labelClass = 'block text-xs font-semibold uppercase tracking-wider text-[var(--text-muted)] mb-1';
  const inputClass = 'w-full px-3.5 py-2.5 text-sm bg-[var(--bg-subtle)] border border-[var(--border-subtle)] rounded-xl focus:ring-2 focus:ring-[var(--brand-ring)] focus:outline-none text-[var(--text-main)] placeholder-[var(--text-muted)] transition';

  useEffect(() => {
    if (reason === 'OpeningStock' && (!selectedItem || selectedItem.hasMovements || selectedItem.quantityOnHand !== 0))
      setReason('Count');
  }, [reason, selectedItem]);

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    setError('');
    const amount = Number(quantityValue);
    const cost = Number(unitCost);
    if (!selectedItem || !Number.isFinite(amount) || amount <= 0) {
      setError('Choose an item and enter a quantity greater than zero.');
      return;
    }
    if (direction === 'add' && (!Number.isFinite(cost) || cost < 0)) {
      setError('Enter a non-negative cost per unit.');
      return;
    }
    setBusy(true);
    try {
      await onSubmit({
        itemId: selectedItem.id,
        quantityChange: direction === 'add' ? amount : -amount,
        unitCost: direction === 'add' ? cost : null,
        reason,
        date: `${date}T00:00:00.000Z`,
        memo: memo.trim() || null,
      });
    } catch (submitError) {
      setError(submitError instanceof Error ? submitError.message : 'Could not post the stock adjustment.');
    } finally {
      setBusy(false);
    }
  };

  return (
    <ModalShell
      open={open}
      onClose={() => {
        if (!busy) onClose();
      }}
      closeOnEscape={!busy}
      closeOnBackdrop={!busy}
      bare
      aria-labelledby="adjust-stock-title"
      panelClassName="max-h-[min(90vh,calc(100dvh-2rem))] w-full max-w-lg overflow-y-auto rounded-2xl border border-[var(--border-subtle)] bg-[var(--bg-surface)] shadow-2xl"
    >
      <div>
        <div className="flex items-center justify-between border-b border-[var(--border-subtle)] px-6 py-4">
          <div className="flex items-center gap-2">
            <Package className="w-5 h-5 text-[var(--brand-primary)]" />
            <h2 id="adjust-stock-title" className="text-base font-bold text-[var(--text-main)]">Adjust stock</h2>
          </div>
          <button type="button" onClick={onClose} disabled={busy} aria-label="Close" className="rounded-lg p-1 text-[var(--text-muted)] hover:text-[var(--text-main)]">
            <X className="h-5 w-5" />
          </button>
        </div>
        <form onSubmit={submit} className="space-y-4 p-6">
          {error && <div role="alert" className="rounded-lg border border-rose-500/20 bg-rose-500/10 p-3 text-sm text-rose-600 dark:text-rose-400">{error}</div>}
          {items.length === 0 ? (
            <p className="text-sm text-[var(--text-muted)]">There are no active stock items to adjust.</p>
          ) : (
            <>
              <div>
                <label htmlFor="adjust-item" className={labelClass}>Item</label>
                <select id="adjust-item" value={itemId} onChange={event => setItemId(event.target.value)} required className={inputClass}>
                  {items.map(item => <option key={item.id} value={item.id}>{item.name}{item.code ? ` (${item.code})` : ''}</option>)}
                </select>
                {selectedItem && (
                  <p className="mt-2 text-xs text-[var(--text-muted)]">
                    On hand: <span className={selectedItem.quantityOnHand < 0 ? 'text-rose-600 dark:text-rose-400' : ''}>{selectedItem.quantityOnHand.toLocaleString(undefined, { maximumFractionDigits: 4 })}</span>
                    {' · '}Average cost: {formatMoney(selectedItem.averageCost)}
                  </p>
                )}
              </div>
              <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
                <div>
                  <label htmlFor="adjust-direction" className={labelClass}>Action</label>
                  <select id="adjust-direction" value={direction} onChange={event => setDirection(event.target.value as 'add' | 'remove')} className={inputClass}>
                    <option value="add">Add stock</option>
                    <option value="remove">Remove stock</option>
                  </select>
                </div>
                <div>
                  <label htmlFor="adjust-quantity" className={labelClass}>Quantity</label>
                  <input id="adjust-quantity" type="number" min="0.0001" step="0.0001" required value={quantityValue} onChange={event => setQuantityValue(event.target.value)} className={`${inputClass} font-mono`} />
                </div>
              </div>
              {direction === 'add' && (
                <div>
                  <label htmlFor="adjust-cost" className={labelClass}>Cost per unit ({currencySymbol})</label>
                  <input id="adjust-cost" type="number" min="0" step="0.0001" required value={unitCost} onChange={event => setUnitCost(event.target.value)} className={`${inputClass} font-mono`} />
                </div>
              )}
              <div>
                <label htmlFor="adjust-reason" className={labelClass}>Reason</label>
                <select id="adjust-reason" value={reason} onChange={event => setReason(event.target.value)} className={inputClass}>
                  <option value="Count">Count correction</option>
                  <option value="Damaged">Damaged</option>
                  <option value="Other">Other</option>
                  {selectedItem && !selectedItem.hasMovements && selectedItem.quantityOnHand === 0 && (
                    <option value="OpeningStock">Opening stock</option>
                  )}
                </select>
              </div>
              <div>
                <label htmlFor="adjust-date" className={labelClass}>Date</label>
                <input id="adjust-date" type="date" required value={date} onChange={event => setDate(event.target.value)} className={inputClass} />
              </div>
              <div>
                <label htmlFor="adjust-memo" className={labelClass}>Memo</label>
                <textarea id="adjust-memo" maxLength={500} rows={2} value={memo} onChange={event => setMemo(event.target.value)} className={`${inputClass} h-auto py-2`} />
              </div>
            </>
          )}
          <p className="text-xs text-[var(--text-muted)]">Posted adjustments can’t be edited. Correct a mistake with another adjustment.</p>
          <div className="flex justify-end gap-2 border-t border-[var(--border-subtle)] pt-4">
            <button type="button" onClick={onClose} disabled={busy} className="h-9 rounded-lg border border-[var(--border-subtle)] px-3 text-sm text-[var(--text-secondary)] hover:bg-[var(--bg-surface-hover)]">Cancel</button>
            {items.length > 0 && <button type="submit" disabled={busy} className="inline-flex h-9 items-center gap-2 rounded-lg bg-[var(--brand-primary)] px-3.5 text-sm font-semibold text-[var(--brand-text)] disabled:opacity-50">{busy && <Loader2 className="h-4 w-4 animate-spin" />}Post adjustment</button>}
          </div>
        </form>
      </div>
    </ModalShell>
  );
};

export default Inventory;
