import { afterEach, expect, it, vi } from 'vitest';
import { cleanup, render, screen } from '@testing-library/react';
import { AccountingPolicyCard } from '../src/components/AccountingPolicyCard';
const response = { reportingFramework: 'IfrsForSmes', accountingBasis: 'Accrual', fiscalYearPattern: 'Custom', fiscalYearStartMonth: 4, booksStartDate: '2026-04-01', taxRegime: 'None', entityType: 'Individual', inventoryCostingMethod: 'None', isLedgerFrozen: true };
vi.mock('../src/auth/AuthContext', () => ({ useAuth: () => ({ authFetch: async () => ({ ok: true, json: async () => response }) }) }));
afterEach(cleanup);
it('shows only the frozen calendar fields for personal books', async () => {
  render(<AccountingPolicyCard canEdit personal />);
  expect(await screen.findByLabelText('Books start date')).toHaveProperty('disabled', true);
  expect(screen.getByLabelText('Fiscal year')).toHaveProperty('disabled', true);
  expect(screen.getByLabelText('Start month')).toHaveProperty('disabled', true);
  for (const label of ['Reporting framework', 'Entity type', 'Tax regime', 'Inventory costing']) expect(screen.queryByLabelText(label)).toBeNull();
});
