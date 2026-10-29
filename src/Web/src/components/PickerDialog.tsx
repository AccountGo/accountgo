import React, { useEffect, useId, useMemo, useRef, useState } from 'react';
import { Check, Search, X, type LucideIcon } from 'lucide-react';
import { ModalShell } from './ModalShell';

export interface PickerColumn<T> {
  header: string;
  /** CSS grid track for this column, e.g. '1fr' or 'auto'. */
  width: string;
  align?: 'left' | 'right';
  render: (item: T) => React.ReactNode;
}

/**
 * Searchable list in a dialog, used to choose a record for a form field (a client, a catalog item). Type to filter,
 * arrows to move, Enter to choose, Escape or a click outside to close. A dialog rather than an inline dropdown: the
 * list has room for its columns, nothing is clipped by a scrolling container, and it becomes a bottom sheet on phones.
 */
export function PickerDialog<T extends { id: number }>({
  title,
  icon: Icon,
  items,
  columns,
  searchText,
  searchPlaceholder,
  selectedId,
  selectedLabel = 'Selected',
  noun,
  footerAction,
  onPick,
  onClose,
}: {
  title: string;
  icon: LucideIcon;
  items: T[];
  columns: PickerColumn<T>[];
  /** Text a search matches against; every word typed must appear in it. */
  searchText: (item: T) => string;
  searchPlaceholder: string;
  selectedId?: number | null;
  selectedLabel?: string;
  /** Singular and plural, for the "3 of 12 clients" count. */
  noun: [string, string];
  /** E.g. "New client", shown at the bottom of the dialog. */
  footerAction?: { label: string; icon?: LucideIcon; onClick: () => void };
  onPick: (item: T) => void;
  onClose: () => void;
}) {
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
      const haystack = searchText(item).toLowerCase();
      return terms.every((t) => haystack.includes(t));
    });
  }, [items, query, searchText]);

  // Start on the field's current value, so Enter keeps it and arrows move from there.
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

  const onKeyDown = (e: React.KeyboardEvent) => {
    if (e.key === 'ArrowDown') {
      e.preventDefault();
      setActive((a) => Math.min(a + 1, matches.length - 1));
    } else if (e.key === 'ArrowUp') {
      e.preventDefault();
      setActive((a) => Math.max(a - 1, 0));
    } else if (e.key === 'Enter' && document.activeElement === inputRef.current) {
      e.preventDefault();
      const item = matches[active];
      if (item) onPick(item);
    }
  };

  const activeItem = matches[active];
  const grid = { gridTemplateColumns: columns.map((c) => c.width).join(' ') };
  const FooterIcon = footerAction?.icon;

  return (
    <ModalShell
      open
      onClose={onClose}
      bare
      align="bottom-sheet"
      aria-labelledby={titleId}
      panelClassName="flex max-h-[85vh] w-full flex-col rounded-t-2xl border border-[var(--border-subtle)] bg-[var(--bg-surface)] shadow-2xl sm:max-h-[70vh] sm:max-w-xl sm:rounded-2xl"
    >
      <div ref={dialogRef} onKeyDown={onKeyDown} className="flex min-h-0 flex-1 flex-col">
        <div className="flex items-center justify-between gap-3 px-5 pt-4 pb-3">
          <h2 id={titleId} className="flex items-center gap-2 text-base font-bold text-[var(--text-main)]">
            <Icon className="h-4 w-4 text-[var(--brand-primary)]" />
            {title}
          </h2>
          <button
            type="button"
            tabIndex={-1}
            onClick={onClose}
            aria-label="Close"
            className="rounded-lg p-1.5 text-[var(--text-muted)] transition hover:bg-[var(--bg-surface-hover)] hover:text-[var(--text-main)]"
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
              aria-label={searchPlaceholder}
              placeholder={searchPlaceholder}
              value={query}
              onChange={(e) => {
                setQuery(e.target.value);
                setActive(0);
              }}
              className="h-10 w-full rounded-lg border border-[var(--border-subtle)] bg-[var(--bg-subtle)] pl-9 pr-3 text-sm text-[var(--text-main)] placeholder-[var(--text-muted)] transition focus:outline-none focus:ring-2 focus:ring-[var(--brand-ring)]"
            />
          </div>
        </div>

        <div
          style={grid}
          className="grid gap-3 border-y border-[var(--border-subtle)] bg-[var(--bg-subtle)] px-5 py-2 text-[11px] font-semibold uppercase tracking-wider text-[var(--text-muted)]"
        >
          {columns.map((c) => (
            <span key={c.header} className={c.align === 'right' ? 'text-right' : ''}>
              {c.header}
            </span>
          ))}
        </div>

        <ul ref={listRef} id={listId} role="listbox" aria-label={title} className="min-h-0 flex-1 overflow-y-auto py-1">
          {matches.map((item, index) => {
            const isSelected = item.id === selectedId;
            return (
              <li
                key={item.id}
                id={`${listId}-${item.id}`}
                data-index={index}
                role="option"
                aria-selected={index === active}
                onMouseMove={() => setActive(index)}
                onClick={() => onPick(item)}
                style={grid}
                className={`mx-2 grid cursor-pointer items-center gap-3 rounded-lg px-3 py-2.5 text-sm transition ${
                  index === active ? 'bg-[var(--bg-surface-hover)]' : ''
                }`}
              >
                {columns.map((c, ci) => (
                  <span key={c.header} className={`flex min-w-0 items-center gap-2 ${c.align === 'right' ? 'justify-end text-right' : ''}`}>
                    <span className="min-w-0">{c.render(item)}</span>
                    {ci === 0 && isSelected && (
                      <span className="inline-flex shrink-0 items-center gap-0.5 text-xs font-medium text-[var(--brand-primary)]">
                        <Check className="h-3.5 w-3.5" /> {selectedLabel}
                      </span>
                    )}
                  </span>
                ))}
              </li>
            );
          })}
          {matches.length === 0 && (
            <li className="px-5 py-10 text-center text-sm text-[var(--text-muted)]">
              {items.length === 0 ? `No ${noun[1]} yet.` : `No ${noun[1]} match “${query.trim()}”.`}
            </li>
          )}
        </ul>

        <div className="flex items-center justify-between gap-3 border-t border-[var(--border-subtle)] px-5 py-2.5 text-xs text-[var(--text-muted)]">
          {footerAction ? (
            <button
              type="button"
              onClick={footerAction.onClick}
              className="inline-flex items-center gap-1.5 rounded-md px-1 py-1 font-semibold text-[var(--brand-primary)] transition hover:opacity-80 focus:outline-none focus-visible:ring-2 focus-visible:ring-[var(--brand-ring)]"
            >
              {FooterIcon && <FooterIcon className="h-3.5 w-3.5" />}
              {footerAction.label}
            </button>
          ) : (
            <span>
              {matches.length} of {items.length} {items.length === 1 ? noun[0] : noun[1]}
            </span>
          )}
          <span className="hidden sm:inline">↑ ↓ to move · Enter to choose · Esc to close</span>
        </div>
      </div>
    </ModalShell>
  );
}
