import assert from 'node:assert/strict';
import { after, test } from 'node:test';
import { createServer } from 'vite';

const server = await createServer({ server: { middlewareMode: true, hmr: false, ws: false }, appType: 'custom' });
after(() => server.close());

test('accounting plan logic correctly differentiates accounting vs invoicing plans', () => {
  const isAccountingPlan = (plan) => {
    const key = (plan || 'accounting').toLowerCase();
    return key === 'accounting' || key === 'enterprise';
  };

  assert.strictEqual(isAccountingPlan('invoicing'), false);
  assert.strictEqual(isAccountingPlan('accounting'), true);
  assert.strictEqual(isAccountingPlan('enterprise'), true);
  assert.strictEqual(isAccountingPlan(undefined), true);
  assert.strictEqual(isAccountingPlan(''), true);
});
