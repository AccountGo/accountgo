import { cleanup, render, screen } from '@testing-library/react';
import { afterEach, expect, it } from 'vitest';
import { PersonalFinanceHealthCard } from '../src/components/PersonalFinanceHealthCard';
import {
  isInvoicingDashboard,
  isPersonalWorkspace,
  nearTermCashPosition,
} from '../src/lib/dashboardProduct';

afterEach(cleanup);

it('treats missing financials as the invoicing dashboard surface', () => {
  expect(isInvoicingDashboard(false)).toBe(true);
  expect(isInvoicingDashboard(true)).toBe(false);
});

it('detects personal purpose from operating policy', () => {
  expect(isPersonalWorkspace({
    id: '1',
    name: 'Home',
    slug: 'personal',
    operatingPolicy: { purpose: 'Personal' },
  })).toBe(true);
  expect(isPersonalWorkspace({
    id: '2',
    name: 'Co',
    slug: 'co',
    operatingPolicy: { purpose: 'Business' },
  })).toBe(false);
});

it('computes near-term cash as cash plus receivables minus payables', () => {
  expect(nearTermCashPosition({
    moneyInBank: 1000,
    outstandingReceivables: 250,
    totalPayables: 100,
  })).toBe(1150);
});

it('renders personal finance health for invoicing without claiming net worth', () => {
  render(
    <PersonalFinanceHealthCard
      loading={false}
      hasFinancials={false}
      currencyFormat={(v) => `$${Number(v ?? 0).toFixed(2)}`}
      moneyInBank={500}
      outstandingReceivables={200}
      totalPayables={50}
    />,
  );

  const card = screen.getByTestId('personal-finance-health');
  expect(card.textContent).toContain('Near-term position');
  expect(card.textContent).toContain('$650.00');
  expect(card.textContent).not.toContain('Net worth');
});
