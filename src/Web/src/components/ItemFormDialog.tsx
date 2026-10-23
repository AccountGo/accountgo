import React, { useEffect, useId, useRef, useState } from 'react';
import { Archive, ArchiveRestore, Loader2, Package, Trash2, X } from 'lucide-react';
import { CatalogItem } from '../types';
import { useAuth } from '../auth/AuthContext';
import { ConfirmDialog } from './ConfirmDialog';

const inputClass =
  'w-full px-3.5 py-2.5 text-sm bg-[var(--bg-subtle)] border border-[var(--border-subtle)] rounded-xl focus:ring-2 focus:ring-[var(--brand-ring)] focus:outline-none text-[var(--text-main)] placeholder-[var(--text-muted)] transition';
const labelClass = 'block text-xs font-semibold uppercase tracking-wider text-[var(--text-muted)] mb-1';

const amountOrNull = (value: string) => (value.trim() === '' ? null : Number(value));

/**
 * Adds a product or service, or edits one; editing also archives, restores or deletes it. Changes apply to new
 * lines: invoice lines keep the text and price they were saved with.
 */
export const ItemFormDialog: React.FC<{
  /** The item to edit; omitted to add one. */
  item?: CatalogItem;
  /** Selling price, shown when the workspace sells. */
  showPrice: boolean;
  /** Purchase cost, shown when the workspace buys. */
  showCost: boolean;
  currencySymbol: string;
  /** The item after it was added, edited, archived or restored. */
  onSaved: (item: CatalogItem) => void;
  onDeleted?: (id: number) => void;
  onClose: () => void;
}> = ({ item, showPrice, showCost, currencySymbol, onSaved, onDeleted, onClose }) => {
  const { authFetch } = useAuth();
  const [name, setName] = useState(item?.name ?? '');
  const [code, setCode] = useState(item?.code ?? '');
  const [price, setPrice] = useState(item?.price != null ? String(item.price) : '');
  const [cost, setCost] = useState(item?.cost != null ? String(item.cost) : '');
  const [busy, setBusy] = useState<'save' | 'archive' | 'delete' | null>(null);
  const [isConfirmingDelete, setIsConfirmingDelete] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const nameRef = useRef<HTMLInputElement>(null);
  const titleId = useId();
  const isEdit = !!item;

  useEffect(() => {
    const trigger = document.activeElement as HTMLElement | null;
    nameRef.current?.focus();
    return () => trigger?.focus();
  }, []);

  const request = async (kind: 'save' | 'archive' | 'delete', url: string, init: RequestInit, failure: string) => {
    setBusy(kind);
    setError(null);
    try {
      const res = await authFetch(url, init);
      const data = res.status === 204 ? null : await res.json().catch(() => null);
      if (!res.ok) {
        throw new Error(res.status === 403 ? "You don't have permission to manage products and services." : data?.message || failure);
      }
      return data;
    } catch (err: any) {
      setError(err.message);
      setBusy(null);
      setIsConfirmingDelete(false);
      return undefined;
    }
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!name.trim()) {
      setError('Enter a name for the item.');
      return;
    }
    const saved = await request(
      'save',
      '/api/Catalog/SaveItem',
      {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          id: item?.id ?? 0,
          name: name.trim(),
          code: code.trim() || null,
          // Hidden fields keep their value.
          price: showPrice ? amountOrNull(price) : item?.price ?? null,
          cost: showCost ? amountOrNull(cost) : item?.cost ?? null,
        }),
      },
      isEdit ? 'The item could not be saved.' : 'The item could not be added.'
    );
    if (saved) onSaved(saved);
  };

  const setArchived = async (archived: boolean) => {
    if (!item) return;
    const saved = await request(
      'archive',
      `/api/Catalog/ArchiveItem?id=${item.id}&archived=${archived}`,
      { method: 'POST' },
      archived ? 'The item could not be archived.' : 'The item could not be restored.'
    );
    if (saved) onSaved(saved);
  };

  const handleDelete = async () => {
    if (!item) return;
    const result = await request('delete', `/api/Catalog/DeleteItem?id=${item.id}`, { method: 'DELETE' }, 'The item could not be deleted.');
    if (result !== undefined) onDeleted?.(item.id);
  };

  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-black/60 backdrop-blur-sm animate-fade-in"
      onKeyDown={(e) => {
        if (e.key === 'Escape' && !busy && !isConfirmingDelete) {
          e.preventDefault();
          onClose();
        }
      }}
    >
      <div
        role="dialog"
        aria-modal="true"
        aria-labelledby={titleId}
        className="bg-[var(--bg-surface)] rounded-2xl max-w-md w-full max-h-[calc(100dvh-2rem)] overflow-y-auto border border-[var(--border-subtle)] shadow-2xl"
      >
        <div className="px-6 py-4 border-b border-[var(--border-subtle)] flex items-center justify-between">
          <div className="flex items-center gap-2">
            <Package className="w-5 h-5 text-[var(--brand-primary)]" />
            <h3 id={titleId} className="font-bold text-[var(--text-main)] text-base">
              {isEdit ? 'Edit item' : 'New item'}
            </h3>
            {item?.archived && (
              <span className="rounded-full border border-slate-500/20 bg-slate-500/10 px-2 py-0.5 text-[10px] font-semibold text-slate-500 dark:text-slate-400">
                Archived
              </span>
            )}
          </div>
          <button
            type="button"
            onClick={onClose}
            disabled={!!busy}
            aria-label="Close"
            className="text-[var(--text-muted)] hover:text-[var(--text-main)] p-1 rounded-lg transition"
          >
            <X className="w-5 h-5" />
          </button>
        </div>

        <form onSubmit={handleSubmit} className="p-6 space-y-4">
          {error && (
            <div role="alert" className="p-3 bg-rose-500/10 border border-rose-500/20 rounded-xl text-xs text-rose-500">
              {error}
            </div>
          )}

          <div>
            <label htmlFor={`${titleId}-name`} className={labelClass}>
              Name <span className="text-rose-500">*</span>
            </label>
            <input
              id={`${titleId}-name`}
              ref={nameRef}
              type="text"
              required
              maxLength={200}
              placeholder="e.g. Website maintenance"
              value={name}
              onChange={(e) => setName(e.target.value)}
              className={inputClass}
            />
            <p className="mt-1 text-xs text-[var(--text-muted)]">Becomes the line's text when you pick it; you can still change the text on each line.</p>
          </div>

          <div>
            <label htmlFor={`${titleId}-code`} className={labelClass}>
              Code
            </label>
            <input
              id={`${titleId}-code`}
              type="text"
              maxLength={30}
              placeholder="Optional, e.g. SKU-104"
              value={code}
              onChange={(e) => setCode(e.target.value)}
              className={`${inputClass} font-mono`}
            />
          </div>

          {(showPrice || showCost) && (
            <div className={`grid gap-3 ${showPrice && showCost ? 'grid-cols-1 sm:grid-cols-2' : 'grid-cols-1'}`}>
              {showPrice && (
                <div>
                  <label htmlFor={`${titleId}-price`} className={labelClass}>
                    Selling price ({currencySymbol})
                  </label>
                  <input
                    id={`${titleId}-price`}
                    type="number"
                    inputMode="decimal"
                    min="0"
                    step="0.01"
                    placeholder="None"
                    value={price}
                    onChange={(e) => setPrice(e.target.value)}
                    className={`${inputClass} font-mono`}
                  />
                </div>
              )}
              {showCost && (
                <div>
                  <label htmlFor={`${titleId}-cost`} className={labelClass}>
                    Purchase cost ({currencySymbol})
                  </label>
                  <input
                    id={`${titleId}-cost`}
                    type="number"
                    inputMode="decimal"
                    min="0"
                    step="0.01"
                    placeholder="None"
                    value={cost}
                    onChange={(e) => setCost(e.target.value)}
                    className={`${inputClass} font-mono`}
                  />
                </div>
              )}
            </div>
          )}

          {isEdit && (
            <p className="text-xs text-[var(--text-secondary)]">
              Changes apply to new lines. Invoices keep the text and price they were saved with.
            </p>
          )}

          <div className="pt-2 flex flex-wrap items-center justify-between gap-3">
            <div className="flex items-center gap-1">
              {item && (
                <button
                  type="button"
                  onClick={() => setArchived(!item.archived)}
                  disabled={!!busy}
                  className="inline-flex items-center gap-1.5 rounded-lg px-2.5 py-2 text-xs font-semibold text-[var(--text-secondary)] transition hover:bg-[var(--bg-surface-hover)] hover:text-[var(--text-main)] disabled:opacity-50"
                >
                  {busy === 'archive' ? (
                    <Loader2 className="w-3.5 h-3.5 animate-spin" />
                  ) : item.archived ? (
                    <ArchiveRestore className="w-3.5 h-3.5" />
                  ) : (
                    <Archive className="w-3.5 h-3.5" />
                  )}
                  {item.archived ? 'Restore' : 'Archive'}
                </button>
              )}
              {item?.canDelete && (
                <button
                  type="button"
                  onClick={() => setIsConfirmingDelete(true)}
                  disabled={!!busy}
                  className="inline-flex items-center gap-1.5 rounded-lg px-2.5 py-2 text-xs font-semibold text-rose-600 transition hover:bg-rose-500/10 dark:text-rose-400 disabled:opacity-50"
                >
                  <Trash2 className="w-3.5 h-3.5" />
                  Delete
                </button>
              )}
            </div>
            <div className="ml-auto flex items-center gap-3">
              <button
                type="button"
                onClick={onClose}
                disabled={!!busy}
                className="px-4 py-2.5 text-sm font-medium text-[var(--text-secondary)] bg-[var(--bg-subtle)] border border-[var(--border-subtle)] rounded-xl hover:bg-[var(--bg-surface-hover)] hover:text-[var(--text-main)] transition"
              >
                Cancel
              </button>
              <button
                type="submit"
                disabled={!!busy}
                className="inline-flex items-center gap-2 px-5 py-2.5 text-sm font-semibold text-[var(--brand-text)] bg-[var(--brand-primary)] rounded-xl hover:bg-[var(--brand-hover)] transition disabled:opacity-50 shadow-sm"
              >
                {busy === 'save' && <Loader2 className="w-4 h-4 animate-spin" />}
                {isEdit ? 'Save item' : 'Add item'}
              </button>
            </div>
          </div>
        </form>
      </div>

      {isConfirmingDelete && item && (
        <ConfirmDialog
          title={`Delete ${item.name}?`}
          message="No invoice or other document uses this item yet, so it can be deleted. This can't be undone."
          confirmLabel="Delete item"
          destructive
          busy={busy === 'delete'}
          onConfirm={handleDelete}
          onCancel={() => setIsConfirmingDelete(false)}
        />
      )}
    </div>
  );
};
