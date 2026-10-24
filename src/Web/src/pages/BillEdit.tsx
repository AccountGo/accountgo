import React, { useCallback, useEffect, useMemo, useState } from 'react';
import { Link, useNavigate, useOutletContext, useParams, useSearchParams } from 'react-router-dom';
import { AlertTriangle, CheckCircle2, ChevronsUpDown, Loader2, Package, Plus, Save, Trash2, Unlink, UserPlus, Users, X } from 'lucide-react';
import { PageHeader, HeaderButton } from '../components/PageHeader';
import { PartyPickerDialog } from '../components/PartyPickerDialog';
import { PartyFormDialog } from '../components/PartyFormDialog';
import { CatalogPickerDialog } from '../components/CatalogPickerDialog';
import { ItemFormDialog } from '../components/ItemFormDialog';
import { usePermissions } from '../components/Gating';
import { Bill, BillAccount, CatalogItem, CustomerOption, ItemOption, PaymentTermOption, TenantContextType, Vendor } from '../types';
import { useAuth } from '../auth/AuthContext';
import { currencySymbol as getCurrencySymbol } from '../lib/currency';
import { addBusinessDays, businessDaysBetween, isValidDate, localToday as today } from '../lib/businessDays';

interface TaxOption {
  id: number;
  taxName: string;
  rate: number;
}

interface LineDraft {
  /** Stable React key; ids are 0 until a line is saved. */
  key: number;
  id: number;
  itemId: number | null;
  description: string;
  accountId: number | '';
  isStockItem: boolean;
  quantity: number;
  amount: number;
  discount: number;
  taxId: number | '';
}

const fieldClass =
  'w-full px-3.5 py-2.5 text-sm bg-[var(--bg-subtle)] border border-[var(--border-subtle)] rounded-xl focus:ring-2 focus:ring-[var(--brand-ring)] focus:outline-none text-[var(--text-main)] placeholder-[var(--text-muted)] transition';
const cellClass =
  'w-full h-9 px-2.5 text-sm bg-[var(--bg-subtle)] border border-[var(--border-subtle)] rounded-lg focus:ring-2 focus:ring-[var(--brand-ring)] focus:outline-none text-[var(--text-main)] transition';
const labelClass = 'block text-xs font-semibold uppercase tracking-wider text-[var(--text-muted)] mb-1.5';

const round2 = (value: number) => Math.round((value + Number.EPSILON) * 100) / 100;
// The same rounding as the server: each line to the cent, tax on top of the line at the tax's rate.
const lineSubtotal = (line: LineDraft) => round2((Number(line.quantity) || 0) * (Number(line.amount) || 0) * (1 - (Number(line.discount) || 0) / 100));

let nextKey = 1;

/**
 * Enters a vendor's bill, or edits a draft. Saving keeps it as a draft; "Save & approve" posts it, after which it
 * can't be changed, only voided. Lines are booked to an expense or asset account each.
 */
