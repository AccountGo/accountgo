import React, { useEffect, useId, useRef, useState } from 'react';
import { Link } from 'react-router-dom';
import { ArrowLeft, Loader2, MoreHorizontal, type LucideIcon } from 'lucide-react';

/**
 * The header every working page shares: an optional back link, the title with its status badges, one line of
 * context, and the page's actions on the right. Actions stay on one row; when the page is narrow they move under
 * the title, secondary buttons drop their labels, and anything rarely used lives in the "More" menu.
 */
export const PageHeader: React.FC<{
  title: React.ReactNode;
  /** Status pills shown beside the title. */
  badges?: React.ReactNode;
  /** One short line under the title. */
  subtitle?: React.ReactNode;
  /** Small trail above the title, e.g. a breadcrumb. */
  eyebrow?: React.ReactNode;
  back?: { to: string; label: string };
  actions?: React.ReactNode;
  className?: string;
}> = ({ title, badges, subtitle, eyebrow, back, actions, className = '' }) => (
  <header className={`flex flex-wrap items-start gap-x-6 gap-y-3 ${className}`}>
    <div className="flex min-w-0 flex-1 basis-64 items-start gap-3">
      {back && (
        <Link
          to={back.to}
          title={back.label}
          aria-label={back.label}
          className="mt-0.5 inline-flex h-9 w-9 shrink-0 items-center justify-center rounded-lg border border-[var(--border-subtle)] text-[var(--text-secondary)] transition hover:bg-[var(--bg-surface-hover)] hover:text-[var(--text-main)] focus:outline-none focus-visible:ring-2 focus-visible:ring-[var(--brand-ring)]"
        >
          <ArrowLeft className="h-4 w-4" />
        </Link>
      )}
      <div className="min-w-0">
        {eyebrow && <div className="mb-1 text-xs text-[var(--text-muted)]">{eyebrow}</div>}
        <div className="flex flex-wrap items-center gap-x-3 gap-y-1.5">
          <h1 className="min-w-0 truncate text-2xl font-bold leading-9 tracking-tight text-[var(--text-main)]">{title}</h1>
          {badges && <div className="flex flex-wrap items-center gap-1.5">{badges}</div>}
        </div>
        {subtitle && <p className="mt-0.5 text-sm text-[var(--text-secondary)]">{subtitle}</p>}
      </div>
    </div>
    {actions && <div className="flex shrink-0 items-center gap-2 sm:ml-auto">{actions}</div>}
  </header>
);

type Variant = 'primary' | 'secondary';

const base =
  'inline-flex h-9 shrink-0 items-center justify-center gap-1.5 whitespace-nowrap rounded-lg text-sm transition focus:outline-none focus-visible:ring-2 focus-visible:ring-[var(--brand-ring)] disabled:cursor-not-allowed disabled:opacity-50';

const variants: Record<Variant, string> = {
  primary:
    'px-3.5 font-semibold text-[var(--brand-text)] bg-[var(--brand-primary)] hover:bg-[var(--brand-hover)] shadow-sm',
  secondary:
    'px-3 font-medium text-[var(--text-main)] bg-[var(--bg-surface)] border border-[var(--border-subtle)] hover:bg-[var(--bg-surface-hover)]',
};

type HeaderButtonProps = {
  label: string;
  icon?: LucideIcon;
  variant?: Variant;
  /** Show only the icon; the label becomes the tooltip and accessible name. */
  iconOnly?: boolean;
  /** Show only the icon below the sm breakpoint. */
  compact?: boolean;
  busy?: boolean;
  disabled?: boolean;
  to?: string;
  onClick?: () => void;
};

/** A header action, as a button or (with `to`) a link. All header actions share this size and shape. */
export const HeaderButton: React.FC<HeaderButtonProps> = ({
  label,
  icon: Icon,
  variant = 'secondary',
  iconOnly = false,
  compact = false,
  busy = false,
  disabled = false,
  to,
  onClick,
}) => {
  const iconClass = `h-4 w-4 ${variant === 'secondary' ? 'text-[var(--text-secondary)]' : ''}`;
  const glyph = busy ? <Loader2 className={`${iconClass} animate-spin`} /> : Icon ? <Icon className={iconClass} /> : null;
  const className = `${base} ${variants[variant]} ${iconOnly ? '!w-9 !px-0' : compact ? 'max-sm:w-9 max-sm:px-0' : ''}`;
  const content = (
    <>
      {glyph}
      <span className={iconOnly ? 'sr-only' : compact && glyph ? 'max-sm:sr-only' : ''}>{label}</span>
    </>
  );
  const title = iconOnly || compact ? label : undefined;

  if (to && !disabled) {
    return (
      <Link to={to} title={title} className={className}>
        {content}
      </Link>
    );
  }
  return (
    <button type="button" title={title} onClick={onClick} disabled={disabled || busy} className={className}>
      {content}
    </button>
  );
};

export type HeaderMenuItem =
  | { label: string; icon?: LucideIcon; onSelect?: () => void; to?: string; danger?: boolean; disabled?: boolean; title?: string }
  | 'separator';

