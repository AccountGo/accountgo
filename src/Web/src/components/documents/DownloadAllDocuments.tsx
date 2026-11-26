// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

import React, { useCallback, useState } from 'react';
import { Download, Loader2 } from 'lucide-react';
import { useAuth } from '../../auth/AuthContext';
import { isStepUpRequired } from '../../lib/apiProblems';
import { StepUpPrompt } from '../StepUpPrompt';
import { DocumentExportRun, documentProblemMessage, formatBytes, readProblem } from '../../lib/documents';

type SaveFilePicker = (options: { suggestedName: string; types?: unknown[] }) => Promise<{
  createWritable: () => Promise<WritableStream<Uint8Array>>;
}>;

/**
 * "Download all documents" (Owner only). Creates an export run, downloads its ZIP once, then reads the run back so the
 * page can say what the server finished sending. The server can't know the browser saved the file, so the wording never
 * claims that.
 */
export const DownloadAllDocuments: React.FC<{
  orgSlug: string;
  onRunChanged?: (run: DocumentExportRun) => void;
  className?: string;
}> = ({ orgSlug, onRunChanged, className = '' }) => {
  const { authFetch } = useAuth();
  const [busy, setBusy] = useState(false);
  const [needsStepUp, setNeedsStepUp] = useState(false);
  const [error, setError] = useState('');
  const [run, setRun] = useState<DocumentExportRun | null>(null);
  const [verified, setVerified] = useState(false);

  const start = useCallback(async () => {
    setBusy(true);
    setError('');
    setNeedsStepUp(false);
    setVerified(false);
    try {
      // The save dialog must open straight from the click, before any network wait.
      const name = `documents-${orgSlug}-${new Date().toISOString().slice(0, 10)}.zip`;
      const target = await chooseTarget(name);
      const created = await authFetch('/api/documents/exports', { method: 'POST' });
      if (!created.ok) {
        const problem = await readProblem(created);
        if (isStepUpRequired(created.status, problem)) {
          setNeedsStepUp(true);
          return;
        }
        if (problem?.code === 'DOCUMENT_EXPORT_RUNNING') {
          setError('An export is already being downloaded. Wait for it to finish, then try again.');
          return;
        }
        setError(documentProblemMessage(problem, 'The export could not be started.'));
        return;
      }
      const ready = (await created.json()) as DocumentExportRun;
      if (!target && ready.selectedBytes > MAX_IN_MEMORY_DOWNLOAD_BYTES) {
        setError(
          `These documents add up to ${formatBytes(ready.selectedBytes)}, too much for this browser to save in one piece. ` +
          'Use Chrome or Edge on a computer, which save the ZIP straight to disk.',
        );
        return;
      }

      const file = await authFetch(`/api/documents/exports/${encodeURIComponent(ready.id)}/file`);
      if (!file.ok) {
        const problem = await readProblem(file);
        if (isStepUpRequired(file.status, problem)) {
          setNeedsStepUp(true);
          return;
        }
        setError(documentProblemMessage(problem, 'The export could not be downloaded.'));
        return;
      }
      await saveResponse(file, name, target);

      const statusRes = await authFetch(`/api/documents/exports/${encodeURIComponent(ready.id)}`);
      if (statusRes.ok) {
        const finished = (await statusRes.json()) as DocumentExportRun;
        setRun(finished);
        onRunChanged?.(finished);
      }
    } catch (err) {
      if ((err as { name?: string })?.name === 'AbortError') {
        setError('Saving was cancelled. Start the download again when you are ready.');
      } else {
        setError('The export could not be downloaded. Check your connection and try again.');
      }
    } finally {
      setBusy(false);
    }
  }, [authFetch, onRunChanged, orgSlug]);

  return (
    <div className={`space-y-2 ${className}`}>
      <button
        type="button"
        onClick={() => void start()}
        disabled={busy}
        className="inline-flex items-center gap-1.5 h-9 px-3 rounded-lg border border-[var(--border-subtle)] bg-[var(--bg-surface)] text-xs font-semibold text-[var(--text-main)] hover:bg-[var(--bg-surface-hover)] disabled:opacity-60"
      >
        {busy ? <Loader2 className="w-3.5 h-3.5 animate-spin" /> : <Download className="w-3.5 h-3.5" />}
        {busy ? 'Preparing download…' : 'Download all documents'}
      </button>
      {/* After verifying, the browser needs a fresh click before it will open the save dialog, so ask for one. */}
      {needsStepUp && <StepUpPrompt actionLabel="downloading all documents" onVerified={() => { setNeedsStepUp(false); setVerified(true); }} />}
      {verified && !busy && <p role="status" className="text-xs text-[var(--text-secondary)]">Verified. Click Download all documents again to start.</p>}
      {error && <p role="alert" className="text-xs text-rose-500">{error}</p>}
      {run && <ExportRunStatus run={run} />}
    </div>
  );
};

export const ExportRunStatus: React.FC<{ run: DocumentExportRun }> = ({ run }) => {
  if (run.status === 'server_finished') {
    return (
      <p role="status" className="text-xs text-[var(--text-secondary)]">
        Server finished sending {run.writtenCount} {run.writtenCount === 1 ? 'file' : 'files'} ({formatBytes(run.selectedBytes)}). Check that
        the downloaded ZIP opens and contains your files; its <span className="font-mono">index.csv</span> ends with
        &ldquo;# complete&rdquo; when every file was included.
      </p>
    );
  }
  if (run.status === 'failed') {
    return (
      <p role="alert" className="text-xs text-rose-500">
        The export didn&apos;t finish ({failureText(run.failureReason)}). {run.writtenCount} of {run.selectedCount} files were sent. Download it
        again before relying on it.
      </p>
    );
  }
  return <p role="status" className="text-xs text-[var(--text-secondary)]">Export {run.status}.</p>;
};

function failureText(reason: string | null): string {
  switch (reason) {
    case 'missing-files':
      return 'some files were missing';
    case 'hash-mismatch':
      return "some files didn't match their stored fingerprint";
    case 'client-disconnected':
      return 'the download was interrupted';
    case 'lease-lost':
    case 'stale':
      return 'the download took too long';
    case 'superseded':
      return 'a newer export replaced it';
    default:
      return 'a server error';
  }
}

/**
 * Without a save dialog that writes straight to disk (Safari, Firefox, phones) the ZIP is assembled in browser memory, so
 * only exports up to this size are offered there.
 */
export const MAX_IN_MEMORY_DOWNLOAD_BYTES = 500 * 1024 * 1024;

type SaveTarget = { createWritable: () => Promise<WritableStream<Uint8Array>> } | null;

/** Where the browser supports it, asks for the file first so the ZIP streams to disk without a copy in memory. */
async function chooseTarget(name: string): Promise<SaveTarget> {
  const picker = (window as unknown as { showSaveFilePicker?: SaveFilePicker }).showSaveFilePicker;
  if (!picker) return null;
  return picker({ suggestedName: name, types: [{ description: 'ZIP archive', accept: { 'application/zip': ['.zip'] } }] });
}

async function saveResponse(response: Response, name: string, target: SaveTarget): Promise<void> {
  if (target && response.body) {
    await response.body.pipeTo(await target.createWritable());
    return;
  }
  const blob = await response.blob();
  const url = URL.createObjectURL(blob);
  try {
    const anchor = document.createElement('a');
    anchor.href = url;
    anchor.download = name;
    document.body.appendChild(anchor);
    anchor.click();
    anchor.remove();
  } finally {
    setTimeout(() => URL.revokeObjectURL(url), 60_000);
  }
}
