// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

import { useCallback, useEffect, useRef, useState } from 'react';
import { useAuth } from '../../auth/AuthContext';
import { announceEntitlementDenial, isEntitlementProblem } from '../../lib/entitlementEvents';
import {
  AI_MAPPING_POLL_INTERVAL_MS,
  AI_MAPPING_POLL_WINDOW_MS,
  ALLOWED_CAPTURE_MIME_TYPES,
  CaptureErrorPresentation,
  describeCaptureError,
  DocumentCaptureCapabilities,
  DocumentCaptureKind,
  DocumentCaptureView,
  MAX_UPLOAD_BYTES,
} from './fields';
import {
  generateCaptureIdempotencyKey,
  getCaptureStatus,
  postCaptureUpload,
} from './captureClient';

export interface UseDocumentCaptureOptions {
  orgSlug: string;
  scopeKey?: string;
  /** Optional pre-fetched capabilities from the host page to avoid duplicate /capabilities fetches. */
  capabilities?: DocumentCaptureCapabilities | null;
}

export function useDocumentCapture({
  orgSlug,
  scopeKey = 'default',
  capabilities: externalCapabilities,
}: UseDocumentCaptureOptions) {
  const { authFetch } = useAuth();
  const hasExternalCapabilities = externalCapabilities !== undefined;

  const [fetchedCapabilities, setFetchedCapabilities] = useState<DocumentCaptureCapabilities | null>(null);
  const [capabilitiesLoading, setCapabilitiesLoading] = useState(!hasExternalCapabilities);
  const capabilities = hasExternalCapabilities ? externalCapabilities : fetchedCapabilities;

  const [selectedFile, setSelectedFile] = useState<File | null>(null);
  const [localPreviewUrl, setLocalPreviewUrl] = useState<string | null>(null);
  const [capture, setCapture] = useState<DocumentCaptureView | null>(null);
  const [lastCaptureId, setLastCaptureId] = useState<string | null>(null);
  const [busy, setBusy] = useState<'uploading' | 'checking' | 'accepting' | 'rejecting' | 'linking' | null>(null);
  const [error, setError] = useState<CaptureErrorPresentation | null>(null);

  const idempotencyKeyRef = useRef<string | null>(null);
  const previewUrlRef = useRef<string | null>(null);
  const generationRef = useRef(0);
  const activeScopeRef = useRef(`${orgSlug}::${scopeKey}`);

  const revokePreview = useCallback(() => {
    if (previewUrlRef.current) {
      URL.revokeObjectURL(previewUrlRef.current);
      previewUrlRef.current = null;
      setLocalPreviewUrl(null);
    }
  }, []);

  const reset = useCallback(() => {
    generationRef.current += 1;
    revokePreview();
    idempotencyKeyRef.current = null;
    setSelectedFile(null);
    setCapture(null);
    setLastCaptureId(null);
    setError(null);
    setBusy(null);
  }, [revokePreview]);

  // Discard any in-flight responses when switching workspace or document.
  useEffect(() => {
    const nextScope = `${orgSlug}::${scopeKey}`;
    if (activeScopeRef.current !== nextScope) {
      activeScopeRef.current = nextScope;
      reset();
    }
  }, [orgSlug, scopeKey, reset]);

  // Always revoke object URLs on unmount.
  useEffect(() => () => revokePreview(), [revokePreview]);

  // Fetch action-aware capture capabilities with opt-in ?warmup=1 when the scan panel opens.
  // When the host already pre-fetched capabilities, fire a non-blocking ?warmup=1 ping if capture is active
  // so the paid extraction container warms without ordinary sidebar/page loads waking it.
  useEffect(() => {
    if (hasExternalCapabilities) {
      setCapabilitiesLoading(false);
      if (externalCapabilities?.enabled && externalCapabilities?.providerAvailable) {
        void Promise.resolve(authFetch('/api/document-captures/capabilities?warmup=1')).catch(() => {});
      }
      return;
    }
    let cancelled = false;
    const expectedOrg = orgSlug;
    setCapabilitiesLoading(true);
    (async () => {
      try {
        const res = await authFetch('/api/document-captures/capabilities?warmup=1');
        if (cancelled || activeScopeRef.current.split('::')[0] !== expectedOrg) return;
        if (!res.ok) {
          setFetchedCapabilities(null);
          return;
        }
        const data: DocumentCaptureCapabilities = await res.json();
        if (!cancelled && activeScopeRef.current.split('::')[0] === expectedOrg) {
          setFetchedCapabilities(data);
        }
      } catch {
        if (!cancelled) setFetchedCapabilities(null);
      } finally {
        if (!cancelled) setCapabilitiesLoading(false);
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [authFetch, externalCapabilities?.enabled, externalCapabilities?.providerAvailable, hasExternalCapabilities, orgSlug]);

  const isCurrent = useCallback(
    (expectedScope: string, expectedGen: number) =>
      activeScopeRef.current === expectedScope && generationRef.current === expectedGen,
    []
  );

  const selectFile = useCallback(
    (file: File | null) => {
      generationRef.current += 1;
      revokePreview();
      setError(null);
      setCapture(null);
      setLastCaptureId(null);

      if (!file) {
        setSelectedFile(null);
        idempotencyKeyRef.current = null;
        return false;
      }

      if (file.size === 0) {
        setSelectedFile(null);
        idempotencyKeyRef.current = null;
        setError(describeCaptureError('DOCUMENT_EMPTY', 400));
        return false;
      }

      const maxBytes = capabilities?.maxUploadBytes ?? MAX_UPLOAD_BYTES;
      if (file.size > maxBytes) {
        setSelectedFile(null);
        idempotencyKeyRef.current = null;
        setError(describeCaptureError('DOCUMENT_TOO_LARGE', 413));
        return false;
      }

      const allowedTypes = capabilities?.allowedMimeTypes ?? [...ALLOWED_CAPTURE_MIME_TYPES];
      if (file.type && !allowedTypes.includes(file.type)) {
        setSelectedFile(null);
        idempotencyKeyRef.current = null;
        setError(describeCaptureError('DOCUMENT_UNSUPPORTED_TYPE', 415));
        return false;
      }

      setSelectedFile(file);
      idempotencyKeyRef.current = generateCaptureIdempotencyKey();
      if (file.type.startsWith('image/')) {
        const url = URL.createObjectURL(file);
        previewUrlRef.current = url;
        setLocalPreviewUrl(url);
      }
      return true;
    },
    [capabilities, revokePreview]
  );

  const upload = useCallback(
    async (kind: DocumentCaptureKind, options?: { file?: File; force?: boolean }): Promise<DocumentCaptureView | null> => {
      const targetFile = options?.file ?? selectedFile;
      if (!targetFile) {
        setError(describeCaptureError('DOCUMENT_EMPTY', 400));
        return null;
      }
      if (!idempotencyKeyRef.current) {
        idempotencyKeyRef.current = generateCaptureIdempotencyKey();
      }
      const key = idempotencyKeyRef.current;
      const scope = activeScopeRef.current;
      const gen = ++generationRef.current;

      setBusy('uploading');
      setError(null);

      try {
        const res = await postCaptureUpload(authFetch, kind, targetFile, capabilities, {
          idempotencyKey: key,
          force: options?.force,
        });

        if (!isCurrent(scope, gen)) return null;

        if (!res.ok) {
          await announceEntitlementDenial(res);
          const body = await res.json().catch(() => null);
          if (!isCurrent(scope, gen)) return null;
          if (res.status === 403 && isEntitlementProblem(body)) {
            setError({
              code: body.code,
              message: body.detail || 'Your current plan does not include document capture.',
              canRetryUpload: false,
              canCheckStatus: false,
              canRetryAccept: false,
            });
            return null;
          }
          if (body?.captureId && typeof body.captureId === 'string') {
            setLastCaptureId(body.captureId);
          }
          const retryAfterHeader = res.headers?.get?.('Retry-After');
          const retryAfter = retryAfterHeader ? Number(retryAfterHeader) : null;
          setError(describeCaptureError(body?.code, res.status, Number.isFinite(retryAfter) ? retryAfter : null));
          return null;
        }

        const view: DocumentCaptureView = await res.json();
        if (!isCurrent(scope, gen)) return null;
        setCapture(view);
        setLastCaptureId(view.id);
        return view;
      } catch {
        if (!isCurrent(scope, gen)) return null;
        setError(describeCaptureError(null, 504));
        return null;
      } finally {
        if (isCurrent(scope, gen)) setBusy(null);
      }
    },
    [authFetch, capabilities, isCurrent, selectedFile]
  );

  const checkStatus = useCallback(
    async (captureIdOverride?: string): Promise<DocumentCaptureView | null> => {
      const targetId = captureIdOverride ?? capture?.id ?? lastCaptureId;
      if (!targetId) return null;

      const scope = activeScopeRef.current;
      const gen = ++generationRef.current;
      setBusy('checking');
      setError(null);

      try {
        const res = await getCaptureStatus(authFetch, targetId);
        if (!isCurrent(scope, gen)) return null;
        if (!res.ok) {
          await announceEntitlementDenial(res);
          const body = await res.json().catch(() => null);
          if (!isCurrent(scope, gen)) return null;
          if (res.status === 403) {
            setCapture(null);
            revokePreview();
            setSelectedFile(null);
          }
          setError(describeCaptureError(body?.code, res.status));
          return null;
        }
        const view: DocumentCaptureView = await res.json();
        if (!isCurrent(scope, gen)) return null;
        setCapture(view);
        setLastCaptureId(view.id);
        return view;
      } catch {
        if (!isCurrent(scope, gen)) return null;
        setError(describeCaptureError(null, 504));
        return null;
      } finally {
        if (isCurrent(scope, gen)) setBusy(null);
      }
    },
    [authFetch, capture?.id, isCurrent, lastCaptureId]
  );

  // While the optional background field mapping is pending, ask for the capture quietly (no busy state, no error) about once a second
  // for a few seconds. When it finishes the improved capture replaces the current one; if it doesn't in time, the pending line goes
  // away and the rule result stands. Stops on unmount, scope change or when the capture changes.
  const aiPendingCaptureId = capture?.status === 'ready' && capture.aiMapping?.status === 'pending' ? capture.id : null;
  useEffect(() => {
    if (!aiPendingCaptureId) return;
    const scope = activeScopeRef.current;
    let cancelled = false;
    let elapsed = 0;
    let inFlight = false;
    const timer = setInterval(async () => {
      if (cancelled || inFlight) return;
      elapsed += AI_MAPPING_POLL_INTERVAL_MS;
      if (elapsed > AI_MAPPING_POLL_WINDOW_MS) {
        clearInterval(timer);
        setCapture((prev) => (prev && prev.id === aiPendingCaptureId && prev.aiMapping?.status === 'pending' ? { ...prev, aiMapping: null } : prev));
        return;
      }
      inFlight = true;
      try {
        const res = await getCaptureStatus(authFetch, aiPendingCaptureId);
        if (cancelled || activeScopeRef.current !== scope || !res.ok) return;
        const view: DocumentCaptureView = await res.json();
        if (cancelled || activeScopeRef.current !== scope) return;
        if (view.id === aiPendingCaptureId && view.status === 'ready' && view.aiMapping?.status !== 'pending') {
          clearInterval(timer);
          setCapture((prev) => (prev && prev.id === aiPendingCaptureId && prev.status === 'ready' ? view : prev));
        }
      } catch {
        // A failed poll changes nothing; the next tick or the window's end settles it.
      } finally {
        inFlight = false;
      }
    }, AI_MAPPING_POLL_INTERVAL_MS);
    return () => {
      cancelled = true;
      clearInterval(timer);
    };
  }, [authFetch, aiPendingCaptureId]);

  const accept = useCallback(
    async (fields: Record<string, unknown>, captureIdOverride?: string): Promise<DocumentCaptureView | null> => {
      const targetId = captureIdOverride ?? capture?.id ?? lastCaptureId;
      if (!targetId) return null;

      const scope = activeScopeRef.current;
      const gen = ++generationRef.current;
      setBusy('accepting');
      setError(null);

      try {
        const res = await authFetch(`/api/document-captures/${encodeURIComponent(targetId)}/accept`, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ fields }),
        });
        if (!isCurrent(scope, gen)) return null;

        if (!res.ok) {
          await announceEntitlementDenial(res);
          const body = await res.json().catch(() => null);
          if (!isCurrent(scope, gen)) return null;
          if (res.status === 403) {
            setCapture(null);
            revokePreview();
            setSelectedFile(null);
          }
          setError(describeCaptureError(body?.code, res.status));
          return null;
        }

        const canonical: DocumentCaptureView = await res.json();
        if (!isCurrent(scope, gen)) return null;
        setCapture(canonical);
        setLastCaptureId(canonical.id);
        return canonical;
      } catch {
        if (!isCurrent(scope, gen)) return null;
        setError({
          code: 'ACCEPT_RESPONSE_LOST',
          message: 'The accept response was lost. Check status or retry accept (retrying the same values is safe).',
          canRetryUpload: false,
          canCheckStatus: true,
          canRetryAccept: true,
        });
        return null;
      } finally {
        if (isCurrent(scope, gen)) setBusy(null);
      }
    },
    [authFetch, capture?.id, isCurrent, lastCaptureId]
  );

  const reject = useCallback(
    async (captureIdOverride?: string): Promise<DocumentCaptureView | null> => {
      const targetId = captureIdOverride ?? capture?.id ?? lastCaptureId;
      if (!targetId) return null;

      const scope = activeScopeRef.current;
      const gen = ++generationRef.current;
      setBusy('rejecting');
      setError(null);

      try {
        const res = await authFetch(`/api/document-captures/${encodeURIComponent(targetId)}/reject`, {
          method: 'POST',
        });
        if (!isCurrent(scope, gen)) return null;
        if (!res.ok) {
          const body = await res.json().catch(() => null);
          if (!isCurrent(scope, gen)) return null;
          setError(describeCaptureError(body?.code, res.status));
          return null;
        }
        const view: DocumentCaptureView = await res.json();
        if (!isCurrent(scope, gen)) return null;
        revokePreview();
        setSelectedFile(null);
        setCapture(view);
        return view;
      } catch {
        if (!isCurrent(scope, gen)) return null;
        setError(describeCaptureError(null, 504));
        return null;
      } finally {
        if (isCurrent(scope, gen)) setBusy(null);
      }
    },
    [authFetch, capture?.id, isCurrent, lastCaptureId]
  );

  const linkCapture = useCallback(
    async (
      captureId: string,
      targetType: 'bill' | 'invoice' | 'bank',
      targetId: string
    ): Promise<{ ok: boolean; view?: DocumentCaptureView; error?: CaptureErrorPresentation }> => {
      try {
        const res = await authFetch(`/api/document-captures/${encodeURIComponent(captureId)}/links`, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ targetType, targetId }),
        });
        if (!res.ok) {
          const body = await res.json().catch(() => null);
          return { ok: false, error: describeCaptureError(body?.code, res.status) };
        }
        const view: DocumentCaptureView = await res.json();
        return { ok: true, view };
      } catch {
        return { ok: false, error: describeCaptureError(null, 504) };
      }
    },
    [authFetch]
  );

  return {
    capabilities,
    capabilitiesLoading,
    selectedFile,
    localPreviewUrl,
    capture,
    lastCaptureId,
    busy,
    error,
    selectFile,
    upload,
    checkStatus,
    accept,
    reject,
    linkCapture,
    reset,
    clearError: () => setError(null),
  };
}
