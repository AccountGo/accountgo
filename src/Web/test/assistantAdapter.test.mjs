import assert from 'node:assert/strict';
import { test } from 'node:test';
import {
  buildRunStreamBody,
  mergeAssistantContextVariables,
} from '../src/assistant/contextVariables.ts';

test('mergeAssistantContextVariables fills page/tenant fields without overriding payload', () => {
  const merged = mergeAssistantContextVariables(
    {
      companySlug: 'acme',
      pageRoute: '/org/acme/dashboard',
      pageTitle: 'Dashboard',
      companyId: '7',
      organizationId: '7',
    },
    { pageTitle: 'Custom' },
  );

  assert.equal(merged.companySlug, 'acme');
  assert.equal(merged.pageRoute, '/org/acme/dashboard');
  assert.equal(merged.pageTitle, 'Custom');
  assert.equal(merged.companyId, '7');
  assert.equal(merged.organizationId, '7');
});

test('buildRunStreamBody mirrors sessionId into contextVariables', () => {
  const body = buildRunStreamBody(
    'What is a trial balance?',
    'session_1',
    mergeAssistantContextVariables({ companySlug: 'acme' }, {}),
    '7',
  );

  assert.equal(body.message, 'What is a trial balance?');
  assert.equal(body.sessionId, 'session_1');
  assert.equal(body.organizationId, '7');
  assert.equal(body.contextVariables.sessionId, 'session_1');
  assert.equal(body.contextVariables.companySlug, 'acme');
});
