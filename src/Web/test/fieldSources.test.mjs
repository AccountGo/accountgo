import assert from 'node:assert/strict';
import test from 'node:test';
import { describeCaptureWarning, isAiAssistedField, parseFieldSources } from '../src/components/document-capture/fields.ts';

test('a capture without fieldSources parses as an empty map and nothing is AI-assisted', () => {
  assert.deepEqual(parseFieldSources(undefined), {});
  assert.deepEqual(parseFieldSources(null), {});
  assert.deepEqual(parseFieldSources('ai'), {});
  assert.deepEqual(parseFieldSources(['vendorName']), {});
  assert.equal(isAiAssistedField(undefined, 'vendorName'), false);
  assert.equal(isAiAssistedField(null, 'vendorName'), false);
});

test('known paths with rules or ai values are kept and everything else is ignored', () => {
  const parsed = parseFieldSources({
    vendorName: 'ai',
    'sourceTotals.grandTotal': 'ai',
    'lines[2].description': 'rules',
    'paymentHints.paymentStatus': 'ai',
    iban: 'ai',
    'lines[200].description': 'ai',
    date: 'human',
    dueDate: 7,
  });
  assert.deepEqual(parsed, {
    vendorName: 'ai',
    'sourceTotals.grandTotal': 'ai',
    'lines[2].description': 'rules',
    'paymentHints.paymentStatus': 'ai',
  });
  assert.equal(isAiAssistedField(parsed, 'vendorName'), true);
  assert.equal(isAiAssistedField(parsed, 'lines[2].description'), false);
  assert.equal(isAiAssistedField(parsed, 'date'), false);
});

test('the mapper warnings have their own user-facing text', () => {
  const generic = describeCaptureWarning('NOT_A_WARNING');
  for (const code of ['AI_VALUE_NOT_IN_SOURCE', 'AI_TOTALS_INCONSISTENT', 'AI_DISAGREES_WITH_RULES', 'AI_UNAVAILABLE']) {
    const text = describeCaptureWarning(code);
    assert.notEqual(text, generic);
    assert.ok(text.length > 20);
  }
});
