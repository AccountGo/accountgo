// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

import React, { useEffect, useMemo, useRef, useState } from 'react';
import {
  AlertTriangle,
  Camera,
  CheckCircle2,
  ExternalLink,
  FileText,
  Loader2,
  Plus,
  RefreshCw,
  Trash2,
  Upload,
  X,
} from 'lucide-react';
import {
  AssistantCaptureFieldItem,
  AssistantCaptureFields,
  CaptureFieldSources,
  CaptureLineField,
  CapturePaymentHints,
  CaptureSourceTotals,
  describeCaptureWarning,
  describeLinkedCaptureTarget,
  DocumentCaptureCapabilities,
  DocumentCaptureKind,
  DocumentCaptureLinkView,
  DocumentCaptureView,
  EditableAssistantFieldDraft,
  EditableLineDraft,
  evaluateFinancialApplyGates,
  getFieldDescriptors,
  isAssistantKind,
  isBankKind,
  isLowConfidence,
  isSalesKind,
  isSupportedSchemaVersion,
  isVendorKind,
  KIND_LABELS,
  mergeUntouched,
  mergeUntouchedLines,
  parseFieldSources,
  ruleHintFor,
  validateAssistantFieldsForAccept,
  validateBankFieldsForAccept,
  validateSalesFieldsForAccept,
  validateVendorFieldsForAccept,
  VendorCaptureFields,
} from './fields';
import { useDocumentCapture } from './useDocumentCapture';

export interface DocumentCapturePanelProps {
  /** Single kind or selectable list of kinds supported by the host form. */
  kind: DocumentCaptureKind | readonly DocumentCaptureKind[];
  orgSlug: string;
  orgCurrency?: string;
  /** Document scope identifier (e.g. billId or 'new') so late responses after navigation are discarded. */
  scopeKey?: string;
  /** Pre-fetched capabilities from the host to avoid duplicate /capabilities fetches. */
  capabilities?: DocumentCaptureCapabilities | null;
  disabled?: boolean;
  /** Called ONLY after POST /accept succeeds, with the server's canonical response. */
  onAccepted: (canonicalCapture: DocumentCaptureView) => void;
  /** Called when an already-linked duplicate is returned so the host can open that record. */
  onOpenLinkedRecord?: (link: DocumentCaptureLinkView) => void;
  onCancel?: () => void;
}

let lineDraftKey = 1;

function toEditableAssistantFields(
  source: AssistantCaptureFieldItem[] | null | undefined
): EditableAssistantFieldDraft[] {
  if (!source || source.length === 0) return [];
  return source.map((item) => ({
    id: lineDraftKey++,
    label: typeof item.label === 'string' ? item.label : '',
    value: typeof item.value === 'string' ? item.value : '',
    confidence: typeof item.confidence === 'number' && Number.isFinite(item.confidence) ? item.confidence : null,
    box: item.box ?? null,
  }));
}

/** Marks a field the optional field mapper filled in. It has text, not only colour. */
const AiAssistedBadge: React.FC = () => (
  <span
    title="Filled in by AccountGo Assistant from the text of this document. Check it against the original."
    className="ml-1.5 inline-flex items-center rounded-full border border-violet-500/30 bg-violet-500/10 px-1.5 py-0.5 text-[10px] font-semibold text-violet-700 dark:text-violet-300"
  >
    AI-assisted
  </span>
);

function toEditableLines(source: CaptureLineField[] | null | undefined): EditableLineDraft[] {
  if (!source || source.length === 0) return [];
  return source.map((line) => {
    const missingQty = !line.quantity || line.quantity.trim() === '';
    return {
      id: lineDraftKey++,
      description: line.description ?? '',
      quantity: missingQty ? '1' : String(line.quantity),
      unitAmount: line.unitAmount ?? '',
      sourceLineTotal: line.sourceLineTotal ?? '',
      taxHint: line.taxHint ?? '',
      discount: line.discount ?? '',
      priceBasis: line.priceBasis ?? '',
      missingQuantityDefaulted: Boolean(line.missingQuantityDefaulted || missingQty),
    };
  });
}

