import React, { useCallback, useEffect, useMemo, useState } from 'react';
import { Link, useNavigate, useOutletContext, useParams, useSearchParams } from 'react-router-dom';
import { AlertTriangle, CheckCircle2, ChevronsUpDown, ExternalLink, Loader2, Package, Plus, RefreshCw, Save, ScanLine, Trash2, Unlink, UserPlus, Users, X } from 'lucide-react';
import { PageHeader, HeaderButton } from '../components/PageHeader';
import { PartyPickerDialog } from '../components/PartyPickerDialog';
import { PartyFormDialog } from '../components/PartyFormDialog';
import { CatalogPickerDialog } from '../components/CatalogPickerDialog';
import { ItemFormDialog } from '../components/ItemFormDialog';
import { ConfirmDialog } from '../components/ConfirmDialog';
import { usePermissions } from '../components/Gating';
import { DocumentCapturePanel } from '../components/document-capture/DocumentCapturePanel';
import {
  computeCaptureDiscrepancy,
  describeCaptureError,
  DocumentCaptureCapabilities,
  DocumentCaptureView,
  evaluateFinancialApplyGates,
  findSuggestedVendor,
  isValidIsoDate,
  MAX_REFERENCE_NO_LENGTH,
  MAX_VENDOR_INVOICE_NO_LENGTH,
  VendorCaptureFields,
} from '../components/document-capture/fields';
import { Bill, BillAccount, CatalogItem, CustomerOption, ItemOption, PaymentTermOption, TenantContextType, Vendor } from '../types';
import { useAuth } from '../auth/AuthContext';
import { currencySymbol as getCurrencySymbol } from '../lib/currency';
import { addBusinessDays, businessDaysBetween, isValidDate, localToday as today } from '../lib/businessDays';
import { archivedTaxMessage, firstArchivedTax, isSelectableTax, lineTaxChoices, taxChoiceLabel, WorkspaceTax } from '../lib/invoiceTax';
import { shortRef } from '../lib/shortRef';

interface LineDraft {
  /** Stable React key; the id is empty until a line is saved. */
  key: number;
  id: string;
  itemId: string | null;
  description: string;
  accountId: string | '';
  isStockItem: boolean;
  quantity: number;
  amount: number;
  discount: number;
  taxId: string | '';
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
  const { currentOrg, hasModule, canWrite } = useOutletContext<TenantContextType>();
  const { billId } = useParams<{ billId: string }>();
  const [searchParams] = useSearchParams();
  const navigate = useNavigate();
  const { authFetch } = useAuth();
  const { hasPermission } = usePermissions();
  const purchasesWritable = canWrite('purchases');
  const canApprove = hasPermission('purchases.bills.post') && purchasesWritable;
  const canManageVendors = hasPermission('purchases.vendors.manage') && purchasesWritable;
  const canManageItems = hasPermission('inventory.items.manage');
  const orgSlug = currentOrg.slug;
  const listPath = `/org/${orgSlug}/purchases`;
  const duplicateOf = isEdit ? null : searchParams.get('from');
  const presetVendor = isEdit ? null : searchParams.get('vendor') || null;

  // Ledger accounts on lines follow the Financials module, whatever the plan is called.
  const showLedgerAccounts = hasModule('financials');

  const [vendors, setVendors] = useState<CustomerOption[]>([]);
  const [paymentTerms, setPaymentTerms] = useState<PaymentTermOption[]>([]);
  const [accounts, setAccounts] = useState<BillAccount[]>([]);
  const [taxes, setTaxes] = useState<WorkspaceTax[]>([]);
  const [catalog, setCatalog] = useState<ItemOption[]>([]);
  const [isLoading, setIsLoading] = useState(true);

  const [vendorId, setVendorId] = useState<string | null>(presetVendor);
  const [vendorInvoiceNo, setVendorInvoiceNo] = useState('');
  const [referenceNo, setReferenceNo] = useState('');
  const [date, setDate] = useState(today());
  const [paymentTermId, setPaymentTermId] = useState<string>('');
  const [dueDate, setDueDate] = useState('');
  const [notes, setNotes] = useState('');
  const [lines, setLines] = useState<LineDraft[]>([]);
  const [duplicatedFrom, setDuplicatedFrom] = useState<string | null>(null);

  const [saving, setSaving] = useState<'draft' | 'approve' | null>(null);
  const [duplicateConfirm, setDuplicateConfirm] = useState<{ approve: boolean; message: string } | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [isVendorPickerOpen, setIsVendorPickerOpen] = useState(false);
  const [isAddingVendor, setIsAddingVendor] = useState(false);
  const [pickerLine, setPickerLine] = useState<number | null>(null);
  const [newItemLine, setNewItemLine] = useState<number | null>(null);

