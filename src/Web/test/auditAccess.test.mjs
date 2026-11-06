import assert from 'node:assert/strict';
import { after, test } from 'node:test';
import { createServer } from 'vite';

const server = await createServer({ server: { middlewareMode: true, hmr: false, ws: false }, appType: 'custom' });
after(() => server.close());

const {
  auditAccessLevel,
  canShowRecordHistory,
  activityTabMode,
  recent90Notice,
  recent90NoticeText,
  activityPlanLimitProblem,
  canViewSecurityLog,
  canExportActivity,
} = await server.ssrLoadModule('/src/lib/auditAccess.ts');
const { planLimitCta } = await server.ssrLoadModule('/src/components/PlanLimitDialog.tsx');

const allowed = (permission) => permission === 'audit.logs.view';
const denied = () => false;
const snapshot = (features = [], plan = 'accounting', effectivePlan) => ({ plan, effectivePlan, features });

test('no audit.logs.view permission means no access at any level', () => {
  assert.equal(auditAccessLevel(snapshot(['audit.history.full', 'audit.activity']), denied), 'none');
  assert.equal(auditAccessLevel(snapshot([], 'community'), denied), 'none');
  assert.equal(auditAccessLevel(null, denied), 'none');
});

test('community edition is full, by plan or effective plan, case-insensitively', () => {
  assert.equal(auditAccessLevel(snapshot([], 'community'), allowed), 'full');
  assert.equal(auditAccessLevel(snapshot([], 'Community'), allowed), 'full');
  assert.equal(auditAccessLevel(snapshot([], 'accounting', 'COMMUNITY'), allowed), 'full');
});

test('audit.history.full is full, and wins over audit.activity', () => {
  assert.equal(auditAccessLevel(snapshot(['audit.history.full']), allowed), 'full');
  assert.equal(auditAccessLevel(snapshot(['audit.activity', 'audit.history.full']), allowed), 'full');
});

test('audit.activity alone is recent90', () => {
  assert.equal(auditAccessLevel(snapshot(['audit.activity']), allowed), 'recent90');
});

test('permission without either feature is singleRecord; no snapshot fails closed to singleRecord', () => {
  assert.equal(auditAccessLevel(snapshot(['sales.invoices']), allowed), 'singleRecord');
  assert.equal(auditAccessLevel(snapshot([]), allowed), 'singleRecord');
  assert.equal(auditAccessLevel(null, allowed), 'singleRecord');
});

test('plan keys other than community never change the level', () => {
  for (const plan of ['accounting', 'some-other-plan']) {
    assert.equal(auditAccessLevel(snapshot([], plan), allowed), 'singleRecord');
  }
});

test('History button shows for every level except none', () => {
  assert.equal(canShowRecordHistory('none'), false);
  for (const level of ['singleRecord', 'recent90', 'full']) assert.equal(canShowRecordHistory(level), true);
});

test('Activity tab: list for recent90 and full, in business and personal books', () => {
  for (const personal of [false, true]) {
    assert.equal(activityTabMode('recent90', personal), 'list');
    assert.equal(activityTabMode('full', personal), 'list');
  }
});

test('Activity tab: plan-limit card for singleRecord in business, hidden in personal books', () => {
  assert.equal(activityTabMode('singleRecord', false), 'planLimit');
  assert.equal(activityTabMode('singleRecord', true), 'hidden');
});

test('Activity tab: hidden without permission', () => {
  assert.equal(activityTabMode('none', false), 'hidden');
  assert.equal(activityTabMode('none', true), 'hidden');
});

test('90-day notice offers upgrade only to people who can manage billing', () => {
  const withBilling = recent90Notice('recent90', false, true);
  assert.equal(
    recent90NoticeText(withBilling),
    'Showing the last 90 days. Your plan keeps the full history; upgrade to see all of it.',
  );
  assert.equal(withBilling.linkLabel, 'upgrade');

  const withoutBilling = recent90Notice('recent90', false, false);
  assert.equal(recent90NoticeText(withoutBilling), 'Showing the last 90 days. Your plan keeps the full history.');
  assert.equal(withoutBilling.linkLabel, null);
  assert.ok(!/upgrade/i.test(recent90NoticeText(withoutBilling)));
});

test('90-day notice never shows in personal books, and only for recent90', () => {
  assert.equal(recent90Notice('recent90', true, true), null);
  assert.equal(recent90Notice('recent90', true, false), null);
  assert.equal(recent90Notice('full', false, true), null);
  assert.equal(recent90Notice('singleRecord', false, true), null);
});

test('security log and export are full only', () => {
  for (const level of ['none', 'singleRecord', 'recent90']) {
    assert.equal(canViewSecurityLog(level), false);
    assert.equal(canExportActivity(level), false);
  }
  assert.equal(canViewSecurityLog('full'), true);
  assert.equal(canExportActivity('full'), true);
});

test('plan-limit card uses the standard problem shape and CTA matrix', () => {
  const owner = activityPlanLimitProblem(true);
  assert.equal(owner.code, 'FEATURE_NOT_ENTITLED');
  assert.equal(owner.requiredFeature, 'audit.activity');
  assert.equal(planLimitCta(owner, '/org/acme/settings?tab=subscription').href, '/org/acme/settings?tab=subscription');

  const member = activityPlanLimitProblem(false);
  const cta = planLimitCta(member, '/org/acme/settings?tab=subscription');
  assert.equal(cta.href, null);
  assert.equal(cta.label, 'Ask your workspace owner');
});
