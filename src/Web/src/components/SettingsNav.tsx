import React, { useRef } from 'react';
import { ChevronDown } from 'lucide-react';

export interface SettingsNavItem<K extends string> {
  key: K;
  label: string;
  icon: React.ComponentType<{ className?: string }>;
}

export interface SettingsNavGroup<K extends string> {
  label: string;
  items: SettingsNavItem<K>[];
}

interface SettingsNavProps<K extends string> {
  groups: SettingsNavGroup<K>[];
  current: K;
  onChange: (key: K) => void;
}

/**
 * Section navigation for the settings page. From lg up it is a grouped vertical tab list beside the
 * content, so adding a section never widens the page; below lg it collapses into a single picker that
 * opens the platform's native list (a bottom sheet on phones).
 */
export function SettingsNav<K extends string>({ groups, current, onChange }: SettingsNavProps<K>) {
  const tabRefs = useRef(new Map<K, HTMLButtonElement>());
  const items = groups.flatMap((group) => group.items);
  const currentItem = items.find((item) => item.key === current) ?? items[0];
  const CurrentIcon = currentItem.icon;

  const handleKeyDown = (event: React.KeyboardEvent<HTMLButtonElement>, index: number) => {
    const last = items.length - 1;
    const next =
      event.key === 'ArrowDown' ? (index === last ? 0 : index + 1)
      : event.key === 'ArrowUp' ? (index === 0 ? last : index - 1)
      : event.key === 'Home' ? 0
      : event.key === 'End' ? last
      : null;
    if (next === null) return;

    event.preventDefault();
    onChange(items[next].key);
    tabRefs.current.get(items[next].key)?.focus();
  };

  return (
    <>
      {/* Compact picker (below lg) */}
      <label className="lg:hidden relative flex items-center w-full rounded-2xl bg-[var(--bg-surface)] shadow-sm focus-within:ring-2 focus-within:ring-[var(--brand-primary)]">
        <span className="sr-only">Settings section</span>
        <span className="pointer-events-none absolute left-3 flex items-center justify-center w-7 h-7 rounded-lg bg-[var(--brand-primary)] text-white">
          <CurrentIcon className="w-3.5 h-3.5" />
        </span>
        <select
          value={current}
          onChange={(event) => onChange(event.target.value as K)}
          className="w-full appearance-none bg-transparent pl-12 pr-10 py-3 text-sm font-semibold text-[var(--text-main)] cursor-pointer focus:outline-none"
        >
          {groups.map((group) => (
            <optgroup key={group.label} label={group.label}>
              {group.items.map((item) => (
                <option key={item.key} value={item.key}>
                  {item.label}
                </option>
              ))}
            </optgroup>
          ))}
        </select>
        <ChevronDown className="pointer-events-none absolute right-3 w-4 h-4 text-[var(--text-muted)]" />
      </label>

      {/* Grouped vertical tabs (lg and up) */}
      <nav
        role="tablist"
        aria-label="Organization Settings Sections"
        aria-orientation="vertical"
        className="hidden lg:flex flex-col gap-5 p-2 rounded-2xl bg-[var(--bg-surface)] backdrop-blur-md shadow-sm lg:sticky lg:top-4"
      >
        {groups.map((group) => (
          <div key={group.label} className="space-y-1">
            <div className="px-3 pt-1 text-[10px] font-semibold uppercase tracking-wider text-[var(--text-muted)]">
              {group.label}
            </div>
            {group.items.map((item) => {
              const Icon = item.icon;
              const selected = item.key === current;
              return (
                <button
                  key={item.key}
                  ref={(el) => {
                    if (el) tabRefs.current.set(item.key, el);
                    else tabRefs.current.delete(item.key);
                  }}
                  type="button"
                  role="tab"
                  id={`tab-${item.key}`}
                  aria-controls={`panel-${item.key}`}
                  aria-selected={selected}
                  tabIndex={selected ? 0 : -1}
                  onClick={() => onChange(item.key)}
                  onKeyDown={(event) => handleKeyDown(event, items.indexOf(item))}
                  className={`w-full flex items-center gap-2.5 px-3 py-2 rounded-xl text-left text-xs font-semibold transition-all cursor-pointer ${
                    selected
                      ? 'bg-[var(--brand-primary)] text-white shadow-sm'
                      : 'text-[var(--text-secondary)] hover:text-[var(--text-main)] hover:bg-[var(--bg-surface-hover)]'
                  }`}
                >
                  <Icon className="w-3.5 h-3.5 shrink-0" />
                  <span>{item.label}</span>
                </button>
              );
            })}
          </div>
        ))}
      </nav>
    </>
  );
}
