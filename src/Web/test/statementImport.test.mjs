import test from 'node:test';
import assert from 'node:assert/strict';
import {
  clampStatementLinePage,
  decodeStatementBytes,
  detectDelimiter,
  hasAmountMapping,
  importSummary,
  kindsForAmount,
  matchVendorIdByPayee,
  normalizePayeeName,
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

test('detectDelimiter skips leading blank lines before probing', () => {
  assert.equal(detectDelimiter('\n\nDate\tAmount\tPayee\n1\t2\t3'), '\t');
  assert.equal(detectDelimiter('\r\n\r\nDate;Amount;Payee'), ';');
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
  assert.equal(sourceForFileName('stmt.camt053.xml'), 4);
  assert.equal(sourceForFileName('export.mt940'), 5);
  assert.equal(sourceForFileName('bank.qif'), 6);
  assert.equal(sourceForFileName('export.xlsx'), 7);
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

test('clampStatementLinePage steps back when the last page is emptied', () => {
  // Matched the only line on page 2 of unmatched (was 101 total → now 100).
  assert.equal(clampStatementLinePage(2, 100, 100), 1);
  assert.equal(clampStatementLinePage(3, 201, 100), 3);
  assert.equal(clampStatementLinePage(3, 200, 100), 2);
  assert.equal(clampStatementLinePage(5, 0, 100), 1);
  assert.equal(clampStatementLinePage(0, 50, 100), 1);
});

test('matchVendorIdByPayee matches normalized names and ignores blanks', () => {
  assert.equal(normalizePayeeName('  Figma  Inc '), 'figma inc');
  const vendors = [
    { id: 1, name: 'Acme' },
    { id: 2, name: 'Figma Inc' },
  ];
  assert.equal(matchVendorIdByPayee('figma  inc', vendors), 2);
  assert.equal(matchVendorIdByPayee('Unknown', vendors), null);
  assert.equal(matchVendorIdByPayee('  ', vendors), null);
});

test('suggestColumnMap maps Wise-style headers', () => {
  const map = suggestColumnMap([
    'transfer_id',
    'activity_id',
    'created_on',
    'finished_on',
    'status',
    'direction',
    'amount',
    'currency',
    'source_name',
    'target_name',
  ]);
  assert.equal(map.dateColumn, 2);
  assert.equal(map.externalIdColumn, 1);
  assert.equal(map.statusColumn, 4);
  assert.equal(map.directionColumn, 5);
  assert.equal(map.amountColumn, 6);
  assert.equal(map.currencyColumn, 7);
});

test('decodeStatementBytes strips UTF-8 BOM and reads UTF-16 LE', () => {
  const bom = new Uint8Array([0xef, 0xbb, 0xbf, 0x6f, 0x6b]);
  assert.equal(decodeStatementBytes(bom), 'ok');
  const utf16 = new Uint8Array([0xff, 0xfe, 0x41, 0x00, 0xf1, 0x00]);
  assert.equal(decodeStatementBytes(utf16), 'Añ');
});
