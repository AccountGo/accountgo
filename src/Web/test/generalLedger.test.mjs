import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { after, test } from 'node:test';
import { createServer } from 'vite';
import { createElement } from 'react';
import { renderToStaticMarkup } from 'react-dom/server';
import { MemoryRouter, Outlet, Route, Routes } from 'react-router-dom';

const server = await createServer({ server: { middlewareMode: true, hmr: false, ws: false }, appType: 'custom' });
after(() => server.close());
const { default: GeneralLedger, LedgerRows, ledgerFilters, ledgerQuery, searchLedger, ledgerCsv, isLedgerBalanced } =
  await server.ssrLoadModule('/src/pages/GeneralLedger.tsx');
const { default: JournalEntries, JournalEditor, journalTotals, canPostJournal } =
  await server.ssrLoadModule('/src/pages/JournalEntries.tsx');
const { AuthProvider } = await server.ssrLoadModule('/src/auth/AuthContext.tsx');

const entry = () => ({
  id: 42, date: '2026-04-01T00:00:00', documentType: 'SalesInvoice', documentTypeLabel: 'Sales Invoice',
  description: 'Invoice INV-5', sourceDocumentId: 5, sourcePath: '/sales/5', reversedGeneralLedgerHeaderId: null,
  totalDebit: 123.45, totalCredit: 123.45, lines: [
    { id: 101, accountId: 12, accountCode: '10120', accountName: 'Accounts Receivable', debit: 123.45, credit: 0 },
    { id: 102, accountId: 40, accountCode: '40100', accountName: 'Sales', debit: 0, credit: 123.45 },
  ],
});
const draft = () => ({
  id: 7, journalDate: '2026-04-01T00:00:00', voucherType: 6, referenceNo: 'ADJ-1', memo: 'Adjustment', posted: false,
  journalEntryLines: [
    { id: 1, accountId: 12, drCr: 1, amount: 0.3, memo: '' },
    { id: 2, accountId: 40, drCr: 2, amount: 0.3, memo: '' },
  ],
});
const accounts = [
  { id: 12, accountCode: '10120', accountName: 'Receivables', canPost: true },
  { id: 40, accountCode: '40100', accountName: 'Sales', canPost: true },
];
function renderPage(Component, permissions, path = '/org/ws-one/financials') {
  return renderToStaticMarkup(createElement(AuthProvider, null,
    createElement(MemoryRouter, { initialEntries: [path] },
      createElement(Routes, null, createElement(Route, {
        path: '/org/:orgSlug', element: createElement(Outlet, { context: {
          currentOrg: { slug: 'ws-one', name: 'WS One', currency: 'PHP' },
          hasPermission: permission => permissions.includes(permission),
        } }),
      }, createElement(Route, { path: 'financials/*', element: createElement(Component) }))))));
}

test('financials mounts the ledger, keeps the module gate, and uses journal-view navigation', async () => {
  const app = await readFile(new URL('../src/App.tsx', import.meta.url), 'utf8');
  assert.match(app, /path: 'financials',[\s\S]*?<RequiresModule module="financials">\s*<GeneralLedger/);
  assert.ok(!app.includes('PlaceholderView'));
  assert.match(app, /path: 'financials\/journals'/);
  for (const name of ['Sidebar', 'MobileBottomNav']) {
    const source = await readFile(new URL(`../src/components/${name}.tsx`, import.meta.url), 'utf8');
    assert.match(source, /path: `\/org\/\$\{currentOrg.slug\}\/financials`[\s\S]*?permission: 'financials.journal.view'/);
  }
});

test('ledger page denies account-only users and exposes working register actions by permission', () => {
  const denied = renderPage(GeneralLedger, ['financials.accounts.view']);
  assert.ok(denied.includes('Access Restricted'));
  assert.ok(!denied.includes('Export CSV'));
  const view = renderPage(GeneralLedger, ['financials.journal.view']);
  for (const label of ['General Ledger', 'Export CSV', 'This Month', 'Quarter-to-Date', 'Year-to-Date', 'All Time', 'Account code', 'Document type', 'Search', 'Manual journals / drafts'])
    assert.ok(view.includes(label), label);
  assert.ok(!view.includes('New Journal Entry'));
  assert.ok(!view.includes('Trial Balance'));
  const creator = renderPage(GeneralLedger, ['financials.journal.view', 'financials.journal.create']);
  assert.ok(creator.includes('/financials/journals?new=1'));
});