/** The "More" menu for actions that don't need a button of their own. Keyboard: arrows, Home/End, Escape. */
export const HeaderMenu: React.FC<{ items: (HeaderMenuItem | false | null | undefined)[]; label?: string }> = ({
  items,
  label = 'More actions',
}) => {
  const [open, setOpen] = useState(false);
  const rootRef = useRef<HTMLDivElement>(null);
  const triggerRef = useRef<HTMLButtonElement>(null);
  const menuRef = useRef<HTMLDivElement>(null);
  const menuId = useId();

  const visible = items.filter((item): item is HeaderMenuItem => !!item);
  // Drop separators at the edges or next to each other, so conditional items never leave stray rules.
  const shown = visible.filter(
    (item, i) => item !== 'separator' || (i > 0 && i < visible.length - 1 && visible[i - 1] !== 'separator'),
  );

  useEffect(() => {
    if (!open) return;
    const onPointer = (e: MouseEvent) => {
      if (rootRef.current && !rootRef.current.contains(e.target as Node)) setOpen(false);
    };
    document.addEventListener('mousedown', onPointer);
    menuRef.current?.querySelector<HTMLElement>('[role="menuitem"]')?.focus();
    return () => document.removeEventListener('mousedown', onPointer);
  }, [open]);

  if (shown.every((item) => item === 'separator')) return null;

  const close = (refocus: boolean) => {
    setOpen(false);
    if (refocus) triggerRef.current?.focus();
  };

  const onMenuKey = (e: React.KeyboardEvent) => {
    const entries = Array.from(menuRef.current?.querySelectorAll<HTMLElement>('[role="menuitem"]') ?? []);
    const at = entries.indexOf(document.activeElement as HTMLElement);
    const move = (i: number) => {
      e.preventDefault();
      entries[(i + entries.length) % entries.length]?.focus();
    };
    if (e.key === 'ArrowDown') move(at + 1);
    else if (e.key === 'ArrowUp') move(at - 1);
    else if (e.key === 'Home') move(0);
    else if (e.key === 'End') move(entries.length - 1);
    else if (e.key === 'Escape') {
      e.preventDefault();
      close(true);
    } else if (e.key === 'Tab') close(false);
  };

  const itemClass = (danger?: boolean) =>
    `flex w-full items-center gap-2.5 rounded-md px-2.5 py-2 text-left text-sm transition focus:outline-none ${
      danger
        ? 'text-rose-500 hover:bg-rose-500/10 focus:bg-rose-500/10'
        : 'text-[var(--text-main)] hover:bg-[var(--bg-surface-hover)] focus:bg-[var(--bg-surface-hover)]'
    }`;

  return (
    <div ref={rootRef} className="relative">
      <button
        ref={triggerRef}
        type="button"
        title={label}
        aria-label={label}
        aria-haspopup="menu"
        aria-expanded={open}
        aria-controls={open ? menuId : undefined}
        onClick={() => setOpen((o) => !o)}
        onKeyDown={(e) => {
          if (e.key === 'ArrowDown' && !open) {
            e.preventDefault();
            setOpen(true);
          }
        }}
        className={`${base} ${variants.secondary} w-9 !px-0`}
      >
        <MoreHorizontal className="h-4 w-4 text-[var(--text-secondary)]" />
      </button>
      {open && (
        <div
          ref={menuRef}
          id={menuId}
          role="menu"
          aria-label={label}
          onKeyDown={onMenuKey}
          className="absolute right-0 top-full z-30 mt-1.5 min-w-48 rounded-lg border border-[var(--border-subtle)] bg-[var(--bg-surface)] p-1 shadow-xl"
        >
          {shown.map((item, i) => {
            if (item === 'separator') {
              return <div key={`sep-${i}`} role="separator" className="my-1 h-px bg-[var(--border-subtle)]" />;
            }
            const Icon = item.icon;
            const inner = (
              <>
                {Icon && <Icon className={`h-4 w-4 ${item.danger ? '' : 'text-[var(--text-secondary)]'}`} />}
                {item.label}
              </>
            );
            return item.to ? (
              <Link key={item.label} to={item.to} title={item.title} role="menuitem" tabIndex={-1} onClick={() => close(false)} className={itemClass(item.danger)}>
                {inner}
              </Link>
            ) : (
              <button
                key={item.label}
                type="button"
                role="menuitem"
                title={item.title}
                disabled={item.disabled}
                tabIndex={-1}
                onClick={() => {
                  if (item.disabled) return;
                  close(false);
                  item.onSelect?.();
                }}
                className={`${itemClass(item.danger)} ${item.disabled ? 'opacity-50 cursor-not-allowed hover:bg-transparent' : ''}`}
              >
                {inner}
              </button>
            );
          })}
        </div>
      )}
    </div>
  );
};

/** Status pill used beside page titles. */
export const StatusPill: React.FC<{ tone: 'green' | 'amber' | 'red' | 'blue' | 'sky' | 'slate'; icon?: LucideIcon; title?: string; strike?: boolean; children: React.ReactNode }> = ({
  tone,
  icon: Icon,
  title,
  strike = false,
  children,
}) => {
  const tones = {
    green: 'bg-emerald-500/10 text-emerald-600 dark:text-emerald-400 border-emerald-500/20',
    amber: 'bg-amber-500/10 text-amber-600 dark:text-amber-400 border-amber-500/20',
    red: 'bg-rose-500/10 text-rose-600 dark:text-rose-400 border-rose-500/20',
    blue: 'bg-blue-500/10 text-blue-600 dark:text-blue-400 border-blue-500/20',
    sky: 'bg-sky-500/10 text-sky-600 dark:text-sky-400 border-sky-500/20',
    slate: 'bg-slate-500/10 text-slate-500 dark:text-slate-400 border-slate-500/20',
  } as const;
  return (
    <span
      title={title}
      className={`inline-flex h-6 items-center gap-1 whitespace-nowrap rounded-full border px-2.5 text-xs font-semibold ${tones[tone]} ${strike ? 'line-through' : ''}`}
    >
      {Icon && <Icon className="h-3.5 w-3.5" />}
      {children}
    </span>
  );
};
