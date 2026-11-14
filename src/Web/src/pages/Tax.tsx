import React, { Fragment, useCallback, useEffect, useRef, useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import {
  AlertTriangle,
  ChevronDown,
  ChevronRight,
  FileDown,
  Loader2,
  Lock,
} from 'lucide-react';
import { useAuth } from '../auth/AuthContext';
import { PermissionDeniedCard, useTenantContext } from '../components/Gating';
import { HeaderButton, PageHeader } from '../components/PageHeader';
import { DEFAULT_CURRENCY } from '../lib/currency';
import {
  buildTaxReportCsv,
  createLatestRequestGuard,
  documentKindLabel,
  isValidTaxDateRange,
  type TaxPeriodItem,
  type TaxPeriodReport,
  type TaxPeriodSideSummary,
} from '../lib/taxReport';

function formatMoney(amount: number, currency: string): string {
  const abs = Math.abs(amount).toFixed(2).replace(/\B(?=(\d{3})+(?!\d))/g, ',');
  return `${amount < 0 ? '-' : ''}${currency} ${abs}`;
}

export const Tax: React.FC = () => {
  const { orgSlug } = useParams<{ orgSlug: string }>();
  const { authFetch } = useAuth();
  const { currentOrg, hasPermission } = useTenantContext();
  const canView = hasPermission('reports.view');

  const [periods, setPeriods] = useState<TaxPeriodItem[]>([]);
  const [selectedMode, setSelectedMode] = useState<'period' | 'custom'>('period');
  const [selectedPeriodIndex, setSelectedPeriodIndex] = useState(0);
  const [customFrom, setCustomFrom] = useState('');
  const [customTo, setCustomTo] = useState('');

  const [report, setReport] = useState<TaxPeriodReport | null>(null);
  const [hasConfiguredTaxes, setHasConfiguredTaxes] = useState<boolean | null>(null);
  const [loadingPeriods, setLoadingPeriods] = useState(true);
  const [loadingReport, setLoadingReport] = useState(false);
  const [exporting, setExporting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const [docsOpen, setDocsOpen] = useState(false);
  const [expandedLedgerAccounts, setExpandedLedgerAccounts] = useState<Record<number, boolean>>({});
  const requestGuardRef = useRef(createLatestRequestGuard());
  const abortRef = useRef<AbortController | null>(null);

  useEffect(() => {
    if (!canView) return;
    let active = true;
    setLoadingPeriods(true);
    Promise.all([
      authFetch('/api/tax/periods?count=8'),
      authFetch('/api/Tax'),
    ])
      .then(async ([periodsRes, taxRes]) => {
        if (!active) return;
        if (periodsRes.ok) {
          const list = (await periodsRes.json()) as TaxPeriodItem[];
          setPeriods(list);
          if (list.length > 0) {
            setCustomFrom(list[0].start);
            setCustomTo(list[0].end);
          }
        } else {
          setError('Tax periods could not be loaded.');
        }
        if (taxRes.ok) {
          const taxes = (await taxRes.json()) as Array<{ isActive?: boolean; rate?: number }>;
          setHasConfiguredTaxes(
            Array.isArray(taxes) &&
              taxes.some((t) => Boolean(t?.isActive) && Number(t?.rate ?? 0) > 0),
          );
        }
      })
      .catch(() => {
        if (active) setError('Tax periods could not be loaded.');
      })
      .finally(() => {
        if (active) setLoadingPeriods(false);
      });
    return () => {
      active = false;
    };
  }, [authFetch, canView]);

  const activeRange =
    selectedMode === 'custom'
      ? { from: customFrom, to: customTo, label: `${customFrom} to ${customTo}` }
      : periods[selectedPeriodIndex]
        ? {
            from: periods[selectedPeriodIndex].start,
            to: periods[selectedPeriodIndex].end,
            label: periods[selectedPeriodIndex].label,
          }
        : null;

  const loadReport = useCallback(
    async (from: string, to: string) => {
      if (!isValidTaxDateRange(from, to)) return;
      const rangeKey = `${from}..${to}`;
      const requestId = requestGuardRef.current.next(rangeKey);
      abortRef.current?.abort();
      const controller = new AbortController();
      abortRef.current = controller;

      setLoadingReport(true);
      setError(null);
      try {
        const res = await authFetch(
          `/api/tax/report?from=${encodeURIComponent(from)}&to=${encodeURIComponent(to)}`,
          { signal: controller.signal },
        );
        if (!requestGuardRef.current.isCurrent(requestId, rangeKey)) return;
        if (!res.ok) {
          const body = (await res.json().catch(() => null)) as { message?: string; detail?: string } | null;
          throw new Error(body?.message || body?.detail || 'Tax report could not be loaded.');
        }
        const data = (await res.json()) as TaxPeriodReport;
        if (!requestGuardRef.current.isCurrent(requestId, rangeKey)) return;
        setReport(data);
      } catch (err) {
        if (err instanceof DOMException && err.name === 'AbortError') return;
        if (!requestGuardRef.current.isCurrent(requestId, rangeKey)) return;
        setReport(null);
        setError(err instanceof Error ? err.message : 'Tax report could not be loaded.');
      } finally {
        if (requestGuardRef.current.isCurrent(requestId, rangeKey)) {
          setLoadingReport(false);
        }
      }
    },
    [authFetch],
  );

  useEffect(() => {
    if (!canView || !activeRange) return;
    if (!isValidTaxDateRange(activeRange.from, activeRange.to)) return;
    void loadReport(activeRange.from, activeRange.to);
  }, [canView, activeRange?.from, activeRange?.to, loadReport]);

  useEffect(() => {
    return () => {
      abortRef.current?.abort();
      requestGuardRef.current.invalidate();
    };
  }, []);

  if (!canView) {
    return <PermissionDeniedCard permission="reports.view" />;
  }

  const handleExportCsv = async () => {
    if (!report) return;
    setExporting(true);
    try {
      let sourceReport = report;
      if (report.documentsTruncated) {
        const res = await authFetch(
          `/api/tax/report?from=${encodeURIComponent(report.from)}&to=${encodeURIComponent(report.to)}&includeAllDocuments=true`,
        );
        if (res.ok) {
          sourceReport = (await res.json()) as TaxPeriodReport;
        }
      }
      const csv = buildTaxReportCsv(
        sourceReport,
        currentOrg?.name || 'Workspace',
        new Date().toISOString(),
      );
      const blob = new Blob([csv], { type: 'text/csv;charset=utf-8;' });
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = `tax-${sourceReport.from}-${sourceReport.to}.csv`;
      document.body.appendChild(a);
      a.click();
      document.body.removeChild(a);
      URL.revokeObjectURL(url);
    } finally {
      setExporting(false);
    }
  };

  const currency = report?.currency || currentOrg?.currency || DEFAULT_CURRENCY;
  const hasAnyRows = Boolean(
    report && (report.output.rows.length > 0 || report.input.rows.length > 0),
  );
  const periodDisplay = report?.periodLabel || activeRange?.label || 'this period';

  const docLink = (kind: string, id: number) => {
    if (!orgSlug) return '#';
    if (kind === 'invoice' || kind === 'invoiceVoid' || kind === 'invoicePayment') {
      return `/org/${orgSlug}/sales/${id}`;
    }
    return `/org/${orgSlug}/purchases/${id}`;
  };

  const toggleLedgerRow = (accountId: number) => {
    setExpandedLedgerAccounts((prev) => ({ ...prev, [accountId]: !prev[accountId] }));
  };

  return (
    <div className="mx-auto max-w-6xl space-y-6 px-4 py-6 sm:px-6">
      <PageHeader
        title="Tax"
        subtitle="Output tax, input tax and what you owe for each tax period."
        actions={
          <HeaderButton
            label="Export CSV"
            onClick={() => void handleExportCsv()}
            disabled={!report || loadingReport}
            busy={exporting}
            icon={FileDown}
          />
        }
      />

      {/* Period picker */}
      <div className="rounded-xl border border-[var(--border-subtle)] bg-[var(--bg-surface)] p-4">
        {loadingPeriods ? (
          <div className="flex items-center gap-2 text-sm text-[var(--text-muted)]">
            <Loader2 className="h-4 w-4 animate-spin" />
            Loading tax periods...
          </div>
        ) : (
          <div className="space-y-3">
            <div className="flex flex-wrap gap-2">
              {periods.map((p, idx) => {
                const selected = selectedMode === 'period' && selectedPeriodIndex === idx;
                return (
                  <button
                    key={`${p.start}-${p.end}`}
                    type="button"
                    onClick={() => {
                      setSelectedMode('period');
                      setSelectedPeriodIndex(idx);
                    }}
                    className={`flex items-center gap-2 rounded-lg border px-3 py-2 text-left text-sm transition ${
                      selected
                        ? 'border-[var(--accent-primary)] bg-[var(--accent-primary)]/10 text-[var(--text-main)] font-medium'
                        : 'border-[var(--border-subtle)] bg-[var(--bg-page)] text-[var(--text-muted)] hover:text-[var(--text-main)]'
                    }`}
                  >
                    <span>{p.label}</span>
                    <span className="inline-flex items-center gap-1 rounded-full bg-[var(--bg-surface)] px-2 py-0.5 text-xs text-[var(--text-muted)] border border-[var(--border-subtle)]">
                      {p.lock === 'full' ? (
                        <>
                          <Lock className="h-3 w-3" />
                          Locked
                        </>
                      ) : p.state === 'open' ? (
                        'Open'
                      ) : (
                        'Ready to file'
                      )}
                    </span>
                  </button>
                );
              })}

              <button
                type="button"
                onClick={() => setSelectedMode('custom')}
                className={`rounded-lg border px-3 py-2 text-sm transition ${
                  selectedMode === 'custom'
                    ? 'border-[var(--accent-primary)] bg-[var(--accent-primary)]/10 text-[var(--text-main)] font-medium'
                    : 'border-[var(--border-subtle)] bg-[var(--bg-page)] text-[var(--text-muted)] hover:text-[var(--text-main)]'
                }`}
              >
                Custom range
              </button>
            </div>

            {selectedMode === 'custom' && (
              <div className="flex flex-wrap items-end gap-3 pt-1">
                <label className="text-xs text-[var(--text-muted)]">
                  From
                  <input
                    type="date"
                    value={customFrom}
                    onChange={(e) => setCustomFrom(e.target.value)}
                    className="mt-1 block rounded-lg border border-[var(--border-subtle)] bg-[var(--bg-page)] px-3 py-1.5 text-sm text-[var(--text-main)]"
                  />
                </label>
                <label className="text-xs text-[var(--text-muted)]">
                  To
                  <input
                    type="date"
                    value={customTo}
                    onChange={(e) => setCustomTo(e.target.value)}
                    className="mt-1 block rounded-lg border border-[var(--border-subtle)] bg-[var(--bg-page)] px-3 py-1.5 text-sm text-[var(--text-main)]"
                  />
                </label>
              </div>
            )}
          </div>
        )}
      </div>

      {/* Error card */}
      {error && (
        <div
          role="alert"
          className="rounded-xl border border-rose-300/60 bg-rose-50/70 p-4 text-sm text-rose-900 dark:border-rose-800/60 dark:bg-rose-950/30 dark:text-rose-100"
        >
          {error}
        </div>
      )}

      {loadingReport && (
        <div className="flex items-center gap-2 text-sm text-[var(--text-muted)]">
          <Loader2 className="h-4 w-4 animate-spin" />
          Loading tax summary...
        </div>
      )}

      {!loadingReport && report && (
        <>
          {/* Empty state: no taxes configured */}
          {hasConfiguredTaxes === false && !hasAnyRows && (
            <div className="rounded-xl border border-[var(--border-subtle)] bg-[var(--bg-surface)] p-5 text-sm text-[var(--text-muted)]">
              Set up your tax rates in{' '}
              <Link
                to={`/org/${orgSlug}/settings?tab=taxes`}
                className="font-medium text-[var(--accent-primary)] underline"
              >
                Settings → Taxes
              </Link>{' '}
              to see tax by period.
            </div>
          )}

          {/* Summary cards */}
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-3">
            <div className="rounded-xl border border-[var(--border-subtle)] bg-[var(--bg-surface)] p-4">
              <div className="text-xs font-medium uppercase tracking-wide text-[var(--text-muted)]">
                Output tax
              </div>
              <div className="mt-1 text-2xl font-semibold text-[var(--text-main)]">
                {formatMoney(report.output.tax, currency)}
              </div>
            </div>
            <div className="rounded-xl border border-[var(--border-subtle)] bg-[var(--bg-surface)] p-4">
              <div className="text-xs font-medium uppercase tracking-wide text-[var(--text-muted)]">
                Input tax
              </div>
              <div className="mt-1 text-2xl font-semibold text-[var(--text-main)]">
                {formatMoney(report.input.tax, currency)}
              </div>
            </div>
            <div className="rounded-xl border border-[var(--border-subtle)] bg-[var(--bg-surface)] p-4">
              <div className="text-xs font-medium uppercase tracking-wide text-[var(--text-muted)]">
                {report.netTax < 0 ? 'Net refundable' : 'Net payable'}
              </div>
              <div className="mt-1 text-2xl font-semibold text-[var(--text-main)]">
                {formatMoney(Math.abs(report.netTax), currency)}
              </div>
            </div>
          </div>

          <div className="space-y-1 text-xs text-[var(--text-muted)]">
            <p>
              {report.basis === 'Cash'
                ? 'Cash basis: by payment date (tax counts when an invoice or bill is paid)'
                : 'Accrual basis: by invoice and bill date'}
            </p>
            <p>
              A working summary of tax on your invoices and bills, not a tax return. Check it
              against your tax authority&apos;s form.
            </p>
            {report.lock?.state === 'full' && report.lock.lockedThrough && (
              <p className="flex items-center gap-1.5 text-[var(--text-main)]">
                <Lock className="h-3.5 w-3.5" />
                Books are locked through {report.lock.lockedThrough}.
              </p>
            )}
            {report.lock?.state === 'partial' && report.lock.lockedThrough && (
              <p className="flex items-center gap-1.5 text-[var(--text-main)]">
                <Lock className="h-3.5 w-3.5" />
                Books are locked through {report.lock.lockedThrough}, part way through this period.
              </p>
            )}
          </div>

          {/* Unposted warnings */}
          {report.warnings.unposted.length > 0 && (
            <div className="rounded-xl border border-amber-300/60 bg-amber-50/70 p-4 text-sm text-amber-950 dark:border-amber-700/60 dark:bg-amber-950/30 dark:text-amber-100">
              <div className="flex items-start gap-2.5">
                <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" />
                <div className="space-y-2">
                  <p>
                    {report.basis === 'Cash'
                      ? `${report.warnings.unposted.length} issued documents aren't posted to the ledger, so they can't be counted on cash basis. Post them to include their tax when paid.`
                      : `${report.warnings.unposted.length} issued documents aren't posted to the ledger. They are included here, but the ledger check below will differ.`}
                  </p>
                  <ul className="flex flex-wrap gap-x-4 gap-y-1 text-xs">
                    {report.warnings.unposted.map((u) => (
                      <li key={`${u.kind}-${u.id}`}>
                        <Link to={docLink(u.kind, u.id)} className="underline">
                          {u.kind === 'invoice' ? 'Invoice' : 'Bill'} {u.no || `#${u.id}`} ({u.date})
                        </Link>
                      </li>
                    ))}
                  </ul>
                </div>
              </div>
            </div>
          )}

          {/* No figures empty state */}
          {hasConfiguredTaxes !== false && !hasAnyRows && (
            <div className="rounded-xl border border-[var(--border-subtle)] bg-[var(--bg-surface)] p-5 text-sm text-[var(--text-muted)]">
              No taxed sales or purchases in {periodDisplay}.
            </div>
          )}

          {/* Sales and Purchases tables */}
          {hasAnyRows && (
            <div className="space-y-6">
              <TaxSideTable title="Sales" side={report.output} currency={currency} />
              <TaxSideTable title="Purchases" side={report.input} currency={currency} />
            </div>
          )}

          {/* Collapsible Documents section */}
          <div className="rounded-xl border border-[var(--border-subtle)] bg-[var(--bg-surface)]">
            <button
              type="button"
              onClick={() => setDocsOpen((v) => !v)}
              className="flex w-full items-center justify-between px-4 py-3 text-left text-sm font-medium text-[var(--text-main)]"
            >
              <span>Documents ({report.documents.length})</span>
              {docsOpen ? <ChevronDown className="h-4 w-4" /> : <ChevronRight className="h-4 w-4" />}
            </button>
            {docsOpen && (
              <div className="border-t border-[var(--border-subtle)]">
                {report.documentsTruncated && (
                  <p className="border-b border-[var(--border-subtle)] px-4 py-2 text-xs text-[var(--text-muted)]">
                    Showing the first 2,000 documents. Export the CSV for all of them.
                  </p>
                )}
                {report.documents.length === 0 ? (
                  <p className="px-4 py-4 text-sm text-[var(--text-muted)]">
                    No documents in this period.
                  </p>
                ) : (
                  <div className="overflow-x-auto">
                    <table className="min-w-full divide-y divide-[var(--border-subtle)] text-sm">
                      <thead className="bg-[var(--bg-page)] text-xs uppercase text-[var(--text-muted)]">
                        <tr>
                          <th className="px-4 py-2 text-left font-medium">Date</th>
                          <th className="px-4 py-2 text-left font-medium">Document</th>
                          <th className="px-4 py-2 text-left font-medium">Party</th>
                          <th className="px-4 py-2 text-right font-medium">Taxable</th>
                          <th className="px-4 py-2 text-right font-medium">Tax</th>
                        </tr>
                      </thead>
                      <tbody className="divide-y divide-[var(--border-subtle)]">
                        {report.documents.map((doc, i) => {
                          const isVoid = doc.kind === 'invoiceVoid' || doc.kind === 'billVoid';
                          const kindLabel = documentKindLabel(doc.kind);
                          return (
                            <tr key={`${doc.kind}-${doc.id}-${doc.taxId ?? 'none'}-${i}`}>
                              <td className="whitespace-nowrap px-4 py-2 text-[var(--text-muted)]">
                                {doc.date}
                              </td>
                              <td className="whitespace-nowrap px-4 py-2">
                                <Link
                                  to={docLink(doc.kind, doc.id)}
                                  className="font-medium text-[var(--accent-primary)] hover:underline"
                                >
                                  {doc.no || `#${doc.id}`}
                                </Link>
                                {kindLabel && (
                                  <span
                                    className={`ml-2 rounded px-1.5 py-0.5 text-xs font-medium ${
                                      isVoid
                                        ? 'bg-rose-100 text-rose-800 dark:bg-rose-900/40 dark:text-rose-200'
                                        : 'bg-slate-100 text-slate-700 dark:bg-slate-800 dark:text-slate-200'
                                    }`}
                                  >
                                    {kindLabel}
                                  </span>
                                )}
                              </td>
                              <td className="px-4 py-2 text-[var(--text-main)]">{doc.party}</td>
                              <td className="whitespace-nowrap px-4 py-2 text-right tabular-nums text-[var(--text-main)]">
                                {formatMoney(doc.taxable, currency)}
                              </td>
                              <td className="whitespace-nowrap px-4 py-2 text-right tabular-nums text-[var(--text-main)]">
                                {formatMoney(doc.tax, currency)}
                              </td>
                            </tr>
                          );
                        })}
                      </tbody>
                    </table>
                  </div>
                )}
              </div>
            )}
          </div>

          {/* Ledger check */}
          {report.ledger ? (
            <div className="rounded-xl border border-[var(--border-subtle)] bg-[var(--bg-surface)]">
              <div className="border-b border-[var(--border-subtle)] px-4 py-3">
                <h2 className="text-sm font-semibold text-[var(--text-main)]">Ledger check</h2>
              </div>
              <div className="overflow-x-auto">
                <table className="min-w-full divide-y divide-[var(--border-subtle)] text-sm">
                  <thead className="bg-[var(--bg-page)] text-xs uppercase text-[var(--text-muted)]">
                    <tr>
                      <th className="px-4 py-2 text-left font-medium">Account</th>
                      <th className="px-4 py-2 text-right font-medium">Movement</th>
                      <th className="px-4 py-2 text-right font-medium">From invoices and bills</th>
                      <th className="px-4 py-2 text-right font-medium">Other entries</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-[var(--border-subtle)]">
                    {report.ledger.accounts.map((acc) => {
                      const expanded = Boolean(expandedLedgerAccounts[acc.accountId]);
                      const hasEntries = acc.entries.length > 0;
                      return (
                        <Fragment key={acc.accountId}>
                          <tr>
                            <td className="px-4 py-2">
                              {hasEntries ? (
                                <button
                                  type="button"
                                  onClick={() => toggleLedgerRow(acc.accountId)}
                                  className="inline-flex items-center gap-1.5 text-left font-medium text-[var(--text-main)]"
                                >
                                  {expanded ? (
                                    <ChevronDown className="h-3.5 w-3.5" />
                                  ) : (
                                    <ChevronRight className="h-3.5 w-3.5" />
                                  )}
                                  <span>
                                    {acc.code} · {acc.name}
                                  </span>
                                </button>
                              ) : (
                                <span className="text-[var(--text-main)]">
                                  {acc.code} · {acc.name}
                                </span>
                              )}
                            </td>
                            <td className="whitespace-nowrap px-4 py-2 text-right tabular-nums text-[var(--text-main)]">
                              {formatMoney(acc.movement, currency)}
                            </td>
                            <td className="whitespace-nowrap px-4 py-2 text-right tabular-nums text-[var(--text-main)]">
                              {formatMoney(acc.fromDocuments, currency)}
                            </td>
                            <td className="whitespace-nowrap px-4 py-2 text-right tabular-nums text-[var(--text-main)]">
                              {formatMoney(acc.other, currency)}
                            </td>
                          </tr>
                          {expanded && hasEntries && (
                            <tr>
                              <td colSpan={4} className="bg-[var(--bg-page)] px-6 py-3">
                                <ul className="space-y-1.5 text-xs">
                                  {acc.entries.map((entry) => (
                                    <li
                                      key={entry.glHeaderId}
                                      className="flex flex-wrap items-center justify-between gap-2"
                                    >
                                      <div className="flex items-center gap-2">
                                        <span className="text-[var(--text-muted)]">{entry.date}</span>
                                        <Link
                                          to={`/org/${orgSlug}/financials/journals`}
                                          className="text-[var(--accent-primary)] hover:underline"
                                        >
                                          {entry.description || entry.documentType}
                                        </Link>
                                      </div>
                                      <span className="tabular-nums font-medium text-[var(--text-main)]">
                                        {formatMoney(entry.amount, currency)}
                                      </span>
                                    </li>
                                  ))}
                                </ul>
                              </td>
                            </tr>
                          )}
                        </Fragment>
                      );
                    })}
                  </tbody>
                </table>
              </div>
              <p className="border-t border-[var(--border-subtle)] px-4 py-2.5 text-xs text-[var(--text-muted)]">
                Other entries are not errors. Tax payments, opening balances and manual journals
                show up here.
              </p>
            </div>
          ) : (
            <p className="text-xs text-[var(--text-muted)]">
              Ledger check and books lock come with accounting.
            </p>
          )}
        </>
      )}
    </div>
  );
};

const TaxSideTable: React.FC<{
  title: string;
  side: TaxPeriodSideSummary;
  currency: string;
}> = ({ title, side, currency }) => (
  <div className="rounded-xl border border-[var(--border-subtle)] bg-[var(--bg-surface)]">
    <div className="border-b border-[var(--border-subtle)] px-4 py-3">
      <h2 className="text-sm font-semibold text-[var(--text-main)]">{title}</h2>
    </div>
    <div className="overflow-x-auto">
      <table className="min-w-full divide-y divide-[var(--border-subtle)] text-sm">
        <thead className="bg-[var(--bg-page)] text-xs uppercase text-[var(--text-muted)]">
          <tr>
            <th className="px-4 py-2 text-left font-medium">Code</th>
            <th className="px-4 py-2 text-left font-medium">Name</th>
            <th className="px-4 py-2 text-right font-medium">Rate</th>
            <th className="px-4 py-2 text-right font-medium">Taxable amount</th>
            <th className="px-4 py-2 text-right font-medium">Tax</th>
          </tr>
        </thead>
        <tbody className="divide-y divide-[var(--border-subtle)]">
          {side.rows.map((row) => (
            <tr key={row.taxId ?? 'no-tax'}>
              <td className="whitespace-nowrap px-4 py-2 font-medium text-[var(--text-main)]">
                {row.taxCode}
              </td>
              <td className="px-4 py-2 text-[var(--text-main)]">{row.taxName}</td>
              <td className="whitespace-nowrap px-4 py-2 text-right tabular-nums text-[var(--text-muted)]">
                {row.rate === null ? '—' : `${row.rate}%`}
              </td>
              <td className="whitespace-nowrap px-4 py-2 text-right tabular-nums text-[var(--text-main)]">
                {formatMoney(row.taxable, currency)}
              </td>
              <td className="whitespace-nowrap px-4 py-2 text-right tabular-nums text-[var(--text-main)]">
                {formatMoney(row.tax, currency)}
              </td>
            </tr>
          ))}
        </tbody>
        <tfoot className="border-t border-[var(--border-subtle)] bg-[var(--bg-page)] font-semibold">
          <tr>
            <td colSpan={3} className="px-4 py-2 text-[var(--text-main)]">
              Total
            </td>
            <td className="whitespace-nowrap px-4 py-2 text-right tabular-nums text-[var(--text-main)]">
              {formatMoney(side.taxable, currency)}
            </td>
            <td className="whitespace-nowrap px-4 py-2 text-right tabular-nums text-[var(--text-main)]">
              {formatMoney(side.tax, currency)}
            </td>
          </tr>
        </tfoot>
      </table>
    </div>
  </div>
);
