import React, { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { Link } from 'react-router-dom';
import { Download, Loader2, Sparkles } from 'lucide-react';
import { useAuth } from '../auth/AuthContext';
import type { WorkspaceMember } from '../types';
import {
  activityPlanLimitProblem,
  canExportActivity,
  canViewSecurityLog,
  recent90Notice,
  type ActivityTabMode,
  type AuditAccessLevel,
} from '../lib/auditAccess';
import {
  activityListUrl,
  activityRecordLabel,
  personFilterOptions,
  problemMessage,
  securityEventsUrl,
  type ActivityFilters,
  type ActivityItem,
  type SecurityEventFilters,
  type SecurityEventItem,
} from '../lib/auditFeed';
import {
  auditActorLabel,
  auditEntityFilterOptions,
  describeChange,
  formatAuditDateTime,
  securityEventFilterOptions,
  securityEventLabel,
  securityOutcomeLabel,
} from '../lib/auditLabels';
import { planLimitCta } from './PlanLimitDialog';

export interface ActivitySettingsTabProps {
  mode: Exclude<ActivityTabMode, 'hidden'>;
  access: AuditAccessLevel;
  personal: boolean;
  members: WorkspaceMember[];
  canManageBilling: boolean;
  /** Where "upgrade" and the plan-limit card's link go; null hides the link. */
  subscriptionHref: string | null;
}

type View = 'changes' | 'security';

const fieldClass = 'h-10 w-full rounded-xl border border-[var(--border-subtle)] bg-[var(--bg-subtle)] px-3 text-sm text-[var(--text-main)] focus:outline-none focus:ring-2 focus:ring-[var(--brand-primary)]';
const labelClass = 'block text-[11px] font-semibold uppercase tracking-wide text-[var(--text-muted)] mb-1';
const secondaryButtonClass = 'h-9 px-4 rounded-xl text-xs font-semibold text-[var(--text-secondary)] bg-[var(--bg-subtle)] hover:bg-[var(--bg-surface-hover)] disabled:opacity-50';

interface Page<T> { items: T[]; nextCursor: number | null }

/** Newest-first list with "Load more"; reloads from the top whenever the URL builder changes. */
function usePagedFeed<T>(buildUrl: (cursor: number | null) => string, enabled: boolean) {
  const { authFetch } = useAuth();
  const [items, setItems] = useState<T[]>([]);
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
      const response = await authFetch(buildUrl(cursor), undefined, true);
      const body = await response.json().catch(() => null);
      if (requestId !== requestRef.current) return;
      if (!response.ok) {
        setError(problemMessage(body, 'Could not load activity.'));
        return;
      }
      const page = body as Page<T>;
      setItems((previous) => (cursor ? [...previous, ...(page.items ?? [])] : page.items ?? []));
      setNextCursor(page.nextCursor ?? null);
      setLoaded(true);
    } catch (err) {
      if (requestId !== requestRef.current) return;
      setError(err instanceof Error ? err.message : 'Could not load activity.');
    } finally {
      if (requestId === requestRef.current) setLoading(false);
    }
  }, [authFetch, buildUrl]);

  useEffect(() => {
    if (!enabled) return;
    setItems([]);
    setNextCursor(null);
    setLoaded(false);
    void load(null);
    return () => { requestRef.current += 1; };
  }, [enabled, load]);

  return { items, nextCursor, loading, loaded, error, loadMore: () => void load(nextCursor) };
}

const FeedFooter: React.FC<{ loading: boolean; hasMore: boolean; onLoadMore: () => void }> = ({ loading, hasMore, onLoadMore }) => (
  <>
    {loading && (
      <p className="flex items-center gap-2 text-xs text-[var(--text-muted)]">
        <Loader2 className="w-4 h-4 animate-spin" aria-hidden="true" /> Loading…
      </p>
    )}
    {hasMore && !loading && <button type="button" onClick={onLoadMore} className={secondaryButtonClass}>Load more</button>}
  </>
);

