import React, { useState, useEffect, useMemo, useCallback } from 'react';
import { useOutletContext, useParams, useNavigate, Link } from 'react-router-dom';
import {
  ArrowLeft,
  Plus,
  Trash2,
  Save,
  Send,
  Building2,
  UserPlus,
  X,
  AlertTriangle,
  CheckCircle2,
  Loader2,
} from 'lucide-react';
import {
  TenantContextType,
  Invoice,
  InvoiceLineItem,
  CustomerOption,
  PaymentTermOption,
  ItemOption,
} from '../types';
import { useAuth } from '../auth/AuthContext';
import { currencySymbol as getCurrencySymbol } from '../lib/currency';

interface InvoiceEditProps {
  isEdit?: boolean;
}

interface FormLineItem {
  id?: number;
  itemId?: number | null;
  itemDescription: string;
  quantity: number;
  amount: number;
  discount: number;
  taxRate: number;
}

export const InvoiceEdit: React.FC<InvoiceEditProps> = ({ isEdit = false }) => {
  const { currentOrg } = useOutletContext<TenantContextType>();
  const { authFetch } = useAuth();
  const { orgSlug, invoiceId } = useParams<{ orgSlug: string; invoiceId?: string }>();
  const navigate = useNavigate();

  // Reference data
  const [customers, setCustomers] = useState<CustomerOption[]>([]);
  const [paymentTerms, setPaymentTerms] = useState<PaymentTermOption[]>([]);
  const [catalogItems, setCatalogItems] = useState<ItemOption[]>([]);
  const [isLoadingRefs, setIsLoadingRefs] = useState<boolean>(true);

  // Form State
  const [selectedCustomerId, setSelectedCustomerId] = useState<number | ''>('');
  const [invoiceNo, setInvoiceNo] = useState<string>('');
  const [referenceNo, setReferenceNo] = useState<string>('');
  const [invoiceDate, setInvoiceDate] = useState<string>(
    new Date().toISOString().split('T')[0]
  );
  const [paymentTermId, setPaymentTermId] = useState<number | ''>('');
  const [dueDate, setDueDate] = useState<string>('');
  const [notes, setNotes] = useState<string>('Thank you for your business!');
  const [paymentInstructions, setPaymentInstructions] = useState<string>(
    'Please send payment via bank transfer or check upon receipt.'
  );

  // Line items state
  const [lines, setLines] = useState<FormLineItem[]>([
    {
      itemDescription: '',
      quantity: 1,
      amount: 0,
      discount: 0,
      taxRate: 0,
    },
  ]);

  // UI state
  const [isSavingDraft, setIsSavingDraft] = useState<boolean>(false);
  const [isSavingIssue, setIsSavingIssue] = useState<boolean>(false);
  const [errorBanner, setErrorBanner] = useState<string | null>(null);
  const [successBanner, setSuccessBanner] = useState<string | null>(null);

  // Add Customer Modal State
  const [isAddCustomerOpen, setIsAddCustomerOpen] = useState<boolean>(false);
  const [newCustomerName, setNewCustomerName] = useState('');
  const [newCustomerEmail, setNewCustomerEmail] = useState('');
  const [newCustomerPhone, setNewCustomerPhone] = useState('');
  const [newCustomerStreet, setNewCustomerStreet] = useState('');
  const [newCustomerProvince, setNewCustomerProvince] = useState('');
  const [newCustomerPostal, setNewCustomerPostal] = useState('');
  const [newCustomerCountry, setNewCustomerCountry] = useState('');
  const [newCustomerTermId, setNewCustomerTermId] = useState<number | ''>('');
  const [isSubmittingCustomer, setIsSubmittingCustomer] = useState(false);
  const [customerModalError, setCustomerModalError] = useState<string | null>(null);

  // Currency
  const currencySymbol = getCurrencySymbol(currentOrg.currency);

  const formatCurrency = useCallback(
    (val: number) => {
      return `${currencySymbol}${val.toLocaleString(undefined, {
        minimumFractionDigits: 2,
        maximumFractionDigits: 2,
      })}`;
    },
    [currencySymbol]
  );

  // Load reference data
  const loadReferenceData = useCallback(async () => {
    setIsLoadingRefs(true);
    try {
      const [custRes, termsRes, itemsRes] = await Promise.all([
        authFetch('/api/Sales/Customers'),
        authFetch('/api/Common/PaymentTerms'),
        authFetch('/api/Common/Items'),
      ]);

      if (custRes.ok) {
        const custList = await custRes.json();
        setCustomers(Array.isArray(custList) ? custList : []);
      }

      if (termsRes.ok) {
        const termsList = await termsRes.json();
        const termsArr: PaymentTermOption[] = Array.isArray(termsList) ? termsList : [];
        setPaymentTerms(termsArr);
        if (!paymentTermId && termsArr.length > 0) {
          setPaymentTermId(termsArr[0].id);
        }
      }

      if (itemsRes.ok) {
        const itemsList = await itemsRes.json();
        setCatalogItems(Array.isArray(itemsList) ? itemsList : []);
      }
    } catch (err: any) {
      console.error('Error loading reference data:', err);
    } finally {
      setIsLoadingRefs(false);
    }
  }, [authFetch, paymentTermId]);

  useEffect(() => {
    loadReferenceData();
  }, [loadReferenceData]);

  // Load existing invoice if edit mode
  useEffect(() => {
    if (isEdit && invoiceId) {
      const loadInvoice = async () => {
        try {
          const res = await authFetch(`/api/Sales/SalesInvoice?id=${invoiceId}`);
          if (!res.ok) throw new Error(`Could not load invoice #${invoiceId}`);
          const data: Invoice = await res.json();

          setSelectedCustomerId(data.customerId);
          setInvoiceNo(data.no || '');
          setReferenceNo(data.referenceNo || '');
          if (data.invoiceDate) {
            setInvoiceDate(data.invoiceDate.split('T')[0]);
          }
          if (data.dueDate) {
            setDueDate(data.dueDate.split('T')[0]);
          }
          if (data.paymentTermId) {
            setPaymentTermId(data.paymentTermId);
          }
          if (data.notes) setNotes(data.notes);
          if (data.paymentInstructions) setPaymentInstructions(data.paymentInstructions);

          if (data.salesInvoiceLines && data.salesInvoiceLines.length > 0) {
            setLines(
              data.salesInvoiceLines.map((l: InvoiceLineItem) => ({
                id: l.id,
                itemId: l.itemId,
                itemDescription: l.itemDescription || '',
                quantity: l.quantity || 1,
                amount: l.amount || 0,
                discount: l.discount || 0,
                taxRate: l.taxRate || 0,
              }))
            );
          }
        } catch (err: any) {
          setErrorBanner(err.message || 'Failed to load invoice for editing.');
        }
      };
      loadInvoice();
    }
  }, [isEdit, invoiceId, authFetch]);

  // Recalculate Due Date when invoiceDate or paymentTermId changes
  useEffect(() => {
    if (!invoiceDate) return;
    const term = paymentTerms.find((t) => t.id === Number(paymentTermId));
    const dueDays = term?.dueAfterDays ?? 30;
    const baseDate = new Date(invoiceDate);
    if (!isNaN(baseDate.getTime())) {
      baseDate.setDate(baseDate.getDate() + dueDays);
      setDueDate(baseDate.toISOString().split('T')[0]);
    }
  }, [invoiceDate, paymentTermId, paymentTerms]);

  // Line item helpers
  const handleLineChange = (index: number, field: keyof FormLineItem, val: any) => {
    setLines((prev) => {
      const updated = [...prev];
      updated[index] = { ...updated[index], [field]: val };
      return updated;
    });
  };

  const handleSelectItem = (index: number, itemIdStr: string) => {
    const itemId = Number(itemIdStr);
    const catalogItem = catalogItems.find((i) => i.id === itemId);
    if (catalogItem) {
      setLines((prev) => {
        const updated = [...prev];
        updated[index] = {
          ...updated[index],
          itemId: catalogItem.id,
          itemDescription: catalogItem.description,
          amount: catalogItem.price ?? updated[index].amount,
        };
        return updated;
      });
    } else {
      handleLineChange(index, 'itemId', null);
    }
  };

  const handleAddLine = () => {
    setLines((prev) => [
      ...prev,
      {
        itemDescription: '',
        quantity: 1,
        amount: 0,
        discount: 0,
        taxRate: 0,
      },
    ]);
  };

  const handleRemoveLine = (index: number) => {
    if (lines.length <= 1) return;
    setLines((prev) => prev.filter((_, i) => i !== index));
  };

  // Calculations
  const calculatedTotals = useMemo(() => {
    let subtotal = 0;
    let totalDiscount = 0;
    let totalTax = 0;

    lines.forEach((l) => {
      const qty = Number(l.quantity) || 0;
      const rate = Number(l.amount) || 0;
      const gross = Math.round(qty * rate * 100) / 100;
      const discPercent = Number(l.discount) || 0;
      const discAmount = Math.round(((gross * discPercent) / 100) * 100) / 100;
      const net = Math.max(0, Math.round((gross - discAmount) * 100) / 100);

      const taxPercent = Number(l.taxRate) || 0;
      const taxAmount = Math.round(((net * taxPercent) / 100) * 100) / 100;

      subtotal += net;
      totalDiscount += discAmount;
      totalTax += taxAmount;
    });

    subtotal = Math.round(subtotal * 100) / 100;
    totalDiscount = Math.round(totalDiscount * 100) / 100;
    totalTax = Math.round(totalTax * 100) / 100;
    const grandTotal = Math.round((subtotal + totalTax) * 100) / 100;

    return {
      subtotal,
      totalDiscount,
      totalTax,
      grandTotal,
    };
  }, [lines]);

  // Submit Handler
  const handleSaveInvoice = async (asDraft: boolean) => {
    setErrorBanner(null);
    setSuccessBanner(null);

    // Validation
    if (!selectedCustomerId) {
      setErrorBanner('Please select or add a client for this invoice.');
      return;
    }

    const validLines = lines.filter((l) => l.itemDescription.trim() !== '' || l.amount > 0);
    if (validLines.length === 0) {
      setErrorBanner('Please provide at least one line item with a description or rate.');
      return;
    }

    if (asDraft) setIsSavingDraft(true);
    else setIsSavingIssue(true);

    try {
      const payload: any = {
        id: isEdit && invoiceId ? Number(invoiceId) : 0,
        customerId: Number(selectedCustomerId),
        invoiceDate: invoiceDate,
        dueDate: dueDate || invoiceDate,
        paymentTermId: paymentTermId ? Number(paymentTermId) : null,
        referenceNo: referenceNo.trim() || undefined,
        status: asDraft ? 'Draft' : 'Open',
        notes: notes.trim() || undefined,
        paymentInstructions: paymentInstructions.trim() || undefined,
        salesInvoiceLines: validLines.map((l) => ({
          id: l.id || 0,
          itemId: l.itemId || null,
          itemDescription: l.itemDescription.trim() || 'Service / Product',
          quantity: Number(l.quantity) || 1,
          amount: Number(l.amount) || 0,
          discount: Number(l.discount) || 0,
          taxRate: Number(l.taxRate) || 0,
        })),
      };

      const res = await authFetch('/api/Sales/SaveSalesInvoice', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(payload),
      });

      if (!res.ok) {
        const errorData = await res.json().catch(() => null);
        let msg = 'Failed to save sales invoice.';
        if (Array.isArray(errorData)) msg = errorData.join(' ');
        else if (errorData?.message) msg = errorData.message;
        throw new Error(msg);
      }

      const savedData = await res.json();
      const targetId = savedData.id || invoiceId;

      setSuccessBanner(
        asDraft
          ? 'Invoice saved as draft successfully!'
          : 'Invoice saved and issued successfully!'
      );

      // Navigate to detail view
      setTimeout(() => {
        navigate(`/org/${orgSlug}/sales/${targetId}`);
      }, 700);
    } catch (err: any) {
      console.error('Save invoice error:', err);
      setErrorBanner(err.message || 'An error occurred while saving the invoice.');
    } finally {
      setIsSavingDraft(false);
      setIsSavingIssue(false);
    }
  };

  // Inline Client Creation Handler
  const handleCreateCustomer = async (e: React.FormEvent) => {
    e.preventDefault();
    setCustomerModalError(null);

    if (!newCustomerName.trim()) {
      setCustomerModalError('Client name is required.');
      return;
    }
    if (!newCustomerEmail.trim()) {
      setCustomerModalError('Client email is required.');
      return;
    }

    setIsSubmittingCustomer(true);
    try {
      const payload = {
        id: 0,
        name: newCustomerName.trim(),
        email: newCustomerEmail.trim(),
        phone: newCustomerPhone.trim() || undefined,
        companyAddress: {
          street1: newCustomerStreet.trim() || undefined,
          province: newCustomerProvince.trim() || undefined,
          postalCode: newCustomerPostal.trim() || undefined,
          country: newCustomerCountry.trim() || undefined,
        },
        paymentTermId: newCustomerTermId ? Number(newCustomerTermId) : undefined,
      };

      const res = await authFetch('/api/Sales/SaveCustomer', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(payload),
      });

      if (!res.ok) {
        const errJson = await res.json().catch(() => null);
        throw new Error(errJson?.message || 'Failed to create client.');
      }

      const created = await res.json();
      const newOption: CustomerOption = {
        id: created.id,
        name: created.name || newCustomerName.trim(),
        email: newCustomerEmail.trim(),
        phone: newCustomerPhone.trim() || undefined,
        paymentTermId: newCustomerTermId ? Number(newCustomerTermId) : undefined,
      };

      // Add to list and select it
      setCustomers((prev) => [newOption, ...prev]);
      setSelectedCustomerId(created.id);
      if (newCustomerTermId) {
        setPaymentTermId(newCustomerTermId);
      }

      // Reset modal fields and close
      setNewCustomerName('');
      setNewCustomerEmail('');
      setNewCustomerPhone('');
      setNewCustomerStreet('');
      setNewCustomerProvince('');
      setNewCustomerPostal('');
      setNewCustomerCountry('');
      setNewCustomerTermId('');
      setIsAddCustomerOpen(false);
    } catch (err: any) {
      setCustomerModalError(err.message || 'Failed to create client.');
    } finally {
      setIsSubmittingCustomer(false);
    }
  };

  return (
    <div className="max-w-6xl mx-auto px-4 sm:px-6 py-6 pb-24 text-[var(--text-main)]">
      {/* Top Breadcrumb & Action Bar */}
      <div className="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-4 mb-6">
        <div className="flex items-center gap-3">
          <Link
            to={`/org/${orgSlug}/sales`}
            className="p-2 text-[var(--text-secondary)] hover:text-[var(--text-main)] rounded-xl hover:bg-[var(--bg-surface-hover)] border border-[var(--border-subtle)] transition"
            title="Back to Invoices"
          >
            <ArrowLeft className="w-5 h-5" />
          </Link>
          <div>
            <h1 className="text-2xl font-bold tracking-tight text-[var(--text-main)]">
              {isEdit ? `Edit Invoice ${invoiceNo || ''}` : 'New Invoice'}
            </h1>
            <p className="text-sm text-[var(--text-secondary)] mt-0.5">
              Create and issue standard invoices to your clients with automated bookkeeping.
            </p>
          </div>
        </div>

        {/* Action Buttons */}
        <div className="flex items-center gap-3">
          <Link
            to={`/org/${orgSlug}/sales`}
            className="px-4 py-2.5 text-sm font-medium text-[var(--text-secondary)] bg-[var(--bg-surface)] border border-[var(--border-subtle)] rounded-xl hover:bg-[var(--bg-surface-hover)] hover:text-[var(--text-main)] transition"
          >
            Cancel
          </Link>

          <button
            type="button"
            onClick={() => handleSaveInvoice(true)}
            disabled={isSavingDraft || isSavingIssue}
            className="inline-flex items-center gap-2 px-4 py-2.5 text-sm font-medium text-[var(--text-main)] bg-[var(--bg-surface)] border border-[var(--border-subtle)] rounded-xl hover:bg-[var(--bg-surface-hover)] transition disabled:opacity-50 shadow-sm"
          >
            {isSavingDraft ? (
              <Loader2 className="w-4 h-4 animate-spin text-[var(--brand-primary)]" />
            ) : (
              <Save className="w-4 h-4 text-[var(--text-secondary)]" />
            )}
            Save as Draft
          </button>

          <button
            type="button"
            onClick={() => handleSaveInvoice(false)}
            disabled={isSavingDraft || isSavingIssue}
            className="inline-flex items-center gap-2 px-5 py-2.5 text-sm font-semibold text-[var(--brand-text)] bg-[var(--brand-primary)] rounded-xl hover:bg-[var(--brand-hover)] shadow-sm transition disabled:opacity-50"
          >
            {isSavingIssue ? (
              <Loader2 className="w-4 h-4 animate-spin" />
            ) : (
              <Send className="w-4 h-4" />
            )}
            Save & Issue
          </button>
        </div>
      </div>

      {/* Notifications */}
      {errorBanner && (
        <div className="mb-6 p-4 bg-rose-500/10 border border-rose-500/20 text-rose-500 rounded-xl flex items-start gap-3">
          <AlertTriangle className="w-5 h-5 shrink-0 mt-0.5" />
          <div className="flex-1 text-sm font-medium">{errorBanner}</div>
          <button
            onClick={() => setErrorBanner(null)}
            className="p-1 hover:opacity-75 transition"
          >
            <X className="w-4 h-4" />
          </button>
        </div>
      )}

      {successBanner && (
        <div className="mb-6 p-4 bg-emerald-500/10 border border-emerald-500/20 text-emerald-500 rounded-xl flex items-center gap-3">
          <CheckCircle2 className="w-5 h-5 shrink-0" />
          <div className="flex-1 text-sm font-medium">{successBanner}</div>
        </div>
      )}

      {/* Main Invoice Card */}
      <div className="bg-[var(--bg-surface)] border border-[var(--border-subtle)] rounded-2xl shadow-sm p-6 sm:p-8 space-y-8">
        {/* Section 1: Parties & Invoice Meta */}
        <div className="grid grid-cols-1 lg:grid-cols-12 gap-8 border-b border-[var(--border-subtle)] pb-8">
          {/* Client Selection */}
          <div className="lg:col-span-6 space-y-4">
            <div className="flex items-center justify-between">
              <label className="flex items-center gap-2 text-xs font-semibold uppercase tracking-wider text-[var(--text-muted)]">
                <span>Bill To (Client)</span>
                <span className="text-rose-500">*</span>
                {isLoadingRefs && <Loader2 className="w-3 h-3 animate-spin text-[var(--brand-primary)]" />}
              </label>
              <button
                type="button"
                onClick={() => setIsAddCustomerOpen(true)}
                className="inline-flex items-center gap-1.5 text-xs font-semibold text-[var(--brand-primary)] hover:opacity-80 transition"
              >
                <UserPlus className="w-3.5 h-3.5" />
                + Add Client
              </button>
            </div>

            <div className="relative">
              <select
                value={selectedCustomerId}
                onChange={(e) => {
                  const val = e.target.value ? Number(e.target.value) : '';
                  setSelectedCustomerId(val);
                  if (val) {
                    const cust = customers.find((c) => c.id === val);
                    if (cust?.paymentTermId) {
                      setPaymentTermId(cust.paymentTermId);
                    }
                  }
                }}
                className="w-full px-4 py-2.5 text-sm bg-[var(--bg-subtle)] border border-[var(--border-subtle)] rounded-xl focus:ring-2 focus:ring-[var(--brand-ring)] focus:outline-none text-[var(--text-main)] transition"
              >
                <option value="">-- Select a Client --</option>
                {customers.map((c) => (
                  <option key={c.id} value={c.id}>
                    {c.name} {c.email ? `(${c.email})` : ''}
                  </option>
                ))}
              </select>
            </div>

            {selectedCustomerId && (
              <div className="p-4 bg-[var(--bg-subtle)] rounded-xl border border-[var(--border-subtle)] text-xs space-y-1">
                {(() => {
                  const cust = customers.find((c) => c.id === Number(selectedCustomerId));
                  if (!cust) return null;
                  return (
                    <>
                      <div className="font-semibold text-[var(--text-main)] text-sm">
                        {cust.name}
                      </div>
                      {cust.email && (
                        <div className="text-[var(--text-secondary)]">{cust.email}</div>
                      )}
                      {cust.phone && (
                        <div className="text-[var(--text-secondary)]">{cust.phone}</div>
                      )}
                    </>
                  );
                })()}
              </div>
            )}
          </div>

          {/* Invoice Metadata (Dates, Terms, PO #) */}
          <div className="lg:col-span-6 grid grid-cols-1 sm:grid-cols-2 gap-4">
            <div>
              <label className="block text-xs font-semibold uppercase tracking-wider text-[var(--text-muted)] mb-1.5">
                Issue Date <span className="text-rose-500">*</span>
              </label>
              <div className="relative">
                <input
                  type="date"
                  value={invoiceDate}
                  onChange={(e) => setInvoiceDate(e.target.value)}
                  className="w-full px-3.5 py-2.5 text-sm bg-[var(--bg-subtle)] border border-[var(--border-subtle)] rounded-xl focus:ring-2 focus:ring-[var(--brand-ring)] focus:outline-none text-[var(--text-main)] transition"
                />
              </div>
            </div>

            <div>
              <label className="block text-xs font-semibold uppercase tracking-wider text-[var(--text-muted)] mb-1.5">
                Payment Terms
              </label>
              <select
                value={paymentTermId}
                onChange={(e) =>
                  setPaymentTermId(e.target.value ? Number(e.target.value) : '')
                }
                className="w-full px-3.5 py-2.5 text-sm bg-[var(--bg-subtle)] border border-[var(--border-subtle)] rounded-xl focus:ring-2 focus:ring-[var(--brand-ring)] focus:outline-none text-[var(--text-main)] transition"
              >
                <option value="">Custom / Default</option>
                {paymentTerms.map((t) => (
                  <option key={t.id} value={t.id}>
                    {t.description}
                  </option>
                ))}
              </select>
            </div>

            <div>
              <label className="block text-xs font-semibold uppercase tracking-wider text-[var(--text-muted)] mb-1.5">
                Due Date
              </label>
              <input
                type="date"
                value={dueDate}
                onChange={(e) => setDueDate(e.target.value)}
                className="w-full px-3.5 py-2.5 text-sm bg-[var(--bg-subtle)] border border-[var(--border-subtle)] rounded-xl focus:ring-2 focus:ring-[var(--brand-ring)] focus:outline-none text-[var(--text-main)] transition"
              />
            </div>

            <div>
              <label className="block text-xs font-semibold uppercase tracking-wider text-[var(--text-muted)] mb-1.5">
                Reference / PO #
              </label>
              <input
                type="text"
                placeholder="e.g. PO-9842"
                value={referenceNo}
                onChange={(e) => setReferenceNo(e.target.value)}
                className="w-full px-3.5 py-2.5 text-sm bg-[var(--bg-subtle)] border border-[var(--border-subtle)] rounded-xl focus:ring-2 focus:ring-[var(--brand-ring)] focus:outline-none text-[var(--text-main)] placeholder-[var(--text-muted)] transition"
              />
            </div>
          </div>
        </div>

        {/* Section 2: Line Items Table */}
        <div>
          <div className="flex items-center justify-between mb-3">
            <h3 className="text-xs font-semibold uppercase tracking-wider text-[var(--text-muted)]">
              Line Items
            </h3>
            {catalogItems.length > 0 && (
              <span className="text-xs text-[var(--text-muted)]">
                Type freeform or pick from catalog
              </span>
            )}
          </div>

          <div className="overflow-x-auto border border-[var(--border-subtle)] rounded-xl bg-[var(--bg-surface)]">
            <table className="w-full text-left border-collapse">
              <thead>
                <tr className="bg-[var(--bg-subtle)] text-xs font-semibold text-[var(--text-muted)] uppercase tracking-wider border-b border-[var(--border-subtle)]">
                  <th className="py-3 px-4 min-w-[220px]">Item & Description</th>
                  <th className="py-3 px-3 w-24 text-right">Qty</th>
                  <th className="py-3 px-3 w-32 text-right">Unit Price</th>
                  <th className="py-3 px-3 w-24 text-right">Disc %</th>
                  <th className="py-3 px-3 w-28 text-right">Tax %</th>
                  <th className="py-3 px-4 w-32 text-right">Line Total</th>
                  <th className="py-3 px-2 w-12 text-center"></th>
                </tr>
              </thead>
              <tbody className="divide-y divide-[var(--border-subtle)] text-sm">
                {lines.map((line, idx) => {
                  const qty = Number(line.quantity) || 0;
                  const rate = Number(line.amount) || 0;
                  const gross = qty * rate;
                  const disc = (gross * (Number(line.discount) || 0)) / 100;
                  const lineTotal = Math.max(0, gross - disc);

                  return (
                    <tr key={idx} className="hover:bg-[var(--bg-surface-hover)]/40 transition">
                      <td className="py-2.5 px-4 space-y-1.5">
                        <input
                          type="text"
                          placeholder="Description (e.g. Consulting, Design, Software license)"
                          value={line.itemDescription}
                          onChange={(e) =>
                            handleLineChange(idx, 'itemDescription', e.target.value)
                          }
                          className="w-full px-3 py-1.5 text-sm bg-[var(--bg-subtle)] border border-[var(--border-subtle)] rounded-lg focus:ring-2 focus:ring-[var(--brand-ring)] focus:outline-none text-[var(--text-main)] placeholder-[var(--text-muted)] transition"
                        />
                        {catalogItems.length > 0 && (
                          <div className="flex items-center gap-1.5">
                            <span className="text-[11px] text-[var(--text-muted)]">or pick:</span>
                            <select
                              value={line.itemId || ''}
                              onChange={(e) => handleSelectItem(idx, e.target.value)}
                              className="text-[11px] py-0.5 px-2 bg-[var(--bg-subtle)] border border-[var(--border-subtle)] rounded text-[var(--text-secondary)] focus:outline-none"
                            >
                              <option value="">Catalog Item...</option>
                              {catalogItems.map((ci) => (
                                <option key={ci.id} value={ci.id}>
                                  {ci.description} ({formatCurrency(ci.price ?? 0)})
                                </option>
                              ))}
                            </select>
                          </div>
                        )}
                      </td>

                      <td className="py-2.5 px-3 text-right">
                        <input
                          type="number"
                          min="1"
                          step="any"
                          value={line.quantity}
                          onChange={(e) =>
                            handleLineChange(idx, 'quantity', parseFloat(e.target.value) || 0)
                          }
                          className="w-full px-2.5 py-1.5 text-sm text-right bg-[var(--bg-subtle)] border border-[var(--border-subtle)] rounded-lg focus:ring-2 focus:ring-[var(--brand-ring)] focus:outline-none text-[var(--text-main)] transition"
                        />
                      </td>

                      <td className="py-2.5 px-3 text-right">
                        <input
                          type="number"
                          min="0"
                          step="0.01"
                          value={line.amount}
                          onChange={(e) =>
                            handleLineChange(idx, 'amount', parseFloat(e.target.value) || 0)
                          }
                          className="w-full px-2.5 py-1.5 text-sm text-right bg-[var(--bg-subtle)] border border-[var(--border-subtle)] rounded-lg focus:ring-2 focus:ring-[var(--brand-ring)] focus:outline-none text-[var(--text-main)] transition"
                        />
                      </td>

                      <td className="py-2.5 px-3 text-right">
                        <input
                          type="number"
                          min="0"
                          max="100"
                          step="0.5"
                          value={line.discount}
                          onChange={(e) =>
                            handleLineChange(idx, 'discount', parseFloat(e.target.value) || 0)
                          }
                          className="w-full px-2 py-1.5 text-sm text-right bg-[var(--bg-subtle)] border border-[var(--border-subtle)] rounded-lg focus:ring-2 focus:ring-[var(--brand-ring)] focus:outline-none text-[var(--text-main)] transition"
                        />
                      </td>

                      <td className="py-2.5 px-3 text-right">
                        <select
                          value={line.taxRate}
                          onChange={(e) =>
                            handleLineChange(idx, 'taxRate', parseFloat(e.target.value) || 0)
                          }
                          className="w-full px-2 py-1.5 text-xs text-right bg-[var(--bg-subtle)] border border-[var(--border-subtle)] rounded-lg focus:ring-2 focus:ring-[var(--brand-ring)] focus:outline-none text-[var(--text-main)] transition"
                        >
                          <option value="0">0%</option>
                          <option value="5">5%</option>
                          <option value="7">7%</option>
                          <option value="10">10%</option>
                          <option value="12">12%</option>
                          <option value="13">13%</option>
                          <option value="15">15%</option>
                          <option value="20">20%</option>
                        </select>
                      </td>

                      <td className="py-2.5 px-4 text-right font-medium font-mono text-[var(--text-main)]">
                        {formatCurrency(lineTotal)}
                      </td>

                      <td className="py-2.5 px-2 text-center">
                        <button
                          type="button"
                          onClick={() => handleRemoveLine(idx)}
                          disabled={lines.length <= 1}
                          className="p-1.5 text-[var(--text-muted)] hover:text-rose-500 rounded-lg hover:bg-[var(--bg-surface-hover)] transition disabled:opacity-20"
                          title="Remove item"
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

          <div className="mt-3">
            <button
              type="button"
              onClick={handleAddLine}
              className="inline-flex items-center gap-1.5 px-3.5 py-2 text-xs font-semibold text-[var(--brand-primary)] bg-[var(--brand-subtle)] rounded-xl hover:opacity-85 transition"
            >
              <Plus className="w-3.5 h-3.5" />
              Add Line Item
            </button>
          </div>
        </div>

        {/* Section 3: Notes & Totals Breakdown */}
        <div className="grid grid-cols-1 lg:grid-cols-12 gap-8 border-t border-[var(--border-subtle)] pt-8">
          {/* Notes & Payment Instructions */}
          <div className="lg:col-span-7 space-y-4">
            <div>
              <label className="block text-xs font-semibold uppercase tracking-wider text-[var(--text-muted)] mb-1.5">
                Client Notes
              </label>
              <textarea
                rows={2}
                value={notes}
                onChange={(e) => setNotes(e.target.value)}
                placeholder="Notes visible to your client on the invoice..."
                className="w-full px-3.5 py-2.5 text-sm bg-[var(--bg-subtle)] border border-[var(--border-subtle)] rounded-xl focus:ring-2 focus:ring-[var(--brand-ring)] focus:outline-none text-[var(--text-main)] placeholder-[var(--text-muted)] transition"
              />
            </div>

            <div>
              <label className="block text-xs font-semibold uppercase tracking-wider text-[var(--text-muted)] mb-1.5">
                Payment Instructions / Bank Wire Info
              </label>
              <textarea
                rows={2}
                value={paymentInstructions}
                onChange={(e) => setPaymentInstructions(e.target.value)}
                placeholder="Bank name, Swift/IBAN, routing, or PayPal info..."
                className="w-full px-3.5 py-2.5 text-sm bg-[var(--bg-subtle)] border border-[var(--border-subtle)] rounded-xl focus:ring-2 focus:ring-[var(--brand-ring)] focus:outline-none text-[var(--text-main)] placeholder-[var(--text-muted)] transition"
              />
            </div>
          </div>

          {/* Totals Summary Box */}
          <div className="lg:col-span-5 bg-[var(--bg-subtle)] border border-[var(--border-subtle)] rounded-2xl p-6 space-y-3">
            <div className="flex items-center justify-between text-sm text-[var(--text-secondary)]">
              <span>Subtotal</span>
              <span className="font-medium font-mono text-[var(--text-main)]">
                {formatCurrency(calculatedTotals.subtotal)}
              </span>
            </div>

            {calculatedTotals.totalDiscount > 0 && (
              <div className="flex items-center justify-between text-sm text-emerald-500">
                <span>Discounts</span>
                <span className="font-mono">-{formatCurrency(calculatedTotals.totalDiscount)}</span>
              </div>
            )}

            <div className="flex items-center justify-between text-sm text-[var(--text-secondary)]">
              <span>Estimated Tax</span>
              <span className="font-medium font-mono text-[var(--text-main)]">
                {formatCurrency(calculatedTotals.totalTax)}
              </span>
            </div>

            <div className="border-t border-[var(--border-subtle)] pt-3 flex items-center justify-between">
              <span className="text-base font-bold text-[var(--text-main)]">
                Total Due
              </span>
              <span className="text-xl font-bold font-mono text-[var(--brand-primary)]">
                {formatCurrency(calculatedTotals.grandTotal)}
              </span>
            </div>
          </div>
        </div>
      </div>

      {/* Inline Modal: Add New Client */}
      {isAddCustomerOpen && (
        <div className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-black/60 backdrop-blur-sm animate-fade-in">
          <div className="bg-[var(--bg-surface)] rounded-2xl max-w-lg w-full border border-[var(--border-subtle)] shadow-2xl overflow-hidden">
            <div className="px-6 py-4 border-b border-[var(--border-subtle)] flex items-center justify-between">
              <div className="flex items-center gap-2">
                <Building2 className="w-5 h-5 text-[var(--brand-primary)]" />
                <h3 className="font-bold text-[var(--text-main)] text-base">
                  Add New Client
                </h3>
              </div>
              <button
                type="button"
                onClick={() => setIsAddCustomerOpen(false)}
                className="text-[var(--text-muted)] hover:text-[var(--text-main)] p-1 rounded-lg transition"
              >
                <X className="w-5 h-5" />
              </button>
            </div>

            <form onSubmit={handleCreateCustomer} className="p-6 space-y-4">
              {customerModalError && (
                <div className="p-3 bg-rose-500/10 border border-rose-500/20 rounded-xl text-xs text-rose-500">
                  {customerModalError}
                </div>
              )}

              <div>
                <label className="block text-xs font-semibold uppercase tracking-wider text-[var(--text-muted)] mb-1">
                  Client / Company Name <span className="text-rose-500">*</span>
                </label>
                <input
                  type="text"
                  required
                  placeholder="e.g. Acme Corporation"
                  value={newCustomerName}
                  onChange={(e) => setNewCustomerName(e.target.value)}
                  className="w-full px-3.5 py-2.5 text-sm bg-[var(--bg-subtle)] border border-[var(--border-subtle)] rounded-xl focus:ring-2 focus:ring-[var(--brand-ring)] focus:outline-none text-[var(--text-main)] placeholder-[var(--text-muted)] transition"
                />
              </div>

              <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
                <div>
                  <label className="block text-xs font-semibold uppercase tracking-wider text-[var(--text-muted)] mb-1">
                    Email Address <span className="text-rose-500">*</span>
                  </label>
                  <input
                    type="email"
                    required
                    placeholder="billing@acme.com"
                    value={newCustomerEmail}
                    onChange={(e) => setNewCustomerEmail(e.target.value)}
                    className="w-full px-3.5 py-2.5 text-sm bg-[var(--bg-subtle)] border border-[var(--border-subtle)] rounded-xl focus:ring-2 focus:ring-[var(--brand-ring)] focus:outline-none text-[var(--text-main)] placeholder-[var(--text-muted)] transition"
                  />
                </div>

                <div>
                  <label className="block text-xs font-semibold uppercase tracking-wider text-[var(--text-muted)] mb-1">
                    Phone Number
                  </label>
                  <input
                    type="tel"
                    placeholder="+1 (555) 000-0000"
                    value={newCustomerPhone}
                    onChange={(e) => setNewCustomerPhone(e.target.value)}
                    className="w-full px-3.5 py-2.5 text-sm bg-[var(--bg-subtle)] border border-[var(--border-subtle)] rounded-xl focus:ring-2 focus:ring-[var(--brand-ring)] focus:outline-none text-[var(--text-main)] placeholder-[var(--text-muted)] transition"
                  />
                </div>
              </div>

              <div>
                <label className="block text-xs font-semibold uppercase tracking-wider text-[var(--text-muted)] mb-1">
                  Street Address
                </label>
                <input
                  type="text"
                  placeholder="123 Market St, Suite 400"
                  value={newCustomerStreet}
                  onChange={(e) => setNewCustomerStreet(e.target.value)}
                  className="w-full px-3.5 py-2.5 text-sm bg-[var(--bg-subtle)] border border-[var(--border-subtle)] rounded-xl focus:ring-2 focus:ring-[var(--brand-ring)] focus:outline-none text-[var(--text-main)] placeholder-[var(--text-muted)] transition"
                />
              </div>

              <div className="grid grid-cols-3 gap-2">
                <div>
                  <label className="block text-xs font-semibold uppercase tracking-wider text-[var(--text-muted)] mb-1">
                    City/State
                  </label>
                  <input
                    type="text"
                    placeholder="New York, NY"
                    value={newCustomerProvince}
                    onChange={(e) => setNewCustomerProvince(e.target.value)}
                    className="w-full px-2.5 py-2 text-xs bg-[var(--bg-subtle)] border border-[var(--border-subtle)] rounded-xl focus:ring-2 focus:ring-[var(--brand-ring)] focus:outline-none text-[var(--text-main)] placeholder-[var(--text-muted)] transition"
                  />
                </div>

                <div>
                  <label className="block text-xs font-semibold uppercase tracking-wider text-[var(--text-muted)] mb-1">
                    Postal Code
                  </label>
                  <input
                    type="text"
                    placeholder="10001"
                    value={newCustomerPostal}
                    onChange={(e) => setNewCustomerPostal(e.target.value)}
                    className="w-full px-2.5 py-2 text-xs bg-[var(--bg-subtle)] border border-[var(--border-subtle)] rounded-xl focus:ring-2 focus:ring-[var(--brand-ring)] focus:outline-none text-[var(--text-main)] placeholder-[var(--text-muted)] transition"
                  />
                </div>

                <div>
                  <label className="block text-xs font-semibold uppercase tracking-wider text-[var(--text-muted)] mb-1">
                    Country
                  </label>
                  <input
                    type="text"
                    placeholder="USA"
                    value={newCustomerCountry}
                    onChange={(e) => setNewCustomerCountry(e.target.value)}
                    className="w-full px-2.5 py-2 text-xs bg-[var(--bg-subtle)] border border-[var(--border-subtle)] rounded-xl focus:ring-2 focus:ring-[var(--brand-ring)] focus:outline-none text-[var(--text-main)] placeholder-[var(--text-muted)] transition"
                  />
                </div>
              </div>

              <div>
                <label className="block text-xs font-semibold uppercase tracking-wider text-[var(--text-muted)] mb-1">
                  Default Payment Terms
                </label>
                <select
                  value={newCustomerTermId}
                  onChange={(e) =>
                    setNewCustomerTermId(e.target.value ? Number(e.target.value) : '')
                  }
                  className="w-full px-3.5 py-2.5 text-sm bg-[var(--bg-subtle)] border border-[var(--border-subtle)] rounded-xl focus:ring-2 focus:ring-[var(--brand-ring)] focus:outline-none text-[var(--text-main)] transition"
                >
                  <option value="">Default Terms</option>
                  {paymentTerms.map((t) => (
                    <option key={t.id} value={t.id}>
                      {t.description}
                    </option>
                  ))}
                </select>
              </div>

              <div className="pt-2 flex items-center justify-end gap-3">
                <button
                  type="button"
                  onClick={() => setIsAddCustomerOpen(false)}
                  className="px-4 py-2.5 text-sm font-medium text-[var(--text-secondary)] bg-[var(--bg-subtle)] border border-[var(--border-subtle)] rounded-xl hover:bg-[var(--bg-surface-hover)] hover:text-[var(--text-main)] transition"
                >
                  Cancel
                </button>
                <button
                  type="submit"
                  disabled={isSubmittingCustomer}
                  className="inline-flex items-center gap-2 px-5 py-2.5 text-sm font-semibold text-[var(--brand-text)] bg-[var(--brand-primary)] rounded-xl hover:bg-[var(--brand-hover)] transition disabled:opacity-50 shadow-sm"
                >
                  {isSubmittingCustomer && <Loader2 className="w-4 h-4 animate-spin" />}
                  Create Client
                </button>
              </div>
            </form>
          </div>
        </div>
      )}
    </div>
  );
};
