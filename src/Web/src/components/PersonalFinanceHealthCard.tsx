import React from 'react';
import { HeartPulse } from 'lucide-react';
import { nearTermCashPosition } from '../lib/dashboardProduct';

interface Props {
  loading: boolean;
  hasFinancials: boolean;
  currencyFormat: (val: number | undefined) => string;
  moneyInBank?: number;
  outstandingReceivables?: number;
  totalPayables?: number;
  /** Equity / accumulated net worth when the ledger is available. */
  netWorth?: number;
}

/**
 * Personal-purpose workspaces: a compact health strip for household cash and
 * (when accounting is on) net worth — not a formal personal financial statement.
 */
export const PersonalFinanceHealthCard: React.FC<Props> = ({
  loading,
  hasFinancials,
  currencyFormat,
  moneyInBank,
  outstandingReceivables,
  totalPayables,
  netWorth,
}) => {
  const nearTerm = nearTermCashPosition({ moneyInBank, outstandingReceivables, totalPayables });

  const cells: { label: string; value: number | undefined; hint: string }[] = [
    {
      label: 'Available cash',
      value: moneyInBank,
      hint: 'Bank and cash balances',
    },
    {
      label: 'Owed to you',
      value: outstandingReceivables,
      hint: 'Open invoices',
    },
    {
      label: 'You owe',
      value: totalPayables,
      hint: 'Open bills',
    },
    hasFinancials
      ? {
          label: 'Net worth',
          value: netWorth,
          hint: 'Assets minus liabilities (books)',
        }
      : {
          label: 'Near-term position',
          value: nearTerm,
          hint: 'Cash + owed to you − you owe',
        },
  ];

  return (
    <section
      aria-labelledby="personal-finance-health-heading"
      className="p-4 sm:p-5 rounded-2xl bg-gradient-to-r from-[var(--bg-surface)] to-[var(--brand-subtle)] shadow-md space-y-3"
      data-testid="personal-finance-health"
    >
      <div className="flex items-center gap-2">
        <HeartPulse className="w-4 h-4 text-[var(--brand-primary)]" />
        <div>
          <h2 id="personal-finance-health-heading" className="text-sm font-bold text-[var(--text-main)]">
            Personal finance health
          </h2>
          <p className="text-[11px] text-[var(--text-muted)]">
            {hasFinancials
              ? 'Household picture from your books. Not a formal personal financial statement.'
              : 'Near-term money picture from invoices and cash. Upgrade to Full accounting for net worth.'}
          </p>
        </div>
      </div>

      <div className="grid grid-cols-2 lg:grid-cols-4 gap-3">
        {cells.map((cell) => (
          <div key={cell.label} className="p-3 rounded-xl bg-[var(--bg-subtle)]/80">
            <div className="text-[10px] font-semibold uppercase tracking-wider text-[var(--text-muted)]">
              {cell.label}
            </div>
            <div className="text-base sm:text-lg font-bold text-[var(--text-main)] mt-1 tracking-tight">
              {loading ? (
                <span className="inline-block w-20 h-5 bg-[var(--bg-surface)] rounded animate-pulse" />
              ) : (
                currencyFormat(cell.value)
              )}
            </div>
            <div className="text-[10px] text-[var(--text-muted)] mt-1">{cell.hint}</div>
          </div>
        ))}
      </div>
    </section>
  );
};

export default PersonalFinanceHealthCard;
