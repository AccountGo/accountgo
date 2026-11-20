import assert from 'node:assert/strict';
import test from 'node:test';
import { shortRef } from '../src/lib/shortRef.ts';

test('shortRef keeps the prefix and the last six characters of a long public id', () => {
  assert.equal(shortRef('gl_01m7wv1z03nxcvpd6w02tmsw3h'), 'gl_…msw3h'.replace('msw3h', 'tmsw3h'));
  assert.equal(shortRef('bill_01m7wv1a2zec243m3grx6hgnaj'), 'bill_…6hgnaj');
});

test('shortRef leaves short ids, blanks and numbers alone', () => {
  assert.equal(shortRef('inv_10'), 'inv_10');
  assert.equal(shortRef(null), '');
  assert.equal(shortRef(undefined), '');
  assert.equal(shortRef(7), '7');
});