export const DocumentCapturePanel: React.FC<DocumentCapturePanelProps> = ({
  kind,
  orgSlug,
  orgCurrency,
  scopeKey = 'new',
  capabilities: externalCapabilities,
  disabled = false,
  onAccepted,
  onOpenLinkedRecord,
  onCancel,
}) => {
  const requestedKinds = useMemo<DocumentCaptureKind[]>(
    () => (Array.isArray(kind) ? [...kind] : [kind as DocumentCaptureKind]),
    [kind]
  );
  const [selectedKind, setSelectedKind] = useState<DocumentCaptureKind>(requestedKinds[0]);

  useEffect(() => {
    if (!requestedKinds.includes(selectedKind)) {
      setSelectedKind(requestedKinds[0]);
    }
  }, [requestedKinds, selectedKind]);

  const {
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
    reset,
  } = useDocumentCapture({ orgSlug, scopeKey, capabilities: externalCapabilities });

  const fileInputRef = useRef<HTMLInputElement | null>(null);
  const cameraInputRef = useRef<HTMLInputElement | null>(null);

  // Editable review state initialized synchronously when capture identity/status changes (avoids 1-frame empty flash).
  const [syncedCaptureKey, setSyncedCaptureKey] = useState<string>('');
  const [headerValues, setHeaderValues] = useState<Record<string, string>>({});
  const [lines, setLines] = useState<EditableLineDraft[]>([]);
  const [assistantFields, setAssistantFields] = useState<EditableAssistantFieldDraft[]>([]);
  const [sourceTotals, setSourceTotals] = useState<CaptureSourceTotals | null>(null);
  const [paymentHints, setPaymentHints] = useState<CapturePaymentHints | null>(null);
  const [validationErrors, setValidationErrors] = useState<string[]>([]);
  // What the review form was first filled with, so a later AI-assisted update only changes fields the user hasn't touched.
  const [baseline, setBaseline] = useState<{ header: Record<string, string>; lines: EditableLineDraft[] } | null>(null);
  const aiMergedForRef = useRef('');

  const currentCaptureKey = capture ? `${capture.id}:${capture.status}:${capture.acceptedAt ?? ''}` : '';
  if (currentCaptureKey !== syncedCaptureKey) {
    setSyncedCaptureKey(currentCaptureKey);
    if (!capture) {
      setBaseline(null);
      setHeaderValues({});
      setLines([]);
      setAssistantFields([]);
      setSourceTotals(null);
      setPaymentHints(null);
      setValidationErrors([]);
    } else {
      const raw = (capture.acceptedFields ?? capture.fields ?? {}) as Record<string, unknown>;
      const warningsSet = new Set(capture.warnings ?? []);
      const nextDescriptors = getFieldDescriptors(capture.kind, capture.schemaVersion);
      const nextHeader: Record<string, string> = {};
      for (const desc of nextDescriptors) {
        if (desc.key === 'date' && capture.status !== 'accepted' && warningsSet.has('AMBIGUOUS_DATE')) {
          // Ambiguous extracted dates remain unresolved until the user explicitly confirms YYYY-MM-DD.
          nextHeader[desc.key] = '';
          continue;
        }
        const v = raw[desc.key];
        nextHeader[desc.key] = typeof v === 'string' ? v : '';
      }
      setHeaderValues(nextHeader);
      let baselineLines: EditableLineDraft[] = [];
      if (isVendorKind(capture.kind) || capture.kind === 'sales-invoice') {
        const financial = raw as VendorCaptureFields;
        const nextLines = toEditableLines(financial.lines);
        baselineLines = nextLines.map((l) => ({ ...l }));
        setLines(nextLines);
        setAssistantFields([]);
        setSourceTotals(financial.sourceTotals ?? null);
        setPaymentHints(financial.paymentHints ?? null);
      } else if (isAssistantKind(capture.kind)) {
        const assistant = raw as AssistantCaptureFields;
        setLines([]);
        setAssistantFields(toEditableAssistantFields(assistant.fields));
        setSourceTotals(null);
        setPaymentHints(null);
      } else {
        setLines([]);
        setAssistantFields([]);
        setSourceTotals(null);
        setPaymentHints(null);
      }
      setBaseline({ header: nextHeader, lines: baselineLines });
      setValidationErrors([]);
    }
  }

  // When the optional background mapping finishes, fold its improvements into fields the user hasn't touched.
  useEffect(() => {
    if (!capture || capture.status !== 'ready' || capture.aiMapping?.status !== 'done' || !baseline) return;
    if (aiMergedForRef.current === capture.id) return;
    aiMergedForRef.current = capture.id;
    const raw = (capture.fields ?? {}) as Record<string, unknown>;
    const ambiguousDate = (capture.warnings ?? []).includes('AMBIGUOUS_DATE');
    const nextHeader: Record<string, string> = {};
    for (const desc of getFieldDescriptors(capture.kind, capture.schemaVersion)) {
      // An ambiguous date stays blank until the user confirms it.
      if (desc.key === 'date' && ambiguousDate) continue;
      const v = raw[desc.key];
      nextHeader[desc.key] = typeof v === 'string' ? v : '';
    }
    setHeaderValues((prev) => mergeUntouched(prev, baseline.header, nextHeader));
    if (isVendorKind(capture.kind) || capture.kind === 'sales-invoice') {
      const financial = raw as VendorCaptureFields;
      setLines((prev) => mergeUntouchedLines(prev, baseline.lines, toEditableLines(financial.lines)));
      setSourceTotals(financial.sourceTotals ?? null);
    }
  }, [capture, baseline]);

  const fieldSources = useMemo<CaptureFieldSources>(
    () =>
      capture?.status === 'ready' && capture.aiMapping?.status === 'done'
        ? parseFieldSources((capture.fields as { fieldSources?: unknown } | null)?.fieldSources)
        : {},
    [capture]
  );
  const isAiField = (path: string) => fieldSources[path] === 'ai';
  const applyRuleValue = (path: string, value: string) => {
    const lineMatch = /^lines\[(\d+)\]\.(\w+)$/.exec(path);
    if (lineMatch) {
      const index = Number(lineMatch[1]);
      setLines((prev) => prev.map((l, i) => (i === index ? { ...l, [lineMatch[2]]: value } : l)));
    } else {
      setHeaderValues((prev) => ({ ...prev, [path]: value }));
    }
  };
  const renderRuleHint = (path: string, currentValue: string) => {
    const hint = capture?.status === 'ready' ? ruleHintFor(capture.aiMapping, path, currentValue) : null;
    if (hint === null) return null;
    return (
      <div className="mt-0.5 flex flex-wrap items-center gap-1.5 text-[10px] text-[var(--text-muted)]">
        <span>rules found: <span className="font-mono">{hint || '(blank)'}</span></span>
        <button
          type="button"
          aria-label={`Use the rules' value for ${path}`}
          onClick={() => applyRuleValue(path, hint)}
          className="font-semibold text-[var(--brand-primary)] hover:opacity-80"
        >
          use this
        </button>
      </div>
    );
  };

  const allowedKinds = useMemo(() => {
    if (!capabilities?.enabled || !capabilities.providerAvailable) return [];
    return requestedKinds.filter((k) => capabilities.kinds.includes(k));
  }, [capabilities, requestedKinds]);

  const newScanAvailable = !disabled && allowedKinds.length > 0;

  if (capabilitiesLoading) {
    return (
      <div className="rounded-2xl border border-[var(--border-subtle)] bg-[var(--bg-surface)] p-4 flex items-center gap-2 text-sm text-[var(--text-secondary)]">
        <Loader2 className="w-4 h-4 animate-spin text-[var(--brand-primary)]" />
        <span>Checking document capture availability...</span>
      </div>
    );
  }

  // Suppress new-scan controls when capabilities disable them and no capture is loaded.
  if (!newScanAvailable && !capture) {
    return null;
  }

  const handleFilePicked = (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0] ?? null;
    // Allow re-picking the same file name after an error.
    e.target.value = '';
    if (!file) return;
    if (selectFile(file)) {
      void upload(selectedKind, { file });
    }
  };

  const handleAcceptClick = async () => {
    if (!capture) return;
    setValidationErrors([]);

    if (!isSupportedSchemaVersion(capture.schemaVersion)) {
      setValidationErrors([`Unsupported schema version (${capture.schemaVersion}). Cannot accept or apply.`]);
      return;
    }
    if (!capture.bytesAvailable) {
      setValidationErrors(['Source file bytes are no longer available.']);
      return;
    }

    if (isVendorKind(capture.kind)) {
      const checked = validateVendorFieldsForAccept(
        {
          vendorName: headerValues.vendorName ?? '',
          vendorInvoiceNo: headerValues.vendorInvoiceNo ?? '',
          date: headerValues.date ?? '',
          dueDate: headerValues.dueDate ?? '',
          referenceNo: headerValues.referenceNo ?? '',
          currency: headerValues.currency ?? '',
          notes: headerValues.notes ?? '',
        },
        lines,
        sourceTotals,
        paymentHints
      );
      if (!checked.valid) {
        setValidationErrors(checked.errors);
        return;
      }
      const canonical = await accept(checked.payload as Record<string, unknown>);
      if (canonical) {
        onAccepted(canonical);
      }
      return;
    }

    if (isSalesKind(capture.kind)) {
      const checked = validateSalesFieldsForAccept(
        {
          customerName: headerValues.customerName ?? '',
          sourceInvoiceNo: headerValues.sourceInvoiceNo ?? '',
          date: headerValues.date ?? '',
          dueDate: headerValues.dueDate ?? '',
          referenceNo: headerValues.referenceNo ?? '',
          currency: headerValues.currency ?? '',
          notes: headerValues.notes ?? '',
        },
        lines,
        sourceTotals
      );
      if (!checked.valid) {
        setValidationErrors(checked.errors);
        return;
      }
      const canonical = await accept(checked.payload as Record<string, unknown>);
      if (canonical) {
        onAccepted(canonical);
      }
      return;
    }

    if (isBankKind(capture.kind)) {
      const checked = validateBankFieldsForAccept({
        name: headerValues.name ?? '',
        bankName: headerValues.bankName ?? '',
        accountNumber: headerValues.accountNumber ?? '',
        routingNumber: headerValues.routingNumber ?? '',
        iban: headerValues.iban ?? '',
        swift: headerValues.swift ?? '',
      });
      if (!checked.valid) {
        setValidationErrors(checked.errors);
        return;
      }
      const canonical = await accept(checked.payload as Record<string, unknown>);
      if (canonical) {
        onAccepted(canonical);
      }
      return;
    }

    if (isAssistantKind(capture.kind)) {
      const checked = validateAssistantFieldsForAccept(assistantFields);
      if (!checked.valid) {
        setValidationErrors(checked.errors);
        return;
      }
      const canonical = await accept(checked.payload as Record<string, unknown>);
      if (canonical) {
        onAccepted(canonical);
      }
      return;
    }

    setValidationErrors([`No typed review schema handler is registered in this panel for kind "${capture.kind}".`]);
  };

  const handleUseAlreadyAccepted = () => {
    if (!capture || capture.status !== 'accepted' || !capture.canApply) return;
    onAccepted(capture);
  };

  const descriptors = capture ? getFieldDescriptors(capture.kind, capture.schemaVersion) : [];
  const schemaSupported = capture ? isSupportedSchemaVersion(capture.schemaVersion) : true;
  const kindHasTypedHandler = capture
    ? isVendorKind(capture.kind) || isSalesKind(capture.kind) || isBankKind(capture.kind) || isAssistantKind(capture.kind)
    : true;
  const gates =
    capture && !isBankKind(capture.kind) && !isAssistantKind(capture.kind)
      ? evaluateFinancialApplyGates(capture, orgCurrency)
      : null;
  const isAlreadyLinked = Boolean(capture?.link);

  return (
    <section
      aria-label="Document capture"
      className="rounded-2xl border border-[var(--border-subtle)] bg-[var(--bg-surface)] p-5 shadow-sm space-y-4"
    >
      <div className="flex items-start justify-between gap-2">
        <div className="min-w-0 flex-1">
          <h3 className="text-sm font-semibold text-[var(--text-main)]">Scan or upload document</h3>
          <p className="text-xs text-[var(--text-secondary)]">
            Extract fields for review. Accepting fills the draft form only and never saves, approves, or posts automatically.
          </p>
        </div>
        {onCancel && (
          <button
            type="button"
            onClick={() => {
              reset();
              onCancel();
            }}
            aria-label="Close document capture"
            className="shrink-0 p-1.5 rounded-lg text-[var(--text-muted)] hover:text-[var(--text-main)] hover:bg-[var(--bg-surface-hover)] transition"
          >
            <X className="w-4 h-4" />
          </button>
        )}
      </div>

      {newScanAvailable && (
        <div className="flex flex-wrap items-center gap-3">
          {allowedKinds.length > 1 && (
            <div className="inline-flex rounded-xl border border-[var(--border-subtle)] bg-[var(--bg-subtle)] p-0.5">
              {allowedKinds.map((k) => (
                <button
                  key={k}
                  type="button"
                  disabled={!!busy}
                  onClick={() => setSelectedKind(k)}
                  className={`px-3 py-1.5 text-xs font-semibold rounded-lg transition ${
                    selectedKind === k
                      ? 'bg-[var(--bg-surface)] text-[var(--text-main)] shadow-sm'
                      : 'text-[var(--text-secondary)] hover:text-[var(--text-main)]'
                  }`}
                >
                  {KIND_LABELS[k]}
                </button>
              ))}
            </div>
          )}

          <input
            ref={fileInputRef}
            type="file"
            accept="image/jpeg,image/png,application/pdf"
            aria-label="Upload image or PDF"
            className="sr-only"
            disabled={!!busy || disabled}
            onChange={handleFilePicked}
          />
          <input
            ref={cameraInputRef}
            type="file"
            accept="image/*"
            capture="environment"
            aria-label="Take photo with camera"
            className="sr-only"
            disabled={!!busy || disabled}
            onChange={handleFilePicked}
          />

          <button
            type="button"
            disabled={!!busy || disabled}
            onClick={() => fileInputRef.current?.click()}
            className="inline-flex items-center gap-1.5 px-3.5 py-2 text-xs font-semibold rounded-xl bg-[var(--brand-subtle)] text-[var(--brand-primary)] hover:opacity-85 transition disabled:opacity-50"
          >
            <Upload className="w-3.5 h-3.5" />
            Choose image or PDF
          </button>

          <button
            type="button"
            disabled={!!busy || disabled}
            onClick={() => cameraInputRef.current?.click()}
            className="inline-flex items-center gap-1.5 px-3.5 py-2 text-xs font-semibold rounded-xl border border-[var(--border-subtle)] bg-[var(--bg-subtle)] text-[var(--text-main)] hover:bg-[var(--bg-surface-hover)] transition disabled:opacity-50"
          >
            <Camera className="w-3.5 h-3.5" />
            Take photo
          </button>
        </div>
      )}

      {/* Indeterminate progress bar during upload/extraction */}
      {busy === 'uploading' && (
        <div role="status" className="rounded-xl border border-[var(--border-subtle)] bg-[var(--bg-subtle)] p-4 flex items-center gap-3 text-sm text-[var(--text-secondary)]">
          <Loader2 className="w-4 h-4 animate-spin text-[var(--brand-primary)] shrink-0" />
          <span>Uploading and extracting fields...</span>
        </div>
      )}

      {/* Local preview (image object URL or PDF filename + page count); hidden once rejected */}
      {capture?.status !== 'rejected' &&
        (localPreviewUrl || selectedFile?.type === 'application/pdf' || capture?.contentType === 'application/pdf') && (
        <div className="rounded-xl border border-[var(--border-subtle)] bg-[var(--bg-subtle)] p-3 flex items-center gap-4">
          {localPreviewUrl ? (
            <img
              src={localPreviewUrl}
              alt="Captured document preview"
              className="h-20 w-20 object-cover rounded-lg border border-[var(--border-subtle)] shrink-0"
            />
          ) : (
            <div className="h-14 w-14 rounded-lg border border-[var(--border-subtle)] bg-[var(--bg-surface)] flex items-center justify-center shrink-0">
              <FileText className="w-6 h-6 text-[var(--brand-primary)]" />
            </div>
          )}
          <div className="min-w-0 flex-1 text-xs space-y-0.5">
            <div className="font-semibold text-[var(--text-main)] truncate">
              {selectedFile?.name || (capture?.contentType === 'application/pdf' ? 'Uploaded PDF document' : 'Uploaded image')}
            </div>
            <div className="text-[var(--text-secondary)]">
              {capture?.contentType === 'application/pdf' || selectedFile?.type === 'application/pdf'
                ? `${capture?.pageCount ?? 1} page${(capture?.pageCount ?? 1) === 1 ? '' : 's'} · PDF`
                : 'Image ready for review'}
            </div>
          </div>
        </div>
      )}

      {/* Panel-scoped errors (all §6 non-entitlement errors + recovery buttons) */}
      {error && (
        <div role="alert" className="rounded-xl border border-rose-500/25 bg-rose-500/10 p-3.5 text-sm text-rose-600 dark:text-rose-400 space-y-2">
          <div className="flex items-start gap-2">
            <AlertTriangle className="w-4 h-4 shrink-0 mt-0.5" />
            <span className="flex-1">{error.message}</span>
          </div>
          <div className="flex flex-wrap gap-2">
            {error.canCheckStatus && (capture?.id || lastCaptureId) && (
              <button
                type="button"
                disabled={!!busy}
                onClick={() => void checkStatus()}
                className="inline-flex items-center gap-1.5 px-3 py-1.5 text-xs font-semibold rounded-lg bg-[var(--bg-surface)] border border-[var(--border-subtle)] text-[var(--text-main)] hover:bg-[var(--bg-surface-hover)] transition"
              >
                <RefreshCw className="w-3.5 h-3.5" />
                Check status
              </button>
            )}
            {error.canRetryUpload && selectedFile && newScanAvailable && (
              <button
                type="button"
                disabled={!!busy}
                onClick={() => void upload(selectedKind)}
                className="inline-flex items-center gap-1.5 px-3 py-1.5 text-xs font-semibold rounded-lg bg-[var(--bg-surface)] border border-[var(--border-subtle)] text-[var(--text-main)] hover:bg-[var(--bg-surface-hover)] transition"
              >
                Retry upload
              </button>
            )}
            {error.canRetryAccept && capture && (
              <button
                type="button"
                disabled={!!busy}
                onClick={() => void handleAcceptClick()}
                className="inline-flex items-center gap-1.5 px-3 py-1.5 text-xs font-semibold rounded-lg bg-[var(--bg-surface)] border border-[var(--border-subtle)] text-[var(--text-main)] hover:bg-[var(--bg-surface-hover)] transition"
              >
                Retry accept
              </button>
            )}
          </div>
        </div>
      )}

      {/* Already-linked duplicate notice */}
      {capture && isAlreadyLinked && capture.link && (
        <div role="status" className="rounded-xl border border-amber-500/30 bg-amber-500/10 p-3.5 text-sm text-amber-700 dark:text-amber-300 flex flex-wrap items-center justify-between gap-3">
          <span>
            This document was already accepted and linked to {describeLinkedCaptureTarget(capture)}.
          </span>
          {onOpenLinkedRecord && (
            <button
              type="button"
              onClick={() => onOpenLinkedRecord(capture.link!)}
              className="inline-flex items-center gap-1.5 px-3 py-1.5 text-xs font-semibold rounded-lg bg-[var(--bg-surface)] border border-[var(--border-subtle)] text-[var(--text-main)] hover:bg-[var(--bg-surface-hover)] transition"
            >
              <ExternalLink className="w-3.5 h-3.5" />
              Open linked record
            </button>
          )}
        </div>
      )}

      {/* Review form once capture is loaded */}
      {capture && capture.status !== 'rejected' && !isAlreadyLinked && (
        <div className="space-y-4 pt-2 border-t border-[var(--border-subtle)]">
          {/* Optional AI field mapping still running: the rule result is already here and stands if it fails. */}
          {capture.status === 'ready' && capture.aiMapping?.status === 'pending' && (
            <p role="status" className="flex items-center gap-1.5 text-xs text-[var(--text-secondary)]">
              <Loader2 className="w-3.5 h-3.5 animate-spin" />
              Improving with AccountGo Assistant…
            </p>
          )}

          {/* Warnings */}
          {capture.warnings && capture.warnings.length > 0 && (
            <div className="rounded-xl border border-amber-500/25 bg-amber-500/10 p-3 space-y-1 text-xs text-amber-800 dark:text-amber-300">
              {capture.warnings.map((w) => (
                <div key={w} className="flex items-start gap-1.5">
                  <AlertTriangle className="w-3.5 h-3.5 shrink-0 mt-0.5" />
                  <span>{describeCaptureWarning(w)}</span>
                </div>
              ))}
            </div>
          )}

          {/* Unknown schema or missing bytes banner */}
          {(!schemaSupported || !capture.bytesAvailable) && (
            <div role="alert" className="rounded-xl border border-rose-500/25 bg-rose-500/10 p-3 text-xs text-rose-600 dark:text-rose-400">
              {!schemaSupported
                ? `Schema version ${capture.schemaVersion} is not supported by this version of the app. Application is disabled.`
                : 'The stored file bytes are no longer available, so this capture cannot be accepted or applied.'}
            </div>
          )}

          {/* Document-class / multi-doc / currency gate explanations */}
          {gates && gates.reasons.length > 0 && (
            <div className="rounded-xl border border-amber-500/25 bg-amber-500/10 p-3 space-y-1 text-xs text-amber-800 dark:text-amber-300">
              {gates.reasons.map((r, i) => (
                <p key={i}>{r}</p>
              ))}
            </div>
          )}

          {/* Editable header fields */}
          {schemaSupported && descriptors.length > 0 && (
            <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-3">
              {descriptors.map((desc) => {
                const readOnly = capture.status === 'accepted';
                return (
                  <div key={desc.key} className={desc.type === 'textarea' ? 'sm:col-span-2 lg:col-span-3' : undefined}>
                    <div className="flex items-center justify-between gap-1 mb-1">
                      <label htmlFor={`capture-field-${desc.key}`} className="text-xs font-medium text-[var(--text-secondary)]">
                        {desc.label}
                        {desc.requiredForAccept && <span className="text-rose-500 ml-0.5">*</span>}
                        {isAiField(desc.key) && <AiAssistedBadge />}
                      </label>
                    </div>
                    {desc.type === 'textarea' ? (
                      <textarea
                        id={`capture-field-${desc.key}`}
                        rows={2}
                        maxLength={desc.maxLength}
                        readOnly={readOnly}
                        value={headerValues[desc.key] ?? ''}
                        onChange={(e) => setHeaderValues((prev) => ({ ...prev, [desc.key]: e.target.value }))}
                        className="w-full px-3 py-2 text-xs bg-[var(--bg-subtle)] border border-[var(--border-subtle)] rounded-lg text-[var(--text-main)]"
                      />
                    ) : (
                      <input
                        id={`capture-field-${desc.key}`}
                        type={desc.type === 'date' ? 'date' : 'text'}
                        maxLength={desc.maxLength}
                        readOnly={readOnly}
                        placeholder={desc.placeholder}
                        value={headerValues[desc.key] ?? ''}
                        onChange={(e) => setHeaderValues((prev) => ({ ...prev, [desc.key]: e.target.value }))}
                        className="w-full px-3 py-2 text-xs bg-[var(--bg-subtle)] border border-[var(--border-subtle)] rounded-lg text-[var(--text-main)]"
                      />
                    )}
                    {renderRuleHint(desc.key, headerValues[desc.key] ?? '')}
                  </div>
                );
              })}
            </div>
          )}

          {/* Bank detail review guidance */}
          {schemaSupported && isBankKind(capture.kind) && (
            <div className="rounded-xl border border-sky-500/25 bg-sky-500/10 p-3 text-xs text-sky-900 dark:text-sky-200">
              Bank capture populates bank details only. Opening balance, opening-balance date, and saving remain manual.
            </div>
          )}

          {/* Assistant attachment masked fields review */}
          {schemaSupported && isAssistantKind(capture.kind) && (
            <div className="space-y-3">
              <div className="rounded-xl border border-sky-500/25 bg-sky-500/10 p-3 text-xs text-sky-900 dark:text-sky-200">
                Sensitive numbers are masked automatically, though masking is not complete PII detection. Review, edit, or remove any field before attaching. Only masked text fields are shared with the assistant (never the original image or PDF).
              </div>
              <div className="flex items-center justify-between">
                <h4 className="text-xs font-semibold uppercase tracking-wider text-[var(--text-muted)]">
                  Masked attachment fields
                </h4>
                {capture.status !== 'accepted' && (
                  <button
                    type="button"
                    onClick={() =>
                      setAssistantFields((prev) => [
                        ...prev,
                        { id: lineDraftKey++, label: '', value: '', confidence: null, box: null },
                      ])
                    }
                    className="inline-flex items-center gap-1 text-xs font-semibold text-[var(--brand-primary)] hover:opacity-80"
                  >
                    <Plus className="w-3.5 h-3.5" />
                    Add field
                  </button>
                )}
              </div>
              {assistantFields.length > 0 && (
                <div className="space-y-2">
                  {assistantFields.map((item, idx) => (
                    <div
                      key={item.id}
                      className="grid grid-cols-1 sm:grid-cols-[1fr_1.5fr_auto] gap-2 items-start rounded-xl border border-[var(--border-subtle)] bg-[var(--bg-subtle)] p-2.5"
                    >
                      <div className="space-y-1">
                        <input
                          type="text"
                          aria-label={`Attachment field ${idx + 1} label`}
                          placeholder="Label"
                          readOnly={capture.status === 'accepted'}
                          value={item.label}
                          onChange={(e) =>
                            setAssistantFields((prev) =>
                              prev.map((f, i) => (i === idx ? { ...f, label: e.target.value } : f))
                            )
                          }
                          className="w-full px-2.5 py-1.5 text-xs bg-[var(--bg-surface)] border border-[var(--border-subtle)] rounded-lg text-[var(--text-main)]"
                        />
                        <div className="flex items-center gap-1.5 text-[10px]">
                          {isLowConfidence(item.confidence) && (
                            <span className="px-1.5 py-0.5 rounded bg-amber-500/15 text-amber-700 dark:text-amber-300 font-semibold">
                              Check this
                            </span>
                          )}
                          {item.box && (
                            <span className="text-[var(--text-muted)]">p.{item.box.page}</span>
                          )}
                        </div>
                      </div>
                      <input
                        type="text"
                        aria-label={`Attachment field ${idx + 1} value`}
                        placeholder="Value"
                        readOnly={capture.status === 'accepted'}
                        value={item.value}
                        onChange={(e) =>
                          setAssistantFields((prev) =>
                            prev.map((f, i) => (i === idx ? { ...f, value: e.target.value } : f))
                          )
                        }
                        className="w-full px-2.5 py-1.5 text-xs bg-[var(--bg-surface)] border border-[var(--border-subtle)] rounded-lg text-[var(--text-main)]"
                      />
                      {capture.status !== 'accepted' && (
                        <button
                          type="button"
                          aria-label={`Remove attachment field ${idx + 1}`}
                          onClick={() => setAssistantFields((prev) => prev.filter((_, i) => i !== idx))}
                          className="p-1.5 text-[var(--text-muted)] hover:text-rose-500 rounded"
                        >
                          <Trash2 className="w-3.5 h-3.5" />
                        </button>
                      )}
                    </div>
                  ))}
                </div>
              )}
            </div>
          )}

          {/* Reference-only & historical-import warning for sales-invoice captures */}
          {schemaSupported && isSalesKind(capture.kind) && (
            <div className="rounded-xl border border-sky-500/25 bg-sky-500/10 p-3 text-xs text-sky-900 dark:text-sky-200">
              Sales capture creates a new draft from a reference document, not a historical import. The source invoice number is kept as a reference only and never replaces AccountGo invoice numbering.
            </div>
          )}

          {/* Editable financial lines */}
          {schemaSupported && (isVendorKind(capture.kind) || isSalesKind(capture.kind)) && (
            <div className="space-y-2">
              <div className="flex items-center justify-between">
                <h4 className="text-xs font-semibold uppercase tracking-wider text-[var(--text-muted)]">
                  Extracted lines
                </h4>
                {capture.status !== 'accepted' && (
                  <button
                    type="button"
                    onClick={() =>
                      setLines((prev) => [
                        ...prev,
                        {
                          id: lineDraftKey++,
                          description: '',
                          quantity: '1',
                          unitAmount: '',
                          sourceLineTotal: '',
                          taxHint: '',
                          discount: '',
                          priceBasis: '',
                          missingQuantityDefaulted: false,
                        },
                      ])
                    }
                    className="inline-flex items-center gap-1 text-xs font-semibold text-[var(--brand-primary)] hover:opacity-80"
                  >
                    <Plus className="w-3.5 h-3.5" />
                    Add line
                  </button>
                )}
              </div>

              {lines.length > 0 && (
                <div className="overflow-x-auto border border-[var(--border-subtle)] rounded-xl">
                  <table className="w-full text-left text-xs border-collapse">
                    <thead>
                      <tr className="bg-[var(--bg-subtle)] text-[var(--text-muted)] border-b border-[var(--border-subtle)]">
                        <th className="py-2 px-3">Description</th>
                        <th className="py-2 px-2 w-24 text-right">Qty</th>
                        <th className="py-2 px-2 w-28 text-right">Unit amount</th>
                        <th className="py-2 px-2 w-24 text-right">Source total</th>
                        <th className="py-2 px-2 w-20 text-right">Disc %</th>
                        <th className="py-2 px-2 w-10"></th>
                      </tr>
                    </thead>
                    <tbody className="divide-y divide-[var(--border-subtle)]">
                      {lines.map((line, idx) => (
                        <tr key={line.id}>
                          <td className="py-1.5 px-3">
                            <input
                              type="text"
                              aria-label={`Captured line ${idx + 1} description`}
                              readOnly={capture.status === 'accepted'}
                              value={line.description}
                              onChange={(e) =>
                                setLines((prev) => prev.map((l, i) => (i === idx ? { ...l, description: e.target.value } : l)))
                              }
                              className="w-full px-2 py-1 text-xs bg-[var(--bg-subtle)] border border-[var(--border-subtle)] rounded text-[var(--text-main)]"
                            />
                            {(['description', 'quantity', 'unitAmount', 'sourceLineTotal', 'discount'] as const).some((f) => isAiField(`lines[${idx}].${f}`)) && (
                              <AiAssistedBadge />
                            )}
                            {(['description', 'quantity', 'unitAmount', 'sourceLineTotal', 'discount'] as const).map((f) => (
                              <React.Fragment key={f}>{renderRuleHint(`lines[${idx}].${f}`, line[f])}</React.Fragment>
                            ))}
                          </td>
                          <td className="py-1.5 px-2">
                            <input
                              type="text"
                              inputMode="decimal"
                              aria-label={`Captured line ${idx + 1} quantity`}
                              readOnly={capture.status === 'accepted'}
                              value={line.quantity}
                              onChange={(e) =>
                                setLines((prev) =>
                                  prev.map((l, i) =>
                                    i === idx ? { ...l, quantity: e.target.value, missingQuantityDefaulted: false } : l
                                  )
                                )
                              }
                              className="w-full px-2 py-1 text-xs text-right bg-[var(--bg-subtle)] border border-[var(--border-subtle)] rounded text-[var(--text-main)]"
                            />
                            {line.missingQuantityDefaulted && (
                              <div className="text-[10px] text-amber-600 dark:text-amber-400 text-right mt-0.5">
                                Defaulted to 1
                              </div>
                            )}
                          </td>
                          <td className="py-1.5 px-2">
                            <input
                              type="text"
                              inputMode="decimal"
                              aria-label={`Captured line ${idx + 1} unit amount`}
                              readOnly={capture.status === 'accepted'}
                              value={line.unitAmount}
                              onChange={(e) =>
                                setLines((prev) => prev.map((l, i) => (i === idx ? { ...l, unitAmount: e.target.value } : l)))
                              }
                              className="w-full px-2 py-1 text-xs text-right bg-[var(--bg-subtle)] border border-[var(--border-subtle)] rounded text-[var(--text-main)]"
                            />
                          </td>
                          <td className="py-1.5 px-2 text-right font-mono text-[var(--text-secondary)]">
                            {line.sourceLineTotal || '—'}
                          </td>
                          <td className="py-1.5 px-2">
                            <input
                              type="text"
                              inputMode="decimal"
                              aria-label={`Captured line ${idx + 1} discount`}
                              readOnly={capture.status === 'accepted'}
                              value={line.discount}
                              onChange={(e) =>
                                setLines((prev) => prev.map((l, i) => (i === idx ? { ...l, discount: e.target.value } : l)))
                              }
                              className="w-full px-2 py-1 text-xs text-right bg-[var(--bg-subtle)] border border-[var(--border-subtle)] rounded text-[var(--text-main)]"
                            />
                          </td>
                          <td className="py-1.5 px-2 text-center">
                            {capture.status !== 'accepted' && (
                              <button
                                type="button"
                                aria-label={`Remove captured line ${idx + 1}`}
                                onClick={() => setLines((prev) => prev.filter((_, i) => i !== idx))}
                                className="p-1 text-[var(--text-muted)] hover:text-rose-500 rounded"
                              >
                                <Trash2 className="w-3.5 h-3.5" />
                              </button>
                            )}
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )}
            </div>
          )}

          {/* Review-only source totals & payment hints */}
          {(sourceTotals || paymentHints) && (
            <div className="grid grid-cols-1 sm:grid-cols-2 gap-3 text-xs">
              {sourceTotals && (
                <div className="rounded-xl border border-[var(--border-subtle)] bg-[var(--bg-subtle)] p-3 space-y-1">
                  <div className="font-semibold text-[var(--text-main)]">
                    Source totals (review only · {sourceTotals.priceBasis || 'unknown'} tax basis)
                    {Object.keys(fieldSources).some((path) => path.startsWith('sourceTotals.')) && <AiAssistedBadge />}
                  </div>
                  <div className="flex justify-between text-[var(--text-secondary)]">
                    <span>Subtotal</span>
                    <span className="font-mono">{sourceTotals.subtotal ?? '—'}</span>
                  </div>
                  <div className="flex justify-between text-[var(--text-secondary)]">
                    <span>Tax</span>
                    <span className="font-mono">{sourceTotals.taxTotal ?? '—'}</span>
                  </div>
                  {sourceTotals.discountTotal && (
                    <div className="flex justify-between text-[var(--text-secondary)]">
                      <span>Discount</span>
                      <span className="font-mono">{sourceTotals.discountTotal}</span>
                    </div>
                  )}
                  {sourceTotals.additionalCharges && (
                    <div className="flex justify-between text-[var(--text-secondary)]">
                      <span>Additional charges</span>
                      <span className="font-mono">{sourceTotals.additionalCharges}</span>
                    </div>
                  )}
                  <div className="flex justify-between font-semibold text-[var(--text-main)] border-t border-[var(--border-subtle)] pt-1">
                    <span>Grand total</span>
                    <span className="font-mono">{sourceTotals.grandTotal ?? '—'}</span>
                  </div>
                </div>
              )}

              {paymentHints && (
                <div className="rounded-xl border border-[var(--border-subtle)] bg-[var(--bg-subtle)] p-3 space-y-1">
                  <div className="font-semibold text-[var(--text-main)]">Payment hints (unverified)</div>
                  <div className="text-[var(--text-secondary)]">
                    Status: <span className="font-medium text-[var(--text-main)]">{paymentHints.paymentStatus ?? 'unknown'}</span>
                    {paymentHints.amountPaid ? ` · Paid: ${paymentHints.amountPaid}` : ''}
                    {paymentHints.paymentMethod ? ` · Method: ${paymentHints.paymentMethod}` : ''}
                  </div>
                  <p className="text-[11px] text-[var(--text-muted)]">
                    Receipt capture creates a bill draft only. Approving a bill does not record payment or settle it.
                  </p>
                </div>
              )}
            </div>
          )}

          {/* Inline validation errors before POST /accept */}
          {validationErrors.length > 0 && (
            <div role="alert" className="rounded-xl border border-rose-500/25 bg-rose-500/10 p-3 text-xs text-rose-600 dark:text-rose-400 space-y-1">
              {validationErrors.map((msg, i) => (
                <div key={i}>{msg}</div>
              ))}
            </div>
          )}

          {/* Action buttons */}
          <div className="flex flex-wrap items-center justify-end gap-2 pt-2">
            {capture.status === 'ready' && (
              <>
                <button
                  type="button"
                  disabled={!!busy}
                  onClick={() => void reject()}
                  className="px-3.5 py-2 text-xs font-semibold rounded-xl border border-[var(--border-subtle)] bg-[var(--bg-subtle)] text-[var(--text-secondary)] hover:text-rose-500 transition disabled:opacity-50"
                >
                  Reject
                </button>
                <button
                  type="button"
                  disabled={!!busy || !schemaSupported || !kindHasTypedHandler || !capture.bytesAvailable || !capture.canAccept}
                  onClick={() => void handleAcceptClick()}
                  className="inline-flex items-center gap-1.5 px-4 py-2 text-xs font-semibold rounded-xl bg-[var(--brand-primary)] text-white hover:opacity-90 transition disabled:opacity-50"
                >
                  {busy === 'accepting' ? (
                    <Loader2 className="w-3.5 h-3.5 animate-spin" />
                  ) : (
                    <CheckCircle2 className="w-3.5 h-3.5" />
                  )}
                  Accept & apply to draft
                </button>
              </>
            )}
            {capture.status === 'accepted' && (
              <button
                type="button"
                disabled={!!busy || !schemaSupported || !kindHasTypedHandler || !capture.bytesAvailable || !capture.canApply}
                onClick={handleUseAlreadyAccepted}
                className="inline-flex items-center gap-1.5 px-4 py-2 text-xs font-semibold rounded-xl bg-[var(--brand-primary)] text-white hover:opacity-90 transition disabled:opacity-50"
              >
                <CheckCircle2 className="w-3.5 h-3.5" />
                Apply accepted fields
              </button>
            )}
          </div>
        </div>
      )}

      {capture && capture.status === 'rejected' && (
        <div role="status" className="rounded-xl border border-[var(--border-subtle)] bg-[var(--bg-subtle)] p-3 text-xs text-[var(--text-secondary)]">
          This capture was rejected. Upload another file if you want to scan again.
        </div>
      )}
    </section>
  );
};
