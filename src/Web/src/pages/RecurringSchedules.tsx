// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

import React, { useCallback, useEffect, useMemo, useState } from 'react';
import { useNavigate, useOutletContext, useParams, useSearchParams } from 'react-router-dom';
import { AlertTriangle, Loader2, Plus, Repeat, Search } from 'lucide-react';
import { useAuth } from '../auth/AuthContext';
import { usePermissions, ReadOnlyModuleBanner } from '../components/Gating';
import { PageHeader, HeaderButton, StatusPill } from '../components/PageHeader';
import { SalesTabs } from '../components/SalesTabs';
import type { TenantContextType } from '../types';
import type { RecurringScheduleSummary } from '../types/recurring';
import { cadenceLabel, scheduleStatusTone, formatRunDate } from '../lib/recurring';

type FilterKey = 'all' | 'active' | 'paused' | 'ended' | 'attention';

export const RecurringSchedules: React.FC = () => {
  const { currentOrg, canWrite } = useOutletContext<TenantContextType>();
  const { hasPermission } = usePermissions();
  const { authFetch } = useAuth();
  const { orgSlug } = useParams<{ orgSlug: string }>();
  const navigate = useNavigate();
  const [searchParams, setSearchParams] = useSearchParams();

  const salesWritable = canWrite('sales');
  const canView = hasPermission('sales.invoices.view');
  const canCreate = hasPermission('sales.invoices.create') && salesWritable;

  const [schedules, setSchedules] = useState<RecurringScheduleSummary[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [errorBanner, setErrorBanner] = useState<string | null>(null);
  const [searchQuery, setSearchQuery] = useState('');

  const initialFilter = (searchParams.get('filter') as FilterKey) || 'all';
  const [filter, setFilter] = useState<FilterKey>(
    ['all', 'active', 'paused', 'ended', 'attention'].includes(initialFilter) ? initialFilter : 'all',
  );

  const loadSchedules = useCallback(async () => {
    setIsLoading(true);
    try {
      const res = await authFetch('/api/RecurringSchedules');
      if (!res.ok) throw new Error(`Failed to load recurring schedules (${res.status}).`);
      const data = await res.json();
      setSchedules(Array.isArray(data) ? data : []);
    } catch (err: any) {
      setErrorBanner(err.message || 'The recurring schedules could not be loaded.');
    } finally {
      setIsLoading(false);
    }
  }, [authFetch]);

  useEffect(() => {
    if (canView) void loadSchedules();
  }, [canView, loadSchedules]);

  const counts = useMemo(() => {
    return {
      all: schedules.length,
      active: schedules.filter((s) => s.status === 'Active').length,
      paused: schedules.filter((s) => s.status === 'Paused').length,
      ended: schedules.filter((s) => s.status === 'Ended').length,
      attention: schedules.filter((s) => s.needsAttention).length,
    };
  }, [schedules]);

  const filtered = useMemo(() => {
    let rows = schedules;
    if (filter === 'attention') rows = rows.filter((s) => s.needsAttention);
    else if (filter !== 'all') {
      const target = filter.charAt(0).toUpperCase() + filter.slice(1);
      rows = rows.filter((s) => s.status === target);
    }
    const q = searchQuery.trim().toLowerCase();
    if (q) {
      rows = rows.filter(
        (s) => s.name.toLowerCase().includes(q) || (s.customerName ?? '').toLowerCase().includes(q),
      );
    }
    return rows;
  }, [schedules, filter, searchQuery]);

  const onFilter = (key: FilterKey) => {
    setFilter(key);
    const next = new URLSearchParams(searchParams);
    if (key === 'all') next.delete('filter');
    else next.set('filter', key);
    setSearchParams(next, { replace: true });
  };

  if (!canView) {
    return (
      <div className="max-w-5xl mx-auto px-4 py-6">
        <p className="text-sm text-[var(--text-muted)]">You do not have permission to view recurring schedules.</p>
      </div>
    );
  }

  const tabs: { key: FilterKey; label: string }[] = [
    { key: 'all', label: 'All' },
    { key: 'active', label: 'Active' },
    { key: 'paused', label: 'Paused' },
    { key: 'ended', label: 'Ended' },
    { key: 'attention', label: 'Needs attention' },
  ];

  return (
    <div className="max-w-6xl mx-auto px-4 sm:px-6 py-6 space-y-6 text-[var(--text-main)]">
      <ReadOnlyModuleBanner module="sales" orgSlug={orgSlug} />

      <PageHeader
        title="Recurring invoices"
        subtitle="Schedules that generate sales invoices on a calendar you control."
        actions={
          canCreate && (
            <HeaderButton
              label="New schedule"
              icon={Plus}
              variant="primary"
              to={`/org/${orgSlug}/sales/recurring/new`}
            />
          )
        }
      />

      <SalesTabs orgSlug={currentOrg.slug} />

      {errorBanner && (
        <div role="alert" className="p-4 bg-rose-500/10 border border-rose-500/20 text-rose-500 rounded-xl flex items-start gap-3">
          <AlertTriangle className="w-5 h-5 shrink-0 mt-0.5" />
          <div className="flex-1 text-sm font-medium">{errorBanner}</div>
        </div>
      )}

      {/* Filters + search */}
      <div className="flex flex-wrap items-center gap-2 justify-between">
        <div className="flex flex-wrap gap-1.5">
          {tabs.map((t) => (
            <button
              key={t.key}
              type="button"
              onClick={() => onFilter(t.key)}
              className={`inline-flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-sm font-medium transition ${
                filter === t.key
                  ? 'bg-[var(--brand-subtle)] text-[var(--brand-primary)]'
                  : 'text-[var(--text-secondary)] hover:bg-[var(--bg-surface-hover)]'
              }`}
            >
              {t.label}
              <span className="text-xs opacity-70">{counts[t.key]}</span>
              {t.key === 'attention' && counts.attention > 0 && (
                <span className="w-1.5 h-1.5 rounded-full bg-amber-400" aria-hidden />
              )}
            </button>
          ))}
        </div>
        <div className="relative">
          <Search className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-[var(--text-muted)]" />
          <input
            type="search"
            value={searchQuery}
            onChange={(e) => setSearchQuery(e.target.value)}
            placeholder="Search schedules or clients"
            aria-label="Search recurring schedules"
            className="h-9 pl-9 pr-3 text-sm bg-[var(--bg-subtle)] border border-[var(--border-subtle)] rounded-lg focus:ring-2 focus:ring-[var(--brand-ring)] focus:outline-none w-64 max-w-full"
          />
        </div>
      </div>

      {/* Table */}
      <div className="border border-[var(--border-subtle)] rounded-xl bg-[var(--bg-surface)] overflow-hidden">
        {isLoading ? (
          <div className="p-10 flex items-center justify-center text-[var(--text-muted)]">
            <Loader2 className="w-5 h-5 animate-spin mr-2" /> Loading…
          </div>
        ) : filtered.length === 0 ? (
          <div className="p-12 text-center">
            <Repeat className="w-8 h-8 mx-auto mb-3 text-[var(--text-muted)]" />
            <p className="text-sm text-[var(--text-secondary)]">
              {schedules.length === 0 ? 'No recurring schedules yet.' : 'No schedules match this filter.'}
            </p>
            {canCreate && schedules.length === 0 && (
              <div className="mt-4">
                <HeaderButton label="New schedule" icon={Plus} variant="primary" to={`/org/${orgSlug}/sales/recurring/new`} />
              </div>
            )}
          </div>
        ) : (
          <table className="w-full text-left border-collapse">
            <thead>
              <tr className="bg-[var(--bg-subtle)] text-xs font-semibold text-[var(--text-muted)] uppercase tracking-wider border-b border-[var(--border-subtle)]">
                <th className="py-3 px-4">Schedule</th>
                <th className="py-3 px-4">Client</th>
                <th className="py-3 px-4">Cadence</th>
                <th className="py-3 px-4">Next run</th>
                <th className="py-3 px-4 text-right">Runs</th>
                <th className="py-3 px-4">Status</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-[var(--border-subtle)] text-sm">
              {filtered.map((s) => (
                <tr
                  key={s.id}
                  onClick={() => navigate(`/org/${orgSlug}/sales/recurring/${s.id}`)}
                  className="cursor-pointer hover:bg-[var(--bg-surface-hover)]/50 transition"
                >
                  <td className="py-3 px-4 font-medium text-[var(--text-main)]">
                    <span className="flex items-center gap-2">
                      {s.name}
                      {s.action === 'AutoIssue' && (
                        <StatusPill tone="blue" title="Auto-issues the invoice when guards pass">Auto-issue</StatusPill>
                      )}
                    </span>
                  </td>
                  <td className="py-3 px-4">
                    <span className={s.customerArchived ? 'text-[var(--text-muted)]' : ''}>
                      {s.customerName ?? '—'}
                      {s.customerArchived && <span className="ml-1 text-xs">(archived)</span>}
                    </span>
                  </td>
                  <td className="py-3 px-4 text-[var(--text-secondary)]">{cadenceLabel(s.cadence, s.interval)}</td>
                  <td className="py-3 px-4">
                    <span className={s.isOverdue ? 'text-amber-600 dark:text-amber-400 font-medium' : 'text-[var(--text-secondary)]'}>
                      {formatRunDate(s.nextRunDate)}
                      {s.isOverdue && ' (due)'}
                    </span>
                  </td>
                  <td className="py-3 px-4 text-right font-mono text-[var(--text-secondary)]">
                    {s.occurrenceCount}
                    {s.maxOccurrences ? ` / ${s.maxOccurrences}` : ''}
                  </td>
                  <td className="py-3 px-4">
                    <span className="flex items-center gap-1.5">
                      <StatusPill tone={scheduleStatusTone(s.status)}>{s.status}</StatusPill>
                      {s.needsAttention && (
                        <StatusPill tone="amber" icon={AlertTriangle} title="This schedule needs attention">
                          Attention
                        </StatusPill>
                      )}
                    </span>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>
    </div>
  );
};

export default RecurringSchedules;
