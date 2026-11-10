// SPDX-License-Identifier: AGPL-3.0-only
import React, { useEffect, useRef, useState } from 'react';
import { BookCheck } from 'lucide-react';
import { getOrgSlugFromLocation, useAuth } from '../auth/AuthContext';
import { BOOKS_NOT_READY_EVENT, type BooksNotReadyProblem, type UnpostedDocument } from '../lib/entitlementEvents';
import { ModalShell } from './ModalShell';

type PostResult = { id: number; number?: string | null; posted: boolean; error?: string | null };

function documentHref(slug: string | null, doc: UnpostedDocument): string | null {
  if (!slug) return null;
  const base = `/org/${encodeURIComponent(slug)}`;
  return doc.type === 'invoice' ? `${base}/sales/${doc.id}` : `${base}/purchases/${doc.id}`;
}

function documentLabel(doc: UnpostedDocument): string {
  const kind = doc.type === 'invoice' ? 'Invoice' : 'Bill';
  return doc.number ? `${kind} ${doc.number}` : `${kind} #${doc.id}`;
}

function formatDate(value: string): string {
  const date = new Date(value);
  return Number.isNaN(date.getTime())
    ? value
    : date.toLocaleDateString(undefined, { year: 'numeric', month: 'short', day: 'numeric', timeZone: 'UTC' });
}

/** Explains BOOKS_NOT_READY wherever it came from, and posts the issued invoices that have no ledger entry. */
export const BooksCheckDialog: React.FC = () => {
  const [problem, setProblem] = useState<BooksNotReadyProblem | null>(null);

  useEffect(() => {
    const onBlocked = (event: Event) => setProblem((event as CustomEvent<BooksNotReadyProblem>).detail);
    window.addEventListener(BOOKS_NOT_READY_EVENT, onBlocked);
    return () => window.removeEventListener(BOOKS_NOT_READY_EVENT, onBlocked);
  }, []);

  if (!problem) return null;
  return <BooksCheckDialogView problem={problem} onClose={() => setProblem(null)} />;
};

export const BooksCheckDialogView: React.FC<{ problem: BooksNotReadyProblem; onClose: () => void }> = ({ problem, onClose }) => {
  const { authFetch } = useAuth();
  const closeRef = useRef<HTMLButtonElement>(null);
  const [invoices, setInvoices] = useState<UnpostedDocument[]>(problem.unpostedInvoices);
  const [bills, setBills] = useState<UnpostedDocument[]>(problem.unpostedBills);
  const [errors, setErrors] = useState<Record<number, string>>({});
  const [busy, setBusy] = useState(false);
  const [failure, setFailure] = useState<string | null>(null);
  const slug = typeof window === 'undefined' ? null : getOrgSlugFromLocation();
  const ready = invoices.length === 0 && bills.length === 0;

  useEffect(() => {
    closeRef.current?.focus();
  }, []);

  const postNow = async () => {
    setBusy(true);
    setFailure(null);
    try {
      const response = await authFetch('/api/Sales/PostUnpostedInvoices', { method: 'POST' }, false);
      if (!response.ok) throw new Error(`Posting failed (HTTP ${response.status}).`);
      const body = await response.json() as {
        results: PostResult[];
        unpostedInvoices: UnpostedDocument[];
        unpostedBills: UnpostedDocument[];
      };
      const nextErrors: Record<number, string> = {};
      for (const result of body.results ?? []) {
        if (!result.posted && result.error) nextErrors[result.id] = result.error;
      }
      setErrors(nextErrors);
      setInvoices(body.unpostedInvoices ?? []);
      setBills(body.unpostedBills ?? []);
    } catch (error) {
      setFailure(error instanceof Error ? error.message : 'Posting failed.');
    } finally {
      setBusy(false);
    }
  };

  const remaining = [...invoices, ...bills];

  return (
    <ModalShell
      open
      onClose={onClose}
      maxWidthClassName="max-w-md"
      aria-labelledby="books-check-title"
      aria-describedby="books-check-detail"
      panelClassName="p-6 sm:p-6 space-y-4"
      zClassName="z-[120]"
    >
      <div className="flex items-center gap-3">
        <div className="w-10 h-10 rounded-xl bg-[var(--brand-subtle)] text-[var(--brand-primary)] flex items-center justify-center shrink-0">
          <BookCheck className="w-5 h-5" aria-hidden="true" />
        </div>
        <h2 id="books-check-title" className="text-base font-bold text-[var(--text-main)]">
          {ready ? 'Your books are ready' : 'Post your documents first'}
        </h2>
      </div>
      <div id="books-check-detail" className="space-y-3 text-sm text-[var(--text-secondary)]">
        {ready ? (
          <p>Every issued invoice and bill now has its ledger entry. Try the change again.</p>
        ) : (
          <>
            <p>
              Accounting shows your general ledger, so every issued document needs its ledger entry first.
              {' '}{remaining.length === 1 ? 'This document is' : `These ${remaining.length} documents are`} missing one.
              Each posts on its own date.
            </p>
            <ul className="space-y-2 max-h-56 overflow-y-auto">
              {remaining.map((doc) => {
                const href = documentHref(slug, doc);
                const error = doc.type === 'invoice' ? errors[doc.id] : undefined;
                return (
                  <li key={`${doc.type}-${doc.id}`} className="rounded-xl bg-[var(--bg-subtle)] p-3">
                    <div className="flex items-center justify-between gap-2">
                      {href
                        ? <a className="font-semibold text-[var(--text-main)] hover:underline" href={href}>{documentLabel(doc)}</a>
                        : <span className="font-semibold text-[var(--text-main)]">{documentLabel(doc)}</span>}
                      <span className="text-xs text-[var(--text-muted)]">{formatDate(doc.date)}</span>
                    </div>
                    {error && <p role="alert" className="mt-1 text-xs text-red-400">{error}</p>}
                    {doc.type === 'bill' && <p className="mt-1 text-xs text-[var(--text-muted)]">Open the bill to review it, or contact support.</p>}
                  </li>
                );
              })}
            </ul>
            {Object.keys(errors).length > 0 && (
              <p className="text-xs">Fix the problems above (for example, an item or tax with no account), then post again.</p>
            )}
          </>
        )}
        {failure && <p role="alert" className="text-xs text-red-400">{failure}</p>}
      </div>
      <div className="flex justify-end gap-2">
        {!ready && invoices.length > 0 && (
          <button
            type="button"
            disabled={busy}
            onClick={() => void postNow()}
            className="h-10 px-4 rounded-xl text-xs font-semibold text-white bg-[var(--brand-primary)] disabled:opacity-50"
          >
            {busy ? 'Posting…' : 'Post now'}
          </button>
        )}
        <button
          type="button"
          ref={closeRef}
          onClick={onClose}
          className={ready
            ? 'h-10 px-4 rounded-xl text-xs font-semibold text-white bg-[var(--brand-primary)]'
            : 'h-10 px-4 rounded-xl text-xs font-semibold text-[var(--text-secondary)] hover:bg-[var(--bg-surface-hover)]'}
        >
          {ready ? 'Done' : 'Not now'}
        </button>
      </div>
    </ModalShell>
  );
};

export default BooksCheckDialog;
