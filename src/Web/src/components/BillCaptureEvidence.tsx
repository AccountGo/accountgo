// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

import React, { useCallback, useEffect, useRef, useState } from 'react';
import { Download, Eye, EyeOff, FileText, Image as ImageIcon, Loader2 } from 'lucide-react';
import { useAuth } from '../auth/AuthContext';
import {
  DocumentCaptureKind,
  DocumentCaptureView,
  KIND_LABELS,
  VendorCaptureFields,
} from './document-capture/fields';

export interface BillCaptureEvidenceProps {
  billId: string;
  onCapturesLoaded?: (captures: DocumentCaptureView[]) => void;
}

function shortHash(hash: string | null | undefined): string {
  if (!hash) return '—';
  return hash.length > 16 ? `${hash.slice(0, 12)}…${hash.slice(-4)}` : hash;
}

export const BillCaptureEvidence: React.FC<BillCaptureEvidenceProps> = ({
  billId,
  onCapturesLoaded,
}) => {
  const { authFetch } = useAuth();
  const [captures, setCaptures] = useState<DocumentCaptureView[]>([]);
  const [loading, setLoading] = useState(true);
  const [mediaError, setMediaError] = useState<string | null>(null);
  const [previewUrls, setPreviewUrls] = useState<Record<string, string>>({});
  const [busyMediaId, setBusyMediaId] = useState<string | null>(null);
  const urlsRef = useRef<Record<string, string>>({});

  const revokeAll = useCallback(() => {
    for (const url of Object.values(urlsRef.current)) {
      URL.revokeObjectURL(url);
    }
    urlsRef.current = {};
    setPreviewUrls({});
  }, []);

  useEffect(() => () => revokeAll(), [revokeAll]);

  useEffect(() => {
    let cancelled = false;
    revokeAll();
    setCaptures([]);
    setMediaError(null);
    setLoading(true);
    (async () => {
      try {
        const res = await authFetch(`/api/document-captures/linked/bill/${encodeURIComponent(billId)}`);
        if (cancelled) return;
        if (!res.ok) {
          onCapturesLoaded?.([]);
          return;
        }
        const raw = await res.json();
        const data: DocumentCaptureView[] = Array.isArray(raw) ? raw : [];
        if (!cancelled) {
          setCaptures(data);
          onCapturesLoaded?.(data);
        }
      } catch {
        if (!cancelled) onCapturesLoaded?.([]);
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [authFetch, billId, onCapturesLoaded, revokeAll]);

  const toggleImagePreview = async (capture: DocumentCaptureView) => {
    setMediaError(null);
    if (previewUrls[capture.id]) {
      URL.revokeObjectURL(previewUrls[capture.id]);
      delete urlsRef.current[capture.id];
      setPreviewUrls((prev) => {
        const next = { ...prev };
        delete next[capture.id];
        return next;
      });
      return;
    }

    setBusyMediaId(capture.id);
    try {
      const res = await authFetch(`/api/document-captures/${encodeURIComponent(capture.id)}/image`);
      if (!res.ok) {
        setMediaError('Could not load source image bytes.');
        return;
      }
      const blob = await res.blob();
      const url = URL.createObjectURL(blob);
      urlsRef.current[capture.id] = url;
      setPreviewUrls((prev) => ({ ...prev, [capture.id]: url }));
    } catch {
      setMediaError('Could not load source image bytes.');
    } finally {
      setBusyMediaId(null);
    }
  };

  const downloadMedia = async (capture: DocumentCaptureView) => {
    setMediaError(null);
    setBusyMediaId(capture.id);
    try {
      const res = await authFetch(`/api/document-captures/${encodeURIComponent(capture.id)}/image`);
      if (!res.ok) {
        setMediaError('Could not download source document.');
        return;
      }
      const blob = await res.blob();
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download =
        capture.contentType === 'application/pdf'
          ? `${capture.id}.pdf`
          : capture.contentType === 'image/jpeg'
            ? `${capture.id}.jpg`
            : `${capture.id}.png`;
      document.body.appendChild(a);
      a.click();
      a.remove();
      URL.revokeObjectURL(url);
    } catch {
      setMediaError('Could not download source document.');
    } finally {
      setBusyMediaId(null);
    }
  };

  if (loading || captures.length === 0) {
    return null;
  }

  return (
    <section
      aria-label="Supporting source documents"
      className="rounded-xl border border-[var(--border-subtle)] bg-[var(--bg-surface)] p-5 shadow-sm space-y-4"
    >
      <div className="flex items-center justify-between">
        <h2 className="text-xs font-semibold uppercase tracking-wider text-[var(--text-muted)]">
          Supporting source documents ({captures.length})
        </h2>
      </div>

      {mediaError && (
        <div role="alert" className="rounded-lg border border-rose-500/25 bg-rose-500/10 px-3 py-2 text-xs text-rose-600 dark:text-rose-400">
          {mediaError}
        </div>
      )}

      <div className="space-y-4">
        {captures.map((cap) => {
          const accepted = (cap.acceptedFields ?? {}) as VendorCaptureFields;
          const isPdf = cap.contentType === 'application/pdf';
          const previewUrl = previewUrls[cap.id];
          const kindLabel = KIND_LABELS[cap.kind as DocumentCaptureKind] ?? cap.kind;
          return (
            <div
              key={cap.id}
              className="rounded-xl border border-[var(--border-subtle)] bg-[var(--bg-subtle)] p-4 space-y-3 text-xs"
            >
              <div className="flex flex-wrap items-center justify-between gap-2">
                <div className="flex items-center gap-2">
                  {isPdf ? (
                    <FileText className="w-4 h-4 text-[var(--brand-primary)]" />
                  ) : (
                    <ImageIcon className="w-4 h-4 text-[var(--brand-primary)]" />
                  )}
                  <span className="font-semibold text-[var(--text-main)]">{kindLabel}</span>
                  <span className="font-mono text-[11px] text-[var(--text-muted)]">{cap.id}</span>
                  <span className="px-1.5 py-0.5 rounded bg-[var(--bg-surface)] border border-[var(--border-subtle)] text-[10px] text-[var(--text-secondary)]">
                    {isPdf ? `${cap.pageCount} page${cap.pageCount === 1 ? '' : 's'} PDF` : cap.contentType}
                  </span>
                </div>

                <div className="flex items-center gap-2">
                  {cap.bytesAvailable ? (
                    <>
                      {!isPdf && (
                        <button
                          type="button"
                          disabled={busyMediaId === cap.id}
                          onClick={() => void toggleImagePreview(cap)}
                          className="inline-flex items-center gap-1 px-2.5 py-1.5 rounded-lg border border-[var(--border-subtle)] bg-[var(--bg-surface)] text-[var(--text-main)] hover:bg-[var(--bg-surface-hover)] transition"
                        >
                          {busyMediaId === cap.id ? (
                            <Loader2 className="w-3.5 h-3.5 animate-spin" />
                          ) : previewUrl ? (
                            <EyeOff className="w-3.5 h-3.5" />
                          ) : (
                            <Eye className="w-3.5 h-3.5" />
                          )}
                          {previewUrl ? 'Hide image' : 'View image'}
                        </button>
                      )}
                      <button
                        type="button"
                        disabled={busyMediaId === cap.id}
                        onClick={() => void downloadMedia(cap)}
                        className="inline-flex items-center gap-1 px-2.5 py-1.5 rounded-lg border border-[var(--border-subtle)] bg-[var(--bg-surface)] text-[var(--text-main)] hover:bg-[var(--bg-surface-hover)] transition"
                      >
                        <Download className="w-3.5 h-3.5" />
                        {isPdf ? 'Download PDF' : 'Download'}
                      </button>
                    </>
                  ) : (
                    <span className="text-[var(--text-muted)]">Source bytes unavailable</span>
                  )}
                </div>
              </div>

              {previewUrl && (
                <div className="pt-1">
                  <img
                    src={previewUrl}
                    alt={`Source evidence ${cap.id}`}
                    className="max-h-80 rounded-lg border border-[var(--border-subtle)] object-contain bg-[var(--bg-surface)]"
                  />
                </div>
              )}

              {/* Canonical accepted fields */}
              <dl className="grid grid-cols-2 sm:grid-cols-3 lg:grid-cols-4 gap-x-4 gap-y-2 pt-2 border-t border-[var(--border-subtle)]">
                <div>
                  <dt className="text-[var(--text-muted)]">Source vendor</dt>
                  <dd className="text-[var(--text-main)]">{accepted.vendorName || '—'}</dd>
                </div>
                <div>
                  <dt className="text-[var(--text-muted)]">Source bill #</dt>
                  <dd className="font-mono text-[var(--text-main)]">{accepted.vendorInvoiceNo || '—'}</dd>
                </div>
                <div>
                  <dt className="text-[var(--text-muted)]">Source date</dt>
                  <dd className="text-[var(--text-main)]">{accepted.date || '—'}</dd>
                </div>
                <div>
                  <dt className="text-[var(--text-muted)]">Source currency</dt>
                  <dd className="text-[var(--text-main)]">{accepted.currency || '—'}</dd>
                </div>
                <div>
                  <dt className="text-[var(--text-muted)]">Source total</dt>
                  <dd className="font-mono text-[var(--text-main)]">
                    {accepted.sourceTotals?.grandTotal ?? '—'}
                    {accepted.sourceTotals?.priceBasis ? ` (${accepted.sourceTotals.priceBasis})` : ''}
                  </dd>
                </div>
                <div>
                  <dt className="text-[var(--text-muted)]">Source subtotal / tax</dt>
                  <dd className="font-mono text-[var(--text-main)]">
                    {accepted.sourceTotals?.subtotal ?? '—'} / {accepted.sourceTotals?.taxTotal ?? '—'}
                  </dd>
                </div>
                {accepted.paymentHints?.paymentStatus && (
                  <div>
                    <dt className="text-[var(--text-muted)]">Payment hint</dt>
                    <dd className="text-[var(--text-main)]">
                      {accepted.paymentHints.paymentStatus}
                      {accepted.paymentHints.amountPaid ? ` (${accepted.paymentHints.amountPaid})` : ''}
                    </dd>
                  </div>
                )}
              </dl>

              {/* Provenance & hashes */}
              <dl className="grid grid-cols-1 sm:grid-cols-3 gap-x-4 gap-y-1.5 pt-2 border-t border-[var(--border-subtle)] text-[11px]">
                <div>
                  <dt className="text-[var(--text-muted)]">Source hash (SHA-256)</dt>
                  <dd className="font-mono text-[var(--text-secondary)]" title={cap.sourceHash}>
                    {shortHash(cap.sourceHash)}
                  </dd>
                </div>
                <div>
                  <dt className="text-[var(--text-muted)]">Stored hash (SHA-256)</dt>
                  <dd className="font-mono text-[var(--text-secondary)]" title={cap.contentHash}>
                    {shortHash(cap.contentHash)}
                  </dd>
                </div>
                <div>
                  <dt className="text-[var(--text-muted)]">Provenance</dt>
                  <dd className="text-[var(--text-secondary)]">
                    {[cap.providerName ? `${cap.providerName}${cap.providerVersion ? ` v${cap.providerVersion}` : ''}` : null, cap.normalizationVersion]
                      .filter(Boolean)
                      .join(' · ') || '—'}
                  </dd>
                </div>
              </dl>
            </div>
          );
        })}
      </div>
    </section>
  );
};
