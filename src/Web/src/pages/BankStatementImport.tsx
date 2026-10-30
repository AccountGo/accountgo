import React, { useCallback, useEffect, useMemo, useState } from 'react';
import { useOutletContext, useParams } from 'react-router-dom';
import { AlertTriangle, CheckCircle2, Link2Off, Loader2, Plus, Upload } from 'lucide-react';
import { PageHeader, HeaderButton } from '../components/PageHeader';
import { StatementLineApplyInvoiceDialog } from '../components/StatementLineApplyInvoiceDialog';
import { StatementLineExcludeDialog } from '../components/StatementLineExcludeDialog';
import { StatementLineExpenseDialog } from '../components/StatementLineExpenseDialog';
import { StatementLineTransactionDialog } from '../components/StatementLineTransactionDialog';
import { STATEMENT_EXCLUSION_REASONS } from '../types/banking';
import { useAuth } from '../auth/AuthContext';
import { useTenantContext } from '../components/Gating';
import { useCapabilities } from '../lib/entitlements';
import {
  CSV_SAMPLE_ROWS,
  clampStatementLinePage,
  detectDelimiter,
  emptyColumnMap,
  hasAmountMapping,
  importSummary,
  parseCsvRows,
  sourceForFileName,
  suggestColumnMap,
} from '../lib/statementImport';
import type { TenantContextType } from '../types';
import type {
  BankAccount,
  BankStatementColumnMap,
  BankStatementCommitResult,
  BankStatementLine,
  BankStatementLineList,
  BankStatementPreview,
  StatementLineStatus,
} from '../types/banking';

const labelClass = 'block text-xs font-semibold uppercase tracking-wider text-[var(--text-muted)] mb-1';
const inputClass =
  'w-full h-10 px-3 rounded-xl border border-[var(--border-subtle)] bg-[var(--bg-subtle)] text-sm text-[var(--text-main)]';
const LIST_PAGE_SIZE = 100;

type ColumnKey =
  | 'dateColumn'
  | 'amountColumn'
  | 'depositColumn'
  | 'withdrawalColumn'
  | 'payeeColumn'
  | 'memoColumn'
  | 'referenceColumn'
  | 'externalIdColumn';

const COLUMN_FIELDS: { key: ColumnKey; label: string; required?: boolean }[] = [
  { key: 'dateColumn', label: 'Date', required: true },
  { key: 'amountColumn', label: 'Amount (signed)' },
  { key: 'depositColumn', label: 'Deposits (if separate)' },
  { key: 'withdrawalColumn', label: 'Withdrawals (if separate)' },
  { key: 'payeeColumn', label: 'Payee / description' },
  { key: 'memoColumn', label: 'Memo' },
  { key: 'referenceColumn', label: 'Reference / check no.' },
  { key: 'externalIdColumn', label: 'Bank transaction id' },
];

