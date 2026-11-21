import React, { useState, useEffect, useCallback } from 'react';
import { useSearchParams } from 'react-router-dom';
import { PageHeader, HeaderButton } from '../components/PageHeader';
import { SalesTabs } from '../components/SalesTabs';
import { PurchasesTabs } from '../components/PurchasesTabs';
import { useTenantContext } from '../components/Gating';
import { ModalShell } from '../components/ModalShell';
import { useAuth } from '../auth/AuthContext';
import { localToday } from '../lib/businessDays';
import { Plus, CheckCircle2, ArrowRightLeft, Undo2, FileWarning, RotateCcw, Save } from 'lucide-react';
import { shortRef } from '../lib/shortRef';

interface CreditNoteRow {
  id: string;
  no: string | null;
  customerId?: string;
  customerName?: string;
  vendorId?: string;
  vendorName?: string;
  vendorCreditNo?: string;
  date: string;
  effectiveDate?: string;
  totalAmount: number;
  appliedAmount: number;
  refundedAmount: number;
  availableAmount: number;
  settlementState: string;
  status: number;
  statusLabel: string;
  reason: string;
  version: number;
}

interface AccountRolePreview {
  roleKey: string;
  roleLabel: string;
  configuredAccountId: string | null;
  configuredAccountCode: string | null;
  configuredAccountName: string | null;
  suggestedAccountId: string | null;
  suggestedAccountCode: string | null;
  suggestedAccountName: string | null;
  isArchived: boolean;
  isMissing: boolean;
}

interface MappingPreview {
  readyForSalesCredits: boolean;
  readyForSupplierCredits: boolean;
  readyForAdvances: boolean;
  readyForWriteOffs: boolean;
  readyForReturns: boolean;
  roles: AccountRolePreview[];
}

interface DocOption {
  id: string;
  no: string | null;
  partyId: string;
  partyName: string;
  balanceDue: number;
  status?: string;
}

interface PartyOption {
  id: string;
  name: string;
}

interface DocLineOption {
  id: string;
  itemId: string | null;
  description: string;
  quantity: number;
}

interface GlAccountOption {
  id: string;
  accountCode: string;
  accountName: string;
}

interface BankPickerOption {
  id: string;
  name: string;
}

type ActiveModal =
  | null
  | { type: 'createCredit' }
  | { type: 'applyCredit'; credit: CreditNoteRow }
  | { type: 'recordRefund'; credit: CreditNoteRow }
  | { type: 'reverseCredit'; credit: CreditNoteRow }
  | { type: 'writeOff' }
  | { type: 'supplierAdvance' }
  | { type: 'physicalReturn' };

function newIdempotencyKey() {
  return `ui-${Date.now()}-${Math.random().toString(36).slice(2, 10)}`;
}

const ROLE_KEY_TO_REQUEST_FIELD: Record<string, string> = {
  CustomerCreditLiability: 'customerCreditLiabilityAccountId',
  SalesReturnsAllowances: 'salesReturnsAllowancesAccountId',
  SupplierCreditAsset: 'supplierCreditAssetAccountId',
  SupplierAdvance: 'supplierAdvanceAccountId',
  SupplierReturnClearing: 'supplierReturnClearingAccountId',
  ReturnValuationDifference: 'returnValuationDifferenceAccountId',
  ReceivableLossAllowance: 'receivableLossAllowanceAccountId',
  ImpairmentExpense: 'impairmentExpenseAccountId',
  BadDebtRecovery: 'badDebtRecoveryAccountId',
};

