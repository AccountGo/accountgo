// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

import React, { useCallback, useEffect, useRef, useState } from 'react';
import { FileText, Image as ImageIcon, Link2, Loader2, Lock, Paperclip, Search, Upload, X } from 'lucide-react';
import { useAuth } from '../../auth/AuthContext';
import { usePermissions } from '../Gating';
import { ModalShell, MODAL_BODY_CLASS, MODAL_HEADER_CLASS, MODAL_SURFACE_CLASS } from '../ModalShell';
import {
  ACCEPTED_FILE_TYPES,
  DocumentPage,
  DocumentTargetType,
  LibraryDocument,
  categoryLabel,
  documentProblemMessage,
  formatBytes,
  readProblem,
} from '../../lib/documents';

interface Props {
  targetType: DocumentTargetType;
  /** The record's public id. */
  targetId: string;
  /** The user may change this record (create/edit permission and a writable module). */
  canAttach: boolean;
  /**
   * The record is still a draft (always true for parties). When false, only links the server marks removable (an Owner or
   * Admin, or the member who linked the file) get a remove button; the rest are kept as evidence.
   */
  canRemove: boolean;
  recordLabel: string;
  /** The record's current status; a change (draft approved, voided) reloads the links' lock state. */
  recordStatus?: string;
}

/**
 * Files attached to one record (receipts, purchase orders, contracts). The scanned source keeps its own evidence panel;
 * this lists attachments, uploads new ones and links existing library files. Hidden when the library is off.
 */
