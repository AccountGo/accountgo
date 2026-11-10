import assert from 'node:assert/strict';
import { after, test } from 'node:test';
import { createServer } from 'vite';
import { createElement } from 'react';
import { renderToStaticMarkup } from 'react-dom/server';

const server = await createServer({ server: { middlewareMode: true, hmr: false, ws: false }, appType: 'custom' });
after(() => server.close());

const { AuthProvider } = await server.ssrLoadModule('/src/auth/AuthContext.tsx');
const { BooksCheckDialogView } = await server.ssrLoadModule('/src/components/BooksCheckDialog.tsx');
const { announceEntitlementDenial, isBooksNotReadyProblem, BOOKS_NOT_READY_EVENT, ENTITLEMENT_DENIED_EVENT } =
  await server.ssrLoadModule('/src/lib/entitlementEvents.ts');
const { rememberSignupPlan, takeSignupPlan, postSetupPath } = await server.ssrLoadModule('/src/lib/signupPlanIntent.ts');

const problem = {
  code: 'BOOKS_NOT_READY',
  detail: '1 issued document has no ledger entry yet.',
  unpostedInvoices: [{ type: 'invoice', id: 7, number: 'INV-0007', date: '2026-10-01T00:00:00' }],
  unpostedBills: [],
};

test('books-not-ready 409 is announced on its own event, without consuming the response', async () => {
  globalThis.window ??= new EventTarget();
  const books = [];
  const denials = [];
  const onBooks = (event) => books.push(event.detail);
  const onDenied = (event) => denials.push(event.detail);
  window.addEventListener(BOOKS_NOT_READY_EVENT, onBooks);
  window.addEventListener(ENTITLEMENT_DENIED_EVENT, onDenied);
  try {
    const response = new Response(JSON.stringify(problem), { status: 409, headers: { 'content-type': 'application/problem+json' } });
    await announceEntitlementDenial(response);
    await announceEntitlementDenial(new Response('{"code":"OTHER"}', { status: 409, headers: { 'content-type': 'application/json' } }));
    assert.equal(books.length, 1);
    assert.equal(books[0].unpostedInvoices[0].id, 7);
    assert.equal(denials.length, 0);
    assert.equal((await response.json()).code, 'BOOKS_NOT_READY');
  } finally {
    window.removeEventListener(BOOKS_NOT_READY_EVENT, onBooks);
    window.removeEventListener(ENTITLEMENT_DENIED_EVENT, onDenied);
  }
  assert.equal(isBooksNotReadyProblem({ code: 'BOOKS_NOT_READY' }), false);
});

test('books check dialog lists unposted documents and offers to post them', () => {
  globalThis.window ??= new EventTarget();
  const hadLocation = 'location' in globalThis.window;
  if (!hadLocation) globalThis.window.location = { pathname: '/org/acme/settings', search: '', href: 'https://app.example/org/acme/settings' };
  let html;
  try {
    html = renderToStaticMarkup(createElement(AuthProvider, null,
      createElement(BooksCheckDialogView, { problem, onClose: () => {} })));
  } finally {
    if (!hadLocation) delete globalThis.window.location;
  }
  assert.ok(html.includes('href="/org/acme/sales/7"'));
  assert.match(html, /role="dialog"[^>]*aria-modal="true"/);
  assert.ok(html.includes('Post your documents first'));
  assert.ok(html.includes('Invoice INV-0007'));
  assert.ok(html.includes('Post now'));
  assert.ok(html.includes('Not now'));
});

test('signup plan intent keeps only plain plan keys for a day and is read once', () => {
  const store = new Map();
  globalThis.window.localStorage = {
    getItem: (k) => (store.has(k) ? store.get(k) : null),
    setItem: (k, v) => store.set(k, String(v)),
    removeItem: (k) => store.delete(k),
  };
  try {
    const now = 1_800_000_000_000;
    assert.equal(rememberSignupPlan('?plan=Team_Plan', now), 'team_plan');
    assert.equal(takeSignupPlan(now + 1000), 'team_plan');
    assert.equal(takeSignupPlan(now + 1000), null);

    assert.equal(rememberSignupPlan('?plan=<script>', now), null);
    assert.equal(takeSignupPlan(now), null);

    rememberSignupPlan('?plan=team_plan', now);
    assert.equal(takeSignupPlan(now + 25 * 60 * 60 * 1000), null);
  } finally {
    delete globalThis.window.localStorage;
  }
  assert.equal(postSetupPath('acme co', 'team_plan'), '/org/acme%20co/settings?tab=subscription&upgrade=team_plan');
  assert.equal(postSetupPath('acme', null), '/org/acme/dashboard');
});

test('upgrade confirmation shows the exact charge before anything is paid', async () => {
  const { UpgradeConfirmDialogView } = { UpgradeConfirmDialogView: (await server.ssrLoadModule('/src/components/UpgradeConfirmDialog.tsx')).UpgradeConfirmDialog };
  const html = renderToStaticMarkup(createElement(UpgradeConfirmDialogView, {
    preview: { planName: 'Team Plan', amountDueNow: 79.43, currency: 'usd', recurringAmount: 99, interval: 'month', nextBillingDate: '2026-12-10T00:00:00Z', paymentMethod: 'Visa ending 4242' },
    busy: false, onConfirm: () => {}, onCancel: () => {},
  }));
  assert.match(html, /role="dialog"[^>]*aria-modal="true"/);
  assert.ok(html.includes('Upgrade to Team Plan?'));
  assert.ok(html.includes('79.43'));
  assert.ok(html.includes('99.00'));
  assert.ok(html.includes('Visa ending 4242'));
  assert.ok(html.includes('Confirm and pay'));
  assert.ok(html.includes('Cancel'));
});
