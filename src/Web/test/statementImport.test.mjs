import test from 'node:test';
import assert from 'node:assert/strict';
import {
  detectDelimiter,
  hasAmountMapping,
  importSummary,
  kindsForAmount,
  parseCsvRows,
  sourceForFileName,
  suggestColumnMap,
} from '../src/lib/statementImport.ts';

test('detectDelimiter picks the most common separator outside quotes', () => {
  assert.equal(detectDelimiter('Date,Amount,Payee\n1,2,3'), ',');
  assert.equal(detectDelimiter('Date;Amount;Payee'), ';');
  assert.equal(detectDelimiter('Date\tAmount\tPayee'), '\t');
  assert.equal(detectDelimiter('"A,B";C;D'), ';');
});

test('parseCsvRows handles quotes, doubled quotes, blank lines, and limits rows', () => {
  const rows = parseCsvRows('a,b\r\n"x, y","say ""hi"""\n\n3,4\n5,6', ',', 3);
  assert.deepEqual(rows, [
    ['a', 'b'],
    ['x, y', 'say "hi"'],
    ['3', '4'],
  ]);
});

test('suggestColumnMap finds common header names', () => {
  const map = suggestColumnMap(['Posted Date', 'Description', 'Amount', 'Memo', 'Check Number', 'FITID']);
  assert.equal(map.dateColumn, 0);
  assert.equal(map.amountColumn, 2);
  assert.equal(map.payeeColumn, 1);
  assert.equal(map.memoColumn, 3);
  assert.equal(map.referenceColumn, 4);
  assert.equal(map.externalIdColumn, 5);
  assert.ok(hasAmountMapping(map));
});

test('suggestColumnMap falls back to deposit/withdrawal columns', () => {
  const map = suggestColumnMap(['Date', 'Details', 'Deposits', 'Withdrawals']);
  assert.equal(map.amountColumn, null);
  assert.equal(map.depositColumn, 2);
  assert.equal(map.withdrawalColumn, 3);
  assert.ok(hasAmountMapping(map));
  assert.equal(hasAmountMapping(suggestColumnMap(['Date', 'Notes'])), false);
});

test('sourceForFileName maps extensions', () => {
  assert.equal(sourceForFileName('Statement.CSV'), 1);
  assert.equal(sourceForFileName('x.qfx'), 2);
  assert.equal(sourceForFileName('x.ofx'), 2);
  assert.equal(sourceForFileName('x.pdf'), null);
});

test('kindsForAmount offers deposit kinds for positive and payment kinds for negative', () => {
  assert.deepEqual(kindsForAmount(10).map((k) => k.value), [1, 5, 6]);
  assert.deepEqual(kindsForAmount(-10).map((k) => k.value), [2, 4, 6]);
  assert.equal(kindsForAmount(-10).find((k) => k.value === 4)?.needsContra, false);
});

test('importSummary describes the result', () => {
  assert.equal(importSummary({ imported: 1, duplicates: 0, errors: 0, autoMatched: 0 }), 'Imported 1 line.');
  assert.equal(
    importSummary({ imported: 3, duplicates: 2, errors: 1, autoMatched: 2 }),
    'Imported 3 lines, 2 matched automatically, 2 already imported, 1 skipped with errors.',
  );
});