const DateRangeFields: React.FC<{
  fromDate: string; toDate: string; onFrom: (value: string) => void; onTo: (value: string) => void;
}> = ({ fromDate, toDate, onFrom, onTo }) => (
  <>
    <label className="block">
      <span className={labelClass}>From</span>
      <input type="date" value={fromDate} max={toDate || undefined} onChange={(event) => onFrom(event.target.value)} className={fieldClass} />
    </label>
    <label className="block">
      <span className={labelClass}>To</span>
      <input type="date" value={toDate} min={fromDate || undefined} onChange={(event) => onTo(event.target.value)} className={fieldClass} />
    </label>
  </>
);

const PersonSelect: React.FC<{
  value: string; onChange: (value: string) => void; members: WorkspaceMember[];
}> = ({ value, onChange, members }) => (
  <label className="block">
    <span className={labelClass}>Person</span>
    <select value={value} onChange={(event) => onChange(event.target.value)} className={fieldClass}>
      <option value="">Everyone</option>
      {personFilterOptions(members).map((option) => <option key={option.value} value={option.value}>{option.label}</option>)}
    </select>
  </label>
);

const PlanLimitCard: React.FC<{ canManageBilling: boolean; subscriptionHref: string | null }> = ({ canManageBilling, subscriptionHref }) => {
  const problem = activityPlanLimitProblem(canManageBilling);
  const cta = planLimitCta(problem, subscriptionHref);
  return (
    <div className="rounded-2xl bg-[var(--bg-surface)] p-6 shadow-sm space-y-3" data-testid="activity-plan-limit">
      <div className="flex items-center gap-3">
        <div className="w-10 h-10 rounded-xl bg-[var(--brand-subtle)] text-[var(--brand-primary)] flex items-center justify-center shrink-0">
          <Sparkles className="w-5 h-5" aria-hidden="true" />
        </div>
        <h2 className="text-base font-bold text-[var(--text-main)]">Activity is not included in your plan</h2>
      </div>
      <p className="text-sm text-[var(--text-secondary)]">{problem.detail}</p>
      {cta.href
        ? <Link to={cta.href} className="inline-flex h-10 items-center rounded-xl px-4 text-xs font-semibold text-[var(--brand-primary)] bg-[var(--brand-subtle)]">{cta.label}</Link>
        : <p className="text-xs font-semibold text-[var(--text-secondary)]">{cta.label}</p>}
    </div>
  );
};

