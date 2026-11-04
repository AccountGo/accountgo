import { cleanup, render, screen } from '@testing-library/react';
import { afterEach, expect, it } from 'vitest';
import { PersonalFinanceHealthCard } from '../src/components/PersonalFinanceHealthCard';
import {
  isPersonalWorkspace,
  recordedCashReceivablesMinusBills,
  resolveDashboardSurface,
} from '../src/lib/dashboardProduct';

afterEach(cleanup);

it('prefers the server-authorized surface over the client module entitlement', () => {
  expect(resolveDashboardSurface('Invoicing', true)).toBe('Invoicing');
  expect(resolveDashboardSurface('Accounting', false)).toBe('Accounting');
  expect(resolveDashboardSurface(undefined, false)).toBe('Invoicing');
  expect(resolveDashboardSurface(undefined, true)).toBe('Accounting');
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

it('computes recorded cash + receivables − bills only when every input is available', () => {
  expect(recordedCashReceivablesMinusBills({
    moneyInBank: 1000,
    outstandingReceivables: 250,
    totalPayables: 100,
    moneyInBankAvailable: true,
    receivablesAvailable: true,
    payablesAvailable: true,
  })).toBe(1150);

  expect(recordedCashReceivablesMinusBills({
    moneyInBank: 1000,
    outstandingReceivables: 250,
    totalPayables: 100,
    moneyInBankAvailable: false,
    receivablesAvailable: true,
    payablesAvailable: true,
  })).toBeNull();
});

it('renders personal money overview without claiming a cash forecast or inventing zeros', () => {
  render(
    <PersonalFinanceHealthCard
      loading={false}
      hasFinancials={false}
      currencyFormat={(v, available) => (available && v != null ? `$${Number(v).toFixed(2)}` : 'Unavailable')}
      moneyInBank={500}
      moneyInBankAvailable
      outstandingReceivables={200}
      receivablesAvailable
      totalPayables={50}
      payablesAvailable
      netWorth={null}
      netWorthAvailable={false}
    />,
  );

  const card = screen.getByTestId('personal-finance-health');
  expect(card.textContent).toContain('Personal money overview');
  expect(card.textContent).toContain('Recorded cash + receivables − bills');
  expect(card.textContent).toContain('$650.00');
  expect(card.textContent).not.toContain('Near-term');
  expect(card.textContent).not.toContain('Net worth');
});

it('shows Unavailable for restricted metrics and omits the combined figure', () => {
  render(
    <PersonalFinanceHealthCard
      loading={false}
      hasFinancials={false}
      currencyFormat={(v, available) => (available && v != null ? `$${Number(v).toFixed(2)}` : 'Unavailable')}
      moneyInBank={0}
      moneyInBankAvailable={false}
      outstandingReceivables={0}
      receivablesAvailable={false}
      totalPayables={0}
      payablesAvailable={false}
      netWorth={null}
      netWorthAvailable={false}
    />,
  );

  const card = screen.getByTestId('personal-finance-health');
  expect(card.textContent).toMatch(/Unavailable/);
  expect(card.textContent).not.toContain('$0.00');
});