const BankStatementImportPage: React.FC = () => {
  const { bankId } = useParams<{ bankId: string }>();
  const id = Number(bankId);
  const { currentOrg } = useOutletContext<TenantContextType>();
  const { authFetch } = useAuth();
  const { hasPermission } = useTenantContext();
  const canManage = hasPermission('banking.accounts.manage');
  const canCreateTx = hasPermission('banking.transactions.create');
  const { canModule, canWrite } = useCapabilities();
  const hasFinancials = canModule('financials');
  const purchasesWritable = canModule('purchases') && canWrite('purchases');
  const canRecordExpense =
    canManage &&
    purchasesWritable &&
    hasPermission('purchases.bills.create') &&
    hasPermission('purchases.bills.post') &&
    hasPermission('purchases.payments.create');
  const canPayOpenBill = canManage && purchasesWritable && hasPermission('purchases.payments.create');
  const salesWritable = canModule('sales') && canWrite('sales');
  const canApplyOpenInvoice =
    canManage && salesWritable && hasPermission('sales.receipts.create');

  const [account, setAccount] = useState<BankAccount | null>(null);
  const [list, setList] = useState<BankStatementLineList | null>(null);
  const [status, setStatus] = useState<StatementLineStatus>('unmatched');
  const [page, setPage] = useState(1);
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  const [fileName, setFileName] = useState('');
  const [content, setContent] = useState('');
  const [source, setSource] = useState<1 | 2 | null>(null);
  const [sampleRows, setSampleRows] = useState<string[][]>([]);
  const [columnMap, setColumnMap] = useState<BankStatementColumnMap>(emptyColumnMap());
  const [preview, setPreview] = useState<BankStatementPreview | null>(null);

  const [createFor, setCreateFor] = useState<BankStatementLine | null>(null);
  const [expenseFor, setExpenseFor] = useState<BankStatementLine | null>(null);
  const [applyFor, setApplyFor] = useState<BankStatementLine | null>(null);
  const [excludeFor, setExcludeFor] = useState<BankStatementLine | null>(null);
  const [pickedCandidate, setPickedCandidate] = useState<Record<number, string>>({});
  const [selectedIds, setSelectedIds] = useState<number[]>([]);

  const formatCurrency = useCallback(
    (n: number) =>
      new Intl.NumberFormat(undefined, {
        style: 'currency',
        currency: currentOrg.currency || 'USD',
      }).format(n),
    [currentOrg.currency],
  );

  const fetchLines = useCallback(
    async (nextStatus: StatementLineStatus, nextPage: number): Promise<BankStatementLineList> => {
      const q = new URLSearchParams({
        status: nextStatus,
        page: String(nextPage),
        pageSize: String(LIST_PAGE_SIZE),
      });
      const res = await authFetch(`/api/Banking/Accounts/${id}/StatementLines?${q}`);
      if (!res.ok) {
        const body = await res.json().catch(() => null);
        throw new Error(body?.message || 'Could not load statement lines.');
      }
      return (await res.json()) as BankStatementLineList;
    },
    [authFetch, id],
  );

  const loadLines = useCallback(
    async (nextStatus: StatementLineStatus, nextPage: number) => {
      let data = await fetchLines(nextStatus, nextPage);
      const clamped = clampStatementLinePage(nextPage, data.totalCount, data.pageSize || LIST_PAGE_SIZE);
      if (clamped !== nextPage) {
        setPage(clamped);
        data = await fetchLines(nextStatus, clamped);
      }
      setList(data);
    },
    [fetchLines],
  );

  // Account only when the bank changes — page/status changes fetch lines alone.
  useEffect(() => {
    let cancelled = false;
    (async () => {
      if (!Number.isFinite(id)) return;
      setLoading(true);
      setError(null);
      setAccount(null);
      setList(null);
      setPage(1);
      try {
        const accRes = await authFetch(`/api/Banking/Accounts/${id}`);
        if (!accRes.ok) throw new Error('Bank account not found.');
        const next = (await accRes.json()) as BankAccount;
        if (!cancelled) setAccount(next);
      } catch (err: unknown) {
        if (!cancelled) setError(err instanceof Error ? err.message : 'Could not load.');
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [authFetch, id]);

  useEffect(() => {
    if (!account || !Number.isFinite(id)) return;
    let cancelled = false;
    (async () => {
      setError(null);
      try {
        await loadLines(status, page);
      } catch (err: unknown) {
        if (!cancelled) setError(err instanceof Error ? err.message : 'Could not load statement lines.');
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [account, id, loadLines, status, page]);

  const totalCount = list?.totalCount ?? 0;
  const pageSize = list?.pageSize ?? LIST_PAGE_SIZE;
  const currentPage = clampStatementLinePage(list?.page ?? page, totalCount, pageSize);
  const showingFrom = totalCount === 0 ? 0 : (currentPage - 1) * pageSize + 1;
  const showingTo = Math.min(currentPage * pageSize, totalCount);
  const canPrev = currentPage > 1;
  const canNext = currentPage * pageSize < totalCount;

  const headers = useMemo(() => {
    if (source !== 1 || sampleRows.length === 0) return [];
    return sampleRows[0].map((cell, index) => (columnMap.hasHeaderRow && cell.trim() ? cell.trim() : `Column ${index + 1}`));
  }, [source, sampleRows, columnMap.hasHeaderRow]);

  const onFile = async (file: File | null) => {
    setPreview(null);
    setNotice(null);
    setError(null);
    if (!file) return;
    const detected = sourceForFileName(file.name);
    if (!detected) {
      setError('Choose a .csv, .ofx, or .qfx file.');
      return;
    }
    const text = await file.text();
    setFileName(file.name);
    setContent(text);
    setSource(detected);
    if (detected === 1) {
      const delimiter = detectDelimiter(text);
      const rows = parseCsvRows(text, delimiter, CSV_SAMPLE_ROWS + 1);
      setSampleRows(rows);
      setColumnMap(rows.length ? suggestColumnMap(rows[0]) : emptyColumnMap());
    } else {
      setSampleRows([]);
    }
  };

  const setColumn = (key: ColumnKey, value: string) => {
    setPreview(null);
    setColumnMap((prev) => ({ ...prev, [key]: value === '' ? null : Number(value) }));
  };

  const payload = () => ({
    source,
    fileName,
    content,
    columnMap: source === 1 ? columnMap : null,
  });

  const mappingReady = source === 2 || (source === 1 && hasAmountMapping(columnMap));

  const runPreview = async () => {
    if (!mappingReady) return;
    setBusy(true);
    setError(null);
    setNotice(null);
    try {
      const res = await authFetch(`/api/Banking/Accounts/${id}/StatementImports/Preview`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(payload()),
      });
      if (!res.ok) {
        const body = await res.json().catch(() => null);
        throw new Error(body?.message || 'Could not read the file.');
      }
      setPreview(await res.json());
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'Could not read the file.');
    } finally {
      setBusy(false);
    }
  };

  const commit = async () => {
    setBusy(true);
    setError(null);
    try {
      const res = await authFetch(`/api/Banking/Accounts/${id}/StatementImports`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(payload()),
      });
      if (!res.ok) {
        const body = await res.json().catch(() => null);
        throw new Error(body?.message || 'Could not import the file.');
      }
      const result: BankStatementCommitResult = await res.json();
      setNotice(importSummary(result));
      setPreview(null);
      setContent('');
      setFileName('');
      setSource(null);
      setSampleRows([]);
      setPage(1);
      await loadLines(status, 1);
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'Could not import the file.');
    } finally {
      setBusy(false);
    }
  };

  const toggleSelected = (id: number) => {
    setSelectedIds((prev) => (prev.includes(id) ? prev.filter((x) => x !== id) : [...prev, id]));
  };

  const bulkRecordExpense = async () => {
    if (selectedIds.length === 0) return;
    const first = (list?.lines ?? []).find((l) => l.id === selectedIds[0]);
    if (!first || first.amount >= 0) {
      setError('Bulk record expense only works on money-out lines.');
      return;
    }
    setExpenseFor(first);
  };

  const lineAction = async (
    line: BankStatementLine,
    action: 'Match' | 'Unmatch' | 'Include',
    glLineId?: number,
  ) => {
    setBusy(true);
    setError(null);
    setNotice(null);
    try {
      const res = await authFetch(`/api/Banking/StatementLines/${line.id}/${action}`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(action === 'Match' ? { generalLedgerLineId: glLineId ?? null } : {}),
      });
      if (!res.ok) {
        const body = await res.json().catch(() => null);
        throw new Error(body?.message || `Could not ${action.toLowerCase()} the line.`);
      }
      await loadLines(status, page);
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'Action failed.');
    } finally {
      setBusy(false);
    }
  };

  if (loading && !account) {
    return (
      <div className="flex items-center gap-2 text-sm text-[var(--text-muted)]">
        <Loader2 className="w-4 h-4 animate-spin" /> Loading…
      </div>
    );
  }

  if (!account) {
    return (
      <div className="space-y-4">
        <PageHeader title="Import statement" back={{ to: `/org/${currentOrg.slug}/banking`, label: 'Banking' }} />
        {error && (
          <div className="p-4 rounded-xl bg-rose-500/10 border border-rose-500/20 text-rose-600 text-sm">{error}</div>
        )}
      </div>
    );
  }

  return (
    <div className="space-y-6">
      <PageHeader
        title="Import statement"
        subtitle={account.name}
        back={{ to: `/org/${currentOrg.slug}/banking/${account.id}`, label: 'Register' }}
      />

      {error && (
        <div className="p-4 rounded-xl bg-rose-500/10 border border-rose-500/20 text-rose-600 text-sm flex gap-2" role="alert">
          <AlertTriangle className="w-4 h-4 shrink-0 mt-0.5" />
          {error}
        </div>
      )}
      {notice && (
        <div className="p-4 rounded-xl bg-emerald-500/10 border border-emerald-500/20 text-emerald-700 text-sm flex gap-2">
          <CheckCircle2 className="w-4 h-4 shrink-0 mt-0.5" />
          {notice}
        </div>
      )}

      {canManage && !account.archivedAt && (
        <section className="rounded-xl border border-[var(--border-subtle)] bg-[var(--bg-surface)] p-5 space-y-4 shadow-sm">
          <h2 className="text-sm font-bold text-[var(--text-main)]">1. Choose a file</h2>
          <input
            type="file"
            accept=".csv,.txt,.ofx,.qfx"
            aria-label="Statement file"
            onChange={(e) => void onFile(e.target.files?.[0] ?? null)}
            className="block text-sm text-[var(--text-secondary)]"
          />

          {source === 1 && sampleRows.length > 0 && (
            <div className="space-y-4">
              <h2 className="text-sm font-bold text-[var(--text-main)]">2. Map the columns</h2>
              <label className="flex items-center gap-2 text-sm text-[var(--text-main)]">
                <input
                  type="checkbox"
                  checked={columnMap.hasHeaderRow}
                  onChange={(e) => {
                    setPreview(null);
                    setColumnMap((prev) => ({ ...prev, hasHeaderRow: e.target.checked }));
                  }}
                />
                First row is a header
              </label>
              <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-3">
                {COLUMN_FIELDS.map((field) => (
                  <div key={field.key}>
                    <label className={labelClass} htmlFor={`col-${field.key}`}>
                      {field.label}
                      {field.required ? ' *' : ''}
                    </label>
                    <select
                      id={`col-${field.key}`}
                      value={columnMap[field.key] ?? ''}
                      onChange={(e) => setColumn(field.key, e.target.value)}
                      className={inputClass}
                    >
                      {!field.required && <option value="">Not in file</option>}
                      {headers.map((header, index) => (
                        <option key={index} value={index}>
                          {header}
                        </option>
                      ))}
                    </select>
                  </div>
                ))}
                <div>
                  <label className={labelClass} htmlFor="col-dateFormat">
                    Date format (required for 03/04/2026-style dates)
                  </label>
                  <input
                    id="col-dateFormat"
                    placeholder="e.g. dd/MM/yyyy or MM/dd/yyyy"
                    value={columnMap.dateFormat ?? ''}
                    onChange={(e) => {
                      setPreview(null);
                      setColumnMap((prev) => ({ ...prev, dateFormat: e.target.value.trim() || null }));
                    }}
                    className={inputClass}
                  />
                </div>
              </div>
              <label className="flex items-center gap-2 text-sm text-[var(--text-main)]">
                <input
                  type="checkbox"
                  checked={columnMap.invertSign}
                  onChange={(e) => {
                    setPreview(null);
                    setColumnMap((prev) => ({ ...prev, invertSign: e.target.checked }));
                  }}
                />
                Payments are shown as positive amounts (flip the sign)
              </label>
              {!hasAmountMapping(columnMap) && (
                <p className="text-xs text-amber-600">Pick the amount column, or the deposit and withdrawal columns.</p>
              )}
            </div>
          )}

          {source && (
            <div className="flex flex-wrap items-center gap-2">
              <HeaderButton
                label="Preview"
                icon={Upload}
                variant="secondary"
                onClick={() => void runPreview()}
                busy={busy}
                disabled={!mappingReady}
              />
              {preview && preview.readyRows > 0 && (
                <HeaderButton
                  label={`Import ${preview.readyRows} line${preview.readyRows === 1 ? '' : 's'}`}
                  variant="primary"
                  onClick={() => void commit()}
                  busy={busy}
                />
              )}
            </div>
          )}

          {preview && (
            <div className="space-y-3">
              <p className="text-sm text-[var(--text-secondary)]">
                {preview.totalRows} rows: {preview.readyRows} new, {preview.duplicateRows} already imported,{' '}
                {preview.errorRows} with errors.
              </p>
              <div className="overflow-x-auto rounded-lg border border-[var(--border-subtle)]">
                <table className="w-full text-sm min-w-[560px]">
                  <thead>
                    <tr className="text-left text-xs uppercase tracking-wider text-[var(--text-muted)] border-b border-[var(--border-subtle)]">
                      <th className="px-3 py-2">Row</th>
                      <th className="px-3 py-2">Date</th>
                      <th className="px-3 py-2">Payee</th>
                      <th className="px-3 py-2 text-right">Amount</th>
                      <th className="px-3 py-2">Status</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-[var(--border-subtle)]">
                    {preview.rows.map((row) => (
                      <tr key={row.rowNumber}>
                        <td className="px-3 py-2">{row.rowNumber}</td>
                        <td className="px-3 py-2 whitespace-nowrap">{row.postedDate ? String(row.postedDate).slice(0, 10) : '—'}</td>
                        <td className="px-3 py-2">{row.payee || row.memo || '—'}</td>
                        <td className="px-3 py-2 text-right font-mono">
                          {row.amount != null ? formatCurrency(row.amount) : '—'}
                        </td>
                        <td className="px-3 py-2">
                          {row.status === 'ok' && 'New'}
                          {row.status === 'duplicate' && <span className="text-[var(--text-muted)]">Already imported</span>}
                          {row.status === 'error' && <span className="text-rose-600">{row.error}</span>}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
              {preview.totalRows > preview.rows.length && (
                <p className="text-xs text-[var(--text-muted)]">Showing the first {preview.rows.length} rows.</p>
              )}
            </div>
          )}
        </section>
      )}

      <section className="space-y-3">
        <div className="flex flex-wrap items-center justify-between gap-3">
          <h2 className="text-sm font-bold text-[var(--text-main)]">
            Statement lines
            {list && (
              <span className="ml-2 font-normal text-[var(--text-muted)]">
                {list.unmatchedCount} unmatched · {list.matchedCount} matched · {list.excludedCount ?? 0}{' '}
                excluded
              </span>
            )}
          </h2>
          <div className="flex gap-1">
            {(['unmatched', 'matched', 'excluded', 'all'] as StatementLineStatus[]).map((value) => (
              <button
                key={value}
                type="button"
                onClick={() => {
                  setStatus(value);
                  setPage(1);
                }}
                className={`h-8 px-3 rounded-lg text-xs font-semibold capitalize ${
                  status === value
                    ? 'bg-[var(--brand-primary)] text-white'
                    : 'text-[var(--text-secondary)] hover:bg-[var(--bg-surface-hover)]'
                }`}
              >
                {value}
              </button>
            ))}
          </div>
        </div>

        {list && (
          <div className="flex flex-wrap items-center justify-between gap-3 text-sm text-[var(--text-secondary)]">
            <p role="status">
              {totalCount === 0
                ? 'Showing 0 of 0'
                : `Showing ${showingFrom}–${showingTo} of ${totalCount}`}
            </p>
            <div className="flex gap-2">
              {canRecordExpense && selectedIds.length > 0 && (
                <button
                  type="button"
                  disabled={busy}
                  onClick={() => void bulkRecordExpense()}
                  className="h-8 px-3 rounded-lg text-xs font-semibold text-white bg-[var(--brand-primary)] disabled:opacity-50"
                >
                  Record expense ({selectedIds.length})
                </button>
              )}
              <button
                type="button"
                disabled={!canPrev || busy || loading}
                onClick={() => setPage((p) => Math.max(1, p - 1))}
                className="h-8 px-3 rounded-lg text-xs font-semibold border border-[var(--border-subtle)] disabled:opacity-40 hover:bg-[var(--bg-surface-hover)]"
              >
                Previous
              </button>
              <button
                type="button"
                disabled={!canNext || busy || loading}
                onClick={() => setPage((p) => p + 1)}
                className="h-8 px-3 rounded-lg text-xs font-semibold border border-[var(--border-subtle)] disabled:opacity-40 hover:bg-[var(--bg-surface-hover)]"
              >
                Next
              </button>
            </div>
          </div>
        )}

        <div className="rounded-xl border border-[var(--border-subtle)] bg-[var(--bg-surface)] overflow-x-auto shadow-sm">
          <table className="w-full text-sm min-w-[720px]">
            <thead>
              <tr className="text-left text-xs uppercase tracking-wider text-[var(--text-muted)] border-b border-[var(--border-subtle)]">
                {canManage && status === 'unmatched' && (
                  <th className="px-3 py-3 font-semibold w-10">
                    <span className="sr-only">Select</span>
                  </th>
                )}
                <th className="px-4 py-3 font-semibold">Date</th>
                <th className="px-4 py-3 font-semibold">Payee / memo</th>
                <th className="px-4 py-3 font-semibold">Reference</th>
                <th className="px-4 py-3 font-semibold text-right">Amount</th>
                <th className="px-4 py-3 font-semibold text-right">Actions</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-[var(--border-subtle)]">
              {(list?.lines ?? []).map((line) => (
                <tr key={line.id} className="text-[var(--text-main)] align-top">
                  {canManage && status === 'unmatched' && (
                    <td className="px-3 py-2.5">
                      {!line.isMatched && !line.isExcluded && line.amount < 0 && (
                        <input
                          type="checkbox"
                          aria-label={`Select line ${line.id}`}
                          checked={selectedIds.includes(line.id)}
                          onChange={() => toggleSelected(line.id)}
                          className="rounded border-[var(--border-subtle)]"
                        />
                      )}
                    </td>
                  )}
                  <td className="px-4 py-2.5 whitespace-nowrap">{String(line.postedDate).slice(0, 10)}</td>
                  <td className="px-4 py-2.5">
                    <div>{line.payee || '—'}</div>
                    {line.memo && <div className="text-xs text-[var(--text-muted)]">{line.memo}</div>}
                  </td>
                  <td className="px-4 py-2.5">{line.reference || '—'}</td>
                  <td className="px-4 py-2.5 text-right font-mono">{formatCurrency(line.amount)}</td>
                  <td className="px-4 py-2.5 text-right">
                    {canManage && line.isExcluded && (
                      <button
                        type="button"
                        disabled={busy}
                        onClick={() => void lineAction(line, 'Include')}
                        className="inline-flex items-center gap-1 h-8 px-3 rounded-lg text-xs font-semibold text-[var(--text-secondary)] hover:bg-[var(--bg-surface-hover)]"
                      >
                        Include
                      </button>
                    )}
                    {canManage && line.isMatched && !line.isExcluded && (
                      <button
                        type="button"
                        disabled={busy}
                        onClick={() => void lineAction(line, 'Unmatch')}
                        className="inline-flex items-center gap-1 h-8 px-3 rounded-lg text-xs font-semibold text-[var(--text-secondary)] hover:bg-[var(--bg-surface-hover)]"
                      >
                        <Link2Off className="w-3.5 h-3.5" /> Unmatch
                      </button>
                    )}
                    {canManage && !line.isMatched && !line.isExcluded && (
                      <div className="flex flex-wrap items-center justify-end gap-2">
                        {line.candidates.length > 0 && (
                          <>
                            <select
                              aria-label="Ledger line to match"
                              value={pickedCandidate[line.id] ?? String(line.candidates[0].generalLedgerLineId)}
                              onChange={(e) => setPickedCandidate((prev) => ({ ...prev, [line.id]: e.target.value }))}
                              className="h-8 px-2 rounded-lg border border-[var(--border-subtle)] bg-[var(--bg-subtle)] text-xs max-w-[220px]"
                            >
                              {line.candidates.map((c) => (
                                <option key={c.generalLedgerLineId} value={c.generalLedgerLineId}>
                                  {String(c.date).slice(0, 10)} · {c.description || `Entry ${c.generalLedgerHeaderId}`}
                                </option>
                              ))}
                            </select>
                            <button
                              type="button"
                              disabled={busy}
                              onClick={() =>
                                void lineAction(
                                  line,
                                  'Match',
                                  Number(pickedCandidate[line.id] ?? line.candidates[0].generalLedgerLineId),
                                )
                              }
                              className="h-8 px-3 rounded-lg text-xs font-semibold text-white bg-[var(--brand-primary)] disabled:opacity-50"
                            >
                              Match
                            </button>
                          </>
                        )}
                        {line.amount < 0 && (canRecordExpense || canPayOpenBill) && (
                          <button
                            type="button"
                            disabled={busy}
                            onClick={() => setExpenseFor(line)}
                            className="inline-flex items-center gap-1 h-8 px-3 rounded-lg text-xs font-semibold text-white bg-[var(--brand-primary)] disabled:opacity-50"
                          >
                            {canRecordExpense ? 'Record expense' : 'Pay open bill'}
                          </button>
                        )}
                        {line.amount > 0 && canApplyOpenInvoice && (
                          <button
                            type="button"
                            disabled={busy}
                            onClick={() => setApplyFor(line)}
                            className="inline-flex items-center gap-1 h-8 px-3 rounded-lg text-xs font-semibold text-white bg-[var(--brand-primary)] disabled:opacity-50"
                          >
                            Apply invoice
                          </button>
                        )}
                        <button
                          type="button"
                          disabled={busy}
                          onClick={() => setExcludeFor(line)}
                          className="inline-flex items-center gap-1 h-8 px-3 rounded-lg text-xs font-semibold text-[var(--text-main)] border border-[var(--border-subtle)] hover:bg-[var(--bg-surface-hover)]"
                        >
                          Exclude
                        </button>
                        {hasFinancials && canCreateTx && (
                          <button
                            type="button"
                            disabled={busy}
                            onClick={() => setCreateFor(line)}
                            className="inline-flex items-center gap-1 h-8 px-3 rounded-lg text-xs font-semibold text-[var(--text-main)] border border-[var(--border-subtle)] hover:bg-[var(--bg-surface-hover)]"
                          >
                            <Plus className="w-3.5 h-3.5" /> Create transaction
                          </button>
                        )}
                      </div>
                    )}
                    {line.isExcluded && line.exclusionReason != null && (
                      <div className="mt-1 text-xs text-[var(--text-muted)]">
                        {STATEMENT_EXCLUSION_REASONS.find((r) => r.value === line.exclusionReason)?.label ||
                          'Excluded'}
                        {line.exclusionNote ? ` · ${line.exclusionNote}` : ''}
                      </div>
                    )}
                  </td>
                </tr>
              ))}
              {list && list.lines.length === 0 && (
                <tr>
                  <td colSpan={5} className="px-4 py-8 text-center text-[var(--text-muted)]">
                    {status === 'unmatched'
                      ? 'Nothing waiting to be matched.'
                      : status === 'excluded'
                        ? 'No excluded lines.'
                        : 'No statement lines.'}
                  </td>
                </tr>
              )}
            </tbody>
          </table>
        </div>
      </section>

      {expenseFor && (
        <StatementLineExpenseDialog
          line={expenseFor}
          additionalLineIds={selectedIds.filter((id) => id !== expenseFor.id)}
          formatCurrency={formatCurrency}
          onClose={() => setExpenseFor(null)}
          onSaved={async () => {
            setExpenseFor(null);
            setSelectedIds([]);
            await loadLines(status, page);
          }}
        />
      )}

      {applyFor && (
        <StatementLineApplyInvoiceDialog
          line={applyFor}
          formatCurrency={formatCurrency}
          onClose={() => setApplyFor(null)}
          onSaved={async () => {
            setApplyFor(null);
            await loadLines(status, page);
          }}
        />
      )}

      {excludeFor && (
        <StatementLineExcludeDialog
          line={excludeFor}
          formatCurrency={formatCurrency}
          onClose={() => setExcludeFor(null)}
          onSaved={async () => {
            setExcludeFor(null);
            await loadLines(status, page);
          }}
        />
      )}

      {createFor && (
        <StatementLineTransactionDialog
          line={createFor}
          formatCurrency={formatCurrency}
          onClose={() => setCreateFor(null)}
          onSaved={async () => {
            setCreateFor(null);
            await loadLines(status, page);
          }}
        />
      )}
    </div>
  );
};

export default BankStatementImportPage;
