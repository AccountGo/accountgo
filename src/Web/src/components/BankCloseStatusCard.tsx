import React, { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { CheckCircle2, Landmark } from 'lucide-react';
import { useAuth } from '../auth/AuthContext';
import { closeStatusMessage, shouldShowCloseStatus, unreconciledBanks } from '../lib/bankCloseStatus';
import type { BankCloseStatus } from '../types/banking';

interface BankCloseStatusCardProps {
  status: BankCloseStatus | null | undefined;
  orgSlug: string;
}

export const BankCloseStatusCard: React.FC<BankCloseStatusCardProps> = ({ status, orgSlug }) => {
  if (!shouldShowCloseStatus(status)) return null;
  const pending = unreconciledBanks(status);

  return (
    <div className="p-4 rounded-2xl bg-[var(--bg-surface)] shadow-md space-y-3" data-testid="bank-close-status">
      <div className="flex items-center justify-between gap-3">
        <Link
          to={`/org/${orgSlug}/banking/reconcile`}
          className="flex items-center gap-2 text-sm font-semibold text-[var(--text-main)] hover:underline"
        >
          {status.isComplete ? (
            <CheckCircle2 className="w-4 h-4 text-emerald-500" />
          ) : (
            <Landmark className="w-4 h-4 text-amber-500" />
          )}
          <span>{closeStatusMessage(status)}</span>
        </Link>
      </div>
      {pending.length > 0 && (
        <ul className="flex flex-wrap gap-2">
          {pending.map((bank) => (
            <li key={bank.bankId}>
              <Link
                to={`/org/${orgSlug}/banking/${bank.bankId}/reconcile`}
                className="inline-flex h-7 items-center px-3 rounded-full text-xs font-semibold bg-amber-500/15 text-amber-700 hover:bg-amber-500/25"
              >
                {bank.name}
              </Link>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
};

/** Fetches the status itself; used where there is no dashboard summary (Reports). */
export const BankCloseStatusLoader: React.FC<{ orgSlug: string }> = ({ orgSlug }) => {
  const { authFetch } = useAuth();
  const [status, setStatus] = useState<BankCloseStatus | null>(null);

  useEffect(() => {
    let cancelled = false;
    void (async () => {
      try {
        const res = await authFetch('/api/Banking/CloseStatus');
        if (!res.ok || res.status === 204) return;
        const body = (await res.json()) as BankCloseStatus;
        if (!cancelled) setStatus(body);
      } catch {
        // The widget is informational; a failed load leaves it hidden.
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [authFetch, orgSlug]);

  return <BankCloseStatusCard status={status} orgSlug={orgSlug} />;
};

export default BankCloseStatusCard;
