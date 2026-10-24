import React from 'react';
import { CheckCircle2 } from 'lucide-react';

export const inputClass =
  'w-full h-11 px-3.5 rounded-xl bg-[var(--bg-subtle)] shadow-inner text-sm text-[var(--text-main)] placeholder:text-[var(--text-muted)] focus:outline-none focus:ring-2 focus:ring-[var(--brand-ring)]';

export const Field: React.FC<{
  id: string;
  label: string;
  hint?: string;
  error?: string;
  children: React.ReactNode;
}> = ({ id, label, hint, error, children }) => (
  <div>
    <label htmlFor={id} className="block text-xs font-semibold text-[var(--text-secondary)] mb-1.5">{label}</label>
    {children}
    {error ? (
      <p id={`${id}-error`} role="alert" className="text-[11px] text-rose-400 mt-1">{error}</p>
    ) : hint ? (
      <p id={`${id}-hint`} className="text-[11px] text-[var(--text-muted)] mt-1">{hint}</p>
    ) : null}
  </div>
);

export interface ChoiceOption<T extends string> {
  value: T;
  title: string;
  description: string;
  badge?: string;
}

// Radio group rendered as selectable cards; native radios keep arrow-key navigation and screen reader semantics.
export function ChoiceCards<T extends string>({
  legend, name, value, options, onChange, columns = 'sm:grid-cols-2',
}: {
  legend: string;
  name: string;
  value: T;
  options: ChoiceOption<T>[];
  onChange: (value: T) => void;
  columns?: string;
}) {
  return (
    <fieldset>
      <legend className="block text-xs font-semibold text-[var(--text-secondary)] mb-1.5">{legend}</legend>
      <div className={`grid grid-cols-1 ${columns} gap-2.5`}>
        {options.map((option) => {
          const selected = option.value === value;
          return (
            <label
              key={option.value}
              className={`relative flex flex-col gap-1 p-3.5 rounded-xl cursor-pointer transition-all focus-within:ring-2 focus-within:ring-[var(--brand-ring)] ${
                selected
                  ? 'bg-[var(--brand-subtle)] ring-2 ring-[var(--brand-primary)]'
                  : 'bg-[var(--bg-subtle)] hover:bg-[var(--bg-surface-hover)]'
              }`}
            >
              <input
                type="radio"
                name={name}
                value={option.value}
                checked={selected}
                onChange={() => onChange(option.value)}
                className="sr-only"
              />
              <span className="flex items-center justify-between gap-2">
                <span className="text-sm font-semibold text-[var(--text-main)]">{option.title}</span>
                {selected && <CheckCircle2 className="w-4 h-4 text-[var(--brand-primary)] flex-shrink-0" aria-hidden="true" />}
              </span>
              <span className="text-xs text-[var(--text-secondary)]">{option.description}</span>
              {option.badge && (
                <span className="text-[10px] uppercase tracking-wider font-semibold text-[var(--brand-primary)]">{option.badge}</span>
              )}
            </label>
          );
        })}
      </div>
    </fieldset>
  );
}
