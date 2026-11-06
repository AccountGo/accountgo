import React, { useCallback, useEffect, useRef, useState } from 'react';
import { History, Loader2, X } from 'lucide-react';
import { useOutletContext } from 'react-router-dom';
import { useAuth } from '../auth/AuthContext';
import type { TenantContextType } from '../types';
import { useOptionalCapabilities } from '../lib/entitlements';
import { auditAccessLevel, canShowRecordHistory } from '../lib/auditAccess';
import { groupHistoryBySave, problemMessage, recordHistoryUrl, type ActivityPage } from '../lib/auditFeed';
import { auditActorLabel, auditEntityName, describeChange, formatAuditDateTime } from '../lib/auditLabels';
import { ModalShell } from './ModalShell';
import { HeaderButton } from './PageHeader';

export interface RecordHistoryPanelProps {
  entityType: string;
  entityId: number | string;
  open: boolean;
  onClose: () => void;
  /** Personal books use their own record names and never mention organizations. */
  personal: boolean;
}

/**
 * Whether this viewer gets History buttons (every access level except none), and whether the books are personal.
 * Safe outside the capabilities provider: without a plan snapshot it falls back to single-record History.
 */
export function useRecordHistoryAccess() {
  const { hasPermission, currentOrg } = useOutletContext<TenantContextType>();
  const capabilities = useOptionalCapabilities();
  const access = auditAccessLevel(capabilities?.entitlements ?? null, hasPermission);
  return { canShow: canShowRecordHistory(access), personal: !!currentOrg?.isPersonalBooks };
}

/** The History button's state for a page with one record: whether to show it, and whether the panel is open. */
export function useRecordHistory() {
  const access = useRecordHistoryAccess();
  const [open, setOpen] = useState(false);
  return {
    ...access,
    open,
    openPanel: useCallback(() => setOpen(true), []),
    closePanel: useCallback(() => setOpen(false), []),
  };
}

/** Button plus panel for a record shown inside a larger page (a journal editor, for example). */
export const RecordHistoryAction: React.FC<{ entityType: string; entityId: number | string }> = ({ entityType, entityId }) => {
  const history = useRecordHistory();
  if (!history.canShow) return null;
  return (
    <>
      <RecordHistoryButton onClick={history.openPanel} />
      <RecordHistoryPanel
        entityType={entityType}
        entityId={entityId}
        open={history.open}
        onClose={history.closePanel}
        personal={history.personal}
      />
    </>
  );
};

/** History button for a detail page's header actions. */
export const RecordHistoryButton: React.FC<{ onClick: () => void }> = ({ onClick }) => (
  <HeaderButton label="History" icon={History} compact onClick={onClick} />
);

export const RecordHistoryPanel: React.FC<RecordHistoryPanelProps> = ({ entityType, entityId, open, onClose, personal }) => {
  const { authFetch } = useAuth();
  const [items, setItems] = useState<ActivityPage['items']>([]);
  const [nextCursor, setNextCursor] = useState<number | null>(null);
  const [loading, setLoading] = useState(false);
  const [loaded, setLoaded] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const requestRef = useRef(0);

  const load = useCallback(async (cursor: number | null) => {
    const requestId = ++requestRef.current;
    setLoading(true);
    setError(null);
    try {
      const response = await authFetch(recordHistoryUrl(entityType, entityId, cursor));
      const body = await response.json().catch(() => null);
      if (requestId !== requestRef.current) return;
      if (!response.ok) {
        setError(problemMessage(body, 'Could not load history.'));
        return;
      }
      const page = body as ActivityPage;
      setItems((previous) => (cursor ? [...previous, ...(page.items ?? [])] : page.items ?? []));
      setNextCursor(page.nextCursor ?? null);
      setLoaded(true);
    } catch (err) {
      if (requestId !== requestRef.current) return;
      setError(err instanceof Error ? err.message : 'Could not load history.');
    } finally {
      if (requestId === requestRef.current) setLoading(false);
    }
  }, [authFetch, entityType, entityId]);

  useEffect(() => {
    if (!open) return;
    setItems([]);
    setNextCursor(null);
    setLoaded(false);
    void load(null);
    return () => { requestRef.current += 1; };
  }, [open, load]);

  const groups = groupHistoryBySave(items);
  const recordName = auditEntityName(entityType, personal);

  return (
    <ModalShell
      open={open}
      onClose={onClose}
      bare
      aria-labelledby="record-history-title"
      panelClassName="ml-auto self-stretch w-full max-w-md flex flex-col rounded-2xl border border-[var(--border-subtle)] bg-[var(--bg-surface)] text-[var(--text-main)] shadow-2xl"
      overlayClassName="no-print"
    >
      <div className="flex items-center justify-between gap-3 p-5 border-b border-[var(--border-subtle)]">
        <div className="min-w-0">
          <h2 id="record-history-title" className="text-base font-bold truncate">History</h2>
          <p className="text-xs text-[var(--text-muted)] truncate">{recordName} changes, newest first</p>
        </div>
        <button
          type="button"
          onClick={onClose}
          aria-label="Close history"
          className="h-9 w-9 shrink-0 inline-flex items-center justify-center rounded-xl text-[var(--text-secondary)] hover:bg-[var(--bg-surface-hover)]"
        >
          <X className="w-4 h-4" aria-hidden="true" />
        </button>
      </div>

      <div className="flex-1 overflow-y-auto p-5 space-y-4" aria-live="polite">
        {error && <p role="alert" className="text-xs text-red-400">{error}</p>}
        {loaded && !error && groups.length === 0 && (
          <p className="text-sm text-[var(--text-muted)]">No changes recorded yet.</p>
        )}
        {groups.map((group) => (
          <section key={group.key} className="rounded-xl bg-[var(--bg-subtle)] p-3 space-y-2">
            <div className="flex flex-wrap items-baseline justify-between gap-x-3 text-xs">
              <span className="font-semibold text-[var(--text-main)]">{auditActorLabel(group)}</span>
              <time dateTime={group.occurredAt} className="text-[var(--text-muted)]">{formatAuditDateTime(group.occurredAt)}</time>
            </div>
            {group.parts.map((part) => (
              <div key={part.heading ?? 'record'} className="space-y-1">
                {part.heading && <p className="text-[11px] font-semibold uppercase tracking-wide text-[var(--text-muted)]">{part.heading}</p>}
                <ul className="space-y-1 text-sm text-[var(--text-secondary)]">
                  {part.rows.flatMap((row) => describeChange(row, personal).map((sentence, index) => (
                    <li key={`${row.id}-${index}`}>{sentence}</li>
                  )))}
                </ul>
              </div>
            ))}
          </section>
        ))}
        {loading && (
          <p className="flex items-center gap-2 text-xs text-[var(--text-muted)]">
            <Loader2 className="w-4 h-4 animate-spin" aria-hidden="true" /> Loading…
          </p>
        )}
        {nextCursor !== null && !loading && (
          <button
            type="button"
            onClick={() => void load(nextCursor)}
            className="h-9 px-4 rounded-xl text-xs font-semibold text-[var(--text-secondary)] bg-[var(--bg-subtle)] hover:bg-[var(--bg-surface-hover)]"
          >
            Load more
          </button>
        )}
      </div>
    </ModalShell>
  );
};

export default RecordHistoryPanel;
