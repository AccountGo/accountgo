// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

import React, { useCallback, useEffect, useState } from 'react';
import { useAuth } from '../../auth/AuthContext';
import { DocumentExportStatus, formatBytes } from '../../lib/documents';
import { DownloadAllDocuments, ExportRunStatus } from './DownloadAllDocuments';

export type DocumentsDeleteChoice = 'exported' | 'skip' | null;

/**
 * The delete-workspace step for documents (design 3.10 T1/T2). Deleting a workspace deletes its files, so when it has any
 * the owner either confirms they opened a current export, or says they don't need them. The server rechecks both.
 */
export const WorkspaceDeleteDocumentsStep: React.FC<{
  orgSlug: string;
  choice: DocumentsDeleteChoice;
  onChoice: (choice: DocumentsDeleteChoice) => void;
  /** True when the step is not needed (no files) or a choice was made. */
  onReady: (ready: boolean) => void;
}> = ({ orgSlug, choice, onChoice, onReady }) => {
  const { authFetch } = useAuth();
  const [status, setStatus] = useState<DocumentExportStatus | null>(null);
  const [failed, setFailed] = useState(false);

  const load = useCallback(async () => {
    try {
      const res = await authFetch('/api/documents/exports/status');
      if (!res.ok) {
        setFailed(true);
        return;
      }
      setStatus(await res.json());
    } catch {
      setFailed(true);
    }
  }, [authFetch]);

  useEffect(() => { void load(); }, [load]);

  const current = !!status?.latest?.current;
  const needed = !!status && status.filedCount > 0;
  useEffect(() => {
    if (!status) onReady(false);
    else if (!needed) onReady(true);
    else onReady(choice === 'skip' || (choice === 'exported' && current));
  }, [status, needed, choice, current, onReady]);
  useEffect(() => {
    if (choice === 'exported' && !current) onChoice(null);
  }, [choice, current, onChoice]);

  if (failed) return <p role="alert" className="text-xs text-rose-500">Your documents could not be checked. Close this and try again.</p>;
  if (!status || !needed) return null;

  return (
    <fieldset className="space-y-3 rounded-xl border border-[var(--border-subtle)] p-3">
      <legend className="px-1 text-xs font-semibold text-[var(--text-main)]">Your documents</legend>
      <p className="text-xs text-[var(--text-secondary)]">
        This workspace keeps {status.filedCount} {status.filedCount === 1 ? 'file' : 'files'} ({formatBytes(status.filedBytes)}), including
        scanned bills and invoices. Deleting the workspace deletes them too. Download them first if you need to keep them.
      </p>
      <DownloadAllDocuments orgSlug={orgSlug} onRunChanged={() => void load()} />
      {status.latest && !current && status.latest.status === 'server_finished' && (
        <p className="text-xs text-amber-600 dark:text-amber-400">Documents changed after your last download. Download them again.</p>
      )}
      {status.latest && status.latest.status === 'failed' && <ExportRunStatus run={status.latest} />}
      <label className={`flex items-start gap-2 text-xs ${current ? 'text-[var(--text-main)]' : 'text-[var(--text-secondary)] opacity-60'}`}>
        <input type="radio" name="documents-choice" disabled={!current} checked={choice === 'exported'} onChange={() => onChoice('exported')} />
        I opened the downloaded ZIP and it contains my files.
      </label>
      <label className="flex items-start gap-2 text-xs text-[var(--text-main)]">
        <input type="radio" name="documents-choice" checked={choice === 'skip'} onChange={() => onChoice('skip')} />
        I don&apos;t need my documents. Delete them with the workspace.
      </label>
    </fieldset>
  );
};
