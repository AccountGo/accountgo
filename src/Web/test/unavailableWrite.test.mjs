import test from 'node:test';
import assert from 'node:assert/strict';
import { unavailableWriteMessage } from '../src/lib/unavailableWrite.ts';

test('a bare 503, 502 or 504 says the change was not saved', () => {
  for (const status of [502, 503, 504]) {
    assert.match(unavailableWriteMessage(status, undefined), /was not saved/);
  }
});

test('a problem body message is kept as is', () => {
  assert.equal(unavailableWriteMessage(503, 'Service is updating.'), null);
});

test('other statuses are left to the caller', () => {
  assert.equal(unavailableWriteMessage(400, undefined), null);
  assert.equal(unavailableWriteMessage(500, ''), null);
});