export const RecordAttachments: React.FC<Props> = ({ targetType, targetId, canAttach, canRemove, recordLabel, recordStatus }) => {
  const { authFetch } = useAuth();
  const { hasPermission } = usePermissions();
  const canBrowseLibrary = hasPermission('documents.view');
  const [attachments, setAttachments] = useState<LibraryDocument[] | null>(null);
  const [available, setAvailable] = useState(true);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const [loadFailed, setLoadFailed] = useState(false);
  const [picking, setPicking] = useState(false);
  const fileInput = useRef<HTMLInputElement>(null);
  const base = `/api/documents/linked/${targetType}/${encodeURIComponent(targetId)}`;

  // The linked list is paginated (100 per page) and includes the scanned source. Pages are read on demand with
  // "Show more", so a record with any number of files never hides one without saying so.
  const [loaded, setLoaded] = useState<{ items: LibraryDocument[]; total: number; page: number }>({ items: [], total: 0, page: 0 });
  const isAttachment = useCallback(
    (d: LibraryDocument) => d.links.some((l) => l.targetType === targetType && l.targetId === targetId && l.role === 'attachment'),
    [targetId, targetType],
  );

  const fetchPage = useCallback(async (pageNumber: number) => {
    const res = await authFetch(`${base}?page=${pageNumber}&pageSize=100`);
    if (!res.ok) {
      const problem = await readProblem(res);
      if (problem?.code === 'DOCUMENTS_DISABLED') setAvailable(false);
      else setLoadFailed(true);
      return null;
    }
    return (await res.json()) as DocumentPage;
  }, [authFetch, base]);

  const load = useCallback(async () => {
    try {
      const page = await fetchPage(1);
      if (!page) return;
      setLoaded({ items: page.items, total: page.total, page: 1 });
      setAttachments(page.items.filter(isAttachment));
      setLoadFailed(false);
    } catch {
      setLoadFailed(true);
    }
  }, [fetchPage, isAttachment]);

  const loadMore = async () => {
    try {
      const page = await fetchPage(loaded.page + 1);
      if (!page) return;
      const items = [...loaded.items, ...page.items];
      setLoaded({ items, total: page.total, page: loaded.page + 1 });
      setAttachments(items.filter(isAttachment));
    } catch {
      setLoadFailed(true);
    }
  };

  useEffect(() => { void load(); }, [load, recordStatus]);

  const run = async (work: () => Promise<Response>, fallback: string) => {
    setBusy(true);
    setError('');
    try {
      const res = await work();
      if (!res.ok) {
        setError(documentProblemMessage(await readProblem(res), fallback));
        return false;
      }
      return true;
    } catch {
      setError(fallback);
      return false;
    } finally {
      setBusy(false);
    }
  };

  const upload = async (files: FileList) => {
    for (const file of Array.from(files)) {
      const form = new FormData();
      form.append('File', file, file.name);
      form.append('LinkTargetType', targetType);
      form.append('LinkTargetId', targetId);
      const ok = await run(
        () => authFetch('/api/documents', { method: 'POST', headers: { 'Idempotency-Key': crypto.randomUUID() }, body: form }),
        `${file.name} could not be attached.`,
      );
      if (!ok) break;
    }
    await load();
  };

  const remove = async (doc: LibraryDocument) => {
    if (await run(() => authFetch(`/api/documents/${encodeURIComponent(doc.id)}/links/${targetType}/${encodeURIComponent(targetId)}`, { method: 'DELETE' }),
      'The attachment could not be removed.'))
      await load();
  };

  const linkExisting = async (doc: LibraryDocument) => {
    setPicking(false);
    if (await run(() => authFetch(`/api/documents/${encodeURIComponent(doc.id)}/links`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ targetType, targetId }),
    }), 'The file could not be attached.'))
      await load();
  };

  const open = async (doc: LibraryDocument) => {
    const res = await authFetch(`/api/documents/${encodeURIComponent(doc.id)}/bytes`);
    if (!res.ok) {
      setError(documentProblemMessage(await readProblem(res), 'The file could not be opened.'));
      return;
    }
    const url = URL.createObjectURL(await res.blob());
    const anchor = document.createElement('a');
    anchor.href = url;
    anchor.rel = 'noopener';
    if (doc.contentType.startsWith('image/')) anchor.target = '_blank';
    else anchor.download = `${doc.title}.pdf`;
    document.body.appendChild(anchor);
    anchor.click();
    anchor.remove();
    setTimeout(() => URL.revokeObjectURL(url), 60_000);
  };

  if (!available) return null;

  return (
    <section aria-labelledby={`attachments-${targetType}`} className="no-print mt-6 rounded-xl border border-[var(--border-subtle)] bg-[var(--bg-surface)] p-4 space-y-3">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h2 id={`attachments-${targetType}`} className="text-sm font-semibold text-[var(--text-main)] flex items-center gap-1.5">
          <Paperclip className="w-4 h-4" /> Attachments
        </h2>
        {canAttach && (
          <div className="flex gap-2">
            <input ref={fileInput} type="file" multiple accept={ACCEPTED_FILE_TYPES} className="hidden" aria-label={`Attach files to this ${recordLabel}`}
              onChange={(e) => {
                if (e.target.files) void upload(e.target.files);
                e.target.value = '';
              }} />
            <button type="button" disabled={busy} onClick={() => fileInput.current?.click()}
              className="inline-flex items-center gap-1.5 h-8 px-3 rounded-lg border border-[var(--border-subtle)] text-xs font-semibold">
              {busy ? <Loader2 className="w-3.5 h-3.5 animate-spin" /> : <Upload className="w-3.5 h-3.5" />} Upload file
            </button>
            {canBrowseLibrary && (
              <button type="button" disabled={busy} onClick={() => setPicking(true)}
                className="inline-flex items-center gap-1.5 h-8 px-3 rounded-lg border border-[var(--border-subtle)] text-xs font-semibold">
                <Link2 className="w-3.5 h-3.5" /> Choose from Documents
              </button>
            )}
          </div>
        )}
      </div>

      {loadFailed ? (
        <p className="text-xs text-[var(--text-secondary)]">Attachments could not be loaded. Refresh the page to try again.</p>
      ) : attachments === null ? (
        <p className="text-xs text-[var(--text-secondary)]">Loading attachments…</p>
      ) : attachments.length === 0 ? (
        <p className="text-xs text-[var(--text-secondary)]">No files attached. Attach the receipt, a purchase order or anything else that supports this {recordLabel}.</p>
      ) : (
        <ul className="divide-y divide-[var(--border-subtle)]">
          {attachments.map((doc) => {
            const link = doc.links.find((l) => l.targetType === targetType && l.targetId === targetId);
            // A bill or invoice that is no longer a draft keeps its attachments, even before the reload confirms it.
            const posted = !!link?.locked || (!canRemove && (targetType === 'bill' || targetType === 'invoice'));
            // Before the reload confirms the new status, the server's flag still describes the draft: wait for it.
            const stale = posted && !link?.locked;
            const removable = canAttach && !!link?.canUnlink && !stale;
            return (
              <li key={doc.id} className="py-2 flex items-center gap-3">
                {doc.contentType.startsWith('image/') ? <ImageIcon className="w-4 h-4 shrink-0" /> : <FileText className="w-4 h-4 shrink-0" />}
                <button type="button" onClick={() => void open(doc)} className="min-w-0 flex-1 text-left">
                  <span className="block text-sm truncate text-[var(--text-main)] hover:underline">{doc.title}</span>
                  <span className="block text-xs text-[var(--text-secondary)]">{categoryLabel(doc.category)} · {formatBytes(doc.byteCount)}</span>
                </button>
                {removable ? (
                  <button type="button" disabled={busy} onClick={() => void remove(doc)} aria-label={`Remove ${doc.title}`}
                    title={posted ? `Remove from this posted ${recordLabel}` : undefined}
                    className="p-1.5 rounded-lg hover:bg-[var(--bg-surface-hover)]">
                    <X className="w-4 h-4" />
                  </button>
                ) : posted ? (
                  <span className="inline-flex items-center gap-1 text-xs text-[var(--text-secondary)]">
                    <Lock className="w-3 h-3" /> Kept as evidence for this posted {recordLabel}
                  </span>
                ) : null}
              </li>
            );
          })}
        </ul>
      )}
      {!loadFailed && loaded.items.length < loaded.total && (
        <button type="button" onClick={() => void loadMore()} className="text-xs font-semibold text-[var(--brand-primary)] hover:underline">
          Show more linked files ({loaded.total - loaded.items.length} not shown yet)
        </button>
      )}
      {error && <p role="alert" className="text-xs text-rose-500">{error}</p>}
      {picking && <LibraryPicker onPick={(doc) => void linkExisting(doc)} onClose={() => setPicking(false)} exclude={(attachments ?? []).map((d) => d.id).join(',')} />}
    </section>
  );
};