  // Document capture state (shared panel, reconciliation, vendor suggestion, draft merge, post-save link)
  const [captureAvailable, setCaptureAvailable] = useState(false);
  const [showCapturePanel, setShowCapturePanel] = useState(false);
  const [acceptedCapture, setAcceptedCapture] = useState<DocumentCaptureView | null>(null);
  const [ambiguousCurrencyConfirmed, setAmbiguousCurrencyConfirmed] = useState(false);
  const [suggestedVendor, setSuggestedVendor] = useState<CustomerOption | null>(null);
  const [pendingDueDate, setPendingDueDate] = useState<string | null>(null);
  const [pendingLineMerge, setPendingLineMerge] = useState<LineDraft[] | null>(null);
  const [ackSnapshot, setAckSnapshot] = useState<{
    captureId: string;
    sourceGrandTotal: number | null;
    formTotal: number;
  } | null>(null);
  const [pendingLink, setPendingLink] = useState<{
    captureId: string;
    billId: string;
    error: string;
  } | null>(null);
  const [retryingLink, setRetryingLink] = useState(false);

  const currencySymbol = getCurrencySymbol(currentOrg.currency);
  const formatCurrency = useCallback(
    (val: number) => `${currencySymbol}${val.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`,
    [currencySymbol]
  );

  // New lines are booked to Purchase A/C unless another account is picked, the same default the server applies.
  const defaultAccountId = useMemo(() => accounts.find((a) => a.code === '50200')?.id ?? '', [accounts]);
  const blankLine = useCallback(
    (): LineDraft => ({ key: nextKey++, id: '', itemId: null, description: '', accountId: defaultAccountId, isStockItem: false, quantity: 1, amount: 0, discount: 0, taxId: '' }),
    [defaultAccountId]
  );