test('period and deep-link filters preserve local full-day bounds without cash-basis reporting', () => {
  const defaults = ledgerFilters(new URLSearchParams(), new Date(2026, 9, 24));
  assert.equal(defaults.from, '2026-10-01');
  assert.equal(defaults.to, '2026-10-24');
  const filters = ledgerFilters(new URLSearchParams({
    from: '2026-04-01T00:00:00', to: '2026-04-30', accountCode: '10120', transactionNo: '42', documentType: 'SalesInvoice',
  }));
  const query = new URLSearchParams(ledgerQuery(filters));
  assert.equal(query.get('from'), '2026-04-01T00:00:00');
  assert.equal(query.get('to'), '2026-04-30T23:59:59.9999999');
  assert.equal(query.get('accountCode'), '10120');
  assert.equal(query.get('transactionNo'), '42');
  assert.equal(query.get('documentType'), 'SalesInvoice');
  assert.equal(query.has('basis'), false);
  const original = new URLSearchParams(ledgerQuery(ledgerFilters(new URLSearchParams({ transactionNo: '1', from: '', to: '' }))));
  assert.equal(original.has('from'), false);
  assert.equal(original.has('to'), false);
  assert.equal(original.get('transactionNo'), '1');
  const invalid = renderPage(GeneralLedger, ['financials.journal.view'], '/org/ws-one/financials?from=2026-05-01&to=2026-04-01');
  assert.ok(invalid.includes('role="alert"'));
  assert.ok(invalid.includes('from must not be after to'));
});

test('search matches number, description, account code and account name but preserves all lines', () => {
  for (const query of ['42', 'inv-5', '10120', 'receivable', ' SALES ']) {
    const found = searchLedger([entry()], query);
    assert.equal(found.length, 1);
    assert.equal(found[0].lines.length, 2);
  }
  assert.equal(searchLedger([entry()], 'missing').length, 0);
});

test('expand controls are native keyboard buttons; full lines, source and reversal links are org-relative', () => {
  const reversal = { ...entry(), reversedGeneralLedgerHeaderId: 10 };
  const html = renderToStaticMarkup(createElement(MemoryRouter, null,
    createElement(LedgerRows, { entries: [reversal], slug: 'ws-one', formatAmount: amount => `PHP ${amount.toFixed(2)}` })));
  assert.match(html, /<button[^>]*aria-expanded="false"[^>]*aria-controls="gl-42"/);
  assert.match(html, /id="gl-42" hidden=""/);
  for (const label of ['Account code', 'Account name', 'Debit', 'Credit', 'Totals (In balance)', 'Accounts Receivable', 'Sales'])
    assert.ok(html.includes(label), label);
  assert.ok(html.includes('href="/org/ws-one/sales/5"'));
  assert.ok(html.includes('Reversal of'));
  assert.match(html, /href="\/org\/ws-one\/financials\?transactionNo=10&amp;from=&amp;to="/);
  assert.equal(isLedgerBalanced(entry()), true);
  const bad = { ...entry(), totalCredit: 120 };
  assert.equal(isLedgerBalanced(bad), false);
  const warning = renderToStaticMarkup(createElement(MemoryRouter, null, createElement(LedgerRows, {
    entries: [bad], slug: 'ws-one', formatAmount: String,
  })));
  assert.ok(warning.includes('role="alert"'));
  assert.ok(warning.includes('Out of balance'));
  assert.equal(isLedgerBalanced({ ...entry(), lines: [] }), false);
});

