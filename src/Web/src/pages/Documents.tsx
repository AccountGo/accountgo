// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

import React, { useCallback, useEffect, useRef, useState } from 'react';
import { useOutletContext } from 'react-router-dom';
import { AlertTriangle, FileText, FolderOpen, Image as ImageIcon, Loader2, Lock, Search, Upload, X } from 'lucide-react';
import { PageHeader } from '../components/PageHeader';
import { useAuth } from '../auth/AuthContext';
import { TenantContextType } from '../types';
import { DocumentDetailDialog } from '../components/documents/DocumentDetailDialog';
import { DownloadAllDocuments } from '../components/documents/DownloadAllDocuments';
import {
  ACCEPTED_FILE_TYPES,
  DOCUMENT_CATEGORIES,
  DocumentCapabilities,
  DocumentPage,
  LibraryDocument,
  categoryLabel,
  documentProblemMessage,
  formatBytes,
  linkLabel,
  readProblem,
} from '../lib/documents';

type View = 'active' | 'archived' | 'trash';
const PAGE_SIZE = 50;

interface UploadNotice {
  fileName: string;
  message: string;
  duplicateOf?: string;
  file?: File;
}

/** The documents library: every kept file of the workspace, uploads, scanned bills and invoices, and attachments. */
export const Documents: React.FC = () => {
  const { currentOrg } = useOutletContext<TenantContextType>();
  const { authFetch } = useAuth();
  const [caps, setCaps] = useState<DocumentCapabilities | null>(null);
  const [view, setView] = useState<View>('active');
  const [category, setCategory] = useState('');
  const [query, setQuery] = useState('');
  const [search, setSearch] = useState('');
  const [page, setPage] = useState<DocumentPage | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [uploading, setUploading] = useState(0);
  const [notices, setNotices] = useState<UploadNotice[]>([]);
  const [selected, setSelected] = useState<LibraryDocument | null>(null);
  const [dragging, setDragging] = useState(false);
  const fileInput = useRef<HTMLInputElement>(null);
  const isOwner = currentOrg.role === 'Owner';

  const loadCaps = useCallback(async () => {
    const res = await authFetch('/api/documents/capabilities');
    if (res.ok) setCaps(await res.json());
  }, [authFetch]);

  const load = useCallback(async (pageNumber = 1) => {
    setLoading(true);
    setError('');
    const params = new URLSearchParams({ page: String(pageNumber), pageSize: String(PAGE_SIZE) });
    if (view === 'archived') params.set('archived', 'true');
    if (view === 'trash') params.set('trash', 'true');
    if (category) params.set('category', category);
    if (search) params.set('q', search);
    try {
      const res = await authFetch(`/api/documents?${params}`);
      if (!res.ok) {
        setError(documentProblemMessage(await readProblem(res), 'Documents could not be loaded.'));
        return;
      }
      const next = (await res.json()) as DocumentPage;
      setPage((prev) => (pageNumber > 1 && prev ? { ...next, items: [...prev.items, ...next.items] } : next));
    } catch {
      setError('Documents could not be loaded.');
    } finally {
      setLoading(false);
    }
  }, [authFetch, category, search, view]);

  useEffect(() => { void loadCaps(); }, [loadCaps]);
  useEffect(() => { void load(1); }, [load]);
  useEffect(() => {
    const handle = setTimeout(() => setSearch(query.trim()), 300);
    return () => clearTimeout(handle);
  }, [query]);

  const upload = async (file: File, force = false) => {
    const form = new FormData();
    form.append('File', file, file.name);
    if (category) form.append('Category', category);
    if (force) form.append('Force', 'true');
    const res = await authFetch('/api/documents', {
      method: 'POST',
      headers: { 'Idempotency-Key': crypto.randomUUID() },
      body: form,
    });
    if (res.ok) return null;
    const problem = await readProblem(res);
    return {
      fileName: file.name,
      message: documentProblemMessage(problem, 'The file could not be uploaded.'),
      duplicateOf: problem?.code === 'DOCUMENT_DUPLICATE' ? problem.documentId : undefined,
      file: problem?.code === 'DOCUMENT_DUPLICATE' ? file : undefined,
    } satisfies UploadNotice;
  };

  const uploadAll = async (files: FileList | File[]) => {
    const list = Array.from(files);
    if (list.length === 0) return;
    setNotices([]);
    setUploading(list.length);
    const problems: UploadNotice[] = [];
    for (const file of list) {
      try {
        const notice = await upload(file);
        if (notice) problems.push(notice);
      } catch {
        problems.push({ fileName: file.name, message: 'The file could not be uploaded. Check your connection.' });
      }
      setUploading((n) => n - 1);
    }
    setNotices(problems);
    if (view !== 'active') setView('active');
    else await load(1);
    await loadCaps();
  };

  const uploadCopy = async (notice: UploadNotice) => {
    if (!notice.file) return;
    setNotices((prev) => prev.filter((n) => n !== notice));
    const again = await upload(notice.file, true);
    if (again) setNotices((prev) => [...prev, again]);
    await load(1);
    await loadCaps();
  };

  const openDuplicate = async (id: string) => {
    const res = await authFetch(`/api/documents/${encodeURIComponent(id)}`);
    if (res.ok) setSelected(await res.json());
  };

  const canUpload = !!caps?.enabled && caps.canManage;
  const items = page?.items ?? [];
  const tabs: [View, string][] = [['active', 'Documents'], ['archived', 'Archived'], ['trash', 'Trash']];

  return (
    <div
      className="space-y-6"
      onDragOver={(e) => {
        if (!canUpload) return;
        e.preventDefault();
        setDragging(true);
      }}
      onDragLeave={() => setDragging(false)}
      onDrop={(e) => {
        if (!canUpload) return;
        e.preventDefault();
        setDragging(false);
        void uploadAll(e.dataTransfer.files);
      }}
    >
      <PageHeader
        title="Documents"
        subtitle="Contracts, agreements, receipts and every file behind your bills and invoices, in one place."
        actions={
          <div className="flex flex-wrap items-start gap-2">
            {isOwner && <DownloadAllDocuments orgSlug={currentOrg.slug} />}
            {canUpload && (
              <>
                <input
                  ref={fileInput}
                  type="file"
                  multiple
                  accept={ACCEPTED_FILE_TYPES}
                  aria-label="Choose files to upload"
                  className="hidden"
                  onChange={(e) => {
                    if (e.target.files) void uploadAll(e.target.files);
                    e.target.value = '';
                  }}
                />
                <button
                  type="button"
                  onClick={() => fileInput.current?.click()}
                  disabled={uploading > 0}
                  className="inline-flex items-center gap-1.5 h-9 px-3 rounded-lg bg-[var(--brand-primary)] text-white text-xs font-semibold disabled:opacity-60"
                >
                  {uploading > 0 ? <Loader2 className="w-3.5 h-3.5 animate-spin" /> : <Upload className="w-3.5 h-3.5" />}
                  {uploading > 0 ? `Uploading ${uploading}…` : 'Upload files'}
                </button>
              </>
            )}
          </div>
        }
      />

      {caps && !caps.enabled ? (
        <p role="status" className="text-sm text-[var(--text-secondary)]">Documents isn&apos;t available in this workspace yet.</p>
      ) : (
        <>
          {caps && (
            <p className="text-xs text-[var(--text-secondary)]">
              {caps.storageLimitBytes != null
                ? `${formatBytes(caps.usedBytes)} of ${formatBytes(caps.storageLimitBytes)} used. Files in the trash still count.`
                : `${formatBytes(caps.usedBytes)} stored.`}{' '}
              PDF, JPEG or PNG, up to {formatBytes(caps.libraryLimit.maxBytes)} ({caps.libraryLimit.maxPdfPages} PDF pages) per file.
            </p>
          )}

          {notices.length > 0 && (
            <div role="alert" className="space-y-2">
              {notices.map((notice, index) => (
                <div key={`${notice.fileName}-${index}`}
                  className="p-3 rounded-xl bg-amber-500/10 border border-amber-500/20 text-xs text-[var(--text-main)] flex items-start justify-between gap-3">
                  <div className="flex gap-2">
                    <AlertTriangle className="w-4 h-4 shrink-0 text-amber-500" />
                    <span>
                      <strong>{notice.fileName}:</strong> {notice.message}
                      {notice.duplicateOf && (
                        <>
                          {' '}
                          <button type="button" className="underline" onClick={() => void openDuplicate(notice.duplicateOf!)}>Open it</button>
                          {' or '}
                          <button type="button" className="underline" onClick={() => void uploadCopy(notice)}>upload a separate copy</button>.
                        </>
                      )}
                    </span>
                  </div>
                  <button type="button" aria-label="Dismiss" onClick={() => setNotices((prev) => prev.filter((n) => n !== notice))}>
                    <X className="w-4 h-4" />
                  </button>
                </div>
              ))}
            </div>
          )}

          <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-3">
            <div className="flex items-center gap-1" role="tablist" aria-label="Document views">
              {tabs.map(([key, label]) => (
                <button
                  key={key}
                  role="tab"
                  aria-selected={view === key}
                  onClick={() => setView(key)}
                  className={`px-3 py-1.5 rounded-lg text-xs font-medium ${
                    view === key ? 'bg-[var(--brand-primary)] text-white' : 'text-[var(--text-secondary)] hover:bg-[var(--bg-surface)]'
                  }`}
                >
                  {label}
                </button>
              ))}
            </div>
            <div className="flex gap-2">
              <select
                aria-label="Category"
                value={category}
                onChange={(e) => setCategory(e.target.value)}
                className="h-8 px-2 text-xs rounded-lg border border-[var(--border-subtle)] bg-[var(--bg-surface)] text-[var(--text-primary)]"
              >
                <option value="">All categories</option>
                {DOCUMENT_CATEGORIES.map((c) => <option key={c.value} value={c.value}>{c.label}</option>)}
              </select>
              <div className="relative w-full sm:w-64">
                <Search className="w-4 h-4 absolute left-3 top-1/2 -translate-y-1/2 text-[var(--text-secondary)] pointer-events-none" />
                <input
                  type="search"
                  aria-label="Search documents"
                  placeholder="Search title, file name or notes"
                  value={query}
                  onChange={(e) => setQuery(e.target.value)}
                  className="w-full h-8 pl-9 pr-3 text-xs rounded-lg border border-[var(--border-subtle)] bg-[var(--bg-surface)] text-[var(--text-primary)]"
                />
              </div>
            </div>
          </div>

          {error && <p role="alert" className="text-sm text-rose-500">{error}</p>}

          <div className={`rounded-xl border bg-[var(--bg-surface)] overflow-hidden ${dragging ? 'border-[var(--brand-primary)] border-dashed' : 'border-[var(--border-subtle)]'}`}>
            {loading && !page ? (
              <div className="py-20 flex flex-col items-center gap-3 text-[var(--text-secondary)]">
                <Loader2 className="w-8 h-8 animate-spin text-[var(--brand-primary)]" />
                <p className="text-sm">Loading documents…</p>
              </div>
            ) : items.length === 0 ? (
              <EmptyState view={view} filtered={!!search || !!category} />
            ) : (
              <ul className="divide-y divide-[var(--border-subtle)]">
                {items.map((doc) => (
                  <li key={doc.id}>
                    <button type="button" onClick={() => setSelected(doc)}
                      className="w-full text-left px-4 py-3 flex items-center gap-3 hover:bg-[var(--bg-muted)]/30">
                      {doc.contentType.startsWith('image/')
                        ? <ImageIcon className="w-5 h-5 shrink-0 text-[var(--text-secondary)]" />
                        : <FileText className="w-5 h-5 shrink-0 text-[var(--text-secondary)]" />}
                      <span className="min-w-0 flex-1">
                        <span className="flex items-center gap-1.5 text-sm font-medium text-[var(--text-primary)]">
                          <span className="truncate">{doc.title}</span>
                          {doc.hasLockedLink && <Lock className="w-3 h-3 shrink-0" aria-label="Kept as evidence" />}
                        </span>
                        <span className="block text-xs text-[var(--text-secondary)] truncate">
                          {categoryLabel(doc.category)}
                          {doc.documentDate ? ` · ${doc.documentDate.slice(0, 10)}` : ''}
                          {' · '}{formatBytes(doc.byteCount)}
                          {doc.links.length > 0 ? ` · ${doc.links.map((l) => linkLabel(l.targetType)).join(', ')}` : ''}
                        </span>
                      </span>
                      <span className="hidden sm:block text-xs text-[var(--text-secondary)] whitespace-nowrap">
                        Added {new Date(doc.filedAt).toLocaleDateString()}
                      </span>
                    </button>
                  </li>
                ))}
              </ul>
            )}
            {page && page.items.length < page.total && (
              <div className="p-3 text-center border-t border-[var(--border-subtle)]">
                <button type="button" onClick={() => void load(page.page + 1)} disabled={loading}
                  className="text-xs font-semibold text-[var(--brand-primary)] hover:underline">
                  Show more ({page.total - page.items.length} left)
                </button>
              </div>
            )}
          </div>
        </>
      )}

      {selected && (
        <DocumentDetailDialog
          document={selected}
          orgSlug={currentOrg.slug}
          onClose={() => setSelected(null)}
          onChanged={(doc) => {
            setSelected(doc);
            setPage((prev) => (prev ? { ...prev, items: prev.items.map((d) => (d.id === doc.id ? doc : d)) } : prev));
          }}
          onRemoved={(id) => {
            setSelected(null);
            setPage((prev) => (prev ? { ...prev, total: prev.total - 1, items: prev.items.filter((d) => d.id !== id) } : prev));
            void loadCaps();
          }}
        />
      )}
    </div>
  );
};

const EmptyState: React.FC<{ view: View; filtered: boolean }> = ({ view, filtered }) => (
  <div className="py-16 px-4 text-center">
    <div className="w-12 h-12 rounded-full bg-[var(--bg-muted)] text-[var(--text-secondary)] mx-auto flex items-center justify-center mb-3">
      <FolderOpen className="w-6 h-6" />
    </div>
    <h3 className="text-base font-semibold text-[var(--text-primary)]">
      {filtered ? 'No matching documents' : view === 'trash' ? 'The trash is empty' : view === 'archived' ? 'No archived documents' : 'No documents yet'}
    </h3>
    <p className="text-xs text-[var(--text-secondary)] max-w-md mx-auto mt-1">
      {filtered
        ? 'Try another search or category.'
        : view === 'trash'
          ? 'Files you move to the trash stay here for 30 days, then are deleted for good.'
          : view === 'archived'
            ? 'Archived files are kept, just out of the way.'
            : 'Files arrive here three ways: upload them here (drag and drop works too), scan a bill or invoice, or attach a file to a bill, invoice, client or vendor.'}
    </p>
  </div>
);

export default Documents;
