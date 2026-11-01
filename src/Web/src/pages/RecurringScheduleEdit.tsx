// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

import React, { useCallback, useEffect, useState } from 'react';
import { useNavigate, useOutletContext, useParams, useSearchParams } from 'react-router-dom';
import { AlertTriangle, CheckCircle2, Loader2, Mail, Package, Plus, Trash2, X } from 'lucide-react';
import { useAuth } from '../auth/AuthContext';
import { usePermissions, ReadOnlyModuleBanner } from '../components/Gating';
import { PageHeader, HeaderButton } from '../components/PageHeader';
import { CatalogPickerDialog } from '../components/CatalogPickerDialog';
import type { CustomerOption, ItemOption, PaymentTermOption, TenantContextType } from '../types';
import type {
  RecurringAction, RecurringCadence, RecurringLineInput, RecurringScheduleDetail,
  RecurringScheduleInput, RecurringServicePeriodMode,
} from '../types/recurring';
import { WorkspaceTax, lineTaxChoices, taxChoiceLabel } from '../lib/invoiceTax';
import { formatRunDate, toDateInput } from '../lib/recurring';

interface Props { isEdit?: boolean }

const emptyLine = (): RecurringLineInput => ({
  description: '', quantity: 1, amount: 0, discount: 0, discountType: 'Percentage', taxId: '',
});

