import assert from 'node:assert/strict';
import { after, test } from 'node:test';
import { createServer } from 'vite';
import { createElement } from 'react';
import { renderToStaticMarkup } from 'react-dom/server';
import { MemoryRouter, Outlet, Route, Routes } from 'react-router-dom';

const server = await createServer({ server: { middlewareMode: true }, appType: 'custom' });
after(() => server.close());
const { default: Reports, buildStatement, getDateRange, loadReportData, reportQuery, statementCsv } =
  await server.ssrLoadModule('/src/pages/Reports.tsx');
const { AuthProvider } = await server.ssrLoadModule('/src/auth/AuthContext.tsx');

const empty = () => ({ trialBalance: [], income: [], balanceSheet: [], accounts: [], years: [] });
const total = (rows, label) => rows.find(row => row.label === label);

test('the reports route renders all statement controls and respects reports.view', () => {
  const render = allowed => renderToStaticMarkup(createElement(AuthProvider, null,
    createElement(MemoryRouter, { initialEntries: ['/org/ws-one/reports'] },
      createElement(Routes, null,
        createElement(Route, {
          path: '/org/:orgSlug',
          element: createElement(Outlet, { context: {
            currentOrg: { slug: 'ws-one', name: 'WS One', currency: 'PHP' },
            hasPermission: permission => allowed && permission === 'reports.view',
          } }),
        }, createElement(Route, { path: 'reports', element: createElement(Reports) }))))));
  const html = render(true);
  for (const label of ['Trial Balance', 'Profit &amp; Loss', 'Balance Sheet', 'Accounting Basis', 'Accrual', 'Cash', 'Export CSV', 'Quarter-to-Date', 'Year-to-Date', 'All Time / As-of Date'])
    assert.ok(html.includes(label), label);
  assert.ok(html.includes('Loading financial reports...'));
  assert.ok(!html.includes('Reconciled'));
  const denied = render(false);
  assert.ok(denied.includes('Access Restricted'));
  assert.ok(!denied.includes('Export CSV'));
});

test('trial balance nets gross postings, sorts codes and totals exact cents', () => {
  const data = empty();
  data.trialBalance = [
    { accountCode: '40100', accountName: 'Sales', debit: 10000, credit: 25000 },
    { accountCode: '10120', accountName: 'Receivables', debit: 25000, credit: 15000 },
    { accountCode: '10111', accountName: 'Bank', debit: 5000, credit: 480 },
    { accountCode: '13302', accountName: 'Equipment', debit: 360, credit: 180 },
    { accountCode: '50200', accountName: 'Purchases', debit: 675, credit: 375 },
    { accountCode: '20110', accountName: 'Payables', debit: 1035, credit: 1035 },
  ];
  const rows = buildStatement('trialBalance', data);
  assert.deepEqual(rows.filter(r => r.kind === 'account').map(r => r.code), ['10111', '10120', '13302', '40100', '50200']);
  assert.equal(rows[0].debit, 452000);
  assert.equal(rows[0].credit, 0);
  assert.equal(total(rows, 'Total Debits / Total Credits').debit, 1500000);
  assert.equal(total(rows, 'Total Debits / Total Credits').credit, 1500000);
  assert.equal(total(rows, 'Difference').debit, 0);
  assert.equal(total(rows, 'Difference').credit, 0);
  assert.equal(buildStatement('trialBalance', data, false).filter(r => r.kind === 'account').length, 6);
  data.trialBalance.push({ accountCode: '999', accountName: 'Imbalance', debit: 0.01, credit: 0 });
  assert.equal(total(buildStatement('trialBalance', data), 'Difference').debit, 1);
});

test('P&L uses cost account ancestry, signed returns and hides zero balances', () => {
  const data = empty();
  data.accounts = [{ id: 10, accountName: 'Cost of Goods Sold', childAccounts: [{ id: 11, accountName: 'Materials', childAccounts: [] }] }];
  data.income = [
    { accountId: 1, accountCode: '4', accountName: 'Sales', isExpense: false, amount: 1000 },
    { accountId: 2, accountCode: '41', accountName: 'Returns', isExpense: false, amount: -100 },
    { accountId: 11, accountCode: '5', accountName: 'Materials', isExpense: true, amount: 200 },
    { accountId: 12, accountCode: '6', accountName: 'Rent', isExpense: true, amount: 300 },
    { accountId: 13, accountCode: '7', accountName: 'Unused', isExpense: true, amount: 0 },
  ];
  const rows = buildStatement('income', data);
  assert.equal(total(rows, 'Gross Profit').amount, 70000);
  assert.equal(total(rows, 'Total Operating Expenses').amount, 30000);
  assert.equal(total(rows, 'Net Profit').amount, 40000);
  assert.ok(!rows.some(r => r.label === 'Unused'));
  assert.ok(buildStatement('income', data, false).some(r => r.label === 'Unused'));
});

