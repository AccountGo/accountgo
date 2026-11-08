import assert from 'node:assert/strict';
import { after, test } from 'node:test';
import { createServer } from 'vite';
import { createElement } from 'react';
import { renderToStaticMarkup } from 'react-dom/server';
import { MemoryRouter, Outlet, Route, Routes } from 'react-router-dom';

const server = await createServer({ server: { middlewareMode: true, hmr: false, ws: false }, appType: 'custom' });
after(() => server.close());

const { BalanceCheckDialog } = await server.ssrLoadModule('/src/components/BalanceCheckDialog.tsx');
const { StatementLineOtherIncomeDialog } = await server.ssrLoadModule('/src/components/StatementLineOtherIncomeDialog.tsx');
const { StatementLineExpenseDialog } = await server.ssrLoadModule('/src/components/StatementLineExpenseDialog.tsx');
const { ThemeProvider } = await server.ssrLoadModule('/src/themes/ThemeContext.tsx');
const { AuthProvider } = await server.ssrLoadModule('/src/auth/AuthContext.tsx');
const { CapabilitiesProvider } = await server.ssrLoadModule('/src/lib/entitlements.ts');

const bank = {
  id: 10,
  name: 'Operating Account',
  type: 1,
  currency: 'USD',
  currentBalance: 5000,
  archived: false,
};

const fakeLine = {
  id: 101,
  bankId: 10,
  postedDate: '2026-07-15T00:00:00Z',
  amount: 250.00,
  payee: 'Acme Client Corp',
  memo: 'Consulting services payment',
  source: 1,
  externalId: 'stmt-line-101',
};

const fakeExpenseLine = {
  id: 102,
  bankId: 10,
  postedDate: '2026-07-16T00:00:00Z',
  amount: -85.50,
  payee: 'Coffee Shop & Supplies',
  memo: 'Office refreshments',
  source: 1,
  externalId: 'stmt-line-102',
};

const tenantContext = {
  currentOrg: { slug: 'pro-client', name: 'Freelancer Studio', currency: 'USD' },
  hasPermission: () => true,
  hasModule: (mod) => mod === 'purchases' || mod === 'sales',
};

test('BalanceCheckDialog pre-fills closing balance and date from statement import handoff', () => {
  const html = renderToStaticMarkup(
    createElement(AuthProvider, null,
      createElement(ThemeProvider, null,
        createElement(BalanceCheckDialog, {
          bank,
          formatCurrency: (n) => `$${n.toFixed(2)}`,
          onClose: () => {},
          initialBalance: 4250.75,
          initialAsOfDate: '2026-07-31T00:00:00Z',
        })
      )
    )
  );

  // Prefilled amount and date are present in input values
  assert.match(html, /value="4250\.75"/);
  assert.match(html, /value="2026-07-31"/);
  assert.match(html, /Check balance/);

  // DOM strictly contains no 5-digit account codes (e.g. 10100, 40100)
  assert.doesNotMatch(html, /\b\d{5}\b/);
});

test('StatementLineOtherIncomeDialog renders without account codes', () => {
  const html = renderToStaticMarkup(
    createElement(AuthProvider, null,
      createElement(ThemeProvider, null,
        createElement(StatementLineOtherIncomeDialog, {
          line: fakeLine,
          formatCurrency: (n) => `$${n.toFixed(2)}`,
          onSaved: () => {},
          onClose: () => {},
        })
      )
    )
  );

  assert.match(html, /Record other income/);
  assert.match(html, /Acme Client Corp/);
  assert.match(html, /Loading…/);
  assert.match(html, /Memo/);

  // DOM strictly contains no 5-digit account codes (e.g. 40100, 10100)
  assert.doesNotMatch(html, /\b\d{5}\b/);
});

test('StatementLineExpenseDialog renders without account codes on freelancer/pro surface', () => {
  const html = renderToStaticMarkup(
    createElement(AuthProvider, null,
      createElement(ThemeProvider, null,
        createElement(CapabilitiesProvider, {
          orgSlug: 'pro-client',
          bootstrapModules: ['purchases', 'sales'],
          hasPermission: () => true,
        },
          createElement(MemoryRouter, { initialEntries: ['/banking'] },
            createElement(Routes, null,
              createElement(Route, {
                path: '/banking',
                element: createElement(Outlet, { context: tenantContext }),
              },
                createElement(Route, {
                  index: true,
                  element: createElement(StatementLineExpenseDialog, {
                    line: fakeExpenseLine,
                    formatCurrency: (n) => `$${n.toFixed(2)}`,
                    onSaved: () => {},
                    onClose: () => {},
                  }),
                })
              )
            )
          )
        )
      )
    )
  );

  assert.match(html, /Coffee Shop/);
  assert.match(html, /Record expense/);

  // DOM strictly contains no 5-digit account codes
  assert.doesNotMatch(html, /\b\d{5}\b/);
});
