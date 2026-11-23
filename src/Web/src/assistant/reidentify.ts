// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

/**
 * Client-side re-identification for AccountGo Assistant replies.
 *
 * MCP tool outputs replace customer/vendor/person/bank names with session-scoped
 * opaque tokens (`<role>-<8 or 12 uppercase hex>`, e.g. `C-7F3A2B10`).
 * This module resolves those tokens through AccountGo's authenticated re-identification
 * endpoint (`POST /api/ai/chat/sessions/{sessionId}/reidentify`) and swaps them back
 * to real names on the user's screen only, without modifying text stored at Anter.
 */

export const MAX_REIDENTIFY_TOKENS_PER_REQUEST = 200;

/** Complete token: role prefix (C, V, P, B) + hyphen + 8 or 12 uppercase hex characters. */
const TOKEN_REGEX = /\b([CVPB]-[0-9A-F]{8}(?:[0-9A-F]{4})?)\b/g;

/**
 * Matches a potential partial token at the end of a streamed chunk so split tokens
 * (including an 8-hex prefix that might continue to a 12-hex collision token) are
 * held until the next chunk or stream flush.
 */
const TRAILING_PARTIAL_TOKEN_REGEX = /(?:^|[^A-Za-z0-9_])([CVPB](?:-(?:[0-9A-F]{0,11})?)?)$/;

export type ReidentifyLookup = (
  sessionId: string,
  tokens: string[],
) => Promise<Record<string, string>>;

/** Extract unique pseudonym tokens from text in encounter order. */
export function extractPseudonymTokens(text: string): string[] {
  if (!text) return [];
  const seen = new Set<string>();
  const tokens: string[] = [];
  TOKEN_REGEX.lastIndex = 0;
  let match: RegExpExecArray | null;
  while ((match = TOKEN_REGEX.exec(text)) !== null) {
    const token = match[1];
    if (!seen.has(token)) {
      seen.add(token);
      tokens.push(token);
    }
  }
  return tokens;
}

/** Replace known tokens in text; unknown tokens remain untouched. */
export function replacePseudonymTokens(
  text: string,
  names: ReadonlyMap<string, string> | Record<string, string>,
): string {
  if (!text) return text;
  const map = names instanceof Map ? names : null;
  const record = map ? null : (names as Record<string, string>);

  TOKEN_REGEX.lastIndex = 0;
  return text.replace(TOKEN_REGEX, (full, token: string) => {
    const resolved = map
      ? map.get(token)
      : Object.prototype.hasOwnProperty.call(record, token)
        ? record![token]
        : undefined;
    return typeof resolved === 'string' && resolved.length > 0 ? resolved : full;
  });
}

/**
 * Returns the index where a trailing partial token begins, or `text.length` if none.
 * Holding `text.slice(idx)` across chunk boundaries guarantees tokens split across
 * SSE chunks are never emitted prematurely.
 */
export function findTrailingPartialTokenIndex(text: string): number {
  if (!text) return 0;
  const match = TRAILING_PARTIAL_TOKEN_REGEX.exec(text);
  if (!match) return text.length;
  const tokenStart = match.index + match[0].length - match[1].length;
  return tokenStart;
}

/**
 * Per-session token resolver with in-memory map caching and automatic 200-token batching.
 * Never throws on lookup errors so chat replies always remain readable.
 */
export class SessionReidentifier {
  private readonly sessionId: string;
  private readonly lookup: ReidentifyLookup;
  private readonly resolved = new Map<string, string>();
  private readonly attempted = new Set<string>();

  constructor(sessionId: string, lookup: ReidentifyLookup) {
    this.sessionId = sessionId;
    this.lookup = lookup;
  }

  async resolveTokens(tokens: readonly string[]): Promise<void> {
    const pending: string[] = [];
    for (const token of tokens) {
      if (!this.attempted.has(token)) {
        this.attempted.add(token);
        pending.push(token);
      }
    }
    if (pending.length === 0) return;

    for (let i = 0; i < pending.length; i += MAX_REIDENTIFY_TOKENS_PER_REQUEST) {
      const batch = pending.slice(i, i + MAX_REIDENTIFY_TOKENS_PER_REQUEST);
      try {
        const names = await this.lookup(this.sessionId, batch);
        if (names && typeof names === 'object') {
          for (const [token, displayName] of Object.entries(names)) {
            if (typeof displayName === 'string' && displayName.length > 0) {
              this.resolved.set(token, displayName);
            }
          }
        }
      } catch {
        // Leave tokens untouched if re-identification fails; never break chat display.
      }
    }
  }

  async reidentifyText(text: string): Promise<string> {
    const tokens = extractPseudonymTokens(text);
    if (tokens.length === 0) return text;
    await this.resolveTokens(tokens);
    return replacePseudonymTokens(text, this.resolved);
  }