test('balance sheet keeps contra assets and unclosed losses signed in equity', () => {
  const data = empty();
  data.balanceSheet = [
    { accountCode: '1', accountName: 'Cash', accountClassId: 1, amount: 100 },
    { accountCode: '2', accountName: 'Depreciation', accountClassId: 1, amount: -20 },
    { accountCode: '3', accountName: 'Payables', accountClassId: 2, amount: 30 },
    { accountCode: '4', accountName: 'Capital', accountClassId: 3, amount: 100 },
    { accountCode: '', accountName: 'Unclosed loss', accountClassId: 3, amount: -50 },
  ];
  const rows = buildStatement('balanceSheet', data);
  assert.equal(total(rows, 'Total Assets').amount, 8000);
  assert.equal(total(rows, 'Total Equity').amount, 5000);
  assert.equal(total(rows, 'Total Liabilities + Equity').amount, 8000);
  assert.equal(total(rows, 'Difference').amount, 0);
});

test('presets use local calendar dates and the active fiscal year, including non-calendar years', () => {
  const today = new Date(2026, 9, 24, 12);
  assert.deepEqual(getDateRange('month', today), { from: '2026-10-01', to: '2026-10-24' });
  assert.deepEqual(getDateRange('quarter', new Date(2026, 8, 2)), { from: '2026-07-01', to: '2026-09-02' });
  assert.deepEqual(getDateRange('all', today), { from: '', to: '2026-10-24' });
  assert.deepEqual(getDateRange('year', today, { startDate: '2026-07-01', endDate: '2027-06-30' }), { from: '2026-07-01', to: '2026-10-24' });
  assert.throws(() => getDateRange('year', today), /No active financial year/);
  assert.throws(() => getDateRange('year', today, { startDate: '2025-01-01', endDate: '2025-12-31' }), /No active financial year/);
  const params = new URLSearchParams(reportQuery({ from: '2026-04-01', to: '2026-04-30' }));
  assert.equal(params.get('from'), '2026-04-01T00:00:00');
  assert.equal(params.get('to'), '2026-04-30T23:59:59.9999999');
  assert.equal(params.has('basis'), false);
  const cashParams = new URLSearchParams(reportQuery({ from: '2026-04-01', to: '2026-04-30' }, 'cash'));
  assert.equal(cashParams.get('basis'), 'Cash');
  assert.equal(new URLSearchParams(reportQuery({ from: '', to: '2026-04-30' })).has('from'), false);
});

test('all three live endpoints receive changed date filters and accounting basis, with explicit HTTP and shape errors', async () => {
  const urls = [];
  const fetcher = async url => { urls.push(url); return Response.json([]); };
  await loadReportData(fetcher, { from: '2026-01-01', to: '2026-04-30' });
  await loadReportData(fetcher, { from: '2026-04-01', to: '2026-04-30' }, 'cash');
  for (const endpoint of ['trialbalance', 'IncomeStatement', 'BalanceSheet']) {
    const calls = urls.filter(url => url.startsWith(`/api/Financials/${endpoint}`));
    assert.equal(calls.length, 2);
    assert.equal(new URL(calls[0], 'http://test').searchParams.get('from'), '2026-01-01T00:00:00');
    assert.equal(new URL(calls[0], 'http://test').searchParams.has('basis'), false);
    assert.equal(new URL(calls[1], 'http://test').searchParams.get('from'), '2026-04-01T00:00:00');
    assert.equal(new URL(calls[1], 'http://test').searchParams.get('basis'), 'Cash');
  }
  await assert.rejects(loadReportData(async () => new Response('', { status: 403 }), { from: '', to: '' }), /HTTP 403/);
  await assert.rejects(loadReportData(async () => Response.json({}), { from: '', to: '' }), /invalid response/);
  await assert.rejects(loadReportData(async url => Response.json(url.includes('trialbalance') ? [{ debit: '100', credit: 0 }] : []), { from: '', to: '' }), /invalid monetary amount/);
});

test('CSV exports every selected statement, escapes text and guards formula injection', () => {
  for (const statement of ['trialBalance', 'income', 'balanceSheet']) {
    const rows = [
      { code: '001', label: 'Bank, "main"\naccount', kind: 'account', debit: 12345, credit: 0, amount: 12345 },
      { code: '=1+1', label: '@SUM(A1)', kind: 'account', amount: -500 },
      { code: '', label: 'Total', kind: 'total', debit: 12345, credit: 0, amount: 11845 },
    ];
    const csv = statementCsv(statement, rows, [['Currency', 'USD']]);
    assert.ok(csv.includes('"Bank, ""main""\naccount"'));
    assert.ok(csv.includes('"123.45"'));
    assert.ok(csv.includes('"\'=1+1","\'@SUM(A1)"'));
    assert.ok(csv.includes('"Total"'));
    assert.ok(!csv.includes('undefined'));
    if (statement !== 'trialBalance') assert.ok(csv.includes('"-5.00"'));
  }
});
