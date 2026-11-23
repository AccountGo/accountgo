// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { cleanup, render, screen } from '@testing-library/react';
import { AssistantSidepanel } from '../src/assistant/AssistantSidepanel';
import { AssistantAdapter } from '../src/assistant/AssistantAdapter';
import {
  DocumentCaptureCapabilities,
  DocumentCaptureView,
  validateAssistantFieldsForAccept,
} from '../src/components/document-capture/fields';

const { assistantFetchMock, assistantFetchStreamMock } = vi.hoisted(() => ({
  assistantFetchMock: vi.fn(),
  assistantFetchStreamMock: vi.fn(),
}));

vi.mock('../src/assistant/assistantFetch', () => ({
  assistantFetch: assistantFetchMock,
  assistantFetchJson: vi.fn(),
  assistantFetchStream: assistantFetchStreamMock,
}));

vi.mock('../src/assistant/AssistantProvider', () => ({
  useAssistant: () => ({ close: vi.fn() }),
}));

vi.mock('@anter/ai-chat-sdk', () => ({
  ChatSidepanel: () => <div data-testid="mock-chat-sidepanel">Chat</div>,
  useChat: () => ({ sendMessage: vi.fn() }),
}));

function jsonResponse(body: unknown, status = 200): Response {
  return {
    ok: status >= 200 && status < 300,
    status,
    headers: new Headers({ 'Content-Type': 'application/json' }),
    json: async () => body,
    text: async () => JSON.stringify(body),
    clone() {
      return jsonResponse(body, status);
    },
  } as unknown as Response;
}

const assistantCaps: DocumentCaptureCapabilities = {
  enabled: true,
  providerAvailable: true,
  kinds: ['assistant-attachment'],
  schemaVersion: 1,
  maxUploadBytes: 8 * 1024 * 1024,
  maxPdfPages: 10,
  maxImageWidth: 4096,
  maxImageHeight: 4096,
  maxImagePixels: 16_000_000,
  allowedMimeTypes: ['image/jpeg', 'image/png', 'application/pdf'],
};

function makeAssistantCapture(overrides: Partial<DocumentCaptureView> = {}): DocumentCaptureView {
  return {
    id: 'cap_asst001',
    kind: 'assistant-attachment',
    schemaVersion: 1,
    status: 'ready',
    contentType: 'image/png',
    byteCount: 128,
    width: 400,
    height: 300,
    pageCount: 1,
    fields: {
      fields: [
        { label: 'Vendor', value: 'Acme Co', confidence: 0.62, box: null },
        { label: 'Account', value: '••••', confidence: null, box: null },
      ],
    },
    acceptedFields: null,
    warnings: [],
    errorCode: null,
    bytesAvailable: true,
    canAccept: true,
    canApply: true,
    providerName: 'fake',
    providerVersion: '1',
    sourceHash: 'a'.repeat(64),
    contentHash: 'b'.repeat(64),
    normalizationVersion: 'image-normalization-v2',
    link: null,
    createdAt: '2026-05-01T10:00:00Z',
    acceptedAt: null,
    rejectedAt: null,
    ...overrides,
  };
}

beforeEach(() => {
  vi.clearAllMocks();
});

afterEach(() => {
  cleanup();
});

