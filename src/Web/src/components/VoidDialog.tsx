import React, { useEffect, useRef, useState } from 'react';
import { ModalShell } from './ModalShell';
import { localToday } from '../lib/businessDays';

interface VoidDialogProps {
  title: string;
  message: string;
  confirmLabel?: string;
  minDate?: string;
  requireReason?: boolean;
  busy?: boolean;
  onConfirm: (date: string, reason: string) => void;
  onCancel: () => void;
}

export const VoidDialog: React.FC<VoidDialogProps> = ({
  title,
  message,
  confirmLabel = 'Void',
  minDate,
  requireReason = false,
  busy = false,
  onConfirm,
  onCancel,
}) => {
  const [date, setDate] = useState<string>(() => {
    const today = localToday();
    if (minDate && today < minDate) return minDate;
    return today;
  });
  const [reason, setReason] = useState('');
  const reasonRef = useRef<HTMLInputElement>(null);

  useEffect(() => {
    reasonRef.current?.focus();
  }, []);

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    if (!date) return;
    if (requireReason && !reason.trim()) return;
    onConfirm(date, reason.trim());
  };

  return (
    <ModalShell
      open
      onClose={() => {
        if (!busy) onCancel();
      }}
      closeOnEscape={!busy}
      closeOnBackdrop={!busy}
      maxWidthClassName="max-w-md"
      role="alertdialog"
      aria-labelledby="void-dialog-title"
      aria-describedby="void-dialog-message"
      panelClassName="p-6 space-y-4"
    >
      <form onSubmit={handleSubmit} className="space-y-4">
        <h2 id="void-dialog-title" className="text-base font-bold text-[var(--text-main)]">
          {title}
        </h2>
        <p id="void-dialog-message" className="text-sm text-[var(--text-secondary)]">
          {message}
        </p>

        <div className="space-y-3">
          <div>
            <label className="block text-xs font-semibold uppercase tracking-wider text-[var(--text-muted)] mb-1">
              Void date <span className="text-rose-500">*</span>
            </label>
            <input
              type="date"
              required
              min={minDate}
              value={date}
              disabled={busy}
              onChange={(e) => setDate(e.target.value)}
              className="w-full px-3.5 py-2.5 text-sm bg-[var(--bg-subtle)] border border-[var(--border-subtle)] rounded-xl focus:ring-2 focus:ring-[var(--brand-ring)] focus:outline-none text-[var(--text-main)] transition"
            />
          </div>

          <div>
            <label className="block text-xs font-semibold uppercase tracking-wider text-[var(--text-muted)] mb-1">
              Reason {requireReason && <span className="text-rose-500">*</span>}
            </label>
            <input
              ref={reasonRef}
              type="text"
              required={requireReason}
              placeholder="Why is this being voided?"
              value={reason}
              disabled={busy}
              onChange={(e) => setReason(e.target.value)}
              className="w-full px-3.5 py-2.5 text-sm bg-[var(--bg-subtle)] border border-[var(--border-subtle)] rounded-xl focus:ring-2 focus:ring-[var(--brand-ring)] focus:outline-none text-[var(--text-main)] placeholder-[var(--text-muted)] transition"
            />
          </div>
        </div>

        <div className="flex justify-end gap-2 pt-2">
          <button
            type="button"
            disabled={busy}
            onClick={onCancel}
            className="h-10 px-4 rounded-xl text-xs font-semibold text-[var(--text-secondary)] hover:bg-[var(--bg-surface-hover)]"
          >
            Cancel
          </button>
          <button
            type="submit"
            disabled={busy || !date || (requireReason && !reason.trim())}
            className="h-10 px-4 rounded-xl text-xs font-semibold text-white bg-rose-600 hover:bg-rose-700 disabled:opacity-50"
          >
            {busy ? 'Voiding...' : confirmLabel}
          </button>
        </div>
      </form>
    </ModalShell>
  );
};
