// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

import assert from 'node:assert/strict';
import { test } from 'node:test';
import {
  MAX_REIDENTIFY_TOKENS_PER_REQUEST,
  SessionReidentifier,
  StreamingTextReidentifier,
  createReidentifyingSseStream,
  extractPseudonymTokens,
  reidentifyMessageContents,
  replacePseudonymTokens,
} from '../src/assistant/reidentify.ts';

test('extractPseudonymTokens finds 8-hex and 12-hex tokens for C/V/P/B roles only', () => {
  const text =
    'Customer C-7F3A2B10 paid V-19B2C4D8 via B-21C09F44A1B2 (contact P-00112233). ' +
    'Repeat C-7F3A2B10. Ignore X-7F3A2B10, c-7f3a2b10, C-1234567, ABC-7F3A2B10, inv_01234567.';
  assert.deepEqual(extractPseudonymTokens(text), [
    'C-7F3A2B10',
    'V-19B2C4D8',
    'B-21C09F44A1B2',
    'P-00112233',
  ]);
});

test('replacePseudonymTokens swaps known tokens and leaves unknown tokens and numbers untouched', () => {
  const input = 'C-7F3A2B10 owes $1,250.00 on INV-0042; unknown V-DEADBEEF stays opaque.';
  const output = replacePseudonymTokens(input, { 'C-7F3A2B10': 'Northwind Retail LLC' });
  assert.equal(
    output,
    'Northwind Retail LLC owes $1,250.00 on INV-0042; unknown V-DEADBEEF stays opaque.',
  );
});

test('StreamingTextReidentifier handles tokens split across arbitrary chunk boundaries', async () => {
  const calls = [];
  const names = {
    'C-7F3A2B10': 'Northwind Retail LLC',
    'V-19B2C4D8': 'Blue Bean Supply',
    'B-21C09F44A1B2': 'Operating Checking',
    'P-00112233': 'Jane Example',
  };
  const lookup = async (sessionId, tokens) => {
    calls.push({ sessionId, tokens });
    const out = {};
    for (const t of tokens) {
      if (names[t]) out[t] = names[t];
    }
    return out;
  };

  // Character-by-character streaming guarantees every possible split point is exercised,
  // including 8-hex vs 12-hex collision tokens and a token at EOF.
  const source =
    'C-7F3A2B10 paid V-19B2C4D8 from B-21C09F44A1B2 (approved by P-00112233) and unknown V-99999999';
  const streamer = new StreamingTextReidentifier(new SessionReidentifier('sess_1', lookup));
  let result = '';
  for (const ch of source) {
    result += await streamer.push(ch);
  }
  result += await streamer.flush();

  assert.equal(
    result,
    'Northwind Retail LLC paid Blue Bean Supply from Operating Checking (approved by Jane Example) and unknown V-99999999',
  );
  assert.equal(calls.every((c) => c.sessionId === 'sess_1'), true);
});

test('StreamingTextReidentifier degrades gracefully when re-identification endpoint fails', async () => {
  const failingLookup = async () => {
    throw new Error('503 unavailable');
  };
  const streamer = new StreamingTextReidentifier(new SessionReidentifier('sess_err', failingLookup));
  const out = (await streamer.push('Invoice for C-7F3A2B10 ')) + (await streamer.flush());
  assert.equal(out, 'Invoice for C-7F3A2B10 ');
});

test('reidentifyMessageContents batches at 200 tokens, skips user messages, and caches across messages', async () => {
  const batchSizes = [];
  const lookup = async (_sessionId, tokens) => {
    batchSizes.push(tokens.length);
    const out = {};
    for (const t of tokens) {
      out[t] = `Name(${t})`;
    }
    return out;
  };

  const uniqueTokens = Array.from({ length: 205 }, (_, i) =>
    `C-${i.toString(16).toUpperCase().padStart(8, '0')}`,
  );
  const messages = [
    { role: 'user', content: `User typed ${uniqueTokens[0]}` },
    { role: 'assistant', content: uniqueTokens.slice(0, 120).join(' ') },
    { role: 'assistant', content: uniqueTokens.slice(100).join(' ') },
  ];

  const updated = await reidentifyMessageContents('sess_hist', messages, lookup);
  assert.deepEqual(batchSizes, [MAX_REIDENTIFY_TOKENS_PER_REQUEST, 5]);
  // User message is untouched
  assert.equal(updated[0].content, `User typed ${uniqueTokens[0]}`);
  // Assistant messages are re-identified
  assert.equal(updated[1].content.includes(`Name(${uniqueTokens[0]})`), true);
  assert.equal(updated[2].content.includes(`Name(${uniqueTokens[204]})`), true);
});

test('createReidentifyingSseStream re-identifies split tokens across SSE events and flushes before [DONE]', async () => {
  const encoder = new TextEncoder();
  const decoder = new TextDecoder();
  const events = [
    'event: started\ndata: {"executionId":"exec_42"}\n\n',
    'event: step\ndata: {"label":"Searching clients"}\n\n',
    'data: {"content":"Balance for C-7F3A"}\n\n',
    'data: {"content":"2B10 is $420.00 (vendor V-19B2"}\n\n',
    'data: {"content":"C4D8"}\n\n',
    'data: [DONE]\n\n',
  ];

  const source = new ReadableStream({
    start(controller) {
      for (const e of events) {
        controller.enqueue(encoder.encode(e));
      }
      controller.close();
    },
  });

  const startedIds = [];
  const wrapped = createReidentifyingSseStream(
    source,
    'sess_sse',
    async (_sid, tokens) => {
      const map = {
        'C-7F3A2B10': 'Acme Corp',
        'V-19B2C4D8': 'Harbor Supply',
      };
      const out = {};
      for (const t of tokens) {
        if (map[t]) out[t] = map[t];
      }
      return out;
    },
    { onExecutionStarted: (id) => startedIds.push(id) },
  );

  const reader = wrapped.getReader();
  let rawOut = '';
  while (true) {
    const { done, value } = await reader.read();
    if (done) break;
    rawOut += decoder.decode(value, { stream: true });
  }
  rawOut += decoder.decode();

  assert.deepEqual(startedIds, ['exec_42']);

  // Parse emitted SSE content chunks before [DONE]
  const blocks = rawOut.split('\n\n').filter((b) => b.trim());
  assert.equal(blocks.at(-1), 'data: [DONE]');

  let combinedContent = '';
  for (const block of blocks) {
    const dataLine = block.split('\n').find((l) => l.startsWith('data: '));
    if (!dataLine || dataLine === 'data: [DONE]') continue;
    const parsed = JSON.parse(dataLine.slice(6));
    if (typeof parsed.content === 'string') {
      combinedContent += parsed.content;
    }
  }
  assert.equal(combinedContent, 'Balance for Acme Corp is $420.00 (vendor Harbor Supply');
});
