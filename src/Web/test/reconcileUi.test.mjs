import test from 'node:test';
import assert from 'node:assert/strict';
import {
  TickSaveController,
  applyPendingTicksToRecon,
  canFinishReconciliation,
  filterVisibleLines,
  lineMatchesSearch,
  mergeBatchIntoPending,
  mergeTickIntoPending,
  nextFocusIndex,
  reconcileLineTypeLabel,
  tickTogglePayload,
} from '../src/lib/reconcileUi.ts';

function makeRecon(version, tickedIds = []) {
  const ticked = new Set(tickedIds);
  return {
    id: 1,
    version,
    status: 0,
    deposits: [
      { lineId: 1, ticked: ticked.has(1) },
      { lineId: 2, ticked: ticked.has(2) },
    ],
    payments: [{ lineId: 3, ticked: ticked.has(3) }],
  };
}

test('nextFocusIndex wraps at both ends', () => {
  assert.equal(nextFocusIndex(5, 4, 1), 0);
  assert.equal(nextFocusIndex(5, 0, -1), 4);
  assert.equal(nextFocusIndex(5, 2, 1), 3);
});

test('tickTogglePayload adds or removes a single line', () => {
  assert.deepEqual(tickTogglePayload(new Set([1, 2]), 3), { add: [3], remove: [] });
  assert.deepEqual(tickTogglePayload(new Set([1, 2]), 2), { add: [], remove: [2] });
});

test('lineMatchesSearch matches amount and text', () => {
  const line = {
    lineId: 1,
    in: 12.5,
    out: 0,
    ticked: false,
    type: 'SalesReceipt',
    reference: 'INV-9',
    payee: 'Acme',
  };
  assert.equal(lineMatchesSearch(line, '12.50'), true);
  assert.equal(lineMatchesSearch(line, 'acme'), true);
  assert.equal(lineMatchesSearch(line, 'inv-9'), true);
  assert.equal(lineMatchesSearch(line, 'receipt'), true);
  assert.equal(lineMatchesSearch(line, 'zzzz'), false);
});

test('reconcileLineTypeLabel maps enum names to friendly labels', () => {
  assert.equal(reconcileLineTypeLabel('SalesReceipt'), 'Receipt');
  assert.equal(reconcileLineTypeLabel('PurchaseInvoicePayment'), 'Bill payment');
  assert.equal(reconcileLineTypeLabel('Opening balance (as of 2026-01-01)'), 'Opening balance (as of 2026-01-01)');
});

test('filterVisibleLines hides ticked when requested', () => {
  const lines = [
    { lineId: 1, in: 10, out: 0, ticked: true, type: 'A' },
    { lineId: 2, in: 0, out: 5, ticked: false, type: 'B' },
  ];
  assert.deepEqual(
    filterVisibleLines(lines, '', true).map((l) => l.lineId),
    [2],
  );
});

test('canFinishReconciliation requires an exact zero difference', () => {
  assert.equal(canFinishReconciliation(0), true);
  assert.equal(canFinishReconciliation(0.0), true);
  assert.equal(canFinishReconciliation(0.01), false);
  assert.equal(canFinishReconciliation(-0.01), false);
});

test('keyboard flow: arrow then space targets the next unticked line id', () => {
  const lines = [
    { lineId: 10, in: 1, out: 0, ticked: false, type: 'Deposit' },
    { lineId: 11, in: 2, out: 0, ticked: false, type: 'Deposit' },
    { lineId: 12, in: 0, out: 3, ticked: true, type: 'Payment' },
  ];
  const visible = filterVisibleLines(lines, '', true);
  assert.deepEqual(
    visible.map((l) => l.lineId),
    [10, 11],
  );
  const focused = nextFocusIndex(visible.length, 0, 1);
  assert.equal(visible[focused].lineId, 11);
  assert.deepEqual(tickTogglePayload(new Set(), visible[focused].lineId), { add: [11], remove: [] });
});

test('applyPendingTicksToRecon overlays still-pending ticks on a server response', () => {
  const pending = { add: new Set([2]), remove: new Set([1]) };
  const overlay = applyPendingTicksToRecon(makeRecon(5, [1]), pending);
  assert.equal(overlay.version, 5);
  assert.equal(overlay.deposits.find((l) => l.lineId === 1).ticked, false);
  assert.equal(overlay.deposits.find((l) => l.lineId === 2).ticked, true);
});

test('two saves back to back serialize and keep the second batch', async () => {
  let recon = makeRecon(1);
  const puts = [];
  let releaseFirst;
  const firstGate = new Promise((resolve) => {
    releaseFirst = resolve;
  });

  const saver = new TickSaveController(
    () => recon,
    () => true,
    async (id, add, remove, version) => {
      const entry = { id, add: [...add], remove: [...remove], version };
      puts.push(entry);
      if (puts.length === 1) await firstGate;
      recon = makeRecon(version + 1, [...new Set([...add])]);
      // Simulate server ticked set: previous server ticks plus adds minus removes is out of scope;
      // for this test each put returns the new version with the add lines ticked.
      const ticked = new Set(add);
      recon = makeRecon(version + 1, [...ticked]);
      return { ok: true, recon };
    },
    async () => recon,
    (updated) => {
      recon = updated;
    },
  );

  mergeTickIntoPending(saver.pending, new Set(), 1);
  const firstFlush = saver.flush();

  // Second toggle while first PUT is in flight — must not share version 1.
  mergeTickIntoPending(saver.pending, new Set([1]), 2);
  const secondFlush = saver.flush();

  releaseFirst();
  const [fromFirst, fromSecond] = await Promise.all([firstFlush, secondFlush]);

  assert.equal(puts.length, 2);
  assert.deepEqual(puts[0], { id: 1, add: [1], remove: [], version: 1 });
  assert.deepEqual(puts[1], { id: 1, add: [2], remove: [], version: 2 });
  assert.equal(fromFirst.version, 3);
  assert.equal(fromSecond.version, 3);
  assert.equal(recon.deposits.find((l) => l.lineId === 2).ticked, true);
});

test('409 from outdated version reloads, re-applies pending, and retries once', async () => {
  let recon = makeRecon(1);
  const puts = [];
  let conflicts = 1;

  const saver = new TickSaveController(
    () => recon,
    () => true,
    async (id, add, remove, version) => {
      puts.push({ add: [...add], remove: [...remove], version });
      if (conflicts > 0) {
        conflicts -= 1;
        return { ok: false, conflict: true };
      }
      recon = makeRecon(version + 1, add);
      return { ok: true, recon };
    },
    async () => {
      recon = makeRecon(7);
      return recon;
    },
    (updated) => {
      recon = updated;
    },
  );

  mergeBatchIntoPending(saver.pending, { add: [1], remove: [] });
  // A newer local tick arrives while we are about to conflict.
  mergeBatchIntoPending(saver.pending, { add: [2], remove: [] });

  const result = await saver.flush();
  assert.equal(puts.length, 2);
  assert.equal(puts[0].version, 1);
  assert.equal(puts[1].version, 7);
  assert.deepEqual(puts[1].add.sort(), [1, 2]);
  assert.equal(result.version, 8);
});
