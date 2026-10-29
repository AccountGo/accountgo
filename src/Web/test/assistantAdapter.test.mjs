import assert from 'node:assert/strict';
import { test } from 'node:test';
import {
  buildRunStreamBody,
  mergeAssistantContextVariables,
} from '../src/assistant/contextVariables.ts';

test('mergeAssistantContextVariables prefers page route/title and drops client tenant keys', () => {
  const merged = mergeAssistantContextVariables(
    {
      companySlug: 'acme',
      pageRoute: '/org/acme/dashboard',
      pageTitle: 'Dashboard',
      companyId: '7',
      organizationId: '7',
    },
    {
      pageTitle: 'Stale',
      companySlug: 'victim',
      companyId: '999',
      organizationId: '999',
      userId: 'forged',
      companyPlan: 'enterprise',
      activeModules: 'financials,reports',
      companyRole: 'Owner',
      subscriptionStatus: 'active',
      hint: 'keep-me',
    },
  );

  assert.equal(merged.pageRoute, '/org/acme/dashboard');
  assert.equal(merged.pageTitle, 'Dashboard');
  assert.equal(merged.hint, 'keep-me');
  assert.equal(merged.companySlug, undefined);
  assert.equal(merged.companyId, undefined);
  assert.equal(merged.organizationId, undefined);
  assert.equal(merged.userId, undefined);
  assert.equal(merged.companyPlan, undefined);
  assert.equal(merged.activeModules, undefined);
  assert.equal(merged.companyRole, undefined);
  assert.equal(merged.subscriptionStatus, undefined);
});

test('buildRunStreamBody mirrors sessionId into contextVariables without organizationId', () => {
  const body = buildRunStreamBody(
    'What is a trial balance?',
    'session_1',
    mergeAssistantContextVariables({ pageRoute: '/org/acme/dashboard' }, {}),
  );

  assert.equal(body.message, 'What is a trial balance?');
  assert.equal(body.sessionId, 'session_1');
  assert.equal(body.organizationId, undefined);
  assert.equal(body.contextVariables.sessionId, 'session_1');
  assert.equal(body.contextVariables.pageRoute, '/org/acme/dashboard');
});
