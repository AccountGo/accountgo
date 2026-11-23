// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

import {
  CURRENT_SCHEMA_VERSION,
  describeCaptureError,
  DocumentCaptureCapabilities,
  DocumentCaptureKind,
  DocumentCaptureView,
  MAX_UPLOAD_BYTES,
} from './fields';

export const ASSISTANT_UPLOAD_ALLOWED_MIME_TYPES = [
  'application/pdf',
  'image/png',
  'image/jpeg',
  'image/webp',
] as const;

const ALLOWED_EXTENSIONS = new Set(['pdf', 'png', 'jpg', 'jpeg', 'webp']);

function generateCaptureIdempotencyKey(): string {
  if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') {
    return `cap_${crypto.randomUUID().replace(/-/g, '')}`;
  }
  return `cap_${Date.now().toString(36)}_${Math.random().toString(36).slice(2, 14)}`;
}

export function validateCaptureUploadFile(
  file: File,
  capabilities?: DocumentCaptureCapabilities | null,
): string | null {
  if (!file || file.size === 0) {
    return describeCaptureError('DOCUMENT_EMPTY', 400).message;
  }

  const maxBytes = capabilities?.maxUploadBytes ?? MAX_UPLOAD_BYTES;
  if (file.size > maxBytes) {
    return describeCaptureError('DOCUMENT_TOO_LARGE', 413).message;
  }

  const mime = (file.type || '').trim().toLowerCase();
  if (mime) {
    const allowed: readonly string[] = ASSISTANT_UPLOAD_ALLOWED_MIME_TYPES;
    if (!allowed.includes(mime)) {
      return 'Only PDF, PNG, JPEG, or WebP files can be attached.';
    }
    return null;
  }

  const ext = file.name.includes('.') ? file.name.split('.').pop()?.toLowerCase() ?? '' : '';
  if (!ALLOWED_EXTENSIONS.has(ext)) {
    return 'Only PDF, PNG, JPEG, or WebP files can be attached.';
  }
  return null;
}

export async function uploadAndWaitForCapture(
  fetchImpl: (input: string, init?: RequestInit) => Promise<Response>,
  kind: DocumentCaptureKind,
  file: File,
  capabilities?: DocumentCaptureCapabilities | null,
  options?: { maxPollAttempts?: number; pollDelayMs?: number },
): Promise<DocumentCaptureView> {
  const validationError = validateCaptureUploadFile(file, capabilities);
  if (validationError) {
    throw new Error(validationError);
  }

  const form = new FormData();
  form.append('Kind', kind);
  form.append('SchemaVersion', String(capabilities?.schemaVersion ?? CURRENT_SCHEMA_VERSION));
  form.append('File', file);

  const res = await fetchImpl('/api/document-captures', {
    method: 'POST',
    headers: { 'Idempotency-Key': generateCaptureIdempotencyKey() },
    body: form,
  });

  if (!res.ok) {
    const body = await res.json().catch(() => null);
    const retryAfterHeader = res.headers?.get?.('Retry-After');
    const retryAfter = retryAfterHeader ? Number(retryAfterHeader) : null;
    const presentation = describeCaptureError(
      body?.code,
      res.status,
      Number.isFinite(retryAfter) ? retryAfter : null,
    );
    throw new Error(body?.detail || presentation.message);
  }

  let view: DocumentCaptureView = await res.json();
  const maxAttempts = options?.maxPollAttempts ?? 15;
  const pollDelayMs = options?.pollDelayMs ?? 1000;

  for (let attempt = 0; attempt < maxAttempts && view.status === 'extracting'; attempt++) {
    await new Promise((resolve) => setTimeout(resolve, pollDelayMs));
    const statusRes = await fetchImpl(`/api/document-captures/${encodeURIComponent(view.id)}`, {
      method: 'GET',
    });
    if (!statusRes.ok) {
      const body = await statusRes.json().catch(() => null);
      throw new Error(describeCaptureError(body?.code, statusRes.status).message);
    }
    view = await statusRes.json();
  }

  return view;
}

export async function rejectCapture(
  fetchImpl: (input: string, init?: RequestInit) => Promise<Response>,
  captureId: string,
): Promise<void> {
  if (!captureId || !captureId.trim()) return;
  const res = await fetchImpl(`/api/document-captures/${encodeURIComponent(captureId.trim())}/reject`, {
    method: 'POST',
  });
  if (!res.ok && res.status !== 404) {
    const body = await res.json().catch(() => null);
    throw new Error(describeCaptureError(body?.code, res.status).message);
  }
}