const ChangesView: React.FC<ActivitySettingsTabProps> = ({ access, personal, members, canManageBilling, subscriptionHref }) => {
  const { authFetch } = useAuth();
  const [filters, setFilters] = useState<ActivityFilters>({});
  const [exporting, setExporting] = useState(false);
  const [exportError, setExportError] = useState<string | null>(null);
  const buildUrl = useCallback((cursor: number | null) => activityListUrl(filters, cursor), [filters]);
  const feed = usePagedFeed<ActivityItem>(buildUrl, true);
  const notice = recent90Notice(access, personal, canManageBilling);
  const entityOptions = useMemo(() => auditEntityFilterOptions(personal), [personal]);
  const update = (patch: Partial<ActivityFilters>) => setFilters((previous) => ({ ...previous, ...patch }));

  const exportCsv = async () => {
    setExporting(true);
    setExportError(null);
    try {
      const response = await authFetch(activityListUrl(filters, null, true), undefined, true);
      if (!response.ok) {
        const body = await response.json().catch(() => null);
        setExportError(problemMessage(body, 'Could not export activity.'));
        return;
      }
      const blob = await response.blob();
      const url = URL.createObjectURL(blob);
      const link = document.createElement('a');
      link.href = url;
      link.download = 'activity-export.csv';
      document.body.appendChild(link);
      link.click();
      link.remove();
      URL.revokeObjectURL(url);
    } catch (err) {
      setExportError(err instanceof Error ? err.message : 'Could not export activity.');
    } finally {
      setExporting(false);
    }
  };

  return (
    <div className="space-y-4">
      {notice && (
        <p role="status" className="rounded-xl bg-[var(--bg-subtle)] p-3 text-xs text-[var(--text-secondary)]">
          {notice.lead}
          {notice.linkLabel && (subscriptionHref
            ? <Link to={subscriptionHref} className="font-semibold text-[var(--brand-primary)] hover:underline">{notice.linkLabel}</Link>
            : notice.linkLabel)}
          {notice.tail}
        </p>
      )}

      <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
        <PersonSelect value={filters.actorUserId ?? ''} onChange={(value) => update({ actorUserId: value })} members={members} />
        <label className="block">
          <span className={labelClass}>Record type</span>
          <select value={filters.entityType ?? ''} onChange={(event) => update({ entityType: event.target.value })} className={fieldClass}>
            <option value="">All records</option>
            {entityOptions.map((option) => <option key={option.value} value={option.value}>{option.label}</option>)}
          </select>
        </label>
        <DateRangeFields
          fromDate={filters.fromDate ?? ''}
          toDate={filters.toDate ?? ''}
          onFrom={(value) => update({ fromDate: value })}
          onTo={(value) => update({ toDate: value })}
        />
      </div>

      {canExportActivity(access) && (
        <div className="flex flex-wrap items-center gap-3">
          <button type="button" onClick={() => void exportCsv()} disabled={exporting} className={`${secondaryButtonClass} inline-flex items-center gap-2`}>
            {exporting ? <Loader2 className="w-4 h-4 animate-spin" aria-hidden="true" /> : <Download className="w-4 h-4" aria-hidden="true" />}
            Export CSV
          </button>
          {exportError && <p role="alert" className="text-xs text-red-400">{exportError}</p>}
        </div>
      )}

      <div className="space-y-3" aria-live="polite">
        {feed.error && <p role="alert" className="text-xs text-red-400">{feed.error}</p>}
        {feed.loaded && !feed.error && feed.items.length === 0 && (
          <p className="text-sm text-[var(--text-muted)]">No changes recorded yet.</p>
        )}
        <ul className="space-y-3">
          {feed.items.map((item) => (
            <li key={item.id} className="rounded-xl bg-[var(--bg-subtle)] p-3 space-y-1.5">
              <div className="flex flex-wrap items-baseline justify-between gap-x-3 text-xs">
                <span className="font-semibold text-[var(--text-main)]">{auditActorLabel(item)}</span>
                <time dateTime={item.occurredAt} className="text-[var(--text-muted)]">{formatAuditDateTime(item.occurredAt)}</time>
              </div>
              <p className="text-[11px] font-semibold uppercase tracking-wide text-[var(--text-muted)]">{activityRecordLabel(item, personal)}</p>
              <ul className="space-y-1 text-sm text-[var(--text-secondary)]">
                {describeChange(item, personal).map((sentence, index) => <li key={index}>{sentence}</li>)}
              </ul>
            </li>
          ))}
        </ul>
        <FeedFooter loading={feed.loading} hasMore={feed.nextCursor !== null} onLoadMore={feed.loadMore} />
      </div>
    </div>
  );
};