const LibraryPicker: React.FC<{ onPick: (doc: LibraryDocument) => void; onClose: () => void; exclude: string }> = ({ onPick, onClose, exclude }) => {
  const { authFetch } = useAuth();
  const [query, setQuery] = useState('');
  const [items, setItems] = useState<LibraryDocument[] | null>(null);

  useEffect(() => {
    let cancelled = false;
    const handle = setTimeout(() => {
      void (async () => {
        const params = new URLSearchParams({ pageSize: '50' });
        if (query.trim()) params.set('q', query.trim());
        const res = await authFetch(`/api/documents?${params}`);
        const skip = new Set(exclude.split(',').filter(Boolean));
        if (!cancelled) setItems(res.ok ? ((await res.json()) as DocumentPage).items.filter((d) => !skip.has(d.id)) : []);
      })();
    }, 250);
    return () => {
      cancelled = true;
      clearTimeout(handle);
    };
  }, [authFetch, query, exclude]);

  return (
    <ModalShell open onClose={onClose} bare aria-labelledby="pick-document-title">
      <div className={`${MODAL_SURFACE_CLASS} max-w-lg`}>
        <div className={MODAL_HEADER_CLASS}>
          <h2 id="pick-document-title" className="text-base font-bold">Choose from Documents</h2>
          <button type="button" onClick={onClose} aria-label="Close" className="p-1.5 rounded-lg"><X className="w-4 h-4" /></button>
        </div>
        <div className={`${MODAL_BODY_CLASS} space-y-3`}>
          <div className="relative">
            <Search className="w-4 h-4 absolute left-3 top-1/2 -translate-y-1/2 text-[var(--text-secondary)]" />
            <input type="search" autoFocus aria-label="Search documents" value={query} onChange={(e) => setQuery(e.target.value)}
              className="w-full h-9 pl-9 pr-3 text-sm rounded-lg border border-[var(--border-subtle)] bg-[var(--bg-subtle)]" />
          </div>
          {items === null ? (
            <p className="text-xs text-[var(--text-secondary)]">Loading…</p>
          ) : items.length === 0 ? (
            <p className="text-xs text-[var(--text-secondary)]">No documents to attach.</p>
          ) : (
            <ul className="divide-y divide-[var(--border-subtle)]">
              {items.map((doc) => (
                <li key={doc.id}>
                  <button type="button" onClick={() => onPick(doc)} className="w-full text-left py-2 hover:bg-[var(--bg-surface-hover)] px-1 rounded">
                    <span className="block text-sm">{doc.title}</span>
                    <span className="block text-xs text-[var(--text-secondary)]">{categoryLabel(doc.category)} · {formatBytes(doc.byteCount)}</span>
                  </button>
                </li>
              ))}
            </ul>
          )}
        </div>
      </div>
    </ModalShell>
  );
};