export const CreditsAndSettlements: React.FC<{ side: 'sales' | 'purchasing' }> = ({ side }) => {
  const { authFetch } = useAuth();
  const { currentOrg } = useTenantContext();
  const [searchParams] = useSearchParams();
  const [credits, setCredits] = useState<CreditNoteRow[]>([]);
  const [mappings, setMappings] = useState<MappingPreview | null>(null);
  const [mappingSelections, setMappingSelections] = useState<Record<string, string>>({});
  const [glAccounts, setGlAccounts] = useState<GlAccountOption[]>([]);
  const [parties, setParties] = useState<PartyOption[]>([]);
  const [allDocs, setAllDocs] = useState<DocOption[]>([]);
  const [openDocs, setOpenDocs] = useState<DocOption[]>([]);
  const [banks, setBanks] = useState<BankPickerOption[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [modal, setModal] = useState<ActiveModal>(null);

  // Create credit form state
  const [partyId, setPartyId] = useState<string>(
    searchParams.get(side === 'sales' ? 'customerId' : 'vendorId') ?? ''
  );
  const [sourceDocId, setSourceDocId] = useState<string>(
    searchParams.get(side === 'sales' ? 'invoiceId' : 'billId') ?? ''
  );
  const [vendorCreditNo, setVendorCreditNo] = useState('');
  const [creditReason, setCreditReason] = useState('');
  const [lineDesc, setLineDesc] = useState('Price adjustment');
  const [lineQty, setLineQty] = useState('1');
  const [linePrice, setLinePrice] = useState('0.00');
  const [previewSummary, setPreviewSummary] = useState<string | null>(null);

  // Apply credit form state
  const [applyTargetId, setApplyTargetId] = useState<string>(
    searchParams.get(side === 'sales' ? 'invoiceId' : 'billId') ?? ''
  );
  const [applyAmount, setApplyAmount] = useState('');

  // External refund form state
  const [refundBankId, setRefundBankId] = useState('');
  const [refundAmount, setRefundAmount] = useState('');
  const [refundMethod, setRefundMethod] = useState('Bank Transfer');
  const [refundExtRef, setRefundExtRef] = useState('');

  // Reverse form state
  const [reverseReason, setReverseReason] = useState('');
  const [reverseDate, setReverseDate] = useState(localToday());

  // Write-off form state
  const [woInvoiceId, setWoInvoiceId] = useState<string>(searchParams.get('invoiceId') ?? '');
  const [woAmount, setWoAmount] = useState('');
  const [woReason, setWoReason] = useState('');
  const [woEvidence, setWoEvidence] = useState('');

  // Supplier advance form state
  const [advVendorId, setAdvVendorId] = useState<string>(searchParams.get('vendorId') ?? '');
  const [advBankId, setAdvBankId] = useState('');
  const [advAmount, setAdvAmount] = useState('');
  const [advPurpose, setAdvPurpose] = useState('');

  // Physical return form state
  const [retDocId, setRetDocId] = useState<string>(
    searchParams.get(side === 'sales' ? 'invoiceId' : 'billId') ?? ''
  );
  const [retDocLines, setRetDocLines] = useState<DocLineOption[]>([]);
  const [retLineId, setRetLineId] = useState('');
  const [retItemId, setRetItemId] = useState('');
  const [retQty, setRetQty] = useState('1');
  const [retCondition, setRetCondition] = useState('1');
  const [retReason, setRetReason] = useState('');
  const [retControlTransferred, setRetControlTransferred] = useState(true);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const endpoint = side === 'sales' ? '/api/sales/credits' : '/api/purchasing/credits';
      const docsEndpoint = side === 'sales' ? '/api/Sales/SalesInvoices' : '/api/Purchasing/Bills';
      const partiesEndpoint = side === 'sales' ? '/api/Sales/Customers' : '/api/Purchasing/Vendors';
      const [listRes, mapRes, docsRes, bankRes, partiesRes, accountsRes] = await Promise.all([
        authFetch(endpoint),
        authFetch('/api/sales/credits/mappings/preview'),
        authFetch(docsEndpoint),
        authFetch('/api/Banking/Accounts/Picker'),
        authFetch(partiesEndpoint),
        authFetch('/api/Financials/Accounts'),
      ]);
      if (listRes.ok) {
        setCredits(await listRes.json());
      } else if (listRes.status === 403) {
        setError('Credits, returns, refunds, and write-offs are not enabled for this workspace or require additional permission.');
      }
      if (mapRes.ok) {
        const preview: MappingPreview = await mapRes.json();
        setMappings(preview);
        const initial: Record<string, string> = {};
        for (const r of preview.roles) {
          const val = r.configuredAccountId ?? r.suggestedAccountId;
          initial[r.roleKey] = val ? String(val) : '';
        }
        setMappingSelections(initial);
      }
      if (docsRes.ok) {
        const rawDocs = await docsRes.json();
        const list = Array.isArray(rawDocs) ? rawDocs : rawDocs.items ?? [];
        const mapped: DocOption[] = list
          .filter((d: any) => d.status !== 'Draft' && d.status !== 'Void')
          .map((d: any) => ({
            id: d.id,
            no: d.no ?? d.vendorInvoiceNo ?? `${shortRef(d.id)}`,
            partyId: side === 'sales' ? d.customerId : d.vendorId,
            partyName: side === 'sales' ? d.customerName : d.vendorName ?? '',
            balanceDue: Number(d.balanceDue ?? 0),
            status: d.status,
          }));
        setAllDocs(mapped);
        setOpenDocs(mapped.filter((d) => d.balanceDue > 0));
      }
      if (bankRes.ok) {
        const rawBanks = await bankRes.json();
        const list = Array.isArray(rawBanks) ? rawBanks : rawBanks.accounts ?? [];
        setBanks(list.map((b: any) => ({ id: b.id, name: b.name || b.accountName || `Bank ${shortRef(b.id)}` })));
        if (list.length > 0) {
          const firstId = String(list[0].id);
          setRefundBankId((prev) => prev || firstId);
          setAdvBankId((prev) => prev || firstId);
        }
      }
      if (partiesRes.ok) {
        const rawParties = await partiesRes.json();
        const list = Array.isArray(rawParties) ? rawParties : rawParties.items ?? [];
        const mappedParties: PartyOption[] = list.map((p: any) => ({
          id: p.id,
          name: p.name || p.partyName || `${side === 'sales' ? 'Customer' : 'Vendor'} ${shortRef(p.id)}`,
        }));
        setParties(mappedParties);
        if (mappedParties.length > 0) {
          const firstId = String(mappedParties[0].id);
          setPartyId((prev) => prev || firstId);
          setAdvVendorId((prev) => prev || firstId);
        }
      }
      if (accountsRes.ok) {
        const rawAccs = await accountsRes.json();
        const list = Array.isArray(rawAccs) ? rawAccs : rawAccs.accounts ?? [];
        setGlAccounts(
          list.map((a: any) => ({
            id: a.id,
            accountCode: a.accountCode ?? '',
            accountName: a.accountName ?? '',
          }))
        );
      }
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Unable to load credits.');
    } finally {
      setLoading(false);
    }
  }, [authFetch, side]);

  useEffect(() => {
    void load();
  }, [load]);

  // Load lines when selecting a source invoice/bill for physical returns
  useEffect(() => {
    if (!retDocId) {
      setRetDocLines([]);
      setRetLineId('');
      setRetItemId('');
      return;
    }
    let cancelled = false;
    const loadLines = async () => {
      const url =
        side === 'sales'
          ? `/api/Sales/SalesInvoice?id=${encodeURIComponent(retDocId)}`
          : `/api/Purchasing/Bill?id=${encodeURIComponent(retDocId)}`;
      const res = await authFetch(url);
      if (!res.ok || cancelled) return;
      const doc = await res.json();
      const rawLines = side === 'sales' ? doc.salesInvoiceLines ?? [] : doc.lines ?? [];
      const mapped: DocLineOption[] = rawLines.map((l: any) => ({
        id: l.id,
        itemId: l.itemId ?? null,
        description: l.itemDescription || l.description || `Line ${shortRef(l.id)}`,
        quantity: Number(l.quantity ?? 1),
      }));
      if (cancelled) return;
      setRetDocLines(mapped);
      if (mapped.length > 0) {
        setRetLineId(String(mapped[0].id));
        setRetItemId(mapped[0].itemId ? String(mapped[0].itemId) : '');
        setRetQty(String(mapped[0].quantity));
      } else {
        setRetLineId('');
        setRetItemId('');
      }
    };
    void loadLines();
    return () => {
      cancelled = true;
    };
  }, [authFetch, retDocId, side]);

  const readProblem = async (res: Response) => {
    try {
      const body = await res.json();
      return body.detail || body.message || body.title || `Request failed (${res.status})`;
    } catch {
      return `Request failed (${res.status})`;
    }
  };

  const handleSaveMappings = async () => {
    setBusy(true);
    setError(null);
    try {
      const payload: Record<string, string | null> = {};
      for (const [roleKey, field] of Object.entries(ROLE_KEY_TO_REQUEST_FIELD)) {
        const raw = mappingSelections[roleKey];
        payload[field] = raw || null;
      }
      const res = await authFetch('/api/sales/credits/mappings', {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(payload),
      });
      if (!res.ok) {
        setError(await readProblem(res));
        return;
      }
      setMappings(await res.json());
      setNotice('Control-account mappings saved.');
    } finally {
      setBusy(false);
    }
  };

  const handlePreviewCredit = async () => {
    setBusy(true);
    setError(null);
    setPreviewSummary(null);
    try {
      const url = side === 'sales' ? '/api/sales/credits/preview' : '/api/purchasing/credits/preview';
      const payload =
        side === 'sales'
          ? {
              customerId: partyId,
              sourceSalesInvoiceHeaderId: sourceDocId ? sourceDocId : null,
              date: localToday(),
              kind: 1,
              reason: creditReason || 'Credit preview',
              lines: [{ description: lineDesc, quantity: Number(lineQty), unitPrice: Number(linePrice) }],
            }
          : {
              vendorId: partyId,
              sourcePurchaseInvoiceHeaderId: sourceDocId ? sourceDocId : null,
              vendorCreditNo: vendorCreditNo || 'PREVIEW',
              date: localToday(),
              effectiveDate: localToday(),
              kind: 1,
              reason: creditReason || 'Credit preview',
              lines: [{ description: lineDesc, quantity: Number(lineQty), unitCost: Number(linePrice) }],
            };
      const res = await authFetch(url, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(payload),
      });
      if (!res.ok) {
        setError(await readProblem(res));
        return;
      }
      const data = await res.json();
      setPreviewSummary(
        `Preview total: ${Number(data.totalAmount ?? 0).toFixed(2)} (Subtotal ${Number(data.subtotal ?? 0).toFixed(2)}, Tax ${Number(data.taxAmount ?? 0).toFixed(2)})`
      );
    } finally {
      setBusy(false);
    }
  };

  const handleSaveCreditDraft = async (e: React.FormEvent) => {
    e.preventDefault();
    setBusy(true);
    setError(null);
    try {
      const url = side === 'sales' ? '/api/sales/credits/drafts' : '/api/purchasing/credits/drafts';
      const payload =
        side === 'sales'
          ? {
              customerId: partyId,
              sourceSalesInvoiceHeaderId: sourceDocId ? sourceDocId : null,
              date: localToday(),
              kind: 1,
              isStandalone: !sourceDocId,
              reason: creditReason,
              lines: [{ description: lineDesc, quantity: Number(lineQty), unitPrice: Number(linePrice), discountAmount: 0 }],
              idempotencyKey: newIdempotencyKey(),
            }
          : {
              vendorId: partyId,
              sourcePurchaseInvoiceHeaderId: sourceDocId ? sourceDocId : null,
              vendorCreditNo,
              date: localToday(),
              effectiveDate: localToday(),
              kind: 1,
              isStandalone: !sourceDocId,
              reason: creditReason,
              lines: [{ description: lineDesc, quantity: Number(lineQty), unitCost: Number(linePrice) }],
              idempotencyKey: newIdempotencyKey(),
            };
      const res = await authFetch(url, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(payload),
      });
      if (!res.ok) {
        setError(await readProblem(res));
        return;
      }
      setNotice('Credit note draft saved.');
      setModal(null);
      await load();
    } finally {
      setBusy(false);
    }
  };

  const handleCreditAction = async (credit: CreditNoteRow, action: 'submit' | 'post') => {
    setBusy(true);
    setError(null);
    try {
      const base = side === 'sales' ? '/api/sales/credits' : '/api/purchasing/credits';
      const url = action === 'submit' ? `${base}/${credit.id}/submit?idempotencyKey=${newIdempotencyKey()}` : `${base}/${credit.id}/post`;
      const res = await authFetch(url, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: action === 'post' ? JSON.stringify({ expectedVersion: credit.version, idempotencyKey: newIdempotencyKey() }) : undefined,
      });
      if (!res.ok) {
        setError(await readProblem(res));
        return;
      }
      setNotice(action === 'submit' ? 'Submitted for review.' : 'Credit note posted to the general ledger.');
      await load();
    } finally {
      setBusy(false);
    }
  };

  const handleApplyCredit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (modal?.type !== 'applyCredit') return;
    setBusy(true);
    setError(null);
    try {
      const base = side === 'sales' ? '/api/sales/credits' : '/api/purchasing/credits';
      const payload =
        side === 'sales'
          ? {
              effectiveDate: localToday(),
              expectedCreditVersion: modal.credit.version,
              applications: [{ salesInvoiceHeaderId: applyTargetId, amount: Number(applyAmount) }],
              idempotencyKey: newIdempotencyKey(),
            }
          : {
              effectiveDate: localToday(),
              expectedCreditVersion: modal.credit.version,
              applications: [{ purchaseInvoiceHeaderId: applyTargetId, amount: Number(applyAmount) }],
              idempotencyKey: newIdempotencyKey(),
            };
      const res = await authFetch(`${base}/${modal.credit.id}/apply`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(payload),
      });
      if (!res.ok) {
        setError(await readProblem(res));
        return;
      }
      setNotice('Credit applied.');
      setModal(null);
      await load();
    } finally {
      setBusy(false);
    }
  };

  const handleRecordRefund = async (e: React.FormEvent) => {
    e.preventDefault();
    if (modal?.type !== 'recordRefund') return;
    setBusy(true);
    setError(null);
    try {
      const isSales = side === 'sales';
      const draftUrl = isSales ? '/api/settlements/refunds/drafts' : '/api/settlements/refunds/supplier/drafts';
      const draftRes = await authFetch(draftUrl, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          direction: isSales ? 1 : 2,
          customerId: isSales ? modal.credit.customerId : null,
          vendorId: !isSales ? modal.credit.vendorId : null,
          date: localToday(),
          bankId: refundBankId,
          paymentMethod: refundMethod,
          externalReference: refundExtRef,
          allocations: [
            {
              sourceType: isSales ? 1 : 2,
              sourceId: modal.credit.id,
              amount: Number(refundAmount),
            },
          ],
          idempotencyKey: newIdempotencyKey(),
        }),
      });
      if (!draftRes.ok) {
        setError(await readProblem(draftRes));
        return;
      }
      const draft = await draftRes.json();
      const submitUrl = isSales
        ? `/api/settlements/refunds/${draft.id}/submit?idempotencyKey=${newIdempotencyKey()}`
        : `/api/settlements/refunds/supplier/${draft.id}/submit?idempotencyKey=${newIdempotencyKey()}`;
      const subRes = await authFetch(submitUrl, { method: 'POST' });
      if (!subRes.ok) {
        setError(await readProblem(subRes));
        return;
      }
      const submitted = await subRes.json();
      const postUrl = isSales
        ? `/api/settlements/refunds/${draft.id}/post`
        : `/api/settlements/refunds/supplier/${draft.id}/post`;
      const postRes = await authFetch(postUrl, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ expectedVersion: submitted.version, idempotencyKey: newIdempotencyKey() }),
      });
      if (!postRes.ok) {
        setNotice('External refund saved and submitted for review (another reviewer must post under team policy).');
      } else {
        setNotice('External refund recorded and posted.');
      }
      setModal(null);
      await load();
    } finally {
      setBusy(false);
    }
  };

  const handleReverseCredit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (modal?.type !== 'reverseCredit') return;
    setBusy(true);
    setError(null);
    try {
      const base = side === 'sales' ? '/api/sales/credits' : '/api/purchasing/credits';
      const res = await authFetch(`${base}/${modal.credit.id}/reverse`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          reversalDate: reverseDate,
          reason: reverseReason,
          idempotencyKey: newIdempotencyKey(),
        }),
      });
      if (!res.ok) {
        setError(await readProblem(res));
        return;
      }
      setNotice('Credit note reversed.');
      setModal(null);
      await load();
    } finally {
      setBusy(false);
    }
  };

  const handleWriteOff = async (e: React.FormEvent) => {
    e.preventDefault();
    setBusy(true);
    setError(null);
    try {
      const reqRes = await authFetch('/api/sales/write-offs/requests', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          salesInvoiceHeaderId: woInvoiceId,
          date: localToday(),
          amount: Number(woAmount),
          reason: woReason,
          collectionEvidence: woEvidence,
          idempotencyKey: newIdempotencyKey(),
        }),
      });
      if (!reqRes.ok) {
        setError(await readProblem(reqRes));
        return;
      }
      const wo = await reqRes.json();
      const postRes = await authFetch(`/api/sales/write-offs/${wo.id}/post`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ expectedVersion: wo.version, idempotencyKey: newIdempotencyKey() }),
      });
      if (!postRes.ok) {
        setNotice('Write-off requested and pending independent review.');
      } else {
        setNotice('Receivable write-off posted.');
      }
      setModal(null);
      await load();
    } finally {
      setBusy(false);
    }
  };

  const handleSupplierAdvance = async (e: React.FormEvent) => {
    e.preventDefault();
    setBusy(true);
    setError(null);
    try {
      const draftRes = await authFetch('/api/settlements/advances/drafts', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          vendorId: advVendorId,
          date: localToday(),
          bankId: advBankId,
          amount: Number(advAmount),
          purpose: advPurpose,
          isRefundable: true,
          idempotencyKey: newIdempotencyKey(),
        }),
      });
      if (!draftRes.ok) {
        setError(await readProblem(draftRes));
        return;
      }
      const adv = await draftRes.json();
      await authFetch(`/api/settlements/advances/${adv.id}/submit?idempotencyKey=${newIdempotencyKey()}`, { method: 'POST' });
      const postRes = await authFetch(`/api/settlements/advances/${adv.id}/post`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ idempotencyKey: newIdempotencyKey() }),
      });
      setNotice(postRes.ok ? 'Supplier advance posted.' : 'Supplier advance submitted for review.');
      setModal(null);
      await load();
    } finally {
      setBusy(false);
    }
  };

  const handlePhysicalReturn = async (e: React.FormEvent) => {
    e.preventDefault();
    setBusy(true);
    setError(null);
    try {
      const isSales = side === 'sales';
      const url = isSales ? '/api/returns/customer' : '/api/returns/supplier/dispatch';
      const payload = isSales
        ? {
            salesInvoiceHeaderId: retDocId,
            receiptDate: localToday(),
            lines: [
              {
                salesInvoiceLineId: retLineId,
                quantity: Number(retQty),
                condition: Number(retCondition),
              },
            ],
            idempotencyKey: newIdempotencyKey(),
          }
        : {
            vendorId: partyId,
            purchaseInvoiceHeaderId: retDocId ? retDocId : null,
            dispatchDate: localToday(),
            hasControlTransferred: retControlTransferred,
            controlTransferDate: retControlTransferred ? localToday() : null,
            reason: retReason,
            lines: [
              {
                purchaseInvoiceLineId: retLineId ? retLineId : null,
                itemId: retItemId,
                quantity: Number(retQty),
              },
            ],
            idempotencyKey: newIdempotencyKey(),
          };
      const res = await authFetch(url, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(payload),
      });
      if (!res.ok) {
        setError(await readProblem(res));
        return;
      }
      setNotice(isSales ? 'Customer return receipt posted.' : 'Supplier return dispatch posted.');
      setModal(null);
      await load();
    } finally {
      setBusy(false);
    }
  };

  const title = side === 'sales' ? 'Customer Credits & Settlements' : 'Supplier Credits & Advances';
  const subtitle =
    side === 'sales'
      ? 'Issue commercial credits, apply to open invoices, record external refunds, record returns, and manage receivable write-offs without altering original invoices.'
      : 'Record supplier credits and advances, apply across bills, and track return clearing without altering approved bills.';

  return (
    <div className="space-y-6">
      <PageHeader
        title={title}
        subtitle={subtitle}
        actions={
          <div className="flex flex-wrap gap-2">
            <HeaderButton
              label={side === 'sales' ? 'New customer credit' : 'New supplier credit'}
              icon={Plus}
              variant="primary"
              onClick={() => {
                setPreviewSummary(null);
                setModal({ type: 'createCredit' });
              }}
            />
            {side === 'sales' ? (
              <>
                <HeaderButton label="Write off invoice" icon={FileWarning} onClick={() => setModal({ type: 'writeOff' })} />
                <HeaderButton label="Customer return" icon={RotateCcw} onClick={() => setModal({ type: 'physicalReturn' })} />
              </>
            ) : (
              <>
                <HeaderButton label="Supplier advance" icon={Plus} onClick={() => setModal({ type: 'supplierAdvance' })} />
                <HeaderButton label="Supplier return dispatch" icon={RotateCcw} onClick={() => setModal({ type: 'physicalReturn' })} />
              </>
            )}
          </div>
        }
      />
      {side === 'sales' ? (
        <SalesTabs orgSlug={currentOrg.slug} />
      ) : (
        <PurchasesTabs orgSlug={currentOrg.slug} />
      )}

      {notice && (
        <div role="status" className="rounded-lg border border-emerald-200 bg-emerald-50 p-4 text-sm text-emerald-900">
          {notice}
        </div>
      )}

      {error && (
        <div role="alert" className="rounded-lg border border-amber-200 bg-amber-50 p-4 text-sm text-amber-900">
          {error}
        </div>
      )}

      {mappings && (
        <div className="rounded-lg border border-slate-200 bg-white p-4 shadow-sm">
          <div className="flex flex-wrap items-center justify-between gap-2">
            <div>
              <h2 className="text-sm font-semibold text-slate-900">Control-Account Mapping Readiness</h2>
              <p className="mt-1 text-xs text-slate-500">
                Accounts are snapshotted at posting. Missing or archived mappings block posting until reviewed.
              </p>
            </div>
            <button
              type="button"
              disabled={busy}
              onClick={() => void handleSaveMappings()}
              className="inline-flex items-center gap-1.5 rounded bg-slate-900 px-3 py-1.5 text-xs font-medium text-white hover:bg-slate-800"
            >
              <Save className="w-3.5 h-3.5" /> Save Mappings
            </button>
          </div>
          <div className="mt-3 grid grid-cols-1 gap-2 sm:grid-cols-2 lg:grid-cols-3">
            {mappings.roles.map((r) => (
              <div key={r.roleKey} className="rounded border border-slate-100 bg-slate-50 p-2 text-xs space-y-1">
                <div className="font-medium text-slate-800">{r.roleLabel}</div>
                {r.configuredAccountCode ? (
                  <div className="text-emerald-700">
                    {r.configuredAccountCode} — {r.configuredAccountName}
                    {r.isArchived ? ' (Archived)' : ''}
                  </div>
                ) : (
                  <div className="text-amber-700">
                    Unconfigured{r.suggestedAccountCode ? ` (Suggested: ${r.suggestedAccountCode} ${r.suggestedAccountName})` : ''}
                  </div>
                )}
                {glAccounts.length > 0 && (
                  <select
                    aria-label={r.roleLabel}
                    value={mappingSelections[r.roleKey] ?? ''}
                    onChange={(e) => setMappingSelections((prev) => ({ ...prev, [r.roleKey]: e.target.value }))}
                    className="w-full rounded border border-slate-300 bg-white px-2 py-1 text-xs"
                  >
                    <option value="">Select account...</option>
                    {glAccounts.map((acc) => (
                      <option key={acc.id} value={acc.id}>
                        {acc.accountCode} — {acc.accountName}
                      </option>
                    ))}
                  </select>
                )}
              </div>
            ))}
          </div>
        </div>
      )}

      <div className="rounded-lg border border-slate-200 bg-white shadow-sm overflow-x-auto">
        <table className="min-w-full divide-y divide-slate-200 text-sm">
          <thead className="bg-slate-50">
            <tr>
              <th className="px-4 py-3 text-left font-medium text-slate-600">Number</th>
              <th className="px-4 py-3 text-left font-medium text-slate-600">{side === 'sales' ? 'Customer' : 'Vendor'}</th>
              <th className="px-4 py-3 text-left font-medium text-slate-600">Date</th>
              <th className="px-4 py-3 text-left font-medium text-slate-600">Status</th>
              <th className="px-4 py-3 text-right font-medium text-slate-600">Total</th>
              <th className="px-4 py-3 text-right font-medium text-slate-600">Applied</th>
              <th className="px-4 py-3 text-right font-medium text-slate-600">Refunded</th>
              <th className="px-4 py-3 text-right font-medium text-slate-600">Available</th>
              <th className="px-4 py-3 text-right font-medium text-slate-600">Actions</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-slate-100">
            {loading ? (
              <tr>
                <td colSpan={9} className="px-4 py-6 text-center text-slate-500">
                  Loading...
                </td>
              </tr>
            ) : credits.length === 0 ? (
              <tr>
                <td colSpan={9} className="px-4 py-6 text-center text-slate-500">
                  No credit notes recorded yet.
                </td>
              </tr>
            ) : (
              credits.map((c) => (
                <tr key={c.id} className="hover:bg-slate-50">
                  <td className="px-4 py-3 font-medium text-slate-900">{c.no ?? `Draft ${shortRef(c.id)}`}</td>
                  <td className="px-4 py-3 text-slate-700">{c.customerName ?? c.vendorName ?? ''}</td>
                  <td className="px-4 py-3 text-slate-600">{c.date.slice(0, 10)}</td>
                  <td className="px-4 py-3 text-slate-700">{c.settlementState}</td>
                  <td className="px-4 py-3 text-right font-mono">{c.totalAmount.toFixed(2)}</td>
                  <td className="px-4 py-3 text-right font-mono">{c.appliedAmount.toFixed(2)}</td>
                  <td className="px-4 py-3 text-right font-mono">{c.refundedAmount.toFixed(2)}</td>
                  <td className="px-4 py-3 text-right font-mono font-semibold">{c.availableAmount.toFixed(2)}</td>
                  <td className="px-4 py-3 text-right space-x-2 whitespace-nowrap">
                    {c.statusLabel === 'Draft' && (
                      <button
                        type="button"
                        disabled={busy}
                        onClick={() => void handleCreditAction(c, 'submit')}
                        className="text-xs font-medium text-indigo-600 hover:underline"
                      >
                        Submit
                      </button>
                    )}
                    {(c.statusLabel === 'Draft' || c.statusLabel === 'PendingReview') && (
                      <button
                        type="button"
                        disabled={busy}
                        onClick={() => void handleCreditAction(c, 'post')}
                        className="inline-flex items-center gap-1 text-xs font-medium text-emerald-700 hover:underline"
                      >
                        <CheckCircle2 className="w-3.5 h-3.5" /> Post
                      </button>
                    )}
                    {c.statusLabel === 'Posted' && c.availableAmount > 0 && (
                      <>
                        <button
                          type="button"
                          onClick={() => {
                            setApplyAmount(c.availableAmount.toFixed(2));
                            setModal({ type: 'applyCredit', credit: c });
                          }}
                          className="inline-flex items-center gap-1 text-xs font-medium text-blue-600 hover:underline"
                        >
                          <ArrowRightLeft className="w-3.5 h-3.5" /> Apply
                        </button>
                        <button
                          type="button"
                          onClick={() => {
                            setRefundAmount(c.availableAmount.toFixed(2));
                            setModal({ type: 'recordRefund', credit: c });
                          }}
                          className="text-xs font-medium text-amber-700 hover:underline"
                        >
                          Record Refund
                        </button>
                      </>
                    )}
                    {c.statusLabel === 'Posted' && (
                      <button
                        type="button"
                        onClick={() => setModal({ type: 'reverseCredit', credit: c })}
                        className="inline-flex items-center gap-1 text-xs font-medium text-rose-600 hover:underline"
                      >
                        <Undo2 className="w-3.5 h-3.5" /> Reverse
                      </button>
                    )}
                  </td>
                </tr>
              ))
            )}
          </tbody>
        </table>
      </div>

      {modal?.type === 'createCredit' && (
        <ModalShell open onClose={() => setModal(null)}>
          <div className="border-b border-slate-200 px-4 py-3 flex items-center justify-between">
            <h3 className="text-base font-semibold text-slate-900">
              {side === 'sales' ? 'New Customer Credit Note' : 'New Supplier Credit Note'}
            </h3>
            <button type="button" onClick={() => setModal(null)} className="text-slate-400 hover:text-slate-600">✕</button>
          </div>
          <form onSubmit={handleSaveCreditDraft} className="space-y-4 p-4">
            <div>
              <label htmlFor="credit-party" className="block text-xs font-medium text-slate-700">
                {side === 'sales' ? 'Customer' : 'Vendor'}
              </label>
              <select
                id="credit-party"
                required
                value={partyId}
                onChange={(e) => setPartyId(e.target.value)}
                className="mt-1 w-full rounded border border-slate-300 px-3 py-2 text-sm"
              >
                <option value="">Select {side === 'sales' ? 'customer' : 'vendor'}...</option>
                {parties.map((p) => (
                  <option key={p.id} value={p.id}>
                    {p.name}
                  </option>
                ))}
              </select>
            </div>
            <div>
              <label htmlFor="credit-source-doc" className="block text-xs font-medium text-slate-700">
                {side === 'sales' ? 'Source Invoice (optional for standalone)' : 'Source Bill (optional for standalone)'}
              </label>
              <select
                id="credit-source-doc"
                value={sourceDocId}
                onChange={(e) => {
                  const id = e.target.value;
                  setSourceDocId(id);
                  const found = allDocs.find((d) => String(d.id) === id);
                  if (found) setPartyId(String(found.partyId));
                }}
                className="mt-1 w-full rounded border border-slate-300 px-3 py-2 text-sm"
              >
                <option value="">Standalone credit (no source document)</option>
                {allDocs.map((d) => (
                  <option key={d.id} value={d.id}>
                    {d.no} — {d.partyName} (Due {d.balanceDue.toFixed(2)})
                  </option>
                ))}
              </select>
            </div>
            {side === 'purchasing' && (
              <div>
                <label htmlFor="vendor-credit-no" className="block text-xs font-medium text-slate-700">Vendor Credit Number</label>
                <input
                  id="vendor-credit-no"
                  required
                  value={vendorCreditNo}
                  onChange={(e) => setVendorCreditNo(e.target.value)}
                  className="mt-1 w-full rounded border border-slate-300 px-3 py-2 text-sm"
                />
              </div>
            )}
            <div>
              <label htmlFor="credit-reason" className="block text-xs font-medium text-slate-700">Reason / Evidence</label>
              <input
                id="credit-reason"
                required
                value={creditReason}
                onChange={(e) => setCreditReason(e.target.value)}
                className="mt-1 w-full rounded border border-slate-300 px-3 py-2 text-sm"
              />
            </div>
            <div className="grid grid-cols-3 gap-2">
              <div>
                <label htmlFor="credit-line-desc" className="block text-xs font-medium text-slate-700">Description</label>
                <input
                  id="credit-line-desc"
                  required
                  value={lineDesc}
                  onChange={(e) => setLineDesc(e.target.value)}
                  className="mt-1 w-full rounded border border-slate-300 px-3 py-2 text-sm"
                />
              </div>
              <div>
                <label htmlFor="credit-line-qty" className="block text-xs font-medium text-slate-700">Qty</label>
                <input
                  id="credit-line-qty"
                  required
                  type="number"
                  step="0.0001"
                  value={lineQty}
                  onChange={(e) => setLineQty(e.target.value)}
                  className="mt-1 w-full rounded border border-slate-300 px-3 py-2 text-sm"
                />
              </div>
              <div>
                <label htmlFor="credit-line-price" className="block text-xs font-medium text-slate-700">Unit Price / Cost</label>
                <input
                  id="credit-line-price"
                  required
                  type="number"
                  step="0.01"
                  value={linePrice}
                  onChange={(e) => setLinePrice(e.target.value)}
                  className="mt-1 w-full rounded border border-slate-300 px-3 py-2 text-sm"
                />
              </div>
            </div>
            {previewSummary && <div className="rounded bg-slate-50 p-2 text-xs text-slate-700">{previewSummary}</div>}
            <div className="flex justify-end gap-2">
              <button type="button" onClick={() => void handlePreviewCredit()} className="rounded border px-3 py-2 text-sm">
                Preview
              </button>
              <button type="submit" disabled={busy} className="rounded bg-indigo-600 px-4 py-2 text-sm font-medium text-white">
                Save Draft
              </button>
            </div>
          </form>
        </ModalShell>
      )}

      {modal?.type === 'applyCredit' && (
        <ModalShell open onClose={() => setModal(null)}>
          <div className="border-b border-slate-200 px-4 py-3 flex items-center justify-between">
            <h3 className="text-base font-semibold text-slate-900">
              {`Apply ${modal.credit.no ?? `Credit ${shortRef(modal.credit.id)}`}`}
            </h3>
            <button type="button" onClick={() => setModal(null)} className="text-slate-400 hover:text-slate-600">✕</button>
          </div>
          <form onSubmit={handleApplyCredit} className="space-y-4 p-4">
            <div>
              <label htmlFor="apply-target" className="block text-xs font-medium text-slate-700">
                {side === 'sales' ? 'Target Invoice' : 'Target Bill'}
              </label>
              <select
                id="apply-target"
                required
                value={applyTargetId}
                onChange={(e) => setApplyTargetId(e.target.value)}
                className="mt-1 w-full rounded border border-slate-300 px-3 py-2 text-sm"
              >
                <option value="">Select open document...</option>
                {openDocs.map((d) => (
                  <option key={d.id} value={d.id}>
                    {d.no} — {d.partyName} (Due {d.balanceDue.toFixed(2)})
                  </option>
                ))}
              </select>
            </div>
            <div>
              <label htmlFor="apply-amount" className="block text-xs font-medium text-slate-700">Amount to Apply</label>
              <input
                id="apply-amount"
                required
                type="number"
                step="0.01"
                value={applyAmount}
                onChange={(e) => setApplyAmount(e.target.value)}
                className="mt-1 w-full rounded border border-slate-300 px-3 py-2 text-sm"
              />
            </div>
            <div className="flex justify-end gap-2">
              <button type="submit" disabled={busy} className="rounded bg-indigo-600 px-4 py-2 text-sm font-medium text-white">
                Apply Credit
              </button>
            </div>
          </form>
        </ModalShell>
      )}

      {modal?.type === 'recordRefund' && (
        <ModalShell open onClose={() => setModal(null)}>
          <div className="border-b border-slate-200 px-4 py-3 flex items-center justify-between">
            <h3 className="text-base font-semibold text-slate-900">Record a refund already sent/received</h3>
            <button type="button" onClick={() => setModal(null)} className="text-slate-400 hover:text-slate-600">✕</button>
          </div>
          <form onSubmit={handleRecordRefund} className="space-y-4 p-4">
            <p className="text-xs text-slate-600">
              Record a refund already sent/received outside AccountGo. This posts the cash movement against your selected bank account and reduces the credit balance.
            </p>
            <div>
              <label htmlFor="refund-bank" className="block text-xs font-medium text-slate-700">Bank Account</label>
              <select
                id="refund-bank"
                required
                value={refundBankId}
                onChange={(e) => setRefundBankId(e.target.value)}
                className="mt-1 w-full rounded border border-slate-300 px-3 py-2 text-sm"
              >
                {banks.map((b) => (
                  <option key={b.id} value={b.id}>
                    {b.name}
                  </option>
                ))}
              </select>
            </div>
            <div className="grid grid-cols-2 gap-2">
              <div>
                <label htmlFor="refund-amount" className="block text-xs font-medium text-slate-700">Amount</label>
                <input
                  id="refund-amount"
                  required
                  type="number"
                  step="0.01"
                  value={refundAmount}
                  onChange={(e) => setRefundAmount(e.target.value)}
                  className="mt-1 w-full rounded border border-slate-300 px-3 py-2 text-sm"
                />
              </div>
              <div>
                <label htmlFor="refund-method" className="block text-xs font-medium text-slate-700">Payment Method</label>
                <input
                  id="refund-method"
                  required
                  value={refundMethod}
                  onChange={(e) => setRefundMethod(e.target.value)}
                  className="mt-1 w-full rounded border border-slate-300 px-3 py-2 text-sm"
                />
              </div>
            </div>
            <div>
              <label htmlFor="refund-ext-ref" className="block text-xs font-medium text-slate-700">External Bank Reference</label>
              <input
                id="refund-ext-ref"
                required
                value={refundExtRef}
                onChange={(e) => setRefundExtRef(e.target.value)}
                className="mt-1 w-full rounded border border-slate-300 px-3 py-2 text-sm"
              />
            </div>
            <div className="flex justify-end gap-2">
              <button type="submit" disabled={busy} className="rounded bg-indigo-600 px-4 py-2 text-sm font-medium text-white">
                Record Refund
              </button>
            </div>
          </form>
        </ModalShell>
      )}

      {modal?.type === 'reverseCredit' && (
        <ModalShell open onClose={() => setModal(null)}>
          <div className="border-b border-slate-200 px-4 py-3 flex items-center justify-between">
            <h3 className="text-base font-semibold text-slate-900">
              {`Reverse ${modal.credit.no ?? `Credit ${shortRef(modal.credit.id)}`}`}
            </h3>
            <button type="button" onClick={() => setModal(null)} className="text-slate-400 hover:text-slate-600">✕</button>
          </div>
          <form onSubmit={handleReverseCredit} className="space-y-4 p-4">
            <div>
              <label htmlFor="reverse-date" className="block text-xs font-medium text-slate-700">Reversal Date</label>
              <input
                id="reverse-date"
                required
                type="date"
                value={reverseDate}
                onChange={(e) => setReverseDate(e.target.value)}
                className="mt-1 w-full rounded border border-slate-300 px-3 py-2 text-sm"
              />
            </div>
            <div>
              <label htmlFor="reverse-reason" className="block text-xs font-medium text-slate-700">Reason</label>
              <input
                id="reverse-reason"
                required
                value={reverseReason}
                onChange={(e) => setReverseReason(e.target.value)}
                className="mt-1 w-full rounded border border-slate-300 px-3 py-2 text-sm"
              />
            </div>
            <div className="flex justify-end">
              <button type="submit" disabled={busy} className="rounded bg-rose-600 px-4 py-2 text-sm font-medium text-white">
                Confirm Reversal
              </button>
            </div>
          </form>
        </ModalShell>
      )}

      {modal?.type === 'writeOff' && (
        <ModalShell open onClose={() => setModal(null)}>
          <div className="border-b border-slate-200 px-4 py-3 flex items-center justify-between">
            <h3 className="text-base font-semibold text-slate-900">Receivable Write-Off</h3>
            <button type="button" onClick={() => setModal(null)} className="text-slate-400 hover:text-slate-600">✕</button>
          </div>
          <form onSubmit={handleWriteOff} className="space-y-4 p-4">
            <div>
              <label htmlFor="wo-invoice" className="block text-xs font-medium text-slate-700">Open Invoice</label>
              <select
                id="wo-invoice"
                required
                value={woInvoiceId}
                onChange={(e) => setWoInvoiceId(e.target.value)}
                className="mt-1 w-full rounded border border-slate-300 px-3 py-2 text-sm"
              >
                <option value="">Select open invoice...</option>
                {openDocs.map((d) => (
                  <option key={d.id} value={d.id}>
                    {d.no} — {d.partyName} (Due {d.balanceDue.toFixed(2)})
                  </option>
                ))}
              </select>
            </div>
            <div>
              <label htmlFor="wo-amount" className="block text-xs font-medium text-slate-700">Write-Off Amount</label>
              <input
                id="wo-amount"
                required
                type="number"
                step="0.01"
                value={woAmount}
                onChange={(e) => setWoAmount(e.target.value)}
                className="mt-1 w-full rounded border border-slate-300 px-3 py-2 text-sm"
              />
            </div>
            <div>
              <label htmlFor="wo-reason" className="block text-xs font-medium text-slate-700">Reason</label>
              <input
                id="wo-reason"
                required
                value={woReason}
                onChange={(e) => setWoReason(e.target.value)}
                className="mt-1 w-full rounded border border-slate-300 px-3 py-2 text-sm"
              />
            </div>
            <div>
              <label htmlFor="wo-evidence" className="block text-xs font-medium text-slate-700">Collection Evidence</label>
              <input
                id="wo-evidence"
                required
                value={woEvidence}
                onChange={(e) => setWoEvidence(e.target.value)}
                className="mt-1 w-full rounded border border-slate-300 px-3 py-2 text-sm"
              />
            </div>
            <div className="flex justify-end">
              <button type="submit" disabled={busy} className="rounded bg-indigo-600 px-4 py-2 text-sm font-medium text-white">
                Submit Write-Off
              </button>
            </div>
          </form>
        </ModalShell>
      )}

      {modal?.type === 'supplierAdvance' && (
        <ModalShell open onClose={() => setModal(null)}>
          <div className="border-b border-slate-200 px-4 py-3 flex items-center justify-between">
            <h3 className="text-base font-semibold text-slate-900">Record Supplier Advance</h3>
            <button type="button" onClick={() => setModal(null)} className="text-slate-400 hover:text-slate-600">✕</button>
          </div>
          <form onSubmit={handleSupplierAdvance} className="space-y-4 p-4">
            <div>
              <label htmlFor="adv-vendor" className="block text-xs font-medium text-slate-700">Vendor</label>
              <select
                id="adv-vendor"
                required
                value={advVendorId}
                onChange={(e) => setAdvVendorId(e.target.value)}
                className="mt-1 w-full rounded border border-slate-300 px-3 py-2 text-sm"
              >
                <option value="">Select vendor...</option>
                {parties.map((p) => (
                  <option key={p.id} value={p.id}>
                    {p.name}
                  </option>
                ))}
              </select>
            </div>
            <div>
              <label htmlFor="adv-bank" className="block text-xs font-medium text-slate-700">Bank Account</label>
              <select
                id="adv-bank"
                required
                value={advBankId}
                onChange={(e) => setAdvBankId(e.target.value)}
                className="mt-1 w-full rounded border border-slate-300 px-3 py-2 text-sm"
              >
                {banks.map((b) => (
                  <option key={b.id} value={b.id}>
                    {b.name}
                  </option>
                ))}
              </select>
            </div>
            <div>
              <label htmlFor="adv-amount" className="block text-xs font-medium text-slate-700">Amount</label>
              <input
                id="adv-amount"
                required
                type="number"
                step="0.01"
                value={advAmount}
                onChange={(e) => setAdvAmount(e.target.value)}
                className="mt-1 w-full rounded border border-slate-300 px-3 py-2 text-sm"
              />
            </div>
            <div>
              <label htmlFor="adv-purpose" className="block text-xs font-medium text-slate-700">Purpose</label>
              <input
                id="adv-purpose"
                required
                value={advPurpose}
                onChange={(e) => setAdvPurpose(e.target.value)}
                className="mt-1 w-full rounded border border-slate-300 px-3 py-2 text-sm"
              />
            </div>
            <div className="flex justify-end">
              <button type="submit" disabled={busy} className="rounded bg-indigo-600 px-4 py-2 text-sm font-medium text-white">
                Save &amp; Submit Advance
              </button>
            </div>
          </form>
        </ModalShell>
      )}

      {modal?.type === 'physicalReturn' && (
        <ModalShell open onClose={() => setModal(null)}>
          <div className="border-b border-slate-200 px-4 py-3 flex items-center justify-between">
            <h3 className="text-base font-semibold text-slate-900">
              {side === 'sales' ? 'Customer Return Receipt' : 'Supplier Return Dispatch'}
            </h3>
            <button type="button" onClick={() => setModal(null)} className="text-slate-400 hover:text-slate-600">✕</button>
          </div>
          <form onSubmit={handlePhysicalReturn} className="space-y-4 p-4">
            {side === 'purchasing' && (
              <div>
                <label htmlFor="ret-vendor" className="block text-xs font-medium text-slate-700">Vendor</label>
                <select
                  id="ret-vendor"
                  required
                  value={partyId}
                  onChange={(e) => setPartyId(e.target.value)}
                  className="mt-1 w-full rounded border border-slate-300 px-3 py-2 text-sm"
                >
                  <option value="">Select vendor...</option>
                  {parties.map((p) => (
                    <option key={p.id} value={p.id}>
                      {p.name}
                    </option>
                  ))}
                </select>
              </div>
            )}
            <div>
              <label htmlFor="ret-doc" className="block text-xs font-medium text-slate-700">
                {side === 'sales' ? 'Source Invoice' : 'Source Bill'}
              </label>
              <select
                id="ret-doc"
                required={side === 'sales'}
                value={retDocId}
                onChange={(e) => {
                  const id = e.target.value;
                  setRetDocId(id);
                  const found = allDocs.find((d) => String(d.id) === id);
                  if (found) setPartyId(String(found.partyId));
                }}
                className="mt-1 w-full rounded border border-slate-300 px-3 py-2 text-sm"
              >
                <option value="">Select {side === 'sales' ? 'invoice' : 'bill'}...</option>
                {allDocs.map((d) => (
                  <option key={d.id} value={d.id}>
                    {d.no} — {d.partyName}
                  </option>
                ))}
              </select>
            </div>
            <div className="grid grid-cols-2 gap-2">
              <div>
                <label htmlFor="ret-line" className="block text-xs font-medium text-slate-700">
                  {side === 'sales' ? 'Invoice Line' : 'Bill Line'}
                </label>
                <select
                  id="ret-line"
                  required={side === 'sales'}
                  value={retLineId}
                  onChange={(e) => {
                    const lineId = e.target.value;
                    setRetLineId(lineId);
                    const found = retDocLines.find((l) => String(l.id) === lineId);
                    if (found) {
                      setRetItemId(found.itemId ? String(found.itemId) : '');
                      setRetQty(String(found.quantity));
                    }
                  }}
                  className="mt-1 w-full rounded border border-slate-300 px-3 py-2 text-sm"
                >
                  <option value="">Select line...</option>
                  {retDocLines.map((l) => (
                    <option key={l.id} value={l.id}>
                      {l.description} (Qty {l.quantity})
                    </option>
                  ))}
                </select>
              </div>
              <div>
                <label htmlFor="ret-qty" className="block text-xs font-medium text-slate-700">Quantity</label>
                <input
                  id="ret-qty"
                  required
                  type="number"
                  step="0.0001"
                  value={retQty}
                  onChange={(e) => setRetQty(e.target.value)}
                  className="mt-1 w-full rounded border border-slate-300 px-3 py-2 text-sm"
                />
              </div>
            </div>
            {side === 'sales' ? (
              <div>
                <label htmlFor="ret-condition" className="block text-xs font-medium text-slate-700">Condition</label>
                <select
                  id="ret-condition"
                  value={retCondition}
                  onChange={(e) => setRetCondition(e.target.value)}
                  className="mt-1 w-full rounded border border-slate-300 px-3 py-2 text-sm"
                >
                  <option value="1">Saleable (Restore full proportional cost)</option>
                  <option value="2">Damaged (Recoverable cost)</option>
                  <option value="3">Zero Value (Track quantity only)</option>
                </select>
              </div>
            ) : (
              <>
                <label className="flex items-center gap-2 text-xs font-medium text-slate-700">
                  <input
                    type="checkbox"
                    checked={retControlTransferred}
                    onChange={(e) => setRetControlTransferred(e.target.checked)}
                  />
                  Control transferred to vendor (credit inventory &amp; debit Return Clearing)
                </label>
                <div>
                  <label htmlFor="ret-reason" className="block text-xs font-medium text-slate-700">Return Reason</label>
                  <input
                    id="ret-reason"
                    required
                    value={retReason}
                    onChange={(e) => setRetReason(e.target.value)}
                    className="mt-1 w-full rounded border border-slate-300 px-3 py-2 text-sm"
                  />
                </div>
              </>
            )}
            <div className="flex justify-end">
              <button type="submit" disabled={busy} className="rounded bg-indigo-600 px-4 py-2 text-sm font-medium text-white">
                Post Return
              </button>
            </div>
          </form>
        </ModalShell>
      )}
    </div>
  );
};
