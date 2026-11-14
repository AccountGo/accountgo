import assert from 'node:assert/strict';
import { test } from 'node:test';

import * as equity from '../src/lib/equityStatement.ts';
const column = (key, label = key) => ({ key, label });
const row = (key, amounts, total = Object.values(amounts).reduce((a, b) => a + b, 0)) => ({ key, label: key, amounts, total });
const statement = {
  columns: [column('capital')],
  rows: [row('opening', { capital: 0 }), row('profit', { capital: 100 }), row('contributions', { capital: 0 }), row('closing', { capital: 100 })],
  balanceSheetEquity: 100, differenceToBalanceSheet: 0,
};

test('single columns have no duplicate total; several components append Total', () => {
  assert.deepEqual(equity.equityTableColumns(statement), [column('capital')]);
  assert.deepEqual(equity.equityTableColumns({ columns: [column('contributed'), column('earnings')] }), [column('contributed'), column('earnings'), column('total', 'Total')]);
});
test('equity is offered only in business books', () => {
  assert.equal(equity.showEquityStatement(false), true);
  assert.equal(equity.showEquityStatement(true), false);
});
test('zero movements are omitted but opening, profit and closing remain', () => {
  assert.deepEqual(equity.equityTableRows(statement).map(r => r.key), ['opening', 'profit', 'closing']);
});
test('reconciliation states agreement within tolerance and reports differences', () => {
  assert.deepEqual(equity.equityReconciliation(statement, String), { agrees: true, message: 'Agrees with the balance sheet (equity 100)' });
  const mismatch = { ...statement, differenceToBalanceSheet: 5 };
  assert.deepEqual(equity.equityReconciliation(mismatch, String), { agrees: false, message: "Doesn't agree with the balance sheet by 5. Check equity accounts posted outside the usual codes." });
  assert.equal(equity.equityReconciliation({ ...statement, differenceToBalanceSheet: 0.004 }, String).agrees, true);
  assert.equal(equity.equityReconciliation({ ...statement, differenceToBalanceSheet: 0.005 }, String).agrees, false);
});
test('a component that does not bridge to closing is warned even if the balance sheet agrees', () => {
  const bad = { ...statement, rows: [row('opening', { capital: 0 }), row('profit', { capital: 40 }), row('closing', { capital: 100 })] };
  assert.equal(equity.equityReconciliation(bad, String).agrees, false);
});

test('CSV exports both periods, signed values, and safely escaped column and row labels', () => {
  const report = { ...statement, from: '2026-01-01', to: '2026-12-31', columns: [column('capital', '=Capital')],
    rows: [row('opening', { capital: 0 }), row('profit', { capital: 100 }),
      { ...row('distributions', { capital: -25 }), label: 'Owner "drawings"' }, row('closing', { capital: 75 })],
    balanceSheetEquity: 75,
  };
  const csv = equity.equityStatementCsv({ ...report, comparative: { ...report, from: '2025-01-01', to: '2025-12-31' } });
  assert.equal(csv?.includes('"Movement","\'=Capital"'), true);
  assert.equal(csv?.includes('"Owner ""drawings""","-25.00"'), true);
  assert.equal(csv?.includes('Previous period: 2025-01-01 – 2025-12-31'), true);
  assert.equal(csv?.includes('Agrees with the balance sheet (equity 75.00)'), true);
});

test('CSV includes the partner profit allocation note only when hasUnallocatedProfit is true', () => {
  const partnerReport = {
    ...statement,
    from: '2026-01-01',
    to: '2026-12-31',
    partnerMode: true,
    columns: [column('partner:1', 'Avery'), column('unallocated', 'Unallocated')],
    rows: [
      row('opening', { 'partner:1': 0, unallocated: 0 }),
      row('profit', { 'partner:1': 0, unallocated: 0 }),
      row('closing', { 'partner:1': 0, unallocated: 50 }),
    ],
    balanceSheetEquity: 50,
  };
  const withoutProfit = equity.equityStatementCsv({ ...partnerReport, hasUnallocatedProfit: false });
  assert.equal(withoutProfit?.includes('Profit is allocated to partners when the fiscal year is closed.'), false);
  const withProfit = equity.equityStatementCsv({ ...partnerReport, hasUnallocatedProfit: true });
  assert.equal(withProfit?.includes('Profit is allocated to partners when the fiscal year is closed.'), true);
});