describe('AssistantSidepanel and composer attachments (#98)', () => {
  it('validates assistant-attachment field limits and allows null confidence and box', () => {
    const valid = validateAssistantFieldsForAccept([
      { id: 1, label: 'Memo', value: 'Masked ••••', confidence: null, box: null },
    ]);
    expect(valid.valid).toBe(true);
    expect(valid.payload.fields).toEqual([{ label: 'Memo', value: 'Masked ••••' }]);

    const tooLong = validateAssistantFieldsForAccept([
      { id: 2, label: 'x'.repeat(201), value: 'y'.repeat(2001), confidence: 0.9, box: null },
    ]);
    expect(tooLong.valid).toBe(false);
    expect(tooLong.errors).toHaveLength(2);
  });

  it('removes the header attachment bar while keeping the privacy notice and ChatSidepanel', () => {
    render(<AssistantSidepanel orgSlug="acme" />);

    expect(screen.getByText('Names and personal details are hidden from the AI.')).toBeTruthy();
    expect(screen.getByTestId('mock-chat-sidepanel')).toBeTruthy();
    expect(screen.queryByRole('button', { name: /Attach document/i })).toBeNull();
  });

  it('rejects unsupported file types and oversized files in uploadFile before making any request', async () => {
    const adapter = new AssistantAdapter();
    adapter.setCaptureCapabilities(assistantCaps);

    const badType = new File(['hello'], 'notes.txt', { type: 'text/plain' });
    await expect(adapter.uploadFile('sess_1', badType)).rejects.toThrow(/PDF, PNG, JPEG, or WebP/i);
    expect(assistantFetchMock).not.toHaveBeenCalled();

    const oversized = new File([new Uint8Array(10)], 'huge.pdf', { type: 'application/pdf' });
    Object.defineProperty(oversized, 'size', { value: 9 * 1024 * 1024 });
    await expect(adapter.uploadFile('sess_1', oversized)).rejects.toThrow(/8 MB|limit/i);
    expect(assistantFetchMock).not.toHaveBeenCalled();
  });

  it('uploads via capture API, polls if extracting, and returns ChatSessionFileRef with empty downloadUrl and field count', async () => {
    const adapter = new AssistantAdapter();
    adapter.setCaptureCapabilities(assistantCaps);

    const ready = makeAssistantCapture();
    assistantFetchMock.mockResolvedValueOnce(jsonResponse(ready));

    const file = new File(['png-bytes'], 'receipt.png', { type: 'image/png' });
    const ref = await adapter.uploadFile('sess_1', file);

    expect(assistantFetchMock).toHaveBeenCalledWith(
      '/api/document-captures',
      expect.objectContaining({ method: 'POST' }),
    );
    expect(ref.id).toBe('cap_asst001');
    expect(ref.fileName).toBe('receipt.png · 2 fields');
    expect(ref.status).toBe('processed');
    expect(ref.downloadUrl).toBe('');
  });

  it('passes attachedFileIds to run-stream as captureId for one file and attachedFileIds for multiple', async () => {
    const adapter = new AssistantAdapter();
    const emptyStream = new ReadableStream<Uint8Array>({
      start(controller) {
        controller.close();
      },
    });
    assistantFetchStreamMock.mockResolvedValue(emptyStream);

    await adapter.sendMessage({
      sessionId: 'sess_1',
      message: 'What is this?',
      attachedFileIds: ['cap_asst001'],
    });

    const firstCallInit = assistantFetchStreamMock.mock.calls[0][1] as RequestInit;
    const firstBody = JSON.parse(String(firstCallInit.body));
    expect(firstBody.captureId).toBe('cap_asst001');
    expect(firstBody.attachedFileIds).toBeUndefined();

    await adapter.sendMessage({
      sessionId: 'sess_1',
      message: 'Two files',
      attachedFileIds: ['cap_asst001', 'cap_asst002'],
    });

    const secondCallInit = assistantFetchStreamMock.mock.calls[1][1] as RequestInit;
    const secondBody = JSON.parse(String(secondCallInit.body));
    expect(secondBody.attachedFileIds).toEqual(['cap_asst001', 'cap_asst002']);
    expect(secondBody.captureId).toBeUndefined();
  });

  it('rejects capture on deleteSessionFile to wipe bytes and fields', async () => {
    const adapter = new AssistantAdapter();
    assistantFetchMock.mockResolvedValueOnce(jsonResponse({ id: 'cap_asst001', status: 'rejected' }));

    await adapter.deleteSessionFile('sess_1', 'cap_asst001');

    expect(assistantFetchMock).toHaveBeenCalledWith(
      '/api/document-captures/cap_asst001/reject',
      expect.objectContaining({ method: 'POST' }),
    );
  });
});