const SecurityLogView: React.FC<Pick<ActivitySettingsTabProps, 'personal' | 'members'>> = ({ personal, members }) => {
  const [filters, setFilters] = useState<SecurityEventFilters>({});
  const buildUrl = useCallback((cursor: number | null) => securityEventsUrl(filters, cursor), [filters]);
  const feed = usePagedFeed<SecurityEventItem>(buildUrl, true);
  const typeOptions = useMemo(() => securityEventFilterOptions(personal), [personal]);
  const update = (patch: Partial<SecurityEventFilters>) => setFilters((previous) => ({ ...previous, ...patch }));

  return (
    <div className="space-y-4">
      <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
        <label className="block">
          <span className={labelClass}>Event</span>
          <select value={filters.type ?? ''} onChange={(event) => update({ type: event.target.value })} className={fieldClass}>
            <option value="">All events</option>
            {typeOptions.map((option) => <option key={option.value} value={option.value}>{option.label}</option>)}
          </select>
        </label>
        <PersonSelect value={filters.actorUserId ?? ''} onChange={(value) => update({ actorUserId: value })} members={members} />
        <DateRangeFields
          fromDate={filters.fromDate ?? ''}
          toDate={filters.toDate ?? ''}
          onFrom={(value) => update({ fromDate: value })}
          onTo={(value) => update({ toDate: value })}
        />
      </div>

      <div className="space-y-3" aria-live="polite">
        {feed.error && <p role="alert" className="text-xs text-red-400">{feed.error}</p>}
        {feed.loaded && !feed.error && feed.items.length === 0 && (
          <p className="text-sm text-[var(--text-muted)]">No security events recorded yet.</p>
        )}
        <ul className="space-y-3">
          {feed.items.map((event) => (
            <li key={event.id} className="rounded-xl bg-[var(--bg-subtle)] p-3 space-y-1">
              <div className="flex flex-wrap items-baseline justify-between gap-x-3 gap-y-1">
                <span className="text-sm font-semibold text-[var(--text-main)]">{securityEventLabel(event.type, personal)}</span>
                <time dateTime={event.createdAt} className="text-xs text-[var(--text-muted)]">{formatAuditDateTime(event.createdAt)}</time>
              </div>
              <p className="text-xs text-[var(--text-secondary)]">
                {event.actorName || 'Unknown user'}
                {event.outcome ? ` · ${securityOutcomeLabel(event.outcome)}` : ''}
                {event.ipAddress ? ` · ${event.ipAddress}` : ''}
              </p>
            </li>
          ))}
        </ul>
        <FeedFooter loading={feed.loading} hasMore={feed.nextCursor !== null} onLoadMore={feed.loadMore} />
      </div>
    </div>
  );
};

/** Settings → Activity: who changed what and when, plus the security log and CSV export on the full level. */
export const ActivitySettingsTab: React.FC<ActivitySettingsTabProps> = (props) => {
  const { mode, access, personal } = props;
  const [view, setView] = useState<View>('changes');

  if (mode === 'planLimit') {
    return <PlanLimitCard canManageBilling={props.canManageBilling} subscriptionHref={props.subscriptionHref} />;
  }

  const showSecurity = canViewSecurityLog(access);
  const activeView: View = showSecurity ? view : 'changes';

  return (
    <div className="rounded-2xl bg-[var(--bg-surface)] p-5 sm:p-6 shadow-sm space-y-5">
      <div className="space-y-1">
        <h2 className="text-base font-bold text-[var(--text-main)]">Activity</h2>
        <p className="text-xs text-[var(--text-muted)]">
          {personal ? 'Changes made in your books, newest first.' : 'Changes made in this workspace, newest first.'}
        </p>
      </div>

      {showSecurity && (
        <div role="group" aria-label="Activity view" className="flex gap-2 text-xs">
          {([['changes', 'Record changes'], ['security', 'Security log']] as const).map(([key, label]) => (
            <button
              key={key}
              type="button"
              aria-pressed={activeView === key}
              onClick={() => setView(key)}
              className={`h-9 px-4 rounded-xl font-semibold ${activeView === key ? 'bg-[var(--brand-primary)] text-white' : 'bg-[var(--bg-subtle)] text-[var(--text-secondary)] hover:bg-[var(--bg-surface-hover)]'}`}
            >
              {label}
            </button>
          ))}
        </div>
      )}

      {activeView === 'changes'
        ? <ChangesView {...props} />
        : <SecurityLogView personal={personal} members={props.members} />}
    </div>
  );
};

export default ActivitySettingsTab;
