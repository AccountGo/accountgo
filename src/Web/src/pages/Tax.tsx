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
import { MODAL_BODY_CLASS, MODAL_FOOTER_CLASS, MODAL_HEADER_CLASS, MODAL_SURFACE_CLASS, ModalShell } from '../components/ModalShell';
import { HeaderButton, PageHeader } from '../components/PageHeader';
import { DEFAULT_CURRENCY } from '../lib/currency';
import {
  buildTaxReportCsv,
  createLatestRequestGuard,
  documentKindLabel,
  formatTaxDifference,
  getPeriodChipStatus,
  isValidTaxDateRange,
  type TaxFilingRecord,
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
  const [filingDialogMode, setFilingDialogMode] = useState<'new' | 'amend' | null>(null);
  const [filedOnInput, setFiledOnInput] = useState(() => new Date().toISOString().slice(0, 10));
  const [referenceInput, setReferenceInput] = useState('');
  const [notesInput, setNotesInput] = useState('');
  const [lockBooksInput, setLockBooksInput] = useState(true);
  const [amendmentReasonInput, setAmendmentReasonInput] = useState('');
  const [submittingFiling, setSubmittingFiling] = useState(false);
  const [filingError, setFilingError] = useState<string | null>(null);
  const requestGuardRef = useRef(createLatestRequestGuard());
  const abortRef = useRef<AbortController | null>(null);

  const loadPeriods = useCallback(async () => {
    const periodsRes = await authFetch('/api/tax/periods?count=8');
    if (periodsRes.ok) {
      const list = (await periodsRes.json()) as TaxPeriodItem[];
      setPeriods(list);
      return list;
    }
    return null;
  }, [authFetch]);

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

  const selectedPeriod =
    selectedMode === 'period' && periods[selectedPeriodIndex] ? periods[selectedPeriodIndex] : null;
  const filings: TaxFilingRecord[] = report?.filings ?? [];
  const latestFiling =
    filings.length > 0 ? [...filings].sort((a, b) => b.sequence - a.sequence)[0] : null;

  const openFilingDialog = (mode: 'new' | 'amend') => {
    setFilingError(null);
    setFiledOnInput(new Date().toISOString().slice(0, 10));
    setReferenceInput('');
    setNotesInput('');
    setLockBooksInput(Boolean(report?.canLockOnFiling));
    setAmendmentReasonInput('');
    setFilingDialogMode(mode);
  };

  const handleSubmitFiling = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!report || !filingDialogMode) return;
    if (filingDialogMode === 'amend' && !amendmentReasonInput.trim()) {
      setFilingError('Give a reason for the amendment.');
      return;
    }

    setSubmittingFiling(true);
    setFilingError(null);
    try {
      const payload = {
        periodStart: report.from,
        periodEnd: report.to,
        filedOn: filedOnInput,
        reference: referenceInput.trim() || null,
        notes: notesInput.trim() || null,
        lockBooks: Boolean(report.canLockOnFiling && lockBooksInput),
        expectedNetTax: report.netTax,
        amendsFilingId: filingDialogMode === 'amend' && latestFiling ? latestFiling.id : null,
        amendmentReason:
          filingDialogMode === 'amend' ? amendmentReasonInput.trim() : null,
      };

      const res = await authFetch('/api/tax/filings', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(payload),
      });

      if (!res.ok) {
        const body = (await res.json().catch(() => null)) as {
          code?: string;
          message?: string;
          detail?: string;
        } | null;
        const msg = body?.message || body?.detail || 'Filing could not be recorded.';
        setFilingError(msg);
        if (res.status === 409 && body?.code === 'tax_figures_changed') {
          await Promise.all([loadPeriods(), loadReport(report.from, report.to)]);
        }
        return;
      }

      setFilingDialogMode(null);
      await Promise.all([loadPeriods(), loadReport(report.from, report.to)]);
    } catch (err) {
      setFilingError(err instanceof Error ? err.message : 'Filing could not be recorded.');
    } finally {
      setSubmittingFiling(false);
    }
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
                const chip = getPeriodChipStatus(p);
                const badgeClass =
                  chip.tone === 'changed'
                    ? 'border-amber-400/70 bg-amber-100/80 text-amber-950 dark:border-amber-700/70 dark:bg-amber-950/50 dark:text-amber-100'
                    : 'border-[var(--border-subtle)] bg-[var(--bg-surface)] text-[var(--text-muted)]';
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
                    <span className={`inline-flex items-center gap-1 rounded-full border px-2 py-0.5 text-xs ${badgeClass}`}>
                      {chip.locked && <Lock className="h-3 w-3" />}
                      {chip.label}
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

          {/* Filing panel */}
          {report.isWholePeriod && selectedMode === 'period' && (
            <div className="space-y-4">
              {selectedPeriod?.state === 'open' && filings.length === 0 && (
                <div className="rounded-xl border border-[var(--border-subtle)] bg-[var(--bg-surface)] px-4 py-3 text-sm text-[var(--text-muted)]">
                  You can record a filing once this period ends.
                </div>
              )}

              {selectedPeriod?.state === 'ended' &&
                filings.length === 0 &&
                report.canRecordFiling && (
                  <div className="flex flex-wrap items-center justify-between gap-3 rounded-xl border border-[var(--border-subtle)] bg-[var(--bg-surface)] p-4">
                    <div className="text-sm text-[var(--text-muted)]">
                      This period has ended and has no recorded filing yet.
                    </div>
                    <button
                      type="button"
                      onClick={() => openFilingDialog('new')}
                      className="rounded-lg bg-[var(--accent-primary)] px-4 py-2 text-sm font-medium text-white hover:opacity-90"
                    >
                      Record filing
                    </button>
                  </div>
                )}

              {report.changedSinceFiling?.changed && latestFiling && (
                <div className="rounded-xl border border-amber-300/70 bg-amber-50/70 p-4 text-sm text-amber-950 dark:border-amber-700/70 dark:bg-amber-950/30 dark:text-amber-100">
                  <div className="flex flex-wrap items-start justify-between gap-3">
                    <div className="flex items-start gap-2.5">
                      <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" />
                      <div>
                        <p className="font-medium">
                          These figures differ from what you filed on {latestFiling.filedOn} by{' '}
                          {formatTaxDifference(report.changedSinceFiling.netDifference, currency)}.
                        </p>
                      </div>
                    </div>
                    {report.canRecordFiling && (
                      <button
                        type="button"
                        onClick={() => openFilingDialog('amend')}
                        className="rounded-lg border border-amber-600/40 bg-white px-3 py-1.5 text-xs font-medium text-amber-950 hover:bg-amber-100 dark:bg-amber-900/40 dark:text-amber-100 dark:hover:bg-amber-900/60"
                      >
                        Record amended filing
                      </button>
                    )}
                  </div>

                  {report.changedSinceFiling.rows.length > 0 && (
                    <div className="mt-3 overflow-x-auto rounded-lg border border-amber-300/60 bg-[var(--bg-surface)] text-[var(--text-main)] dark:border-amber-700/60">
                      <table className="min-w-full divide-y divide-[var(--border-subtle)] text-xs">
                        <thead className="bg-[var(--bg-page)] uppercase text-[var(--text-muted)]">
                          <tr>
                            <th className="px-3 py-2 text-left font-medium">Code</th>
                            <th className="px-3 py-2 text-left font-medium">Side</th>
                            <th className="px-3 py-2 text-right font-medium">Filed</th>
                            <th className="px-3 py-2 text-right font-medium">Current</th>
                            <th className="px-3 py-2 text-right font-medium">Difference</th>
                          </tr>
                        </thead>
                        <tbody className="divide-y divide-[var(--border-subtle)]">
                          {report.changedSinceFiling.rows.map((r) => (
                            <tr key={`${r.side}-${r.taxId ?? 'no-tax'}`}>
                              <td className="whitespace-nowrap px-3 py-2 font-medium">
                                {r.taxCode}
                              </td>
                              <td className="whitespace-nowrap px-3 py-2 capitalize text-[var(--text-muted)]">
                                {r.side}
                              </td>
                              <td className="whitespace-nowrap px-3 py-2 text-right tabular-nums">
                                {formatMoney(r.filed, currency)}
                              </td>
                              <td className="whitespace-nowrap px-3 py-2 text-right tabular-nums">
                                {formatMoney(r.current, currency)}
                              </td>
                              <td className="whitespace-nowrap px-3 py-2 text-right tabular-nums font-medium">
                                {formatTaxDifference(r.difference, currency)}
                              </td>
                            </tr>
                          ))}
                        </tbody>
                      </table>
                    </div>
                  )}

                  <p className="mt-3 text-xs">
                    Record an amended filing, or include the correction in your next return if your
                    tax authority allows it.
                  </p>
                </div>
              )}

              {latestFiling && (
                <div className="rounded-xl border border-[var(--border-subtle)] bg-[var(--bg-surface)] p-4">
                  <div className="flex flex-wrap items-start justify-between gap-3">
                    <div>
                      <h2 className="text-sm font-semibold text-[var(--text-main)]">
                        {latestFiling.sequence === 0
                          ? 'Recorded filing'
                          : `Recorded filing (Amendment #${latestFiling.sequence})`}
                      </h2>
                      <p className="mt-1 text-xs text-[var(--text-muted)]">
                        Filed on {latestFiling.filedOn}
                        {latestFiling.reference ? ` · Ref: ${latestFiling.reference}` : ''}
                        {' · '}Basis: {latestFiling.basis}
                        {' · '}Recorded by {latestFiling.createdBy} on{' '}
                        {latestFiling.createdAt.slice(0, 10)}
                      </p>
                    </div>
                    <div className="grid grid-cols-3 gap-4 text-right text-xs">
                      <div>
                        <div className="text-[var(--text-muted)]">Filed output</div>
                        <div className="font-semibold tabular-nums text-[var(--text-main)]">
                          {formatMoney(latestFiling.outputTax, currency)}
                        </div>
                      </div>
                      <div>
                        <div className="text-[var(--text-muted)]">Filed input</div>
                        <div className="font-semibold tabular-nums text-[var(--text-main)]">
                          {formatMoney(latestFiling.inputTax, currency)}
                        </div>
                      </div>
                      <div>
                        <div className="text-[var(--text-muted)]">Filed net</div>
                        <div className="font-semibold tabular-nums text-[var(--text-main)]">
                          {formatMoney(latestFiling.netTax, currency)}
                        </div>
                      </div>
                    </div>
                  </div>

                  {latestFiling.notes && (
                    <p className="mt-2 text-xs text-[var(--text-muted)]">
                      Notes: {latestFiling.notes}
                    </p>
                  )}

                  {filings.length > 1 && (
                    <div className="mt-4 border-t border-[var(--border-subtle)] pt-3">
                      <div className="text-xs font-medium text-[var(--text-muted)]">
                        Filing history
                      </div>
                      <ul className="mt-2 space-y-1.5 text-xs">
                        {[...filings]
                          .sort((a, b) => b.sequence - a.sequence)
                          .map((f) => (
                            <li
                              key={f.id}
                              className="flex flex-wrap items-center justify-between gap-2 text-[var(--text-muted)]"
                            >
                              <span>
                                <strong className="text-[var(--text-main)]">
                                  {f.sequence === 0 ? 'Original' : `Amendment #${f.sequence}`}
                                </strong>{' '}
                                · Filed {f.filedOn}
                                {f.reference ? ` · ${f.reference}` : ''}
                                {f.amendmentReason ? ` — ${f.amendmentReason}` : ''}
                              </span>
                              <span className="tabular-nums text-[var(--text-main)]">
                                {formatMoney(f.netTax, currency)}
                              </span>
                            </li>
                          ))}
                      </ul>
                    </div>
                  )}
                </div>
              )}
            </div>
          )}

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
      {filingDialogMode && report && (
        <ModalShell
          open
          onClose={() => { if (!submittingFiling) setFilingDialogMode(null); }}
          closeOnEscape={!submittingFiling}
          closeOnBackdrop={!submittingFiling}
          bare
          aria-labelledby="tax-filing-dialog-title"
          panelClassName={`${MODAL_SURFACE_CLASS} max-w-md`}
        >
          <div className={MODAL_HEADER_CLASS}>
            <div>
              <h2 id="tax-filing-dialog-title" className="text-base font-semibold text-[var(--text-main)]">
                {filingDialogMode === 'amend' ? 'Record amended filing' : 'Record filing'}
              </h2>
              <p className="mt-0.5 text-xs text-[var(--text-muted)]">
                {periodDisplay} · Net {report.netTax < 0 ? 'refundable' : 'payable'}:{' '}
                {formatMoney(Math.abs(report.netTax), currency)}
              </p>
            </div>
          </div>

          <form onSubmit={(e) => void handleSubmitFiling(e)} className="flex flex-col flex-1 min-h-0">
            <div className={`${MODAL_BODY_CLASS} space-y-3`}>
              {filingError && (
                <div
                  role="alert"
                  className="rounded-lg border border-rose-300/70 bg-rose-50/80 px-3 py-2 text-xs text-rose-900 dark:border-rose-800/70 dark:bg-rose-950/40 dark:text-rose-100"
                >
                  {filingError}
                </div>
              )}

              <label className="block text-xs font-medium text-[var(--text-main)]">
                Filed on
                <input
                  type="date"
                  required
                  value={filedOnInput}
                  onChange={(e) => setFiledOnInput(e.target.value)}
                  className="mt-1 block w-full rounded-lg border border-[var(--border-subtle)] bg-[var(--bg-page)] px-3 py-2 text-sm text-[var(--text-main)]"
                />
              </label>

              {filingDialogMode === 'amend' && (
                <label className="block text-xs font-medium text-[var(--text-main)]">
                  Reason for the amendment
                  <input
                    type="text"
                    required
                    maxLength={500}
                    value={amendmentReasonInput}
                    onChange={(e) => setAmendmentReasonInput(e.target.value)}
                    placeholder="Why are you amending this filing?"
                    className="mt-1 block w-full rounded-lg border border-[var(--border-subtle)] bg-[var(--bg-page)] px-3 py-2 text-sm text-[var(--text-main)]"
                  />
                </label>
              )}

              <label className="block text-xs font-medium text-[var(--text-main)]">
                Reference
                <input
                  type="text"
                  maxLength={100}
                  value={referenceInput}
                  onChange={(e) => setReferenceInput(e.target.value)}
                  placeholder="Optional return or confirmation number"
                  className="mt-1 block w-full rounded-lg border border-[var(--border-subtle)] bg-[var(--bg-page)] px-3 py-2 text-sm text-[var(--text-main)]"
                />
              </label>

              <label className="block text-xs font-medium text-[var(--text-main)]">
                Notes
                <textarea
                  rows={2}
                  maxLength={1000}
                  value={notesInput}
                  onChange={(e) => setNotesInput(e.target.value)}
                  placeholder="Optional notes"
                  className="mt-1 block w-full rounded-lg border border-[var(--border-subtle)] bg-[var(--bg-page)] px-3 py-2 text-sm text-[var(--text-main)]"
                />
              </label>

              {report.canLockOnFiling && (
                <label className="flex items-start gap-2.5 rounded-lg border border-[var(--border-subtle)] bg-[var(--bg-page)] p-3 text-xs">
                  <input
                    type="checkbox"
                    checked={lockBooksInput}
                    onChange={(e) => setLockBooksInput(e.target.checked)}
                    className="mt-0.5 h-4 w-4 rounded border-[var(--border-subtle)]"
                  />
                  <span>
                    <span className="block font-medium text-[var(--text-main)]">
                      Lock books through {report.to}
                    </span>
                    <span className="mt-0.5 block text-[var(--text-muted)]">
                      Stops changes dated in this period. An owner or admin can reopen with a
                      reason.
                    </span>
                  </span>
                </label>
              )}
            </div>

            <div className={MODAL_FOOTER_CLASS}>
              <button
                type="button"
                onClick={() => setFilingDialogMode(null)}
                disabled={submittingFiling}
                className="rounded-lg border border-[var(--border-subtle)] px-3 py-2 text-xs font-medium text-[var(--text-main)] hover:bg-[var(--bg-page)]"
              >
                Cancel
              </button>
              <button
                type="submit"
                disabled={submittingFiling}
                className="rounded-lg bg-[var(--accent-primary)] px-4 py-2 text-xs font-medium text-white hover:opacity-90 disabled:opacity-50"
              >
                {submittingFiling ? 'Saving...' : 'Save filing'}
              </button>
            </div>
          </form>
        </ModalShell>
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
