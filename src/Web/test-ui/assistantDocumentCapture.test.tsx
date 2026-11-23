// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { AssistantSidepanel } from '../src/assistant/AssistantSidepanel';
import { assistantAdapter } from '../src/assistant/AssistantAdapter';
import {
  DocumentCaptureCapabilities,
  DocumentCaptureView,
  validateAssistantFieldsForAccept,
} from '../src/components/document-capture/fields';

const authFetch = vi.fn();

vi.mock('../src/auth/AuthContext', () => ({
  useAuth: () => ({ authFetch }),
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
        { label: 'Vendor', value: 'Acme Co', confidence: 0.62, box: { page: 1, x: 0.1, y: 0.1, width: 0.2, height: 0.1 } },
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

let originalCreateObjectURL: typeof URL.createObjectURL;
let originalRevokeObjectURL: typeof URL.revokeObjectURL;

beforeEach(() => {
  vi.clearAllMocks();
  assistantAdapter.setPendingCaptureId(null);
  originalCreateObjectURL = URL.createObjectURL;
  originalRevokeObjectURL = URL.revokeObjectURL;
  URL.createObjectURL = vi.fn(() => 'blob:asst-preview-1');
  URL.revokeObjectURL = vi.fn();
});

afterEach(() => {
  cleanup();
  assistantAdapter.setPendingCaptureId(null);
  URL.createObjectURL = originalCreateObjectURL;
  URL.revokeObjectURL = originalRevokeObjectURL;
});

describe('AssistantSidepanel document capture integration (PR 5)', () => {
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

  it('hides attach document control when capabilities disable assistant-attachment', async () => {
    authFetch.mockResolvedValueOnce(jsonResponse({ ...assistantCaps, kinds: ['expense-receipt'] }));

    render(<AssistantSidepanel orgSlug="acme" />);

    await waitFor(() => expect(authFetch).toHaveBeenCalledWith('/api/document-captures/capabilities'));
    expect(screen.queryByRole('button', { name: /Attach document/i })).toBeNull();
  });

  it('fetches plain capabilities on AssistantSidepanel load and sends warmup=1 only when DocumentCapturePanel opens', async () => {
    authFetch.mockResolvedValue(jsonResponse(assistantCaps));

    render(<AssistantSidepanel orgSlug="acme" />);

    const attachBtn = await screen.findByRole('button', { name: /Attach document/i });
    const beforeOpenUrls = authFetch.mock.calls.map(([url]) => url);
    expect(beforeOpenUrls).toContain('/api/document-captures/capabilities');
    expect(beforeOpenUrls).not.toContain('/api/document-captures/capabilities?warmup=1');

    fireEvent.click(attachBtn);
    await screen.findByRole('button', { name: /Choose image or PDF/i });

    const capCalls = authFetch.mock.calls.filter(([url]) => url === '/api/document-captures/capabilities');
    expect(capCalls).toHaveLength(1);
    const warmupCalls = authFetch.mock.calls.filter(([url]) => url === '/api/document-captures/capabilities?warmup=1');
    expect(warmupCalls).toHaveLength(1);
  });

  it('accepts masked assistant fields, sets public captureId on AssistantAdapter, and shows attached chip', async () => {
    const ready = makeAssistantCapture();
    const accepted = makeAssistantCapture({
      status: 'accepted',
      acceptedFields: ready.fields,
      acceptedAt: '2026-05-01T10:01:00Z',
    });

    authFetch.mockImplementation(async (url: string) => {
      if (url === '/api/document-captures/capabilities') return jsonResponse(assistantCaps);
      if (url === '/api/document-captures') return jsonResponse(ready);
      if (url === '/api/document-captures/cap_asst001/accept') return jsonResponse(accepted);
      return jsonResponse({});
    });

    render(<AssistantSidepanel orgSlug="acme" />);

    fireEvent.click(await screen.findByRole('button', { name: /Attach document/i }));
    const file = new File(['png'], 'note.png', { type: 'image/png' });
    fireEvent.change(screen.getByLabelText('Upload image or PDF'), { target: { files: [file] } });

    expect(await screen.findByText('Check this')).toBeTruthy();
    expect(screen.getByText('p.1')).toBeTruthy();

    fireEvent.click(screen.getByRole('button', { name: /Accept & apply to draft/i }));

    await screen.findByText(/Attached cap_asst001 \(2 masked fields\)/i);
    expect(assistantAdapter.getPendingCaptureId()).toBe('cap_asst001');
  });

  it('allows explicit reuse of an accepted assistant attachment after detaching it', async () => {
    const ready = makeAssistantCapture();
    const accepted = makeAssistantCapture({
      status: 'accepted',
      acceptedFields: ready.fields,
      acceptedAt: '2026-05-01T10:01:00Z',
    });

    authFetch.mockImplementation(async (url: string) => {
      if (url === '/api/document-captures/capabilities') return jsonResponse(assistantCaps);
      if (url === '/api/document-captures') return jsonResponse(ready);
      if (url === '/api/document-captures/cap_asst001/accept') return jsonResponse(accepted);
      return jsonResponse({});
    });

    render(<AssistantSidepanel orgSlug="acme" />);

    fireEvent.click(await screen.findByRole('button', { name: /Attach document/i }));
    fireEvent.change(screen.getByLabelText('Upload image or PDF'), {
      target: { files: [new File(['png'], 'note.png', { type: 'image/png' })] },
    });
    fireEvent.click(await screen.findByRole('button', { name: /Accept & apply to draft/i }));
    await screen.findByText(/Attached cap_asst001/i);

    fireEvent.click(screen.getByRole('button', { name: /Remove attachment/i }));
    expect(assistantAdapter.getPendingCaptureId()).toBeNull();

    fireEvent.click(screen.getByRole('button', { name: /Reuse cap_asst001/i }));
    expect(assistantAdapter.getPendingCaptureId()).toBe('cap_asst001');
    expect(screen.getByText(/Attached cap_asst001/i)).toBeTruthy();
  });

  it('clears pending captureId when switching workspace orgSlug', async () => {
    const ready = makeAssistantCapture();
    const accepted = makeAssistantCapture({
      status: 'accepted',
      acceptedFields: ready.fields,
      acceptedAt: '2026-05-01T10:01:00Z',
    });

    authFetch.mockImplementation(async (url: string) => {
      if (url === '/api/document-captures/capabilities') return jsonResponse(assistantCaps);
      if (url === '/api/document-captures') return jsonResponse(ready);
      if (url === '/api/document-captures/cap_asst001/accept') return jsonResponse(accepted);
      return jsonResponse({});
    });

    const { rerender } = render(<AssistantSidepanel orgSlug="org-a" />);

    fireEvent.click(await screen.findByRole('button', { name: /Attach document/i }));
    fireEvent.change(screen.getByLabelText('Upload image or PDF'), {
      target: { files: [new File(['png'], 'note.png', { type: 'image/png' })] },
    });
    fireEvent.click(await screen.findByRole('button', { name: /Accept & apply to draft/i }));
    await screen.findByText(/Attached cap_asst001/i);
    expect(assistantAdapter.getPendingCaptureId()).toBe('cap_asst001');

    rerender(<AssistantSidepanel orgSlug="org-b" />);
    expect(assistantAdapter.getPendingCaptureId()).toBeNull();
    expect(screen.queryByText(/Attached cap_asst001/i)).toBeNull();
  });
});
