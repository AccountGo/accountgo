import * as ownerMovement from '../src/lib/statementOwnerMovement.ts';
import test from 'node:test';
import assert from 'node:assert/strict';
import { ownerActionForLines, ownerSuggestionLabel, ownerButtonSet } from '../src/lib/statementOwnerMovement.ts';

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
  assert.equal(ownerSuggestionLabel(line(-10, { suggestedOwnerAction: 'drawing', suggestedPartnerName: 'Avery' }), { ...drawing, label: 'Partner drawing' }), 'Suggested: Partner drawing – Avery');
  assert.equal(ownerSuggestionLabel(line(-10, { suggestedOwnerAction: 'drawing' }), { ...drawing, label: 'Partner drawing' }), 'Suggested: Partner drawing');
});

test('partner actions offer a drawing action for money-out lines', () => {
  const partnerDrawing = { accountId: 21, accountCode: '30851', accountName: 'Drawings – Avery', label: 'Partner drawing' };
  const partnerActions = {
    drawing: null,
    contribution: null,
    partners: [{ partnerId: 1, name: 'Avery', drawing: partnerDrawing, contribution: null }],
  };
  assert.deepEqual(show([line(-10)], partnerActions), partnerDrawing);
  assert.equal(show([line(10)], partnerActions), null);
});

test('linked Personal books take precedence when the user can create linked entries', () => {
  assert.equal(ownerButtonSet({ linkedPersonalCompanyId: 2, canCreateLinkedEntry: true, ownerActionsAvailable: true }), 'linked');
  assert.equal(ownerButtonSet({ linkedPersonalCompanyId: 2, canCreateLinkedEntry: true, ownerActionsAvailable: false }), 'linked');
});
test('partner mode uses owner movements even when linked Personal books are available', () => {
  assert.equal(ownerButtonSet({ linkedPersonalCompanyId: 2, canCreateLinkedEntry: true, ownerActionsAvailable: true, partnerMode: true }), 'owner');
});
test('linked workspaces fall back to owner actions without journal permissions', () => {
  assert.equal(ownerButtonSet({ linkedPersonalCompanyId: 2, canCreateLinkedEntry: false, ownerActionsAvailable: true }), 'owner');
});
test('workspaces without linked Personal books use only owner actions', () => {
  for (const linkedPersonalCompanyId of [null, undefined])
    assert.equal(ownerButtonSet({ linkedPersonalCompanyId, canCreateLinkedEntry: true, ownerActionsAvailable: true }), 'owner');
});
test('no available action set renders nothing', () => {
  assert.equal(ownerButtonSet({ linkedPersonalCompanyId: null, canCreateLinkedEntry: true, ownerActionsAvailable: false }), 'none');
  assert.equal(ownerButtonSet({ linkedPersonalCompanyId: 2, canCreateLinkedEntry: false, ownerActionsAvailable: false }), 'none');
});

test('matched owner lines offer Undo instead of Unmatch only with both permissions and the owner button set', () => {
  const matched = line(-10, { isMatched: true, matchedOwnerAction: 'drawing' });
  assert.equal(ownerMovement.matchedLineAction(matched, true, true, 'owner'), 'undo');
  assert.equal(ownerMovement.matchedLineAction({ ...matched, matchedOwnerAction: 'contribution' }, true, true, 'owner'), 'undo');
  assert.equal(ownerMovement.matchedLineAction(matched, true, false, 'owner'), 'unmatch');
  assert.equal(ownerMovement.matchedLineAction(matched, false, true, 'owner'), null);
  assert.equal(ownerMovement.matchedLineAction(matched, true, true, 'linked'), 'unmatch');
  assert.equal(ownerMovement.matchedLineAction({ ...matched, matchedOwnerAction: null }, true, true, 'owner'), 'unmatch');
  assert.equal(ownerMovement.matchedLineAction({ ...matched, isMatched: false }, true, true, 'owner'), null);
  assert.equal(ownerMovement.matchedLineAction({ ...matched, isExcluded: true }, true, true, 'owner'), null);
});
