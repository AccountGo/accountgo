// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

import React, { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { Archive, ArchiveRestore, Download, ExternalLink, Lock, RotateCcw, Trash2, X } from 'lucide-react';
import { useAuth } from '../../auth/AuthContext';
import { ConfirmDialog } from '../ConfirmDialog';
import { ModalShell, MODAL_BODY_CLASS, MODAL_FOOTER_CLASS, MODAL_HEADER_CLASS, MODAL_SURFACE_CLASS } from '../ModalShell';
import {
  DOCUMENT_CATEGORIES,
  LibraryDocument,
  categoryLabel,
  documentProblemMessage,
  formatBytes,
  linkLabel,
  linkPath,
  readProblem,
} from '../../lib/documents';

interface Props {
  document: LibraryDocument;
  orgSlug: string;
  onChanged: (doc: LibraryDocument) => void;
  /** The file left this view (trashed, restored, permanently deleted). */
  onRemoved: (id: string) => void;
  onClose: () => void;
}

/** One document: preview, details, the records it belongs to, and what the user may do with it. */
export const DocumentDetailDialog: React.FC<Props> = ({ document: doc, orgSlug, onChanged, onRemoved, onClose }) => {
  const { authFetch } = useAuth();
  const [title, setTitle] = useState(doc.title);
  const [category, setCategory] = useState(doc.category ?? '');
  const [documentDate, setDocumentDate] = useState(doc.documentDate?.slice(0, 10) ?? '');
  const [notes, setNotes] = useState(doc.notes ?? '');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const [previewUrl, setPreviewUrl] = useState<string | null>(null);
  const [confirmDelete, setConfirmDelete] = useState(false);
  const isImage = doc.contentType.startsWith('image/');
  const scanSource = doc.kind !== 'document' && doc.hasSourceLink;
  const dirty =
    title !== doc.title || category !== (doc.category ?? '') || documentDate !== (doc.documentDate?.slice(0, 10) ?? '') || notes !== (doc.notes ?? '');

  useEffect(() => {
    if (!isImage) return;
    let url: string | null = null;
    let cancelled = false;
    void (async () => {
      const res = await authFetch(`/api/documents/${encodeURIComponent(doc.id)}/bytes`);
      if (!res.ok || cancelled) return;
      url = URL.createObjectURL(await res.blob());
      if (!cancelled) setPreviewUrl(url);
    })();
    return () => {
      cancelled = true;
      if (url) URL.revokeObjectURL(url);
    };
  }, [authFetch, doc.id, isImage]);

  const call = async (path: string, init: RequestInit, fallback: string): Promise<Response | null> => {
    setBusy(true);
    setError('');
    try {
      const res = await authFetch(path, init);
      if (!res.ok) {
        setError(documentProblemMessage(await readProblem(res), fallback));
        return null;
      }
      return res;
    } catch {
      setError(fallback);
      return null;
    } finally {
      setBusy(false);
    }
  };

  const base = `/api/documents/${encodeURIComponent(doc.id)}`;
  const save = async () => {
    const res = await call(base, {
      method: 'PATCH',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ title, category: category || null, documentDate: documentDate || null, notes: notes || null }),
    }, 'The details could not be saved.');
    if (res) onChanged(await res.json());
  };
  const action = async (path: string, method: string, fallback: string, removed: boolean) => {
    const res = await call(path, { method }, fallback);
    if (!res) return;
    if (removed) onRemoved(doc.id);
    else onChanged(await res.json());
  };
  const open = async () => {
    const res = await call(`${base}/bytes`, { method: 'GET' }, 'The file could not be opened.');
    if (!res) return;
    const url = URL.createObjectURL(await res.blob());
    const anchor = window.document.createElement('a');
    anchor.href = url;
    if (isImage) anchor.target = '_blank';
    else anchor.download = `${doc.title}${doc.contentType === 'application/pdf' ? '.pdf' : ''}`;
    anchor.rel = 'noopener';
    window.document.body.appendChild(anchor);
    anchor.click();
    anchor.remove();
    setTimeout(() => URL.revokeObjectURL(url), 60_000);
  };

  const inputClass =
    'w-full h-9 px-3 rounded-lg border border-[var(--border-subtle)] bg-[var(--bg-subtle)] text-sm text-[var(--text-main)] focus:outline-none focus:ring-2 focus:ring-[var(--brand-primary)] disabled:opacity-60';

  return (
    <ModalShell open onClose={onClose} bare aria-labelledby="document-detail-title" maxWidthClassName="max-w-2xl">
      <div className={`${MODAL_SURFACE_CLASS} max-w-2xl`}>
        <div className={MODAL_HEADER_CLASS}>
          <div className="min-w-0">
            <h2 id="document-detail-title" className="text-base font-bold text-[var(--text-main)] truncate">{doc.title}</h2>
            <p className="text-xs text-[var(--text-secondary)]">
              {categoryLabel(doc.category)} · {formatBytes(doc.byteCount)}
              {doc.contentType === 'application/pdf' ? ` · ${doc.pageCount} ${doc.pageCount === 1 ? 'page' : 'pages'}` : ''}
              {doc.inTrash ? ' · In trash' : doc.archived ? ' · Archived' : ''}
            </p>
          </div>
          <button type="button" onClick={onClose} aria-label="Close" className="p-1.5 rounded-lg hover:bg-[var(--bg-surface-hover)]">
            <X className="w-4 h-4" />
          </button>
        </div>

        <div className={`${MODAL_BODY_CLASS} space-y-4`}>
          {isImage && previewUrl && (
            <img src={previewUrl} alt={`Preview of ${doc.title}`} className="max-h-72 mx-auto rounded-lg border border-[var(--border-subtle)]" />
          )}
          <button type="button" onClick={() => void open()} disabled={busy}
            className="inline-flex items-center gap-1.5 text-xs font-semibold text-[var(--brand-primary)] hover:underline">
            {isImage ? <ExternalLink className="w-3.5 h-3.5" /> : <Download className="w-3.5 h-3.5" />}
            {isImage ? 'Open full size' : 'Download file'}
          </button>

          {scanSource && (
            <p className="text-xs rounded-lg bg-[var(--bg-subtle)] p-3 text-[var(--text-secondary)] flex gap-2">
              <Lock className="w-3.5 h-3.5 shrink-0 mt-0.5" />
              Scanned source of a bill or invoice. Delete or edit the draft to remove it.
            </p>
          )}
          {doc.hasLockedLink && (
            <p className="text-xs rounded-lg bg-[var(--bg-subtle)] p-3 text-[var(--text-secondary)] flex gap-2">
              <Lock className="w-3.5 h-3.5 shrink-0 mt-0.5" />
              Kept as evidence for a posted bill or invoice. It can be archived, but not removed.
            </p>
          )}

          <div className="grid gap-3 sm:grid-cols-2">
            <label className="block text-xs font-semibold text-[var(--text-secondary)] sm:col-span-2">
              Title
              <input className={`${inputClass} mt-1`} value={title} maxLength={200} disabled={!doc.canManage || busy}
                onChange={(e) => setTitle(e.target.value)} />
            </label>
            <label className="block text-xs font-semibold text-[var(--text-secondary)]">
              Category
              <select className={`${inputClass} mt-1`} value={category} disabled={!doc.canManage || busy}
                onChange={(e) => setCategory(e.target.value)}>
                <option value="">Uncategorised</option>
                {DOCUMENT_CATEGORIES.map((c) => <option key={c.value} value={c.value}>{c.label}</option>)}
              </select>
            </label>
            <label className="block text-xs font-semibold text-[var(--text-secondary)]">
              Document date
              <input type="date" className={`${inputClass} mt-1`} value={documentDate} disabled={!doc.canManage || busy}
                onChange={(e) => setDocumentDate(e.target.value)} />
            </label>
            <label className="block text-xs font-semibold text-[var(--text-secondary)] sm:col-span-2">
              Notes
              <textarea className={`${inputClass} mt-1 h-20 py-2`} value={notes} maxLength={1000} disabled={!doc.canManage || busy}
                onChange={(e) => setNotes(e.target.value)} />
            </label>
          </div>

          <section>
            <h3 className="text-xs font-semibold text-[var(--text-secondary)] mb-1">Linked to</h3>
            {doc.links.length === 0 ? (
              <p className="text-xs text-[var(--text-secondary)]">Not linked to any record you can open.</p>
            ) : (
              <ul className="flex flex-wrap gap-1.5">
                {doc.links.map((link) => (
                  <li key={`${link.targetType}:${link.targetId}`}>
                    <Link to={linkPath(orgSlug, link)} onClick={onClose}
                      className="inline-flex items-center gap-1 px-2 py-1 rounded-full border border-[var(--border-subtle)] text-xs hover:bg-[var(--bg-surface-hover)]">
                      {linkLabel(link.targetType)}{link.role === 'source' ? ' (source)' : ''}
                      {link.locked && <Lock className="w-3 h-3" aria-label="posted" />}
                    </Link>
                  </li>
                ))}
              </ul>
            )}
          </section>

          <details className="text-xs text-[var(--text-secondary)]">
            <summary className="cursor-pointer">File fingerprints</summary>
            <dl className="mt-2 space-y-2 break-all">
              <div>
                <dt className="font-semibold">Stored file SHA-256</dt>
                <dd className="font-mono">{doc.storedSha256}</dd>
                <dd>Matches the file AccountGo keeps and includes in &ldquo;Download all documents&rdquo;.</dd>
              </div>
              <div>
                <dt className="font-semibold">Uploaded file SHA-256</dt>
                <dd className="font-mono">{doc.uploadedSha256}</dd>
                <dd>
                  Matches the file as it was uploaded. It only proves that a copy you kept yourself is the same file.
                  {doc.storedCopyOptimised && ' Images are stored as an optimised copy, so the two fingerprints differ.'}
                </dd>
              </div>
            </dl>
          </details>

          {error && <p role="alert" className="text-xs text-rose-500">{error}</p>}
        </div>

        {doc.canManage && (
          <div className={`${MODAL_FOOTER_CLASS} flex-wrap justify-between`}>
            <div className="flex flex-wrap gap-2">
              {doc.inTrash ? (
                <>
                  <button type="button" disabled={busy} onClick={() => void action(`${base}/restore`, 'POST', 'The file could not be restored.', true)}
                    className="inline-flex items-center gap-1.5 h-9 px-3 rounded-lg text-xs font-semibold border border-[var(--border-subtle)]">
                    <RotateCcw className="w-3.5 h-3.5" /> Restore
                  </button>
                  <button type="button" disabled={busy || doc.hasLockedLink || scanSource}
                    onClick={() => setConfirmDelete(true)}
                    className="inline-flex items-center gap-1.5 h-9 px-3 rounded-lg text-xs font-semibold text-rose-500 border border-rose-500/40 disabled:opacity-50">
                    <Trash2 className="w-3.5 h-3.5" /> Delete permanently
                  </button>
                </>
              ) : (
                <>
                  <button type="button" disabled={busy}
                    onClick={() => void action(`${base}/${doc.archived ? 'unarchive' : 'archive'}`, 'POST', 'The file could not be updated.', true)}
                    className="inline-flex items-center gap-1.5 h-9 px-3 rounded-lg text-xs font-semibold border border-[var(--border-subtle)]">
                    {doc.archived ? <ArchiveRestore className="w-3.5 h-3.5" /> : <Archive className="w-3.5 h-3.5" />}
                    {doc.archived ? 'Unarchive' : 'Archive'}
                  </button>
                  <button type="button" disabled={busy || doc.hasLockedLink || scanSource}
                    onClick={() => void action(base, 'DELETE', 'The file could not be moved to the trash.', true)}
                    className="inline-flex items-center gap-1.5 h-9 px-3 rounded-lg text-xs font-semibold text-rose-500 border border-rose-500/40 disabled:opacity-50">
                    <Trash2 className="w-3.5 h-3.5" /> Move to trash
                  </button>
                </>
              )}
            </div>
            <button type="button" disabled={!dirty || busy} onClick={() => void save()}
              className="h-9 px-4 rounded-lg bg-[var(--brand-primary)] text-white text-xs font-semibold disabled:opacity-50">
              Save details
            </button>
          </div>
        )}
      </div>
      {confirmDelete && (
        <ConfirmDialog
          title="Delete this file permanently?"
          message="The file and its details are removed now and can't be restored. This frees its storage space."
          confirmLabel="Delete permanently"
          destructive
          busy={busy}
          onCancel={() => setConfirmDelete(false)}
          onConfirm={() => {
            setConfirmDelete(false);
            void action(`${base}/permanent`, 'DELETE', 'The file could not be deleted.', true);
          }}
        />
      )}
    </ModalShell>
  );
};
