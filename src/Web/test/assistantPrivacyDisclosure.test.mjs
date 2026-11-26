// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { test } from 'node:test';

const source = fs.readFileSync(path.resolve(import.meta.dirname, '../src/assistant/AssistantSidepanel.tsx'), 'utf8');

test('chat disclosure separates pseudonymized book data from attachment text', () => {
  assert.match(source, /Names from your books are hidden from the AI\./);
  assert.match(source, /Attachment text is sent with only account numbers masked\./);
  assert.match(source, /masking is limited: other names, email ' \+\s*'addresses and phone numbers in it are sent as they appear/);
  assert.match(source, /Text you type is sent as written\./);
});

test('chat disclosure no longer claims all names and personal details are hidden', () => {
  assert.doesNotMatch(source, /Names and personal details are hidden from the AI\./);
  assert.doesNotMatch(source, /Workspace names and personal details are hidden from the AI/);
});

test('banner and empty state both use the shared disclosure', () => {
  assert.equal(source.match(/\{ASSISTANT_PRIVACY_DETAIL\}/g)?.length, 2);
  assert.equal(source.match(/\{ASSISTANT_PRIVACY_SUMMARY\}/g)?.length, 1);
});