export const BillEdit: React.FC<{ isEdit?: boolean }> = ({ isEdit = false }) => {
  const { currentOrg, hasModule } = useOutletContext<TenantContextType>();
  const { billId } = useParams<{ billId: string }>();
  const [searchParams] = useSearchParams();
  const navigate = useNavigate();
  const { authFetch } = useAuth();
  const { hasPermission } = usePermissions();
  const canApprove = hasPermission('purchases.bills.post');
  const canManageVendors = hasPermission('purchases.vendors.manage');
  const canManageItems = hasPermission('inventory.items.manage');
  const orgSlug = currentOrg.slug;
  const listPath = `/org/${orgSlug}/purchases`;
  const duplicateOf = isEdit ? null : searchParams.get('from');
  const presetVendor = isEdit ? null : Number(searchParams.get('vendor')) || null;

  const [vendors, setVendors] = useState<CustomerOption[]>([]);
  const [paymentTerms, setPaymentTerms] = useState<PaymentTermOption[]>([]);
  const [accounts, setAccounts] = useState<BillAccount[]>([]);
  const [taxes, setTaxes] = useState<TaxOption[]>([]);
  const [catalog, setCatalog] = useState<ItemOption[]>([]);
  const [isLoading, setIsLoading] = useState(true);

  const [vendorId, setVendorId] = useState<number | null>(presetVendor);
  const [vendorInvoiceNo, setVendorInvoiceNo] = useState('');
  const [referenceNo, setReferenceNo] = useState('');
  const [date, setDate] = useState(today());
  const [paymentTermId, setPaymentTermId] = useState<number | ''>('');
  const [dueDate, setDueDate] = useState('');
  const [notes, setNotes] = useState('');
  const [lines, setLines] = useState<LineDraft[]>([]);
  const [duplicatedFrom, setDuplicatedFrom] = useState<string | null>(null);

  const [saving, setSaving] = useState<'draft' | 'approve' | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [isVendorPickerOpen, setIsVendorPickerOpen] = useState(false);
  const [isAddingVendor, setIsAddingVendor] = useState(false);
  const [pickerLine, setPickerLine] = useState<number | null>(null);
  const [newItemLine, setNewItemLine] = useState<number | null>(null);

  const currencySymbol = getCurrencySymbol(currentOrg.currency);
  const formatCurrency = useCallback(
    (val: number) => `${currencySymbol}${val.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`,
    [currencySymbol]
  );

  // New lines are booked to Purchase A/C unless another account is picked, the same default the server applies.
  const defaultAccountId = useMemo(() => accounts.find((a) => a.code === '50200')?.id ?? '', [accounts]);
  const blankLine = useCallback(
    (): LineDraft => ({ key: nextKey++, id: 0, itemId: null, description: '', accountId: defaultAccountId, isStockItem: false, quantity: 1, amount: 0, discount: 0, taxId: '' }),
    [defaultAccountId]
  );

  const toLine = (line: Bill['lines'][number], keepId: boolean): LineDraft => ({
    key: nextKey++,
    id: keepId ? line.id : 0,
    itemId: line.itemId ?? null,
    description: line.description ?? '',
    accountId: line.accountId ?? '',
    isStockItem: line.isStockItem ?? false,
    quantity: line.quantity,
    amount: line.amount,
    discount: line.discount ?? 0,
    taxId: line.taxId ?? '',
  });

  useEffect(() => {
    let cancelled = false;
    (async () => {
      setIsLoading(true);
      try {
        const [vendorsRes, termsRes, accountsRes, taxesRes, catalogRes] = await Promise.all([
          authFetch('/api/Purchasing/Vendors'),
          authFetch('/api/Common/PaymentTerms'),
          authFetch('/api/Purchasing/BillAccounts'),
          authFetch('/api/Tax/Taxes'),
          authFetch('/api/Catalog/Items'),
        ]);
        if (cancelled) return;
        if (!vendorsRes.ok || !accountsRes.ok) throw new Error('The bill form could not be loaded.');
        const vendorList: Vendor[] = await vendorsRes.json();
        setVendors(vendorList.map((v) => ({ id: v.id, name: v.name, email: v.email ?? undefined, phone: v.phone ?? undefined, paymentTermId: v.paymentTermId ?? undefined, archived: v.archived })));
        const accountList: BillAccount[] = await accountsRes.json();
        setAccounts(accountList);
        if (termsRes.ok) setPaymentTerms(await termsRes.json());
        if (taxesRes.ok) {
          const data = await taxesRes.json();
          setTaxes(((data?.taxes ?? []) as (TaxOption & { isActive: boolean })[]).filter((t) => t.isActive && t.rate > 0));
        }
        // Without permission to see the catalog, lines are typed.
        if (catalogRes.ok) {
          const items: CatalogItem[] = await catalogRes.json();
          setCatalog(items.filter((i) => !i.archived).map((i) => ({
            id: i.id,
            code: i.code ?? undefined,
            description: i.name,
            price: i.cost ?? undefined,
            trackStock: i.trackStock,
            quantityOnHand: i.quantityOnHand,
            inventoryAccountId: i.inventoryAccountId,
          })));
        }

        const sourceId = isEdit ? billId : duplicateOf;
        if (sourceId) {
          const res = await authFetch(`/api/Purchasing/Bill?id=${encodeURIComponent(sourceId)}`);
          if (cancelled) return;
          if (!res.ok) throw new Error(res.status === 404 ? 'That bill was not found.' : 'The bill could not be loaded.');
          const bill: Bill = await res.json();
          if (isEdit && bill.approved) {
            navigate(`/org/${orgSlug}/purchases/${bill.id}`, { replace: true });
            return;
          }
          setVendorId(bill.vendorId);
          setReferenceNo(bill.referenceNo ?? '');
          setNotes(bill.notes ?? '');
          setPaymentTermId(bill.paymentTermId ?? '');
          setLines(bill.lines.map((l) => toLine(l, isEdit)));
          if (isEdit) {
            setVendorInvoiceNo(bill.vendorInvoiceNo ?? '');
            setDate(bill.date.slice(0, 10));
            setDueDate(bill.dueDate?.slice(0, 10) ?? '');
          } else {
            // A copy is a new bill: dated today, due on the same terms, without the vendor's number.
            const gap = bill.dueDate ? businessDaysBetween(bill.date.slice(0, 10), bill.dueDate.slice(0, 10)) : null;
            setDueDate(gap !== null ? addBusinessDays(today(), gap) : '');
            setDuplicatedFrom(bill.vendorInvoiceNo || (bill.no ? `#${bill.no}` : 'a draft'));
          }
        } else {
          const defaultAccount = accountList.find((a) => a.code === '50200')?.id ?? '';
          setLines([{ key: nextKey++, id: 0, itemId: null, description: '', accountId: defaultAccount, isStockItem: false, quantity: 1, amount: 0, discount: 0, taxId: '' }]);
        }
      } catch (err: any) {
        if (!cancelled) setError(err.message || 'The bill form could not be loaded.');
      } finally {
        if (!cancelled) setIsLoading(false);
      }
    })();
    return () => {
      cancelled = true;
    };
    // Loads once per bill; the other values are read from the URL.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [authFetch, billId, duplicateOf, isEdit]);

  const vendor = vendors.find((v) => v.id === vendorId) ?? null;

  // A vendor's own terms apply to a new bill when there are no terms yet.
  useEffect(() => {
    if (isEdit || !vendor || paymentTermId !== '' || dueDate) return;
    const termId = vendor.paymentTermId ?? paymentTerms[0]?.id;
    if (termId) applyTerm(termId);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [vendor?.id, paymentTerms]);

  // Terms count business days, as on invoices. A term sets the due date; a date that no term fits is custom.
  const applyTerm = (termId: number | '', from = date) => {
    setPaymentTermId(termId);
    const term = paymentTerms.find((t) => t.id === termId);
    if (term && isValidDate(from)) setDueDate(addBusinessDays(from, term.dueAfterDays ?? 0));
  };
  const dueInBusinessDays = isValidDate(date) && isValidDate(dueDate) ? businessDaysBetween(date, dueDate) : undefined;

  const updateLine = (index: number, changes: Partial<LineDraft>) =>
    setLines((prev) => prev.map((line, i) => (i === index ? { ...line, ...changes } : line)));

  const taxRate = (taxId: number | '') => taxes.find((t) => t.id === taxId)?.rate ?? 0;
  const totals = useMemo(() => {
    let subtotal = 0;
    let tax = 0;
    for (const line of lines) {
      const sub = lineSubtotal(line);
      subtotal += sub;
      tax += round2((sub * taxRate(line.taxId)) / 100);
    }
    return { subtotal: round2(subtotal), tax: round2(tax), total: round2(subtotal + tax) };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [lines, taxes]);

  const handleSave = async (approve: boolean) => {
    setError(null);
    if (!vendorId) {
      setError('Pick the vendor the bill is from.');
      return;
    }
    if (lines.length === 0 || lines.some((l) => !l.itemId && !l.description.trim())) {
      setError('Describe each line, or pick an item for it.');
      return;
    }
    if (lines.some((l) => !(Number(l.quantity) > 0))) {
      setError("Each line's quantity must be more than zero.");
      return;
    }
    if (dueInBusinessDays === null) {
      setError("The due date can't be before the bill date.");
      return;
    }
    if (approve && totals.total <= 0) {
      setError("A bill's total must be more than zero to approve it.");
      return;
    }

    setSaving(approve ? 'approve' : 'draft');
    try {
      const res = await authFetch('/api/Purchasing/SaveBill', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          id: isEdit ? Number(billId) : 0,
          vendorId,
          vendorInvoiceNo: vendorInvoiceNo.trim() || null,
          referenceNo: referenceNo.trim() || null,
          date,
          dueDate: dueDate || null,
          paymentTermId: paymentTermId || null,
          notes: notes.trim() || null,
          status: approve ? 'Open' : 'Draft',
          lines: lines.map((l) => ({
            id: l.id,
            itemId: l.itemId,
            description: l.description.trim() || null,
            accountId: l.accountId || null,
            quantity: Number(l.quantity) || 0,
            amount: Number(l.amount) || 0,
            discount: Number(l.discount) || null,
            taxId: l.taxId || null,
          })),
        }),
      });
      const data = await res.json().catch(() => null);
      if (!res.ok) {
        throw new Error(
          res.status === 403
            ? approve
              ? "You can save this bill as a draft, but approving bills takes the 'Post / Approve Bills' permission."
              : "You don't have permission to enter bills."
            : data?.message || 'The bill could not be saved.'
        );
      }
      navigate(`/org/${orgSlug}/purchases/${(data as Bill).id}`);
    } catch (err: any) {
      setError(err.message);
      setSaving(null);
    }
  };

  if (isLoading) {
    return (
      <div className="py-24 flex flex-col items-center justify-center text-[var(--text-secondary)] gap-3">
        <Loader2 className="w-8 h-8 animate-spin text-[var(--brand-primary)]" />
        <p className="text-sm">Loading bill...</p>
      </div>
    );
  }

  const pickerItem = pickerLine !== null ? lines[pickerLine] : null;
  const expenseAccounts = accounts.filter((a) => a.group === 'Expense');
  const assetAccounts = accounts.filter((a) => a.group === 'Asset');

  return (
    <div className="max-w-6xl mx-auto px-4 sm:px-6 py-6 pb-24 text-[var(--text-main)]">
      <PageHeader
        className="mb-6"
        back={{ to: listPath, label: 'Back to bills' }}
        title={isEdit ? 'Edit draft bill' : 'New bill'}
        subtitle={
          duplicatedFrom
            ? `A copy of ${duplicatedFrom}, dated today. Nothing is saved until you save it.`
            : 'Enter the bill as your vendor sent it. Save a draft to finish later, or approve it to record what you owe.'
        }
        actions={
          <>
            <HeaderButton label="Cancel" to={isEdit ? `/org/${orgSlug}/purchases/${billId}` : listPath} />
            <HeaderButton label="Save draft" icon={Save} compact busy={saving === 'draft'} disabled={!!saving} onClick={() => handleSave(false)} />
            {/* Approving takes its own permission; without it, bills are saved as drafts for someone else to approve. */}
            {canApprove && (
              <HeaderButton label="Save & approve" icon={CheckCircle2} variant="primary" busy={saving === 'approve'} disabled={!!saving} onClick={() => handleSave(true)} />
            )}
          </>
        }
      />

      {error && (
        <div role="alert" className="mb-6 p-4 bg-rose-500/10 border border-rose-500/20 text-rose-500 rounded-xl flex items-start gap-3">
          <AlertTriangle className="w-5 h-5 shrink-0 mt-0.5" />
          <div className="flex-1 text-sm font-medium">{error}</div>
          <button onClick={() => setError(null)} aria-label="Dismiss" className="p-1 hover:opacity-75 transition">
            <X className="w-4 h-4" />
          </button>
        </div>
      )}

      <div className="bg-[var(--bg-surface)] border border-[var(--border-subtle)] rounded-2xl shadow-sm p-6 sm:p-8 space-y-8">
        <div className="grid grid-cols-1 lg:grid-cols-12 gap-8 border-b border-[var(--border-subtle)] pb-8">
          <div className="lg:col-span-6 space-y-4">
            <div className="flex items-center justify-between">
              <span className="flex items-center gap-2 text-xs font-semibold uppercase tracking-wider text-[var(--text-muted)]">
                Bill from (vendor) <span className="text-rose-500">*</span>
              </span>
              {canManageVendors && (
                <button
                  type="button"
                  onClick={() => setIsAddingVendor(true)}
                  className="inline-flex items-center gap-1.5 text-xs font-semibold text-[var(--brand-primary)] hover:opacity-80 transition"
                >
                  <UserPlus className="w-3.5 h-3.5" />
                  New vendor
                </button>
              )}
            </div>

            {vendor ? (
              <button
                type="button"
                onClick={() => setIsVendorPickerOpen(true)}
                title="Change vendor"
                className="group flex w-full items-center gap-3 rounded-xl border border-[var(--border-subtle)] bg-[var(--bg-subtle)] p-3 text-left transition hover:border-[var(--brand-ring)] focus:outline-none focus-visible:ring-2 focus-visible:ring-[var(--brand-ring)]"
              >
                <span className="flex h-10 w-10 shrink-0 items-center justify-center rounded-full bg-[var(--brand-subtle)] text-sm font-bold text-[var(--brand-primary)]">
                  {vendor.name.trim().charAt(0).toUpperCase() || '?'}
                </span>
                <span className="min-w-0 flex-1">
                  <span className="block truncate text-sm font-semibold text-[var(--text-main)]">{vendor.name}</span>
                  <span className="block truncate text-xs text-[var(--text-secondary)]">
                    {[vendor.email, vendor.phone].filter(Boolean).join(' · ') || 'No email or phone on record'}
                  </span>
                </span>
                <span className="shrink-0 text-xs font-semibold text-[var(--text-muted)] transition group-hover:text-[var(--brand-primary)]">Change</span>
              </button>
            ) : (
              <button
                type="button"
                onClick={() => setIsVendorPickerOpen(true)}
                className="flex h-11 w-full items-center gap-2.5 rounded-xl border border-dashed border-[var(--border-subtle)] bg-[var(--bg-subtle)] px-4 text-left text-sm text-[var(--text-muted)] transition hover:border-[var(--brand-ring)] hover:text-[var(--text-main)] focus:outline-none focus-visible:ring-2 focus-visible:ring-[var(--brand-ring)]"
              >
                <Users className="h-4 w-4" />
                <span className="flex-1">Select a vendor</span>
                <ChevronsUpDown className="h-4 w-4" />
              </button>
            )}
            {vendors.length === 0 && !canManageVendors && (
              <p className="text-xs text-[var(--text-muted)]">There are no vendors yet, and adding one takes the "Create & Edit Vendors" permission.</p>
            )}
          </div>

          <div className="lg:col-span-6 grid grid-cols-1 sm:grid-cols-2 gap-4">
            <div>
              <label htmlFor="vendor-invoice-no" className={labelClass}>
                Vendor's bill #
              </label>
              <input
                id="vendor-invoice-no"
                type="text"
                maxLength={50}
                autoComplete="off"
                placeholder="As printed on the bill"
                value={vendorInvoiceNo}
                onChange={(e) => setVendorInvoiceNo(e.target.value)}
                className={`${fieldClass} font-mono`}
              />
            </div>
            <div>
              <label htmlFor="bill-reference" className={labelClass}>
                Reference / PO #
              </label>
              <input
                id="bill-reference"
                type="text"
                maxLength={100}
                placeholder="Optional"
                value={referenceNo}
                onChange={(e) => setReferenceNo(e.target.value)}
                className={fieldClass}
              />
            </div>
            <div>
              <label htmlFor="bill-date" className={labelClass}>
                Bill date <span className="text-rose-500">*</span>
              </label>
              <input
                id="bill-date"
                type="date"
                required
                value={date}
                onChange={(e) => {
                  setDate(e.target.value);
                  if (paymentTermId !== '') applyTerm(paymentTermId, e.target.value);
                }}
                className={fieldClass}
              />
            </div>
            <div>
              <label htmlFor="bill-terms" className={labelClass}>
                Payment terms
              </label>
              <select id="bill-terms" value={paymentTermId} onChange={(e) => applyTerm(e.target.value ? Number(e.target.value) : '')} className={fieldClass}>
                <option value="">Custom</option>
                {paymentTerms.map((t) => (
                  <option key={t.id} value={t.id}>
                    {t.description}
                  </option>
                ))}
              </select>
            </div>
            <div>
              <label htmlFor="bill-due" className={labelClass}>
                Due date
              </label>
              <input
                id="bill-due"
                type="date"
                value={dueDate}
                min={date || undefined}
                aria-describedby="bill-due-hint"
                onChange={(e) => {
                  setDueDate(e.target.value);
                  setPaymentTermId('');
                }}
                className={`${fieldClass} ${dueInBusinessDays === null ? '!border-rose-500/60' : ''}`}
              />
              {dueInBusinessDays !== undefined && (
                <p id="bill-due-hint" className={`mt-1.5 text-xs ${dueInBusinessDays === null ? 'text-rose-500' : 'text-[var(--text-muted)]'}`}>
                  {dueInBusinessDays === null
                    ? 'The due date is before the bill date.'
                    : dueInBusinessDays === 0
                      ? 'Due on the bill date'
                      : `${dueInBusinessDays} business day${dueInBusinessDays === 1 ? '' : 's'} after the bill date`}
                </p>
              )}
            </div>
          </div>
        </div>

        <div>
          <div className="flex items-center justify-between mb-3">
            <h3 className="text-xs font-semibold uppercase tracking-wider text-[var(--text-muted)]">Lines</h3>
            <span className="hidden sm:inline text-xs text-[var(--text-muted)]">Each line is booked to the account you pick: an expense, or an asset such as equipment.</span>
          </div>

          <div className="overflow-x-auto border border-[var(--border-subtle)] rounded-xl bg-[var(--bg-surface)]">
            <table className="w-full text-left border-collapse">
              <thead>
                <tr className="bg-[var(--bg-subtle)] text-xs font-semibold text-[var(--text-muted)] uppercase tracking-wider border-b border-[var(--border-subtle)]">
                  <th className="py-3 px-4 min-w-[220px]">Description</th>
                  <th className="py-3 px-3 min-w-[190px]">Account</th>
                  <th className="py-3 px-3 w-20 text-right">Qty</th>
                  <th className="py-3 px-3 w-28 text-right">Unit cost</th>
                  <th className="py-3 px-3 w-20 text-right">Disc %</th>
                  {taxes.length > 0 && <th className="py-3 px-3 min-w-[130px]">Tax</th>}
                  <th className="py-3 px-4 w-28 text-right">Amount</th>
                  <th className="py-3 px-2 w-12">
                    <span className="sr-only">Remove</span>
                  </th>
                </tr>
              </thead>
              <tbody className="divide-y divide-[var(--border-subtle)] text-sm">
                {lines.map((line, idx) => {
                  const linked = line.itemId ? catalog.find((c) => c.id === line.itemId) : undefined;
                  return (
                    <tr key={line.key} className="align-middle">
                      <td className="py-2.5 px-4">
                        <div className="relative">
                          <input
                            type="text"
                            aria-label={`Line ${idx + 1} description`}
                            placeholder="e.g. Office rent, Printer paper"
                            value={line.description}
                            onChange={(e) => updateLine(idx, { description: e.target.value })}
                            className={`${cellClass} ${linked ? 'pr-[4.25rem]' : catalog.length > 0 ? 'pr-10' : ''}`}
                          />
                          {catalog.length > 0 && (
                            <div className="absolute right-1 top-1/2 -translate-y-1/2 flex items-center">
                              <button
                                type="button"
                                onClick={() => setPickerLine(idx)}
                                title={linked ? `Catalog item: ${linked.description}` : 'Pick from catalog'}
                                aria-label={linked ? `Line ${idx + 1} is catalog item ${linked.description}; change it` : `Pick line ${idx + 1} from catalog`}
                                className={`inline-flex h-7 w-7 items-center justify-center rounded-md transition focus:outline-none focus-visible:ring-2 focus-visible:ring-[var(--brand-ring)] ${
                                  linked ? 'text-[var(--brand-primary)] hover:bg-[var(--brand-subtle)]' : 'text-[var(--text-muted)] hover:text-[var(--text-main)] hover:bg-[var(--bg-surface-hover)]'
                                }`}
                              >
                                <Package className="w-4 h-4" />
                              </button>
                              {linked && (
                                <button
                                  type="button"
                                  onClick={() => updateLine(idx, {
                                    itemId: null,
                                    isStockItem: false,
                                    accountId: line.isStockItem ? defaultAccountId : line.accountId,
                                  })}
                                  title="Unlink from catalog"
                                  aria-label={`Unlink line ${idx + 1} from the catalog`}
                                  className="inline-flex h-7 w-7 items-center justify-center rounded-md text-[var(--text-muted)] transition hover:text-[var(--text-main)] hover:bg-[var(--bg-surface-hover)]"
                                >
                                  <Unlink className="w-4 h-4" />
                                </button>
                              )}
                            </div>
                          )}
                        </div>
                      </td>
                      <td className="py-2.5 px-3">
                        <select
                          aria-label={`Line ${idx + 1} account`}
                          value={line.accountId}
                          onChange={(e) => updateLine(idx, { accountId: e.target.value ? Number(e.target.value) : '' })}
                          disabled={line.isStockItem}
                          className={`${cellClass} disabled:opacity-60 disabled:cursor-not-allowed`}
                        >
                          <option value="">Vendor's default</option>
                          <optgroup label="Expenses">
                            {expenseAccounts.map((a) => (
                              <option key={a.id} value={a.id}>
                                {a.code} · {a.name}
                              </option>
                            ))}
                          </optgroup>
                          <optgroup label="Assets">
                            {assetAccounts.map((a) => (
                              <option key={a.id} value={a.id}>
                                {a.code} · {a.name}
                              </option>
                            ))}
                          </optgroup>
                        </select>
                        {line.isStockItem && <p className="mt-1 text-[10px] text-[var(--text-muted)]">Stock is always booked to Inventory.</p>}
                      </td>
                      <td className="py-2.5 px-3">
                        <input
                          type="number"
                          aria-label={`Line ${idx + 1} quantity`}
                          min="0"
                          step="any"
                          value={line.quantity}
                          onChange={(e) => updateLine(idx, { quantity: parseFloat(e.target.value) || 0 })}
                          className={`${cellClass} text-right`}
                        />
                      </td>
                      <td className="py-2.5 px-3">
                        <input
                          type="number"
                          aria-label={`Line ${idx + 1} unit cost`}
                          step="0.01"
                          value={line.amount}
                          onChange={(e) => updateLine(idx, { amount: parseFloat(e.target.value) || 0 })}
                          className={`${cellClass} text-right`}
                        />
                      </td>
                      <td className="py-2.5 px-3">
                        <input
                          type="number"
                          aria-label={`Line ${idx + 1} discount percent`}
                          min="0"
                          max="100"
                          step="0.5"
                          value={line.discount}
                          onChange={(e) => updateLine(idx, { discount: parseFloat(e.target.value) || 0 })}
                          className={`${cellClass} text-right`}
                        />
                      </td>
                      {taxes.length > 0 && (
                        <td className="py-2.5 px-3">
                          <select
                            aria-label={`Line ${idx + 1} tax`}
                            value={line.taxId}
                            onChange={(e) => updateLine(idx, { taxId: e.target.value ? Number(e.target.value) : '' })}
                            className={cellClass}
                          >
                            <option value="">No tax</option>
                            {taxes.map((t) => (
                              <option key={t.id} value={t.id}>
                                {t.taxName} ({t.rate}%)
                              </option>
                            ))}
                          </select>
                        </td>
                      )}
                      <td className="py-2.5 px-4 text-right font-medium font-mono text-[var(--text-main)] whitespace-nowrap">
                        {formatCurrency(lineSubtotal(line))}
                      </td>
                      <td className="py-2.5 px-2 text-center">
                        <button
                          type="button"
                          onClick={() => setLines((prev) => prev.filter((_, i) => i !== idx))}
                          disabled={lines.length <= 1}
                          aria-label={`Remove line ${idx + 1}`}
                          className="p-1.5 text-[var(--text-muted)] hover:text-rose-500 rounded-lg hover:bg-[var(--bg-surface-hover)] transition disabled:opacity-20"
                        >
                          <Trash2 className="w-4 h-4" />
                        </button>
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>

          <button
            type="button"
            onClick={() => setLines((prev) => [...prev, blankLine()])}
            className="mt-3 inline-flex items-center gap-1.5 px-3.5 py-2 text-xs font-semibold text-[var(--brand-primary)] bg-[var(--brand-subtle)] rounded-xl hover:opacity-85 transition"
          >
            <Plus className="w-3.5 h-3.5" />
            Add line
          </button>
        </div>

        <div className="grid grid-cols-1 lg:grid-cols-12 gap-8 border-t border-[var(--border-subtle)] pt-8">
          <div className="lg:col-span-7">
            <label htmlFor="bill-notes" className={labelClass}>
              Notes
            </label>
            <textarea
              id="bill-notes"
              rows={3}
              maxLength={2000}
              value={notes}
              onChange={(e) => setNotes(e.target.value)}
              placeholder="For your own records: what the bill is for, who approved the purchase..."
              className={fieldClass}
            />
          </div>
          <div className="lg:col-span-5 bg-[var(--bg-subtle)] border border-[var(--border-subtle)] rounded-2xl p-6 space-y-3">
            <div className="flex items-center justify-between text-sm text-[var(--text-secondary)]">
              <span>Subtotal</span>
              <span className="font-medium font-mono text-[var(--text-main)]">{formatCurrency(totals.subtotal)}</span>
            </div>
            <div className="flex items-center justify-between text-sm text-[var(--text-secondary)]">
              <span>Tax</span>
              <span className="font-medium font-mono text-[var(--text-main)]">{formatCurrency(totals.tax)}</span>
            </div>
            <div className="border-t border-[var(--border-subtle)] pt-3 flex items-center justify-between">
              <span className="text-base font-bold text-[var(--text-main)]">Total</span>
              <span className="text-xl font-bold font-mono text-[var(--brand-primary)]">{formatCurrency(totals.total)}</span>
            </div>
          </div>
        </div>
      </div>

      {!canApprove && (
        <p className="mt-4 text-xs text-[var(--text-muted)]">
          You can save bills as drafts. Someone with the "Post / Approve Bills" permission approves them.{' '}
          <Link to={listPath} className="text-[var(--brand-primary)] hover:underline">
            Back to bills
          </Link>
        </p>
      )}

      {isVendorPickerOpen && (
        <PartyPickerDialog
          role="vendor"
          parties={vendors.filter((v) => !v.archived || v.id === vendorId)}
          selectedId={vendorId}
          onPick={(picked) => {
            setVendorId(picked.id);
            if (picked.paymentTermId) applyTerm(picked.paymentTermId);
            setIsVendorPickerOpen(false);
          }}
          onAddNew={
            canManageVendors
              ? () => {
                  setIsVendorPickerOpen(false);
                  setIsAddingVendor(true);
                }
              : undefined
          }
          onClose={() => setIsVendorPickerOpen(false)}
        />
      )}

      {isAddingVendor && (
        <PartyFormDialog<Vendor>
          role="vendor"
          paymentTerms={paymentTerms}
          onClose={() => setIsAddingVendor(false)}
          onSaved={(saved) => {
            const option: CustomerOption = { id: saved.id, name: saved.name, email: saved.email ?? undefined, phone: saved.phone ?? undefined, paymentTermId: saved.paymentTermId ?? undefined, archived: saved.archived };
            setVendors((prev) => [option, ...prev]);
            setVendorId(saved.id);
            if (saved.paymentTermId) applyTerm(saved.paymentTermId);
            setIsAddingVendor(false);
          }}
        />
      )}

      {pickerItem && pickerLine !== null && (
        <CatalogPickerDialog
          items={catalog}
          priceLabel="Cost"
          selectedId={pickerItem.itemId}
          formatPrice={formatCurrency}
          onPick={(item) => {
            const isStockItem = item.trackStock === true;
            const inventoryAccountId = item.inventoryAccountId ?? accounts.find((account) => account.isInventoryAccount)?.id;
            updateLine(pickerLine, {
              itemId: item.id,
              description: item.description,
              amount: item.price ?? pickerItem.amount,
              isStockItem,
              accountId: isStockItem
                ? inventoryAccountId ?? ''
                : pickerItem.isStockItem
                  ? defaultAccountId
                  : pickerItem.accountId,
            });
            setPickerLine(null);
          }}
          onAddNew={
            canManageItems
              ? () => {
                  setNewItemLine(pickerLine);
                  setPickerLine(null);
                }
              : undefined
          }
          onClose={() => setPickerLine(null)}
        />
      )}

      {newItemLine !== null && (
        <ItemFormDialog
          showPrice={hasModule('sales')}
          showCost
          hasInventoryModule={hasModule('inventory')}
          currencySymbol={currencySymbol}
          onSaved={(item) => {
            const option: ItemOption = {
              id: item.id,
              code: item.code ?? undefined,
              description: item.name,
              price: item.cost ?? undefined,
              trackStock: item.trackStock,
              quantityOnHand: item.quantityOnHand,
              inventoryAccountId: item.inventoryAccountId,
            };
            setCatalog((prev) => [...prev, option].sort((a, b) => a.description.localeCompare(b.description)));
            if (lines[newItemLine]) {
              const inventoryAccountId = accounts.find((account) => account.isInventoryAccount)?.id;
              updateLine(newItemLine, {
                itemId: option.id,
                description: option.description,
                amount: option.price ?? lines[newItemLine].amount,
                isStockItem: option.trackStock === true,
                accountId: option.trackStock ? inventoryAccountId ?? '' : defaultAccountId,
              });
            }
            setNewItemLine(null);
          }}
          onClose={() => setNewItemLine(null)}
        />
      )}
    </div>
  );
};
