import assert from 'node:assert/strict';
import { after, test } from 'node:test';
import { createServer } from 'vite';

const server = await createServer({ server: { middlewareMode: true, hmr: false, ws: false }, appType: 'custom' });
after(() => server.close());

const { shouldShowUpgradeAvailableBadge } = await server.ssrLoadModule('/src/components/Gating.tsx');

test('Upgrade Available badge: no transitions → no badge; one transition → badge', () => {
  assert.equal(shouldShowUpgradeAvailableBadge(null), false);
  assert.equal(shouldShowUpgradeAvailableBadge({}), false);
  assert.equal(shouldShowUpgradeAvailableBadge({ allowedTransitions: [] }), false);
  assert.equal(shouldShowUpgradeAvailableBadge({ allowedTransitions: ['plan_b'] }), true);
});