  replaceKnown(text: string): string {
    return replacePseudonymTokens(text, this.resolved);
  }
}

/** Re-identify stored assistant messages in a single batched pass per session. */
export async function reidentifyMessageContents<T extends { role: string; content: string }>(
  sessionId: string,
  messages: readonly T[],
  lookup: ReidentifyLookup,
): Promise<T[]> {
  if (!sessionId || messages.length === 0) return [...messages];

  const allTokens: string[] = [];
  const seen = new Set<string>();
  for (const msg of messages) {
    if (msg.role !== 'assistant' || !msg.content) continue;
    for (const token of extractPseudonymTokens(msg.content)) {
      if (!seen.has(token)) {
        seen.add(token);
        allTokens.push(token);
      }
    }
  }

  if (allTokens.length === 0) return [...messages];

  const resolver = new SessionReidentifier(sessionId, lookup);
  await resolver.resolveTokens(allTokens);

  return messages.map((msg) =>
    msg.role === 'assistant' && msg.content
      ? { ...msg, content: resolver.replaceKnown(msg.content) }
      : msg,
  );
}

/**
 * Incremental text re-identifier for streamed chunks. Buffers any trailing partial token
 * until the next chunk completes it or `flush()` is called at end-of-stream.
 */
export class StreamingTextReidentifier {
  private readonly resolver: SessionReidentifier;
  private buffer = '';

  constructor(resolver: SessionReidentifier) {
    this.resolver = resolver;
  }

  async push(chunk: string): Promise<string> {
    if (!chunk) return '';
    const combined = this.buffer + chunk;
    const splitAt = findTrailingPartialTokenIndex(combined);
    const safePrefix = combined.slice(0, splitAt);
    this.buffer = combined.slice(splitAt);
    if (!safePrefix) return '';
    return this.resolver.reidentifyText(safePrefix);
  }

  async flush(): Promise<string> {
    if (!this.buffer) return '';
    const remaining = this.buffer;
    this.buffer = '';
    return this.resolver.reidentifyText(remaining);
  }
}

type ContentTarget =
  | { kind: 'root-content' }
  | { kind: 'payload-text' }
  | { kind: 'payload-content' };

const RUNNER_CONTROL_EVENTS = new Set([
  'started',
  'step',
  'artifact',
  'context_resolved',
  'tool_call',
  'tool_result',
  'tool_approval',
  'approval_required',
  'thinking',
  'reasoning',
  'done',
  'complete',
  'completed',
  'cancelled',
  'canceled',
  'error',
]);

function detectContentTarget(
  eventName: string | undefined,
  parsed: Record<string, unknown>,
): { target: ContentTarget; text: string } | null {
  const outerType =
    (typeof eventName === 'string' && eventName) ||
    (typeof parsed.event === 'string' && parsed.event) ||
    (typeof parsed.type === 'string' && parsed.type) ||
    '';
  if (outerType && RUNNER_CONTROL_EVENTS.has(outerType.toLowerCase())) {
    return null;
  }
  if (typeof parsed.content === 'string') {
    return { target: { kind: 'root-content' }, text: parsed.content };
  }
  const payload = parsed.payload;
  if (payload && typeof payload === 'object') {
    const rec = payload as Record<string, unknown>;
    if (typeof rec.text === 'string') {
      return { target: { kind: 'payload-text' }, text: rec.text };
    }
    if (typeof rec.content === 'string') {
      return { target: { kind: 'payload-content' }, text: rec.content };
    }
  }
  return null;
}

function applyContentTarget(
  parsed: Record<string, unknown>,
  target: ContentTarget,
  text: string,
): Record<string, unknown> {
  if (target.kind === 'root-content') {
    return { ...parsed, content: text };
  }
  const payload = (parsed.payload as Record<string, unknown>) || {};
  if (target.kind === 'payload-text') {
    return { ...parsed, payload: { ...payload, text } };
  }
  return { ...parsed, payload: { ...payload, content: text } };
}

function isTerminalSseEvent(eventName: string | undefined, parsed: Record<string, unknown>): boolean {
  const outerType = (
    (typeof eventName === 'string' && eventName) ||
    (typeof parsed.event === 'string' && parsed.event) ||
    (typeof parsed.type === 'string' && parsed.type) ||
    ''
  ).toLowerCase();
  return outerType === 'done' || outerType === 'complete' || parsed.isComplete === true;
}

function formatSseEvent(eventName: string | undefined, data: string): string {
  return eventName ? `event: ${eventName}\ndata: ${data}\n\n` : `data: ${data}\n\n`;
}

/**
 * Wraps an SSE `ReadableStream<Uint8Array>` to re-identify pseudonym tokens in streamed
 * assistant content events while preserving all non-content events and handling tokens
 * split across arbitrary SSE chunk boundaries.
 */
