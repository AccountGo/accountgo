import React from 'react';
import { HeartPulse } from 'lucide-react';
import { recordedCashReceivablesMinusBills } from '../lib/dashboardProduct';

interface Props {
  loading: boolean;
  hasFinancials: boolean;
  /** Formats a loaded amount; pass null/undefined through as Unavailable. */
  currencyFormat: (val: number | null | undefined, available: boolean) => string;
  moneyInBank?: number | null;
  moneyInBankAvailable: boolean;
  outstandingReceivables?: number | null;
  receivablesAvailable: boolean;
  totalPayables?: number | null;
  payablesAvailable: boolean;
  /** Equity / accumulated net worth when the ledger is available. */
  netWorth?: number | null;
  netWorthAvailable: boolean;
}

/**
 * Personal-purpose workspaces: household money overview from authorized metrics.
 * Not a formal personal financial statement or a near-term cash forecast.
 */
export const PersonalFinanceHealthCard: React.FC<Props> = ({
  loading,
  hasFinancials,
  currencyFormat,
  moneyInBank,
  moneyInBankAvailable,
  outstandingReceivables,
  receivablesAvailable,
  totalPayables,
  payablesAvailable,
  netWorth,
  netWorthAvailable,
}) => {
  const recordedPosition = recordedCashReceivablesMinusBills({
    moneyInBank,
    outstandingReceivables,
    totalPayables,
    moneyInBankAvailable,
    receivablesAvailable,
    payablesAvailable,
  });

  const cells: { label: string; value: number | null | undefined; available: boolean; hint: string }[] = [
    {
      label: 'Available cash',
      value: moneyInBank,
      available: moneyInBankAvailable,
      hint: 'Bank and cash balances',
    },
    {
      label: 'Owed to you',
      value: outstandingReceivables,
      available: receivablesAvailable,
      hint: 'Open invoices (incl. tax)',
    },
    {
      label: 'You owe',
      value: totalPayables,
      available: payablesAvailable,
      hint: 'Open bills',
    },
    hasFinancials
      ? {
          label: 'Net worth (book value)',
          value: netWorth,
          available: netWorthAvailable,
          hint: 'Assets minus liabilities (books)',
        }
      : {
          label: 'Recorded cash + receivables − bills',
          value: recordedPosition,
          available: recordedPosition != null,
          hint: 'Not a cash forecast — no due-date horizon',
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
            Personal money overview
          </h2>
          <p className="text-[11px] text-[var(--text-muted)]">
            {hasFinancials
              ? 'Household picture from your books. Not a formal personal financial statement.'
              : 'Recorded invoice and cash totals only. Upgrade to Full accounting for net worth.'}
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
                currencyFormat(cell.value, cell.available)
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