  const toLine = (line: Bill['lines'][number], keepId: boolean): LineDraft => ({
    key: nextKey++,
    id: keepId ? line.id : '',
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
          setTaxes((data?.taxes ?? []) as WorkspaceTax[]);
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
          setLines([{ key: nextKey++, id: '', itemId: null, description: '', accountId: defaultAccount, isStockItem: false, quantity: 1, amount: 0, discount: 0, taxId: '' }]);
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
  const applyTerm = (termId: string | '', from = date) => {
    setPaymentTermId(termId);
    const term = paymentTerms.find((t) => t.id === termId);
    if (term && isValidDate(from)) setDueDate(addBusinessDays(from, term.dueAfterDays ?? 0));
  };
  const dueInBusinessDays = isValidDate(date) && isValidDate(dueDate) ? businessDaysBetween(date, dueDate) : undefined;

  const updateLine = (index: number, changes: Partial<LineDraft>) =>
    setLines((prev) => prev.map((line, i) => (i === index ? { ...line, ...changes } : line)));

  const taxRate = (taxId: string | '') => taxes.find((t) => t.id === taxId)?.rate ?? 0;
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

  // Discover document-capture capabilities for this workspace; keep new-scan controls hidden when unavailable.
  useEffect(() => {
    let cancelled = false;
    setCaptureAvailable(false);
    setShowCapturePanel(false);
    setAcceptedCapture(null);
    setSuggestedVendor(null);
    setPendingDueDate(null);
    setPendingLineMerge(null);
    setAckSnapshot(null);
    setPendingLink(null);
    if (!purchasesWritable) return;
    (async () => {
      try {
        const res = await authFetch('/api/document-captures/capabilities');
        if (cancelled || !res.ok) return;
        const caps: DocumentCaptureCapabilities = await res.json();
        if (cancelled) return;
        const hasVendorKind =
          caps.enabled &&
          caps.providerAvailable &&
          caps.kinds.some((k) => k === 'expense-receipt' || k === 'purchase-invoice');
        setCaptureAvailable(hasVendorKind);
      } catch {
        if (!cancelled) setCaptureAvailable(false);
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [authFetch, orgSlug, purchasesWritable]);

  const capturedFields = useMemo<VendorCaptureFields | null>(() => {
    if (!acceptedCapture) return null;
    return ((acceptedCapture.acceptedFields ?? acceptedCapture.fields) as VendorCaptureFields | null) ?? null;
  }, [acceptedCapture]);

  const captureGates = useMemo(
    () =>
      acceptedCapture
        ? evaluateFinancialApplyGates(acceptedCapture, currentOrg.currency, ambiguousCurrencyConfirmed)
        : null,
    [acceptedCapture, currentOrg.currency, ambiguousCurrencyConfirmed]
  );

  const captureDiscrepancy = useMemo(
    () => computeCaptureDiscrepancy(capturedFields?.sourceTotals ?? null, totals.total),
    [capturedFields, totals.total]
  );

  // Acknowledgement is bound to (captureId, sourceGrandTotal, current form total). Any edit that changes formTotal invalidates it.
  const discrepancyAcknowledged = Boolean(
    acceptedCapture &&
      ackSnapshot &&
      ackSnapshot.captureId === acceptedCapture.id &&
      ackSnapshot.sourceGrandTotal === captureDiscrepancy.sourceGrandTotal &&
      ackSnapshot.formTotal === captureDiscrepancy.formTotal
  );

  const mapCapturedLines = useCallback(
    (fields: VendorCaptureFields): LineDraft[] => {
      const sourceLines = fields.lines ?? [];
      return sourceLines
        .filter((l) => (l.description && l.description.trim() !== '') || (l.unitAmount && l.unitAmount.trim() !== ''))
        .map((l) => ({
          key: nextKey++,
          id: '',
          itemId: null,
          description: (l.description ?? '').trim(),
          accountId: defaultAccountId,
          isStockItem: false,
          quantity: Number(l.quantity) > 0 ? Number(l.quantity) : 1,
          amount: Number(l.unitAmount) || 0,
          discount: l.discount ? Number(l.discount) || 0 : 0,
          taxId: '',
        }));
    },
    [defaultAccountId]
  );

  const hasPopulatedLines = useMemo(
    () => lines.some((l) => l.itemId !== null || l.description.trim() !== '' || Number(l.amount) !== 0),
    [lines]
  );

  const applyCapturedMonetaryLines = useCallback(
    (fields: VendorCaptureFields) => {
      const mapped = mapCapturedLines(fields);
      if (mapped.length === 0) return;
      if (hasPopulatedLines) {
        setPendingLineMerge(mapped);
      } else {
        setLines(mapped);
        setPendingLineMerge(null);
      }
    },
    [hasPopulatedLines, mapCapturedLines]
  );

  const handleCaptureAccepted = useCallback(
    (canonical: DocumentCaptureView) => {
      setAcceptedCapture(canonical);
      setShowCapturePanel(false);
      setAmbiguousCurrencyConfirmed(false);
      setAckSnapshot(null);
      setPendingLink(null);
      setPendingDueDate(null);
      setPendingLineMerge(null);

      const fields = ((canonical.acceptedFields ?? canonical.fields) as VendorCaptureFields | null) ?? {};
      const gates = evaluateFinancialApplyGates(canonical, currentOrg.currency, false);

      // Exact-name vendor suggestion requires user confirmation and never creates a vendor.
      const matched = findSuggestedVendor(fields.vendorName, vendors, canonical.suggestedVendorId);
      setSuggestedVendor(matched && matched.id !== vendorId ? matched : null);

      if (!gates.canApplyNonMonetary) {
        return;
      }

      if (fields.vendorInvoiceNo?.trim()) {
        setVendorInvoiceNo(fields.vendorInvoiceNo.trim().slice(0, MAX_VENDOR_INVOICE_NO_LENGTH));
      }
      if (fields.referenceNo?.trim()) {
        setReferenceNo(fields.referenceNo.trim().slice(0, MAX_REFERENCE_NO_LENGTH));
      }
      if (fields.date && isValidIsoDate(fields.date)) {
        setDate(fields.date);
      }
      if (fields.dueDate && isValidIsoDate(fields.dueDate)) {
        // Never silently replace an existing payment term.
        if (paymentTermId === '') {
          setDueDate(fields.dueDate);
        } else {
          setPendingDueDate(fields.dueDate);
        }
      }
      if (fields.notes?.trim()) {
        setNotes((prev) => (prev.trim() ? prev : fields.notes!.trim()));
      }

      if (gates.canApplyMonetary) {
        applyCapturedMonetaryLines(fields);
      }
    },
    [applyCapturedMonetaryLines, currentOrg.currency, paymentTermId, vendorId, vendors]
  );

  const linkCaptureToBill = useCallback(
    async (captureId: string, savedBillId: string) => {
      setRetryingLink(true);
      try {
        const linkRes = await authFetch(`/api/document-captures/${encodeURIComponent(captureId)}/links`, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ targetType: 'bill', targetId: savedBillId }),
        });
        if (!linkRes.ok) {
          const body = await linkRes.json().catch(() => null);
          const presentation = describeCaptureError(body?.code, linkRes.status);
          setPendingLink({ captureId, billId: savedBillId, error: presentation.message });
          return false;
        }
        setPendingLink(null);
        navigate(`/org/${orgSlug}/purchases/${savedBillId}`);
        return true;
      } catch {
        setPendingLink({
          captureId,
          billId: savedBillId,
          error: 'Could not attach the source document right now. Retry attachment without saving the bill again.',
        });
        return false;
      } finally {
        setRetryingLink(false);
      }
    },
    [authFetch, navigate, orgSlug]
  );

  const handleSave = async (approve: boolean, confirmDuplicate = false) => {
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
    if (approve && acceptedCapture && captureDiscrepancy.hasDiscrepancy && !discrepancyAcknowledged) {
      setError('The bill total differs from the captured source total. Correct the lines or check the reconciliation acknowledgement before approving.');
      return;
    }
    const archived = firstArchivedTax(taxes, lines.map((line) => line.taxId));
    if (archived) {
      setError(archivedTaxMessage(archived.taxName));
      return;
    }

    setSaving(approve ? 'approve' : 'draft');
    try {
      const res = await authFetch('/api/Purchasing/SaveBill', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          id: isEdit ? billId : '',
          vendorId,
          vendorInvoiceNo: vendorInvoiceNo.trim() || null,
          referenceNo: referenceNo.trim() || null,
          date,
          dueDate: dueDate || null,
          paymentTermId: paymentTermId || null,
          notes: notes.trim() || null,
          status: approve ? 'Open' : 'Draft',
          confirmDuplicate,
          captureReview: acceptedCapture
            ? {
                captureId: acceptedCapture.id,
                sourceGrandTotal: captureDiscrepancy.sourceGrandTotal,
                formTotal: captureDiscrepancy.formTotal,
                discrepancyAcknowledged,
                missingEvidenceAcknowledged: Boolean(pendingLink),
              }
            : undefined,
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
        if (res.status === 409 && data?.code === 'possible_duplicate') {
          setDuplicateConfirm({
            approve,
            message: data?.message || 'A bill from this vendor with the same date and total has already been entered.',
          });
          setSaving(null);
          return;
        }
        if (res.status === 409 && data?.code === 'DOCUMENT_REVIEW_REQUIRED') {
          setError(data?.detail || 'The bill total differs from the captured source total. Correct the bill or acknowledge the difference before approving.');
          setSaving(null);
          return;
        }
        throw new Error(
          res.status === 403
            ? approve
              ? "You can save this bill as a draft, but approving bills takes the 'Post / Approve Bills' permission."
              : "You don't have permission to enter bills."
            : data?.message || data?.detail || 'The bill could not be saved.'
        );
      }
      const savedBill = data as Bill;
      if (acceptedCapture && acceptedCapture.link?.targetId !== savedBill.id) {
        const linked = await linkCaptureToBill(acceptedCapture.id, savedBill.id);
        if (!linked) {
          setSaving(null);
          return;
        }
        return;
      }
      navigate(`/org/${orgSlug}/purchases/${savedBill.id}`);
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
  const selectableTaxCount = taxes.filter(isSelectableTax).length;

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
            {purchasesWritable && captureAvailable && (
              <HeaderButton
                label={showCapturePanel ? 'Hide scan' : 'Scan receipt / bill'}
                icon={ScanLine}
                compact
                disabled={!!saving}
                onClick={() => setShowCapturePanel((prev) => !prev)}
              />
            )}
            {purchasesWritable && (
              <HeaderButton label="Save draft" icon={Save} compact busy={saving === 'draft'} disabled={!!saving || !!pendingLink} onClick={() => handleSave(false)} />
            )}
            {/* Approving takes its own permission; without it, bills are saved as drafts for someone else to approve. */}
            {canApprove && (
              <HeaderButton label="Save & approve" icon={CheckCircle2} variant="primary" busy={saving === 'approve'} disabled={!!saving || !!pendingLink} onClick={() => handleSave(true)} />
            )}
          </>
        }
      />

      {showCapturePanel && purchasesWritable && (
        <div className="mb-6">
          <DocumentCapturePanel
            kind={['expense-receipt', 'purchase-invoice']}
            orgSlug={orgSlug}
            orgCurrency={currentOrg.currency}
            scopeKey={isEdit ? billId : 'new'}
            disabled={!!saving}
            onAccepted={handleCaptureAccepted}
            onOpenLinkedRecord={(link) => {
              if (link.targetType === 'bill') {
                navigate(`/org/${orgSlug}/purchases/${link.targetId}`);
              }
            }}
            onCancel={() => setShowCapturePanel(false)}
          />
        </div>
      )}

      {/* Pending evidence link recovery after a successful bill save — never repeats SaveBill */}
      {pendingLink && (
        <div role="alert" className="mb-6 rounded-2xl border border-amber-500/30 bg-amber-500/10 p-4 text-sm text-amber-800 dark:text-amber-300 space-y-2">
          <div className="flex items-start gap-2.5">
            <AlertTriangle className="w-5 h-5 shrink-0 mt-0.5" />
            <div className="flex-1 space-y-1">
              <div className="font-semibold">Bill saved ({pendingLink.billId}), but attaching the source document failed.</div>
              <p className="text-xs">{pendingLink.error}</p>
            </div>
          </div>
          <div className="flex flex-wrap items-center gap-2 pl-7">
            <button
              type="button"
              disabled={retryingLink}
              onClick={() => void linkCaptureToBill(pendingLink.captureId, pendingLink.billId)}
              className="inline-flex items-center gap-1.5 px-3 py-1.5 text-xs font-semibold rounded-lg bg-[var(--bg-surface)] border border-[var(--border-subtle)] text-[var(--text-main)] hover:bg-[var(--bg-surface-hover)] transition disabled:opacity-50"
            >
              <RefreshCw className={`w-3.5 h-3.5 ${retryingLink ? 'animate-spin' : ''}`} />
              Retry attach evidence
            </button>
            <Link
              to={`/org/${orgSlug}/purchases/${pendingLink.billId}`}
              className="inline-flex items-center gap-1 text-xs font-semibold underline"
            >
              <ExternalLink className="w-3.5 h-3.5" />
              Open saved bill
            </Link>
          </div>
        </div>
      )}

      {/* Vendor suggestion confirmation (exact-name match only, never auto-creates a vendor) */}
      {suggestedVendor && (
        <div role="status" className="mb-4 rounded-xl border border-[var(--brand-ring)]/40 bg-[var(--brand-subtle)]/40 p-3.5 flex flex-wrap items-center justify-between gap-2 text-xs">
          <span>
            Suggested vendor match from scan: <strong className="text-[var(--text-main)]">{suggestedVendor.name}</strong>
          </span>
          <div className="flex items-center gap-2">
            <button
              type="button"
              onClick={() => {
                setVendorId(suggestedVendor.id);
                setSuggestedVendor(null);
              }}
              className="px-3 py-1.5 font-semibold rounded-lg bg-[var(--brand-primary)] text-white hover:opacity-90 transition"
            >
              Confirm vendor
            </button>
            <button
              type="button"
              onClick={() => setSuggestedVendor(null)}
              className="px-2.5 py-1.5 font-semibold rounded-lg border border-[var(--border-subtle)] bg-[var(--bg-surface)] text-[var(--text-secondary)]"
            >
              Dismiss
            </button>
          </div>
        </div>
      )}

      {/* Explicit due-date / payment-term choice when a payment term was already selected */}
      {pendingDueDate && (
        <div role="status" className="mb-4 rounded-xl border border-amber-500/30 bg-amber-500/10 p-3.5 flex flex-wrap items-center justify-between gap-2 text-xs text-amber-800 dark:text-amber-300">
          <span>
            Captured due date is <strong>{pendingDueDate}</strong>, but this bill already uses payment terms. Keep existing terms or switch to the captured due date?
          </span>
          <div className="flex items-center gap-2">
            <button
              type="button"
              onClick={() => {
                setPaymentTermId('');
                setDueDate(pendingDueDate);
                setPendingDueDate(null);
              }}
              className="px-3 py-1.5 font-semibold rounded-lg bg-[var(--bg-surface)] border border-[var(--border-subtle)] text-[var(--text-main)]"
            >
              Use captured due date
            </button>
            <button
              type="button"
              onClick={() => setPendingDueDate(null)}
              className="px-2.5 py-1.5 font-semibold rounded-lg text-[var(--text-secondary)]"
            >
              Keep existing terms
            </button>
          </div>
        </div>
      )}

      {/* Explicit merge choice when applying captured lines to a populated draft */}
      {pendingLineMerge && (
        <div role="status" className="mb-4 rounded-xl border border-amber-500/30 bg-amber-500/10 p-3.5 flex flex-wrap items-center justify-between gap-2 text-xs text-amber-800 dark:text-amber-300">
          <span>
            This draft already has line items. Choose how to apply the {pendingLineMerge.length} captured line{pendingLineMerge.length === 1 ? '' : 's'}:
          </span>
          <div className="flex flex-wrap items-center gap-2">
            <button
              type="button"
              onClick={() => setPendingLineMerge(null)}
              className="px-3 py-1.5 font-semibold rounded-lg bg-[var(--bg-surface)] border border-[var(--border-subtle)] text-[var(--text-main)]"
            >
              Keep existing lines
            </button>
            <button
              type="button"
              onClick={() => {
                setLines((prev) => [...prev, ...pendingLineMerge]);
                setPendingLineMerge(null);
              }}
              className="px-3 py-1.5 font-semibold rounded-lg bg-[var(--bg-surface)] border border-[var(--border-subtle)] text-[var(--text-main)]"
            >
              Append captured lines
            </button>
            <button
              type="button"
              onClick={() => {
                setLines(pendingLineMerge);
                setPendingLineMerge(null);
              }}
              className="px-3 py-1.5 font-semibold rounded-lg bg-amber-600 text-white hover:opacity-90 transition"
            >
              Replace existing lines
            </button>
          </div>
        </div>
      )}

      {/* Persistent reconciliation, currency/class gate, and paid-receipt guidance card */}
      {acceptedCapture && capturedFields && captureGates && (
        <section
          aria-label="Captured document review"
          className="mb-6 rounded-2xl border border-[var(--border-subtle)] bg-[var(--bg-surface)] p-5 shadow-sm space-y-3 text-xs"
        >
          <div className="flex flex-wrap items-center justify-between gap-2">
            <div className="font-semibold text-sm text-[var(--text-main)]">
              Captured source review ({acceptedCapture.id})
            </div>
            <div className="flex flex-wrap items-center gap-2 text-[11px] text-[var(--text-secondary)]">
              <span>Source currency: <strong className="text-[var(--text-main)]">{captureGates.sourceCurrency ?? currentOrg.currency ?? '—'}</strong></span>
              <span>·</span>
              <span>
                Price basis:{' '}
                <strong className="text-[var(--text-main)]">
                  {captureGates.priceBasis === 'inclusive'
                    ? 'Tax-inclusive'
                    : captureGates.priceBasis === 'exclusive'
                      ? 'Tax-exclusive'
                      : 'Unknown'}
                </strong>
              </span>
            </div>
          </div>

          {/* Document-class / foreign-currency / ambiguous-currency notices */}
          {captureGates.reasons.length > 0 && (
            <div className="rounded-xl border border-amber-500/30 bg-amber-500/10 p-3 space-y-1.5 text-amber-800 dark:text-amber-300">
              {captureGates.reasons.map((reason, idx) => (
                <p key={idx}>{reason}</p>
              ))}
              {captureGates.unsupportedClass && (
                <p>
                  <Link to={`/org/${orgSlug}/purchases/credits`} className="font-semibold underline">
                    Go to Supplier Credits
                  </Link>
                </p>
              )}
              {captureGates.ambiguousCurrency && !ambiguousCurrencyConfirmed && captureGates.canApplyNonMonetary && (
                <button
                  type="button"
                  onClick={() => {
                    setAmbiguousCurrencyConfirmed(true);
                    applyCapturedMonetaryLines(capturedFields);
                  }}
                  className="mt-1 px-3 py-1.5 font-semibold rounded-lg bg-[var(--bg-surface)] border border-[var(--border-subtle)] text-[var(--text-main)]"
                >
                  Confirm amounts are in {currentOrg.currency || 'workspace currency'} and apply lines
                </button>
              )}
            </div>
          )}

          {/* Tax-inclusive guidance */}
          {captureGates.priceBasis === 'inclusive' && (
            <div className="rounded-xl border border-amber-500/25 bg-amber-500/10 p-3 text-amber-800 dark:text-amber-300">
              Source amounts include tax. Adjust unit costs to tax-exclusive amounts before selecting a tax rate so tax is not added a second time.
            </div>
          )}

          {/* Paid receipt / payment hint guidance (CFO3) */}
          {(acceptedCapture.kind === 'expense-receipt' ||
            capturedFields.paymentHints?.paymentStatus === 'paid' ||
            capturedFields.paymentHints?.paymentStatus === 'partial') && (
            <div className="rounded-xl border border-sky-500/25 bg-sky-500/10 p-3 text-sky-900 dark:text-sky-200 space-y-1">
              <div className="font-semibold">
                Receipt settlement notice
                {capturedFields.paymentHints?.paymentStatus
                  ? ` (hint: ${capturedFields.paymentHints.paymentStatus}${capturedFields.paymentHints.amountPaid ? `, ${capturedFields.paymentHints.amountPaid}` : ''})`
                  : ''}
              </div>
              <p>
                Approving this bill records what you owe and does not record a payment or settle it. If this receipt was already paid, record the payment via <strong>Pay bill</strong> after approving, or match the existing bank transaction in{' '}
                <Link to={`/org/${orgSlug}/banking`} className="underline font-semibold">
                  Banking
                </Link>{' '}
                to avoid recording a duplicate expense.
              </p>
            </div>
          )}

          {/* Source vs form totals comparison */}
          <div className="grid grid-cols-1 sm:grid-cols-3 gap-3 rounded-xl border border-[var(--border-subtle)] bg-[var(--bg-subtle)] p-3">
            <div>
              <div className="text-[var(--text-muted)]">Source totals</div>
              <div className="font-mono text-[var(--text-main)] mt-0.5">
                Subtotal: {capturedFields.sourceTotals?.subtotal ?? '—'} · Tax: {capturedFields.sourceTotals?.taxTotal ?? '—'}
              </div>
              <div className="font-mono font-semibold text-[var(--text-main)]">
                Source total: {capturedFields.sourceTotals?.grandTotal ?? '—'}
              </div>
            </div>
            <div>
              <div className="text-[var(--text-muted)]">Current bill form</div>
              <div className="font-mono text-[var(--text-main)] mt-0.5">
                Subtotal: {formatCurrency(totals.subtotal)} · Tax: {formatCurrency(totals.tax)}
              </div>
              <div className="font-mono font-semibold text-[var(--text-main)]">
                Form total: {formatCurrency(totals.total)}
              </div>
            </div>
            <div>
              <div className="text-[var(--text-muted)]">Difference</div>
              <div
                className={`font-mono font-semibold mt-0.5 ${
                  captureDiscrepancy.hasDiscrepancy ? 'text-rose-600 dark:text-rose-400' : 'text-emerald-600 dark:text-emerald-400'
                }`}
              >
                {captureDiscrepancy.difference === null
                  ? 'No source total'
                  : captureDiscrepancy.hasDiscrepancy
                    ? `${captureDiscrepancy.difference > 0 ? '+' : ''}${formatCurrency(captureDiscrepancy.difference)}`
                    : 'Matches source total'}
              </div>
            </div>
          </div>

          {/* Discrepancy acknowledgement bound to current form values */}
          {captureDiscrepancy.hasDiscrepancy && (
            <label className="flex items-start gap-2.5 rounded-xl border border-rose-500/25 bg-rose-500/10 p-3 text-rose-700 dark:text-rose-300 cursor-pointer">
              <input
                type="checkbox"
                checked={discrepancyAcknowledged}
                onChange={(e) => {
                  if (e.target.checked) {
                    setAckSnapshot({
                      captureId: acceptedCapture.id,
                      sourceGrandTotal: captureDiscrepancy.sourceGrandTotal,
                      formTotal: captureDiscrepancy.formTotal,
                    });
                  } else {
                    setAckSnapshot(null);
                  }
                }}
                className="mt-0.5 rounded"
              />
              <span>
                I have reviewed the difference between the captured source total ({capturedFields.sourceTotals?.grandTotal}) and the current bill total ({formatCurrency(totals.total)}) and acknowledge it for approval. (Saving a draft does not require acknowledgement; changing amounts resets this acknowledgement.)
              </span>
            </label>
          )}
        </section>
      )}

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
              <select id="bill-terms" value={paymentTermId} onChange={(e) => applyTerm(e.target.value)} className={fieldClass}>
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
            <h3 className="text-xs font-semibold uppercase tracking-wider text-[var(--text-muted)]">
              Line Items
            </h3>
            {catalog.length > 0 && (
              <span className="text-xs text-[var(--text-muted)]">
                Type a description, or pick an item with <Package className="inline w-3.5 h-3.5 -mt-0.5" />
              </span>
            )}
          </div>

          <div className="overflow-x-auto border border-[var(--border-subtle)] rounded-xl bg-[var(--bg-surface)]">
            <table className="w-full text-left border-collapse">
              <thead>
                <tr className="bg-[var(--bg-subtle)] text-xs font-semibold text-[var(--text-muted)] uppercase tracking-wider border-b border-[var(--border-subtle)]">
                  <th className="py-3 px-4 min-w-[260px]">Item & Description</th>
                  <th className="py-3 px-3 w-20 text-right">Qty</th>
                  <th className="py-3 px-3 w-28 text-right">Unit cost</th>
                  <th className="py-3 px-3 w-20 text-right">Disc %</th>
                  <th className="py-3 px-3 w-40 text-right">
                    <span className="inline-flex items-center justify-end gap-2">
                      Tax
                      {selectableTaxCount === 0 && hasPermission('admin.taxes.manage') && (
                        <Link to={`/org/${orgSlug}/settings?tab=taxes`} className="normal-case tracking-normal font-semibold text-[var(--brand-primary)]">Manage taxes</Link>
                      )}
                    </span>
                  </th>
                  <th className="py-3 px-4 w-28 text-right">Line Total</th>
                  <th className="py-3 px-2 w-12 text-center"></th>
                </tr>
              </thead>
              <tbody className="divide-y divide-[var(--border-subtle)] text-sm">
                {lines.map((line, idx) => {
                  const linked = line.itemId ? catalog.find((c) => c.id === line.itemId) : undefined;
                  return (
                    <tr key={line.key} className="align-middle hover:bg-[var(--bg-surface-hover)]/40 transition">
                      <td className="py-2.5 px-4">
                        <div className="relative">
                          <input
                            type="text"
                            aria-label={`Line ${idx + 1} description`}
                            placeholder="Description (e.g. Office rent, Printer paper)"
                            value={line.description}
                            onChange={(e) => updateLine(idx, { description: e.target.value })}
                            className={`w-full h-9 pl-3 ${linked ? 'pr-[4.25rem]' : catalog.length > 0 ? 'pr-10' : 'pr-3'} text-sm bg-[var(--bg-subtle)] border border-[var(--border-subtle)] rounded-lg focus:ring-2 focus:ring-[var(--brand-ring)] focus:outline-none text-[var(--text-main)] placeholder-[var(--text-muted)] transition`}
                          />
                          {catalog.length > 0 && (
                            <div className="absolute right-1 top-1/2 -translate-y-1/2 flex items-center">
                              <button
                                type="button"
                                onClick={() => setPickerLine(idx)}
                                title={linked ? `Catalog item: ${linked.code ? `${linked.code} · ` : ''}${linked.description}` : 'Pick from catalog'}
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
                                  className="inline-flex h-7 w-7 items-center justify-center rounded-md text-[var(--text-muted)] transition hover:text-[var(--text-main)] hover:bg-[var(--bg-surface-hover)] focus:outline-none focus-visible:ring-2 focus-visible:ring-[var(--brand-ring)]"
                                >
                                  <Unlink className="w-4 h-4" />
                                </button>
                              )}
                            </div>
                          )}
                        </div>
                        {showLedgerAccounts && (
                          line.isStockItem ? (
                            <p className="mt-1 text-[11px] text-[var(--text-muted)] truncate">
                              Stock is booked to Inventory
                            </p>
                          ) : (
                            <div className="mt-1.5 flex items-center gap-1.5">
                              <span className="text-[11px] text-[var(--text-muted)] whitespace-nowrap">Account:</span>
                              <select
                                aria-label={`Line ${idx + 1} account`}
                                value={line.accountId}
                                onChange={(e) => updateLine(idx, { accountId: e.target.value })}
                                className="h-6 px-1.5 text-xs bg-[var(--bg-subtle)] border border-[var(--border-subtle)] rounded text-[var(--text-muted)] hover:text-[var(--text-main)] focus:text-[var(--text-main)] focus:ring-1 focus:ring-[var(--brand-ring)] focus:outline-none transition max-w-[220px]"
                              >
                                <option value="">Default (50200 · Purchase A/C)</option>
                                {line.accountId && !accounts.some((a) => a.id === line.accountId) && (
                                  <option value={line.accountId}>
                                    Account {shortRef(line.accountId)}
                                  </option>
                                )}
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
                            </div>
                          )
                        )}
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
                      <td className="py-2.5 px-3">
                        <select
                          aria-label={`Line ${idx + 1} tax`}
                          value={line.taxId}
                          onChange={(e) => updateLine(idx, { taxId: e.target.value })}
                          className={cellClass}
                        >
                          <option value="">No tax</option>
                          {lineTaxChoices(taxes, line.taxId).map((t) => (
                            <option key={t.id} value={t.id}>
                              {taxChoiceLabel(t)}
                            </option>
                          ))}
                        </select>
                      </td>
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

      {duplicateConfirm && (
        <ConfirmDialog
          title="Possible duplicate bill"
          message={`${duplicateConfirm.message} Do you want to save anyway?`}
          confirmLabel="Save anyway"
          busy={!!saving}
          onConfirm={() => {
            const approve = duplicateConfirm.approve;
            setDuplicateConfirm(null);
            handleSave(approve, true);
          }}
          onCancel={() => setDuplicateConfirm(null)}
        />
      )}
    </div>
  );
};