export function createReidentifyingSseStream(
  source: ReadableStream<Uint8Array>,
  sessionId: string | undefined,
  lookup: ReidentifyLookup,
  options?: { onExecutionStarted?: (executionId: string) => void },
): ReadableStream<Uint8Array> {
  const encoder = new TextEncoder();
  const decoder = new TextDecoder();
  const textReidentifier = sessionId
    ? new StreamingTextReidentifier(new SessionReidentifier(sessionId, lookup))
    : null;

  let sseBuffer = '';
  let lastContentTarget: ContentTarget = { kind: 'root-content' };

  const flushBufferedContent = async (
    controller: ReadableStreamDefaultController<Uint8Array>,
  ): Promise<void> => {
    if (!textReidentifier) return;
    const flushed = await textReidentifier.flush();
    if (!flushed) return;
    const synthetic =
      lastContentTarget.kind === 'root-content'
        ? { content: flushed }
        : lastContentTarget.kind === 'payload-text'
          ? { payload: { text: flushed } }
          : { payload: { content: flushed } };
    controller.enqueue(encoder.encode(formatSseEvent(undefined, JSON.stringify(synthetic))));
  };

  const processBlock = async (
    rawBlock: string,
    controller: ReadableStreamDefaultController<Uint8Array>,
  ): Promise<void> => {
    if (!rawBlock.trim()) {
      controller.enqueue(encoder.encode(`${rawBlock}\n\n`));
      return;
    }

    const lines = rawBlock.split(/\r?\n/);
    let eventName: string | undefined;
    const dataLines: string[] = [];
    const passthroughLines: string[] = [];

    for (const line of lines) {
      if (line.startsWith('event:')) {
        eventName = line.slice(6).trim();
      } else if (line.startsWith('data:')) {
        dataLines.push(line.slice(5).trimStart());
      } else {
        passthroughLines.push(line);
      }
    }

    if (dataLines.length === 0) {
      controller.enqueue(encoder.encode(`${rawBlock}\n\n`));
      return;
    }

    const rawData = dataLines.join('\n');
    if (rawData === '[DONE]') {
      await flushBufferedContent(controller);
      controller.enqueue(encoder.encode(`${rawBlock}\n\n`));
      return;
    }

    let parsed: Record<string, unknown>;
    try {
      const candidate = JSON.parse(rawData);
      if (!candidate || typeof candidate !== 'object' || Array.isArray(candidate)) {
        controller.enqueue(encoder.encode(`${rawBlock}\n\n`));
        return;
      }
      parsed = candidate as Record<string, unknown>;
    } catch {
      controller.enqueue(encoder.encode(`${rawBlock}\n\n`));
      return;
    }

    if (
      (eventName === 'started' || parsed.type === 'started' || parsed.event === 'started') &&
      typeof parsed.executionId === 'string' &&
      parsed.executionId
    ) {
      options?.onExecutionStarted?.(parsed.executionId);
    }

    if (isTerminalSseEvent(eventName, parsed)) {
      await flushBufferedContent(controller);
      controller.enqueue(encoder.encode(`${rawBlock}\n\n`));
      return;
    }

    if (!textReidentifier) {
      controller.enqueue(encoder.encode(`${rawBlock}\n\n`));
      return;
    }

    const contentMatch = detectContentTarget(eventName, parsed);
    if (!contentMatch) {
      controller.enqueue(encoder.encode(`${rawBlock}\n\n`));
      return;
    }

    lastContentTarget = contentMatch.target;
    const replaced = await textReidentifier.push(contentMatch.text);
    if (!replaced) {
      // Entire chunk was held as a trailing partial token prefix; emit on next chunk or flush.
      return;
    }

    const updated = applyContentTarget(parsed, contentMatch.target, replaced);
    const prefix = passthroughLines.length > 0 ? `${passthroughLines.join('\n')}\n` : '';
    controller.enqueue(encoder.encode(prefix + formatSseEvent(eventName, JSON.stringify(updated))));
  };

  return new ReadableStream<Uint8Array>({
    async start(controller) {
      const reader = source.getReader();
      try {
        while (true) {
          const { done, value } = await reader.read();
          if (done) break;
          sseBuffer += decoder.decode(value, { stream: true });
          const parts = sseBuffer.split(/\r?\n\r?\n/);
          sseBuffer = parts.pop() ?? '';
          for (const block of parts) {
            await processBlock(block, controller);
          }
        }

        sseBuffer += decoder.decode();
        if (sseBuffer.trim()) {
          await processBlock(sseBuffer, controller);
          sseBuffer = '';
        }
        await flushBufferedContent(controller);
        controller.close();
      } catch (err) {
        controller.error(err);
      } finally {
        reader.releaseLock();
      }
    },
    cancel(reason) {
      return source.cancel(reason);
    },
  });
}
