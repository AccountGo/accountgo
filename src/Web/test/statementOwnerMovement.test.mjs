import test from 'node:test';
import assert from 'node:assert/strict';
import { ownerActionForLines, ownerSuggestionLabel } from '../src/lib/statementOwnerMovement.ts';

const drawing = { accountId: 1, accountCode: '30800', accountName: 'Drawings', label: 'Owner drawing' };
const contribution = { accountId: 2, accountCode: '30100', accountName: 'Capital', label: 'Owner contribution' };
const actions = { drawing, contribution };
const line = (amount, extra = {}) => ({ amount, postedDate: '2026-06-15', isMatched: false, isExcluded: false, ...extra });
const show = (lines, available = actions, manage = true, create = true) => ownerActionForLines(lines, available, manage, create, '2026-01-01');

test('each direction offers the current account and entity label', () => {
  assert.deepEqual(show([line(-10)]), drawing);
  assert.deepEqual(show([line(10)]), contribution);
  assert.equal(show([line(0)]), null);
});
test('missing availability or either permission hides actions', () => {
  assert.equal(show([line(-10)], null), null);
  assert.equal(show([line(-10)], { drawing: null, contribution }), null);
  assert.equal(show([line(10)], { drawing, contribution: null }), null);
  assert.equal(show([line(-10)], actions, false, true), null);
  assert.equal(show([line(-10)], actions, true, false), null);
});
test('matched, excluded and before books start lines have no owner action', () => {
  for (const extra of [{ isMatched: true }, { isExcluded: true }, { postedDate: '2025-12-31' }])
    assert.equal(show([line(-10, extra)]), null);
});
test('bulk allows one direction only and refuses any ineligible line', () => {
  assert.deepEqual(show([line(-10), line(-20)]), drawing);
  assert.deepEqual(show([line(10), line(20)]), contribution);
  assert.equal(show([line(-10), line(20)]), null);
  assert.equal(show([]), null);
  assert.equal(show([line(-10), line(-20, { isMatched: true })]), null);
});
test('suggestion chip uses the available label only for the matching direction', () => {
  assert.equal(ownerSuggestionLabel(line(-10, { suggestedOwnerAction: 'drawing' }), drawing), 'Suggested: Owner drawing');
  assert.equal(ownerSuggestionLabel(line(10, { suggestedOwnerAction: 'drawing' }), contribution), null);
  assert.equal(ownerSuggestionLabel(line(-10, { suggestedOwnerAction: 'drawing' }), null), null);
  assert.equal(ownerSuggestionLabel(line(-10), drawing), null);
});
