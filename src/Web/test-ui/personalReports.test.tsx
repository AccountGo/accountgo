import { expect, it } from 'vitest';
import { buildStatement, type ReportData } from '../src/pages/Reports';
it('personal income separates freelance transfers from earned income', () => {
  const data = { income: [
    { accountId: 1, accountCode: '40100', accountName: 'Pay', isExpense: false, amount: 100 },
    { accountId: 2, accountCode: '40900', accountName: 'Transfer', isExpense: false, amount: 50 },
    { accountId: 3, accountCode: '60100', accountName: 'Food', isExpense: true, amount: 30 },
  ], balanceSheet: [], trialBalance: [], accounts: [], cashFlow: {}, years: [] } as unknown as ReportData;
  const rows = buildStatement('income', data, true, true);
  expect(rows.find(x => x.label === 'Total Earned income')?.amount).toBe(10000);
  expect(rows.find(x => x.label === 'Money from my freelance work')?.amount).toBe(5000);
  expect(rows.find(x => x.label === 'Net')?.amount).toBe(12000);
  expect(rows.some(x => /profit|revenue/i.test(x.label))).toBe(false);
});