test('CSV includes header audit metadata and each complete account line with spreadsheet safety', () => {
  const sample = entry();
  sample.description = '=HYPERLINK("evil")';
  sample.lines[0].accountName = 'Name, with "quotes"\nand newline';
  const csv = ledgerCsv([sample]);
  assert.ok(csv.startsWith('"Transaction","Date","Document type"'));
  assert.ok(csv.includes('"Total debit","Total credit","Line ID","Account code","Account name","Debit","Credit"'));
  assert.ok(csv.includes('"123.45","123.45","101","10120"'));
  assert.ok(csv.includes('"102","40100","Sales","0.00","123.45"'));
  assert.ok(csv.includes(`"'=HYPERLINK(""evil"")"`));
  assert.ok(csv.includes('"Name, with ""quotes""\nand newline"'));
  assert.equal(ledgerCsv([{ ...sample, lines: [] }]).split('\r\n').length, 2);
});

test('manual journal route is gated by journal.view and separates drafts from the posted register', () => {
  assert.ok(renderPage(JournalEntries, []).includes('Access Restricted'));
  const html = renderPage(JournalEntries, ['financials.journal.view']);
  assert.ok(html.includes('Draft vouchers are separate from the posted General Ledger'));
  assert.ok(!html.includes('New Journal Entry'));
  assert.ok(renderPage(JournalEntries, ['financials.journal.view', 'financials.journal.create']).includes('New Journal Entry'));
});

test('journal editor independently gates create and post and makes posted vouchers read-only', () => {
  const render = (initial, canCreate, canPost) => renderToStaticMarkup(createElement(JournalEditor, {
    initial, accounts, canCreate, canPost, fetcher: async () => Response.json({}), onSaved: () => {}, currency: 'USD',
  }));
  const creator = render(draft(), true, false);
  assert.ok(creator.includes('Save draft'));
  assert.ok(!creator.includes('Post journal'));
  assert.ok(creator.includes('Add line'));
  const poster = render(draft(), false, true);
  assert.ok(!poster.includes('Save draft'));
  assert.match(poster, /<button[^>]*>.*?Post journal/s);
  assert.match(poster, /<fieldset disabled=""/);
  const posted = render({ ...draft(), posted: true }, true, true);
  assert.ok(posted.includes('Posted (read-only)'));
  for (const action of ['Save draft', 'Post journal', 'Add line']) assert.ok(!posted.includes(action), action);
  const unsaved = render({ ...draft(), id: 0 }, true, true);
  assert.ok(unsaved.includes('Save the draft before posting.'));
});

test('posting readiness uses exact cents, positive balanced lines and saved account selections', () => {
  const initial = draft();
  assert.deepEqual(journalTotals(initial), { debit: 30, credit: 30 });
  assert.equal(canPostJournal(initial), true);
  for (const change of [
    { posted: true }, { journalEntryLines: [] },
    { journalEntryLines: [{ ...initial.journalEntryLines[0], accountId: 0 }, initial.journalEntryLines[1]] },
    { journalEntryLines: [{ ...initial.journalEntryLines[0], amount: -0.3 }, initial.journalEntryLines[1]] },
    { journalEntryLines: [{ ...initial.journalEntryLines[0], amount: 0.31 }, initial.journalEntryLines[1]] },
    { journalEntryLines: [{ ...initial.journalEntryLines[0], drCr: 0 }, initial.journalEntryLines[1]] },
  ]) assert.equal(canPostJournal({ ...initial, ...change }), false);
});

test('Trial Balance and COA drill links preserve account and explicit date range', async () => {
  const reports = await readFile(new URL('../src/pages/Reports.tsx', import.meta.url), 'utf8');
  assert.match(reports, /statement === 'trialBalance' && row.kind === 'account' && hasPermission\('financials.journal.view'\)/);
  assert.match(reports, /accountCode: row.code, from: range.from, to: range.to/);
  const coa = await readFile(new URL('../src/pages/ChartOfAccounts.tsx', import.meta.url), 'utf8');
  assert.match(coa, /a.canPost && hasPermission\('financials.journal.view'\)/);
  assert.match(coa, /accountCode: a.accountCode, from: '', to: ''/);
});
