// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

import React, { useRef, useState } from 'react';
import { Loader2, Sparkles } from 'lucide-react';
import { ModalShell } from './ModalShell';
import {
  AI_DISCLOSURE_URL,
  AI_MAPPING_CONSENT_TEXT,
  CaptureAiMappingSettings,
  describeUnavailableReason,
} from '../lib/captureAiMapping';

interface Props {
  settings: CaptureAiMappingSettings;
  update: (turnOn: boolean) => Promise<{ ok: true } | { ok: false; message: string }>;
}

/**
 * Settings → Documents. The owner turns AI field reading on or off. Everyone else sees the state read-only.
 * Turning it on asks for consent to the published wording; turning it off is one click and takes effect immediately.
 */
export const CaptureAiMappingSettingsTab: React.FC<Props> = ({ settings, update }) => {
  const [dialogOpen, setDialogOpen] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const toggleRef = useRef<HTMLButtonElement>(null);
  const canToggle = settings.canManage && settings.available;

  const run = async (turnOn: boolean) => {
    setBusy(true);
    setError(null);
    const result = await update(turnOn);
    setBusy(false);
    if (!result.ok) {
      setError(result.message);
      return false;
    }
    return true;
  };

  const closeDialog = () => {
    if (busy) return;
    setDialogOpen(false);
    // The modal shell restores focus to the opener; this keeps it on the toggle even if the trigger re-rendered.
    requestAnimationFrame(() => toggleRef.current?.focus());
  };

  const onToggle = async () => {
    if (!canToggle || busy) return;
    if (settings.enabled) await run(false);
    else {
      setError(null);
      setDialogOpen(true);
    }
  };

  const confirm = async () => {
    if (await run(true)) {
      setDialogOpen(false);
      requestAnimationFrame(() => toggleRef.current?.focus());
    }
  };

  return (
    <section className="rounded-2xl border border-[var(--border-subtle)] bg-[var(--bg-surface)] p-5 sm:p-6 space-y-4" aria-labelledby="ai-mapping-heading">
      <div className="flex items-start justify-between gap-4">
        <div className="min-w-0 space-y-1">
          <h2 id="ai-mapping-heading" className="flex items-center gap-2 text-base font-bold text-[var(--text-main)]">
            <Sparkles className="h-4 w-4 text-[var(--brand-primary)]" aria-hidden="true" />
            AI field reading
          </h2>
          <p id="ai-mapping-description" className="text-sm text-[var(--text-secondary)]">
            Reads scanned documents more accurately. Fields it fills are labelled “AI-assisted” and nothing is saved until you review it.
          </p>
        </div>
        <button
          ref={toggleRef}
          type="button"
          role="switch"
          aria-checked={settings.enabled}
          aria-labelledby="ai-mapping-heading"
          aria-describedby="ai-mapping-description"
          disabled={!canToggle || busy}
          onClick={() => void onToggle()}
          className={`relative inline-flex h-6 w-11 shrink-0 items-center rounded-full transition focus:outline-none focus-visible:ring-2 focus-visible:ring-[var(--brand-ring)] disabled:cursor-not-allowed disabled:opacity-50 ${
            settings.enabled ? 'bg-[var(--brand-primary)]' : 'bg-[var(--border-subtle)]'
          }`}
        >
          <span className={`inline-block h-5 w-5 rounded-full bg-white shadow transition-transform ${settings.enabled ? 'translate-x-5' : 'translate-x-0.5'}`} />
        </button>
      </div>

      {!settings.available && <p role="status" className="text-sm text-[var(--text-secondary)]">{describeUnavailableReason(settings.unavailableReason)}</p>}
      {settings.available && !settings.canManage && (
        <p role="status" className="text-sm text-[var(--text-secondary)]">
          {settings.enabled ? 'On.' : 'Off.'} Only the workspace owner can change this.
        </p>
      )}
      {settings.available && settings.canManage && settings.needsReconsent && !settings.enabled && (
        <p role="status" className="text-sm text-[var(--text-secondary)]">The wording changed since you agreed. Turn it on again to continue.</p>
      )}
      {settings.enabled && settings.consentedAt && (
        <p className="text-xs text-[var(--text-muted)]">
          Turned on {new Date(settings.consentedAt).toLocaleDateString()}
          {settings.consentedBy ? ` by ${settings.consentedBy}` : ''}.
        </p>
      )}
      {error && !dialogOpen && <p role="alert" className="text-sm text-red-600">{error}</p>}

      {dialogOpen && (
        <ModalShell
          open
          onClose={closeDialog}
          closeOnEscape={!busy}
          closeOnBackdrop={!busy}
          maxWidthClassName="max-w-md"
          aria-labelledby="ai-mapping-consent-title"
          aria-describedby="ai-mapping-consent-body"
          panelClassName="p-6 sm:p-6 space-y-4"
        >
          <h2 id="ai-mapping-consent-title" className="text-base font-bold text-[var(--text-main)]">Improve field reading with AI?</h2>
          <div id="ai-mapping-consent-body" className="space-y-3 text-sm text-[var(--text-secondary)]">
            <p>{AI_MAPPING_CONSENT_TEXT}</p>
            <ul className="list-disc space-y-1 pl-5">
              <li>Only text is sent, never the image.</li>
              <li>Account-like numbers are masked. Names are included.</li>
              <li>Bank-detail documents and your client and vendor list are never sent.</li>
              <li>Fields it fills are labelled “AI-assisted”, and nothing is saved until you review it.</li>
            </ul>
            <p>Processed by Anter, our AI service provider.</p>
            <p>
              <a href={AI_DISCLOSURE_URL} target="_blank" rel="noopener noreferrer" className="font-semibold text-[var(--brand-primary)] underline">
                Read the AI disclosure
              </a>
            </p>
            {error && <p role="alert" className="text-red-600">{error}</p>}
          </div>
          <div className="flex justify-end gap-2">
            <button type="button" onClick={closeDialog} disabled={busy} className="rounded-xl border border-[var(--border-subtle)] px-4 py-2 text-sm font-medium hover:bg-[var(--bg-subtle)] disabled:opacity-50">
              Cancel
            </button>
            <button type="button" onClick={() => void confirm()} disabled={busy} className="inline-flex items-center gap-2 rounded-xl bg-[var(--brand-primary)] px-4 py-2 text-sm font-semibold text-white disabled:opacity-50">
              {busy && <Loader2 className="h-4 w-4 animate-spin" aria-hidden="true" />}
              Turn on
            </button>
          </div>
        </ModalShell>
      )}
    </section>
  );
};
