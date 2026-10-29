import test from 'node:test';
import assert from 'node:assert/strict';
import { canReviewReconciliation, reviewStatusText } from '../src/lib/reconciliationReview.ts';

const FINISHED = 2;
const DRAFT = 1;

test('preparer cannot sign off their own reconciliation', () => {
  const item = { status: FINISHED, finishedBy: 'Ann@Example.com', reviewedAt: null };
  assert.equal(canReviewReconciliation(item, 'ann@example.com', true), false);
  assert.equal(canReviewReconciliation(item, 'bob@example.com', true), true);
});

test('only finished, unreviewed reconciliations can be signed off', () => {
  assert.equal(canReviewReconciliation({ status: DRAFT, finishedBy: null, reviewedAt: null }, 'bob', true), false);
  assert.equal(canReviewReconciliation({ status: FINISHED, finishedBy: 'ann', reviewedAt: '2026-10-30T00:00:00Z' }, 'bob', true), false);
});

test('sign-off needs the reconcile permission', () => {
  assert.equal(canReviewReconciliation({ status: FINISHED, finishedBy: 'ann', reviewedAt: null }, 'bob', false), false);
});

test('reviewStatusText shows date and reviewer', () => {
  assert.equal(reviewStatusText({ reviewedAt: null }), 'Not reviewed');
  assert.equal(reviewStatusText({ reviewedAt: '2026-10-30T10:00:00Z', reviewedBy: 'bob' }), '2026-10-30 · bob');
});