export const RecurringScheduleEdit: React.FC<Props> = ({ isEdit = false }) => {
  const { canWrite } = useOutletContext<TenantContextType>();
  const { hasPermission } = usePermissions();
  const { authFetch } = useAuth();
  const { orgSlug, scheduleId } = useParams<{ orgSlug: string; scheduleId?: string }>();
  const navigate = useNavigate();
  const [searchParams] = useSearchParams();
  const fromInvoiceId = isEdit ? null : searchParams.get('from');

  const salesWritable = canWrite('sales');
  const canManage = hasPermission('sales.invoices.create') && salesWritable;
  const canPost = hasPermission('sales.invoices.post') && salesWritable;

  // Reference data
  const [customers, setCustomers] = useState<CustomerOption[]>([]);
  const [paymentTerms, setPaymentTerms] = useState<PaymentTermOption[]>([]);
  const [catalogItems, setCatalogItems] = useState<ItemOption[]>([]);
  const [taxes, setTaxes] = useState<WorkspaceTax[]>([]);
  const [isLoadingRefs, setIsLoadingRefs] = useState(true);

  // Form fields
  const [customerId, setCustomerId] = useState<number | ''>('');
  const [name, setName] = useState('');
  const [cadence, setCadence] = useState<RecurringCadence>('Monthly');
  const [interval, setInterval] = useState(1);
  const [dayOfMonth, setDayOfMonth] = useState<number | ''>('');
  const [startDate, setStartDate] = useState(toDateInput(new Date()));
  const [endDate, setEndDate] = useState('');
  const [maxOccurrences, setMaxOccurrences] = useState<number | ''>('');
  const [action, setAction] = useState<RecurringAction>('CreateDraft');
  const [servicePeriodMode, setServicePeriodMode] = useState<RecurringServicePeriodMode>('CurrentMonth');
  const [paymentTermId, setPaymentTermId] = useState<number | ''>('');
  const [notes, setNotes] = useState('');
  const [paymentInstructions, setPaymentInstructions] = useState('');
  const [lines, setLines] = useState<RecurringLineInput[]>([emptyLine()]);
  const [pickerLine, setPickerLine] = useState<number | null>(null);

  const [preview, setPreview] = useState<string[]>([]);
  const [isLoading, setIsLoading] = useState(isEdit);
  const [isSaving, setIsSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [success, setSuccess] = useState<string | null>(null);

  // ----- load reference data -----
  const loadRefs = useCallback(async () => {
    setIsLoadingRefs(true);
    try {
      const [custRes, termsRes, itemsRes, taxesRes] = await Promise.all([
        authFetch('/api/Sales/Customers'),
        authFetch('/api/Common/PaymentTerms'),
        authFetch('/api/Common/Items'),
        authFetch('/api/Tax/Taxes'),
      ]);
      if (custRes.ok) setCustomers((await custRes.json()) as CustomerOption[]);
      if (termsRes.ok) setPaymentTerms((await termsRes.json()) as PaymentTermOption[]);
      if (itemsRes.ok) setCatalogItems((await itemsRes.json()) as ItemOption[]);
      if (taxesRes.ok) {
        const taxData = await taxesRes.json();
        setTaxes((taxData?.taxes ?? []) as WorkspaceTax[]);
      }
    } catch {
      setError('The schedule form could not be loaded.');
    } finally {
      setIsLoadingRefs(false);
    }
  }, [authFetch]);

  useEffect(() => { void loadRefs(); }, [loadRefs]);

  // ----- load existing schedule (edit) -----
  useEffect(() => {
    if (!isEdit || !scheduleId) return;
    let cancelled = false;
    (async () => {
      setIsLoading(true);
      try {
        const res = await authFetch(`/api/RecurringSchedules/${scheduleId}`);
        if (!res.ok) throw new Error('The schedule could not be loaded.');
        const s: RecurringScheduleDetail = await res.json();
        if (cancelled) return;
        setCustomerId(s.customerId);
        setName(s.name);
        setCadence(s.cadence);
        setInterval(s.interval);
        setDayOfMonth(s.dayOfMonth ?? '');
        setStartDate(s.startDate.slice(0, 10));
        setEndDate(s.endDate ? s.endDate.slice(0, 10) : '');
        setMaxOccurrences(s.maxOccurrences ?? '');
        setAction(s.action === 'AutoApprove' ? 'CreateDraft' : s.action);
        setServicePeriodMode(s.servicePeriodMode);
        setPaymentTermId(s.paymentTermId ?? '');
        setNotes(s.notes ?? '');
        setPaymentInstructions(s.paymentInstructions ?? '');
        setLines(
          s.lines.length
            ? s.lines.map((l) => ({
                id: l.id, itemId: l.itemId ?? null, description: l.description ?? '',
                quantity: l.quantity, amount: l.amount, discount: l.discount,
                discountType: l.discountType, taxId: l.taxId ?? '', accountId: l.accountId ?? null,
              }))
            : [emptyLine()],
        );
      } catch (err: any) {
        if (!cancelled) setError(err.message || 'The schedule could not be loaded.');
      } finally {
        if (!cancelled) setIsLoading(false);
      }
    })();
    return () => { cancelled = true; };
  }, [authFetch, isEdit, scheduleId]);

  // ----- prefill from an invoice (Make recurring) -----
  useEffect(() => {
    if (!fromInvoiceId || isLoadingRefs) return;
    let cancelled = false;
    (async () => {
      try {
        const res = await authFetch(`/api/Sales/SalesInvoice?id=${fromInvoiceId}`);
        if (!res.ok || cancelled) return;
        const inv = await res.json();
        setCustomerId(inv.customerId ?? '');
        setName(inv.customerName ? `Recurring — ${inv.customerName}` : 'Recurring invoice');
        if (inv.paymentTermId) setPaymentTermId(inv.paymentTermId);
        if (inv.notes) setNotes(inv.notes);
        if (inv.paymentInstructions) setPaymentInstructions(inv.paymentInstructions);
        if (Array.isArray(inv.salesInvoiceLines) && inv.salesInvoiceLines.length) {
          setLines(inv.salesInvoiceLines.map((l: any): RecurringLineInput => ({
            itemId: l.itemId ?? null,
            description: l.itemDescription ?? '',
            quantity: l.quantity ?? 1,
            amount: l.amount ?? 0,
            discount: l.discount ?? 0,
            discountType: 'Percentage',
            taxId: l.taxId ?? '',
            accountId: l.salesAccountId ?? null,
          })));
        }
      } catch { /* prefill is best-effort */ }
    })();
    return () => { cancelled = true; };
  }, [authFetch, fromInvoiceId, isLoadingRefs]);

  const needsDayOfMonth = cadence !== 'Weekly';

  // ----- live preview (debounced) -----
  useEffect(() => {
    if (!startDate) { setPreview([]); return; }
    const handle = setTimeout(async () => {
      try {
        const res = await authFetch('/api/RecurringSchedules/preview', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({
            cadence, interval: Number(interval) || 1,
            startDate, dayOfMonth: needsDayOfMonth ? (dayOfMonth || undefined) : undefined,
            endDate: endDate || undefined,
            maxOccurrences: maxOccurrences || undefined,
            count: 3,
          }),
        });
        if (res.ok) {
          const data = await res.json();
          setPreview(Array.isArray(data?.dates) ? data.dates : []);
        } else {
          setPreview([]);
        }
      } catch { setPreview([]); }
    }, 300);
    return () => clearTimeout(handle);
  }, [authFetch, cadence, interval, startDate, dayOfMonth, endDate, maxOccurrences, needsDayOfMonth]);

  // auto-issue is only offered to users who can post invoices
  useEffect(() => {
    if (!canPost && action === 'AutoIssue') setAction('CreateDraft');
  }, [canPost, action]);

  const setLine = (idx: number, patch: Partial<RecurringLineInput>) =>
    setLines((prev) => prev.map((l, i) => (i === idx ? { ...l, ...patch } : l)));
  const addLine = () => setLines((prev) => [...prev, emptyLine()]);
  const removeLine = (idx: number) => setLines((prev) => (prev.length <= 1 ? prev : prev.filter((_, i) => i !== idx)));
  const pickItem = (idx: number, item: ItemOption) => {
    setLine(idx, { itemId: item.id, description: item.description, amount: item.price ?? lines[idx].amount });
    setPickerLine(null);
  };

  const selectedCustomer = customers.find((c) => c.id === Number(customerId));

  const save = async () => {
    setError(null);
    setSuccess(null);
    if (!customerId) { setError('Select a client for this schedule.'); return; }
    if (!name.trim()) { setError('Give the schedule a name.'); return; }
    if (needsDayOfMonth && !dayOfMonth) { setError('Choose which day of the month to run.'); return; }
    const meaningful = lines.filter((l) => l.itemId || (l.description ?? '').trim() !== '' || l.amount > 0);
    if (meaningful.length === 0) { setError('Add at least one line.'); return; }

    const payload: RecurringScheduleInput = {
      id: isEdit && scheduleId ? Number(scheduleId) : undefined,
      customerId: Number(customerId),
      name: name.trim(),
      cadence,
      interval: Number(interval) || 1,
      dayOfMonth: needsDayOfMonth ? Number(dayOfMonth) : null,
      startDate,
      endDate: endDate || null,
      maxOccurrences: maxOccurrences ? Number(maxOccurrences) : null,
      action,
      servicePeriodMode,
      paymentTermId: paymentTermId ? Number(paymentTermId) : null,
      notes: notes.trim() || null,
      paymentInstructions: paymentInstructions.trim() || null,
      lines: meaningful.map((l, i) => ({
        id: l.id,
        itemId: l.itemId ?? null,
        description: (l.description ?? '').trim() || null,
        quantity: Number(l.quantity) || 1,
        amount: Number(l.amount) || 0,
        discount: Number(l.discount) || 0,
        discountType: l.discountType,
        taxId: l.taxId === '' || l.taxId == null ? null : Number(l.taxId),
        accountId: l.accountId ?? null,
        sortOrder: i,
      })),
    };

    setIsSaving(true);
    try {
      const url = isEdit ? `/api/RecurringSchedules/${scheduleId}` : '/api/RecurringSchedules';
      const res = await authFetch(url, {
        method: isEdit ? 'PUT' : 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(payload),
      });
      if (!res.ok) {
        const data = await res.json().catch(() => null);
        throw new Error(data?.message || 'The schedule could not be saved.');
      }
      const targetId = isEdit ? Number(scheduleId) : (await res.json())?.id;
      setSuccess('Schedule saved.');
      setTimeout(() => navigate(`/org/${orgSlug}/sales/recurring/${targetId}`), 500);
    } catch (err: any) {
      setError(err.message || 'The schedule could not be saved.');
    } finally {
      setIsSaving(false);
    }
  };

  if (!canManage) {
    return (
      <div className="max-w-3xl mx-auto px-4 py-6">
        <p className="text-sm text-[var(--text-muted)]">You do not have permission to manage recurring schedules.</p>
      </div>
    );
  }

  if (isLoading || isLoadingRefs) {
    return (
      <div className="max-w-3xl mx-auto px-4 py-16 flex items-center justify-center text-[var(--text-muted)]">
        <Loader2 className="w-5 h-5 animate-spin mr-2" /> Loading…
      </div>
    );
  }

  const inputClass = 'w-full h-9 px-3 text-sm bg-[var(--bg-subtle)] border border-[var(--border-subtle)] rounded-lg focus:ring-2 focus:ring-[var(--brand-ring)] focus:outline-none text-[var(--text-main)]';
  const labelClass = 'block text-xs font-semibold uppercase tracking-wider text-[var(--text-muted)] mb-1.5';

  return (
    <div className="max-w-4xl mx-auto px-4 sm:px-6 py-6 space-y-6 text-[var(--text-main)]">
      <ReadOnlyModuleBanner module="sales" orgSlug={orgSlug} />

      <PageHeader
        back={{ to: `/org/${orgSlug}/sales/recurring`, label: 'Back to recurring invoices' }}
        title={isEdit ? 'Edit recurring schedule' : 'New recurring schedule'}
        actions={
          <HeaderButton label={isEdit ? 'Save changes' : 'Create schedule'} variant="primary" busy={isSaving} onClick={save} />
        }
      />

      {error && (
        <div role="alert" className="p-4 bg-rose-500/10 border border-rose-500/20 text-rose-500 rounded-xl flex items-start gap-3">
          <AlertTriangle className="w-5 h-5 shrink-0 mt-0.5" />
          <div className="flex-1 text-sm font-medium">{error}</div>
          <button onClick={() => setError(null)} className="p-1 hover:opacity-75"><X className="w-4 h-4" /></button>
        </div>
      )}
      {success && (
        <div className="p-4 bg-emerald-500/10 border border-emerald-500/20 text-emerald-500 rounded-xl flex items-center gap-3">
          <CheckCircle2 className="w-5 h-5 shrink-0" />
          <div className="flex-1 text-sm font-medium">{success}</div>
        </div>
      )}

      {/* Section 1: basics */}
      <div className="rounded-2xl border border-[var(--border-subtle)] bg-[var(--bg-surface)] p-6 space-y-4">
        <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
          <div>
            <label className={labelClass}>Schedule name</label>
            <input className={inputClass} value={name} onChange={(e) => setName(e.target.value)} placeholder="Monthly retainer — Acme Corp" />
          </div>
          <div>
            <label className={labelClass}>Client</label>
            <select className={inputClass} value={customerId} onChange={(e) => setCustomerId(e.target.value ? Number(e.target.value) : '')}>
              <option value="">Select a client…</option>
              {customers.filter((c) => !c.archived || c.id === Number(customerId)).map((c) => (
                <option key={c.id} value={c.id}>{c.name}{c.archived ? ' (archived)' : ''}</option>
              ))}
            </select>
            {selectedCustomer?.archived && (
              <p className="mt-1 text-xs text-rose-500">This client is archived; choose an active client.</p>
            )}
          </div>
        </div>
      </div>

      {/* Section 2: schedule */}
      <div className="rounded-2xl border border-[var(--border-subtle)] bg-[var(--bg-surface)] p-6 space-y-4">
        <h3 className="text-xs font-semibold uppercase tracking-wider text-[var(--text-muted)]">Schedule</h3>
        <div className="grid grid-cols-1 sm:grid-cols-3 gap-4">
          <div>
            <label className={labelClass}>Repeat</label>
            <select className={inputClass} value={cadence} onChange={(e) => setCadence(e.target.value as RecurringCadence)}>
              <option value="Weekly">Weekly</option>
              <option value="Monthly">Monthly</option>
              <option value="Quarterly">Quarterly</option>
              <option value="Yearly">Yearly</option>
            </select>
          </div>
          <div>
            <label className={labelClass}>Every</label>
            <input type="number" min={1} className={inputClass} value={interval} onChange={(e) => setInterval(Math.max(1, Number(e.target.value) || 1))} />
          </div>
          {needsDayOfMonth && (
            <div>
              <label className={labelClass}>Day of month</label>
              <input
                type="number" min={1} max={31} className={inputClass}
                value={dayOfMonth}
                onChange={(e) => setDayOfMonth(e.target.value ? Math.min(31, Math.max(1, Number(e.target.value))) : '')}
                placeholder="e.g. 1"
              />
              <p className="mt-1 text-[11px] text-[var(--text-muted)]">Use 31 for the last day of the month.</p>
            </div>
          )}
        </div>
        <div className="grid grid-cols-1 sm:grid-cols-3 gap-4">
          <div>
            <label className={labelClass}>Start date</label>
            <input type="date" className={inputClass} value={startDate} onChange={(e) => setStartDate(e.target.value)} />
          </div>
          <div>
            <label className={labelClass}>End date (optional)</label>
            <input type="date" className={inputClass} value={endDate} onChange={(e) => setEndDate(e.target.value)} />
          </div>
          <div>
            <label className={labelClass}>Max runs (optional)</label>
            <input type="number" min={1} className={inputClass} value={maxOccurrences} onChange={(e) => setMaxOccurrences(e.target.value ? Math.max(1, Number(e.target.value)) : '')} placeholder="No limit" />
          </div>
        </div>

        {/* Next-dates preview */}
        <div className="rounded-xl bg-[var(--bg-subtle)] p-3">
          <div className="text-xs font-semibold uppercase tracking-wider text-[var(--text-muted)] mb-2">Next runs</div>
          {preview.length === 0 ? (
            <p className="text-sm text-[var(--text-muted)]">Fill in the schedule to preview the next dates.</p>
          ) : (
            <div className="flex flex-wrap gap-2">
              {preview.map((d) => (
                <span key={d} className="px-2.5 py-1 rounded-lg bg-[var(--bg-surface)] text-sm text-[var(--text-secondary)]">
                  {formatRunDate(d)}
                </span>
              ))}
            </div>
          )}
        </div>
      </div>

      {/* Section 3: what to generate */}
      <div className="rounded-2xl border border-[var(--border-subtle)] bg-[var(--bg-surface)] p-6 space-y-4">
        <h3 className="text-xs font-semibold uppercase tracking-wider text-[var(--text-muted)]">On each run</h3>
        <div className="space-y-2">
          <label className="flex items-start gap-2.5 cursor-pointer">
            <input type="radio" className="mt-1" checked={action === 'CreateDraft'} onChange={() => setAction('CreateDraft')} />
            <span>
              <span className="block text-sm font-medium">Create a draft</span>
              <span className="block text-xs text-[var(--text-muted)]">A draft invoice is created for someone to review and issue.</span>
            </span>
          </label>
          {canPost && (
            <label className="flex items-start gap-2.5 cursor-pointer">
              <input type="radio" className="mt-1" checked={action === 'AutoIssue'} onChange={() => setAction('AutoIssue')} />
              <span>
                <span className="block text-sm font-medium">Create and issue automatically</span>
                <span className="block text-xs text-[var(--text-muted)]">The invoice is issued when all checks pass; otherwise it stays a draft.</span>
              </span>
            </label>
          )}
          {!canPost && (
            <p className="text-xs text-[var(--text-muted)]">Auto-issue needs the invoice-post permission.</p>
          )}
        </div>

        {/* Email opt-in: shown but off/disabled for this manual-first release */}
        <div className="flex items-start gap-2.5 opacity-60">
          <input type="checkbox" className="mt-1" checked={false} disabled aria-disabled />
          <span>
            <span className="flex items-center gap-1.5 text-sm font-medium">
              <Mail className="w-3.5 h-3.5" /> Email the invoice to the client
            </span>
            <span className="block text-xs text-[var(--text-muted)]">Automatic emailing isn’t available in this release.</span>
          </span>
        </div>
      </div>

      {/* Section 4: lines (reuses the invoice line editor patterns) */}
      <div className="rounded-2xl border border-[var(--border-subtle)] bg-[var(--bg-surface)] p-6 space-y-3">
        <h3 className="text-xs font-semibold uppercase tracking-wider text-[var(--text-muted)]">Line items</h3>
        <div className="overflow-x-auto border border-[var(--border-subtle)] rounded-xl">
          <table className="w-full text-left border-collapse">
            <thead>
              <tr className="bg-[var(--bg-subtle)] text-xs font-semibold text-[var(--text-muted)] uppercase tracking-wider border-b border-[var(--border-subtle)]">
                <th className="py-3 px-4 min-w-[240px]">Item & description</th>
                <th className="py-3 px-3 w-20 text-right">Qty</th>
                <th className="py-3 px-3 w-28 text-right">Amount</th>
                <th className="py-3 px-3 w-20 text-right">Disc %</th>
                <th className="py-3 px-3 w-40 text-right">Tax</th>
                <th className="py-3 px-2 w-12" />
              </tr>
            </thead>
            <tbody className="divide-y divide-[var(--border-subtle)] text-sm">
              {lines.map((line, idx) => {
                const linked = line.itemId ? catalogItems.find((ci) => ci.id === line.itemId) : undefined;
                return (
                  <tr key={idx}>
                    <td className="py-2.5 px-4">
                      <div className="relative">
                        <input
                          type="text"
                          aria-label={`Line ${idx + 1} description`}
                          value={line.description ?? ''}
                          onChange={(e) => setLine(idx, { description: e.target.value })}
                          placeholder="Description"
                          className={`${inputClass} ${catalogItems.length > 0 ? 'pr-10' : ''}`}
                        />
                        {catalogItems.length > 0 && (
                          <button
                            type="button"
                            onClick={() => setPickerLine(idx)}
                            title={linked ? `Catalog item: ${linked.description}` : 'Pick from catalog'}
                            aria-label={`Pick line ${idx + 1} from catalog`}
                            className={`absolute right-1 top-1/2 -translate-y-1/2 inline-flex h-7 w-7 items-center justify-center rounded-md transition ${
                              linked ? 'text-[var(--brand-primary)] hover:bg-[var(--brand-subtle)]' : 'text-[var(--text-muted)] hover:text-[var(--text-main)] hover:bg-[var(--bg-surface-hover)]'
                            }`}
                          >
                            <Package className="w-4 h-4" />
                          </button>
                        )}
                      </div>
                    </td>
                    <td className="py-2.5 px-3 text-right">
                      <input type="number" min={0} step="any" value={line.quantity} onChange={(e) => setLine(idx, { quantity: parseFloat(e.target.value) || 0 })} className={`${inputClass} text-right`} />
                    </td>
                    <td className="py-2.5 px-3 text-right">
                      <input type="number" min={0} step="0.01" value={line.amount} onChange={(e) => setLine(idx, { amount: parseFloat(e.target.value) || 0 })} className={`${inputClass} text-right`} />
                    </td>
                    <td className="py-2.5 px-3 text-right">
                      <input type="number" min={0} max={100} step="0.5" value={line.discount} onChange={(e) => setLine(idx, { discount: parseFloat(e.target.value) || 0 })} className={`${inputClass} text-right`} />
                    </td>
                    <td className="py-2.5 px-3 text-right">
                      <select
                        aria-label={`Line ${idx + 1} tax`}
                        value={line.taxId}
                        onChange={(e) => setLine(idx, { taxId: e.target.value ? Number(e.target.value) : '' })}
                        className={`${inputClass} text-right`}
                      >
                        <option value="">No tax</option>
                        {lineTaxChoices(taxes, line.taxId ?? '').map((t) => (
                          <option key={t.id} value={t.id}>{taxChoiceLabel(t)}</option>
                        ))}
                      </select>
                    </td>
                    <td className="py-2.5 px-2 text-center">
                      <button type="button" onClick={() => removeLine(idx)} disabled={lines.length <= 1} className="p-1.5 text-[var(--text-muted)] hover:text-rose-500 rounded-lg hover:bg-[var(--bg-surface-hover)] transition disabled:opacity-20" title="Remove line">
                        <Trash2 className="w-4 h-4" />
                      </button>
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
        <button type="button" onClick={addLine} className="inline-flex items-center gap-1.5 px-3.5 py-2 text-xs font-semibold text-[var(--brand-primary)] bg-[var(--brand-subtle)] rounded-xl hover:opacity-85 transition">
          <Plus className="w-3.5 h-3.5" /> Add line
        </button>
      </div>

      {/* Section 5: terms, notes */}
      <div className="rounded-2xl border border-[var(--border-subtle)] bg-[var(--bg-surface)] p-6 space-y-4">
        <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
          <div>
            <label className={labelClass}>Payment terms</label>
            <select className={inputClass} value={paymentTermId} onChange={(e) => setPaymentTermId(e.target.value ? Number(e.target.value) : '')}>
              <option value="">Default</option>
              {paymentTerms.map((t) => <option key={t.id} value={t.id}>{t.description}</option>)}
            </select>
          </div>
          <div>
            <label className={labelClass}>Service period</label>
            <select className={inputClass} value={servicePeriodMode} onChange={(e) => setServicePeriodMode(e.target.value as RecurringServicePeriodMode)}>
              <option value="CurrentMonth">Current period</option>
              <option value="PreviousMonth">Previous period</option>
            </select>
          </div>
        </div>
        <div>
          <label className={labelClass}>Client notes</label>
          <textarea rows={2} className={`${inputClass} h-auto py-2`} value={notes} onChange={(e) => setNotes(e.target.value)} />
        </div>
        <div>
          <label className={labelClass}>Payment instructions</label>
          <textarea rows={2} className={`${inputClass} h-auto py-2`} value={paymentInstructions} onChange={(e) => setPaymentInstructions(e.target.value)} />
        </div>
      </div>

      {pickerLine !== null && (
        <CatalogPickerDialog
          items={catalogItems}
          selectedId={lines[pickerLine]?.itemId ?? null}
          formatPrice={(v) => v.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 })}
          onPick={(item) => pickItem(pickerLine, item)}
          onClose={() => setPickerLine(null)}
        />
      )}
    </div>
  );
};

export default RecurringScheduleEdit;
