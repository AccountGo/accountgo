import React, { useEffect, useId, useMemo, useRef, useState } from 'react';
import { Check, Package, Search, X } from 'lucide-react';
import { ItemOption } from '../types';

/**
 * Picks a catalog item for an invoice line. Type to filter by code or name, arrows to move, Enter to choose,
 * Escape to close. A full dialog rather than an inline dropdown: the list has room for its columns, nothing is
 * clipped by the scrolling line-item table, and it becomes a bottom sheet on phones.
 */
export const CatalogPickerDialog: React.FC<{
  items: ItemOption[];
  selectedId?: number | null;
  formatPrice: (value: number) => string;
  onPick: (item: ItemOption) => void;
  onClose: () => void;
}> = ({ items, selectedId, formatPrice, onPick, onClose }) => {
  const [query, setQuery] = useState('');
  const [active, setActive] = useState(0);
  const dialogRef = useRef<HTMLDivElement>(null);
  const inputRef = useRef<HTMLInputElement>(null);
  const listRef = useRef<HTMLUListElement>(null);
  const titleId = useId();
  const listId = useId();

  const matches = useMemo(() => {
    const terms = query.trim().toLowerCase().split(/\s+/).filter(Boolean);
    if (terms.length === 0) return items;
    return items.filter((item) => {
      const haystack = `${item.code ?? ''} ${item.description}`.toLowerCase();
      return terms.every((t) => haystack.includes(t));
    });
  }, [items, query]);

  // Start on the line's current item, so Enter keeps it and arrows move from there.
  useEffect(() => {
    const trigger = document.activeElement as HTMLElement | null;
    inputRef.current?.focus();
    const current = items.findIndex((i) => i.id === selectedId);
    if (current >= 0) setActive(current);
    return () => trigger?.focus();
  }, []);

  useEffect(() => {
    listRef.current?.querySelector<HTMLElement>(`[data-index="${active}"]`)?.scrollIntoView({ block: 'nearest' });
  }, [active]);

  const choose = (item: ItemOption | undefined) => {
    if (!item) return;
    onPick(item);
  };

  const onKeyDown = (e: React.KeyboardEvent) => {
    if (e.key === 'Escape') {
      e.preventDefault();
      onClose();
    } else if (e.key === 'ArrowDown') {
      e.preventDefault();
      setActive((a) => Math.min(a + 1, matches.length - 1));
    } else if (e.key === 'ArrowUp') {
      e.preventDefault();
      setActive((a) => Math.max(a - 1, 0));
    } else if (e.key === 'Enter') {
      e.preventDefault();
      choose(matches[active]);
    } else if (e.key === 'Tab') {
      // Only the search box and the close button take focus; keep it inside the dialog.
      const focusable = dialogRef.current?.querySelectorAll<HTMLElement>('input, button[data-focusable]');
      if (!focusable?.length) return;
      const first = focusable[0];
      const last = focusable[focusable.length - 1];
      if (e.shiftKey && document.activeElement === first) {
        e.preventDefault();
        last.focus();
      } else if (!e.shiftKey && document.activeElement === last) {
        e.preventDefault();
        first.focus();
      }
    }
  };

  const activeItem = matches[active];
  // Codes are optional; drop the column when no item has one.
  const showCode = items.some((i) => i.code);
  const columns = showCode ? 'grid-cols-[5.5rem_1fr_auto]' : 'grid-cols-[1fr_auto]';

  return (
    <div
      className="fixed inset-0 z-50 flex items-end sm:items-start justify-center bg-black/60 backdrop-blur-sm sm:p-4 sm:pt-[12vh]"
      onMouseDown={(e) => {
        if (e.target === e.currentTarget) onClose();
      }}
    >
      <div
        ref={dialogRef}
        role="dialog"
        aria-modal="true"
        aria-labelledby={titleId}
        onKeyDown={onKeyDown}
        className="flex max-h-[85vh] w-full flex-col rounded-t-2xl border border-[var(--border-subtle)] bg-[var(--bg-surface)] shadow-2xl sm:max-h-[70vh] sm:max-w-xl sm:rounded-2xl"
      >
        <div className="flex items-center justify-between gap-3 px-5 pt-4 pb-3">
          <h2 id={titleId} className="flex items-center gap-2 text-base font-bold text-[var(--text-main)]">
            <Package className="h-4 w-4 text-[var(--brand-primary)]" />
            Pick from catalog
          </h2>
          <button
            type="button"
            data-focusable
            onClick={onClose}
            aria-label="Close"
            className="rounded-lg p-1.5 text-[var(--text-muted)] transition hover:bg-[var(--bg-surface-hover)] hover:text-[var(--text-main)] focus:outline-none focus-visible:ring-2 focus-visible:ring-[var(--brand-ring)]"
          >
            <X className="h-4 w-4" />
          </button>
        </div>

        <div className="px-5 pb-3">
          <div className="relative">
            <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-[var(--text-muted)]" />
            <input
              ref={inputRef}
              type="search"
              role="combobox"
              aria-expanded="true"
              aria-controls={listId}
              aria-activedescendant={activeItem ? `${listId}-${activeItem.id}` : undefined}
              aria-label="Search the catalog"
              placeholder="Search by name or code"
              value={query}
              onChange={(e) => {
                setQuery(e.target.value);
                setActive(0);
              }}
              className="h-10 w-full rounded-lg border border-[var(--border-subtle)] bg-[var(--bg-subtle)] pl-9 pr-3 text-sm text-[var(--text-main)] placeholder-[var(--text-muted)] transition focus:outline-none focus:ring-2 focus:ring-[var(--brand-ring)]"
            />
          </div>
        </div>

        <div className={`grid ${columns} gap-3 border-y border-[var(--border-subtle)] bg-[var(--bg-subtle)] px-5 py-2 text-[11px] font-semibold uppercase tracking-wider text-[var(--text-muted)]`}>
          {showCode && <span>Code</span>}
          <span>Item</span>
          <span className="text-right">Price</span>
        </div>

        <ul ref={listRef} id={listId} role="listbox" aria-label="Catalog items" className="min-h-0 flex-1 overflow-y-auto py-1">
          {matches.map((item, index) => {
            const isActive = index === active;
            const isSelected = item.id === selectedId;
            return (
              <li
                key={item.id}
                id={`${listId}-${item.id}`}
                data-index={index}
                role="option"
                aria-selected={isActive}
                onMouseMove={() => setActive(index)}
                onClick={() => choose(item)}
                className={`mx-2 grid cursor-pointer ${columns} items-center gap-3 rounded-lg px-3 py-2.5 text-sm transition ${
                  isActive ? 'bg-[var(--bg-surface-hover)]' : ''
                }`}
              >
                {showCode && <span className="truncate font-mono text-xs text-[var(--text-muted)]">{item.code || '—'}</span>}
                <span className="flex min-w-0 items-center gap-2">
                  <span className="truncate text-[var(--text-main)]">{item.description}</span>
                  {isSelected && (
                    <span className="inline-flex shrink-0 items-center gap-0.5 text-xs font-medium text-[var(--brand-primary)]">
                      <Check className="h-3.5 w-3.5" /> On this line
                    </span>
                  )}
                </span>
                <span className="text-right font-mono text-[var(--text-main)]">
                  {item.price != null ? formatPrice(item.price) : '—'}
                </span>
              </li>
            );
          })}
          {matches.length === 0 && (
            <li className="px-5 py-10 text-center text-sm text-[var(--text-muted)]">
              No items match “{query.trim()}”.
            </li>
          )}
        </ul>

        <div className="flex items-center justify-between gap-3 border-t border-[var(--border-subtle)] px-5 py-2.5 text-xs text-[var(--text-muted)]">
          <span>
            {matches.length} of {items.length} item{items.length === 1 ? '' : 's'}
          </span>
          <span className="hidden sm:inline">
            <kbd className="font-sans">↑</kbd> <kbd className="font-sans">↓</kbd> to move · <kbd className="font-sans">Enter</kbd> to
            choose · <kbd className="font-sans">Esc</kbd> to close
          </span>
        </div>
      </div>
    </div>
  );
};
