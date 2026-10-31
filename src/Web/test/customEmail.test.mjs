import test from 'node:test';
import assert from 'node:assert/strict';
import { pendingEmailKey, prepareCustomEmailAttempt, readPendingEmail } from '../src/lib/customEmail.ts';

const memory = () => {
  const data = new Map();
  return { getItem: key => data.get(key) ?? null, setItem: (key, value) => data.set(key, value), removeItem: key => data.delete(key) };
};

test('an unresolved custom send retains its dispatch ID after the dialog or page reopens', () => {
  const storage = memory();
  const key = pendingEmailKey('one', 42);
  const intent = { senderId: 'sender-1', followUp: false, attachPdf: true, additionalRecipients: [], cc: [] };
  const original = prepareCustomEmailAttempt(storage, key, intent, () => 'dispatch-1');
  const retry = prepareCustomEmailAttempt(storage, key, intent, () => 'dispatch-2');
  assert.equal(original.dispatchId, 'dispatch-1');
  assert.equal(retry.dispatchId, 'dispatch-1');
  assert.equal(readPendingEmail(storage, pendingEmailKey('two', 42)), null);
});

test('an ambiguous send cannot change sender or recipients under a new retry attempt', () => {
  const storage = memory();
  const key = pendingEmailKey('one', 42);
  const intent = { senderId: 'sender-1', followUp: false, attachPdf: false, additionalRecipients: [], cc: [] };
  prepareCustomEmailAttempt(storage, key, intent, () => 'dispatch-1');
  assert.throws(() => prepareCustomEmailAttempt(storage, key, { ...intent, senderId: 'sender-2' }, () => 'dispatch-2'), /unresolved/);
  assert.throws(() => prepareCustomEmailAttempt(storage, key, { ...intent, cc: ['other@example.com'] }, () => 'dispatch-2'), /unresolved/);
});

test('a successful send clears its pending intent so an intentional resend gets a new ID', () => {
  const storage = memory();
  const key = pendingEmailKey('one', 42);
  const intent = { senderId: 'sender-1', followUp: false, attachPdf: false, additionalRecipients: [], cc: [] };
  prepareCustomEmailAttempt(storage, key, intent, () => 'dispatch-1');
  storage.removeItem(key);
  assert.equal(prepareCustomEmailAttempt(storage, key, intent, () => 'dispatch-2').dispatchId, 'dispatch-2');
});
