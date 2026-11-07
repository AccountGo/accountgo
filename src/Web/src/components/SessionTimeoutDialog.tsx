import React, { useEffect, useRef, useState } from 'react';
import { ModalShell } from './ModalShell';
import type { SessionWarning } from '../lib/idleSession';

interface SessionTimeoutDialogProps {
  warning: NonNullable<SessionWarning>;
  onStay: () => void;
  onDismissExpiry: () => void;
  onSignOut: () => void;
}

function formatRemaining(ms: number): string {
  const total = Math.max(0, Math.ceil(ms / 1000));
  const minutes = Math.floor(total / 60);
  const seconds = total % 60;
  return minutes > 0 ? `${minutes}:${String(seconds).padStart(2, '0')}` : `${seconds} second${seconds === 1 ? '' : 's'}`;
}

/** Shown before an automatic sign-out: idle ("Are you still there?") or the sign-in's 12-hour limit. */
export const SessionTimeoutDialog: React.FC<SessionTimeoutDialogProps> = ({ warning, onStay, onDismissExpiry, onSignOut }) => {
  const [now, setNow] = useState(() => Date.now());
  const primaryRef = useRef<HTMLButtonElement>(null);
  const idle = warning.kind === 'idle';

  useEffect(() => {
    const id = window.setInterval(() => setNow(Date.now()), 1000);
    return () => window.clearInterval(id);
  }, []);

  useEffect(() => {
    primaryRef.current?.focus();
  }, [warning.kind]);

  const remaining = formatRemaining(warning.deadline - now);
  const close = idle ? onStay : onDismissExpiry;

  return (
    <ModalShell
      open
      onClose={close}
      maxWidthClassName="max-w-md"
      role="alertdialog"
      aria-labelledby="session-timeout-title"
      aria-describedby="session-timeout-message"
      panelClassName="p-6 sm:p-6 space-y-4"
    >
      <h2 id="session-timeout-title" className="text-base font-bold text-[var(--text-main)]">
        {idle ? 'Are you still there?' : 'Your session is ending'}
      </h2>
      <p id="session-timeout-message" className="text-sm text-[var(--text-secondary)]">
        {idle
          ? <>For your security, you'll be signed out in <span className="font-semibold tabular-nums text-[var(--text-main)]">{remaining}</span> because there has been no activity.</>
          : <>For your security, sessions end 12 hours after sign-in. Save your work — you'll be signed out in <span className="font-semibold tabular-nums text-[var(--text-main)]">{remaining}</span>.</>}
      </p>
      <div className="flex justify-end gap-2">
        <button
          type="button"
          onClick={onSignOut}
          className="h-10 px-4 rounded-xl text-xs font-semibold text-[var(--text-secondary)] hover:bg-[var(--bg-surface-hover)]"
        >
          {idle ? 'Sign out' : 'Sign in again now'}
        </button>
        <button
          type="button"
          ref={primaryRef}
          onClick={close}
          className="h-10 px-4 rounded-xl text-xs font-semibold text-white bg-[var(--brand-primary)]"
        >
          {idle ? 'Stay signed in' : 'Continue working'}
        </button>
      </div>
    </ModalShell>
  );
};

export default SessionTimeoutDialog;
