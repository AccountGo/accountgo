import React from 'react';
import { Link } from 'react-router-dom';
import { moneyInBankRows, MoneyInBankKpis } from '../lib/moneyInBank';

interface MoneyInBankBreakdownProps {
  kpis: MoneyInBankKpis | undefined | null;
  orgSlug: string;
  currencySymbol: string;
  formatAmount: (value: number | undefined) => string;
}

export const MoneyInBankBreakdown: React.FC<MoneyInBankBreakdownProps> = ({ kpis, orgSlug, currencySymbol, formatAmount }) => {
  const rows = moneyInBankRows(kpis, orgSlug);
  if (rows.length === 0) return null;

  return (
    <ul className="mt-3 space-y-1" data-testid="money-in-bank-breakdown">
      {rows.map((row) => (
        <li key={row.key}>
          <Link
            to={row.path}
            className="flex items-center justify-between gap-3 text-xs text-[var(--text-secondary)] hover:text-[var(--text-main)] hover:underline"
          >
            <span className="truncate">{row.label}</span>
            <span className="font-mono tabular-nums">
              {currencySymbol}
              {formatAmount(row.balance)}
            </span>
          </Link>
        </li>
      ))}
    </ul>
  );
};
