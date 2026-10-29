import React, { useEffect, useRef } from 'react';
import { ModalShell } from './ModalShell';

interface ConfirmDialogProps {
  title: string;
  message: string;
  confirmLabel: string;
  destructive?: boolean;
  busy?: boolean;
  onConfirm: () => void;
  onCancel: () => void;
}

// In-app replacement for window.confirm: modal, focus-trapped, Escape cancels, focus returns to the trigger.
export const ConfirmDialog: React.FC<ConfirmDialogProps> = ({
  title,
  message,
  confirmLabel,
  destructive = false,
  busy = false,
  onConfirm,
  onCancel,
}) => {
  const cancelRef = useRef<HTMLButtonElement>(null);

  useEffect(() => {
    cancelRef.current?.focus();
  }, []);

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
      aria-labelledby="confirm-dialog-title"
      aria-describedby="confirm-dialog-message"
      panelClassName="p-6 sm:p-6 space-y-4"
    >
      <h2 id="confirm-dialog-title" className="text-base font-bold text-[var(--text-main)]">
        {title}
      </h2>
      <p id="confirm-dialog-message" className="text-sm text-[var(--text-secondary)]">
        {message}
      </p>
      <div className="flex justify-end gap-2">
        <button
          type="button"
          ref={cancelRef}
          disabled={busy}
          onClick={onCancel}
          className="h-10 px-4 rounded-xl text-xs font-semibold text-[var(--text-secondary)] hover:bg-[var(--bg-surface-hover)]"
        >
          Cancel
        </button>
        <button
          type="button"
          disabled={busy}
          onClick={onConfirm}
          className={`h-10 px-4 rounded-xl text-xs font-semibold text-white disabled:opacity-50 ${
            destructive ? 'bg-rose-600' : 'bg-[var(--brand-primary)]'
          }`}
        >
          {busy ? 'Working…' : confirmLabel}
        </button>
      </div>
    </ModalShell>
  );
};

export default ConfirmDialog;
