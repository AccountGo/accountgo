import React, { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { AlertTriangle } from 'lucide-react';
import { useAuth } from '../auth/AuthContext';
import { rowsWithOutstanding, staleMessage, staleTotal, type AgingRow } from '../lib/outstandingAging';
import type { BankAccount, OutstandingAging } from '../types/banking';

interface OutstandingAgingCardProps {
  accounts: BankAccount[];
  orgSlug: string;
  formatCurrency: (n: number) => string;
}

/** Accounting only: uncleared items per account, by age. The 90+ column is flagged as stale. */
export const OutstandingAgingCard: React.FC<OutstandingAgingCardProps> = ({ accounts, orgSlug, formatCurrency }) => {
  const { authFetch } = useAuth();
  const [rows, setRows] = useState<AgingRow[]>([]);

  useEffect(() => {
    let cancelled = false;
    void (async () => {
      const active = accounts.filter((a) => !a.archivedAt);
      const results = await Promise.all(
        active.map(async (account): Promise<AgingRow | null> => {
          try {
            const res = await authFetch(`/api/Banking/Accounts/${account.id}/OutstandingAging`);
            if (!res.ok) return null;
            return { account, aging: (await res.json()) as OutstandingAging };
          } catch {
            return null;
          }
        }),
      );
      if (!cancelled) setRows(rowsWithOutstanding(results.filter((r): r is AgingRow => r !== null)));
    })();
    return () => {
      cancelled = true;
    };
  }, [accounts, authFetch]);

  if (rows.length === 0) return null;
  const stale = staleTotal(rows);
  const labels = rows[0].aging.buckets.map((b) => b.label);

  return (
    <section aria-labelledby="outstanding-aging-title" className="space-y-3">
      <h2 id="outstanding-aging-title" className="text-sm font-bold text-[var(--text-main)]">
        Outstanding items by age
      </h2>
      {stale > 0 && (
        <div className="p-3 rounded-xl bg-amber-500/10 border border-amber-500/30 text-amber-700 text-xs flex gap-2">
          <AlertTriangle className="w-4 h-4 shrink-0" />
          {staleMessage(stale)}
        </div>
      )}
      <div className="rounded-xl border border-[var(--border-subtle)] bg-[var(--bg-surface)] overflow-x-auto shadow-sm">
        <table className="w-full text-sm min-w-[560px]">
          <thead>
            <tr className="text-left text-xs uppercase tracking-wider text-[var(--text-muted)] border-b border-[var(--border-subtle)]">
              <th className="px-4 py-3 font-semibold">Account</th>
              {labels.map((label) => (
                <th key={label} className="px-4 py-3 font-semibold text-right">
                  {label} days
                </th>
              ))}
            </tr>
          </thead>
          <tbody className="divide-y divide-[var(--border-subtle)]">
            {rows.map(({ account, aging }) => (
              <tr key={account.id}>
                <td className="px-4 py-2.5">
                  <Link to={`/org/${orgSlug}/banking/${account.id}`} className="text-[var(--brand-primary)] hover:underline">
                    {account.name}
                  </Link>
                </td>
                {aging.buckets.map((bucket) => (
                  <td
                    key={bucket.label}
                    className={`px-4 py-2.5 text-right ${bucket.isStale && bucket.count > 0 ? 'text-amber-700 font-semibold' : ''}`}
                  >
                    {bucket.count === 0 ? (
                      <span className="text-[var(--text-muted)]">—</span>
                    ) : (
                      <>
                        <span>{bucket.count}</span>
                        <span className="block text-xs font-mono text-[var(--text-muted)]">{formatCurrency(bucket.net)}</span>
                        {bucket.isStale && <span className="block text-[10px] uppercase tracking-wider">Stale</span>}
                      </>
                    )}
                  </td>
                ))}
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </section>
  );
};

export default OutstandingAgingCard;
