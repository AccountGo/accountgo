// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

/**
 * Shared versioned field definitions, bounded validation constants, and financial review rules
 * for DocumentCapturePanel and its consumers (bills, invoices, bank accounts, assistant).
 */

export type DocumentCaptureKind =
  | 'expense-receipt'
  | 'purchase-invoice'
  | 'sales-invoice'
  | 'bank'
  | 'assistant-attachment';

export type FinancialCaptureKind = 'expense-receipt' | 'purchase-invoice' | 'sales-invoice';
export type PriceBasis = 'inclusive' | 'exclusive' | 'unknown';
export type PaymentHintStatus = 'paid' | 'unpaid' | 'partial' | 'unknown';

export const CURRENT_SCHEMA_VERSION = 1;
export const SUPPORTED_SCHEMA_VERSIONS: readonly number[] = [1];

export const MAX_UPLOAD_BYTES = 8 * 1024 * 1024;
export const MAX_PDF_PAGES = 10;
export const MAX_FINANCIAL_LINES = 100;
export const MAX_ASSISTANT_FIELDS = 64;
export const MAX_ASSISTANT_LABEL_LENGTH = 200;
export const MAX_TEXT_LENGTH = 2_000;
export const MAX_LINE_DESCRIPTION_LENGTH = 1_000;
export const MAX_VENDOR_INVOICE_NO_LENGTH = 50;
export const MAX_REFERENCE_NO_LENGTH = 100;
export const MAX_SALES_SOURCE_INVOICE_NO_LENGTH = 200;
export const MAX_SALES_REFERENCE_NO_LENGTH = 200;
export const MAX_BANK_ACCOUNT_NUMBER_LENGTH = 64;
export const MAX_BANK_ROUTING_NUMBER_LENGTH = 32;
export const MAX_BANK_IBAN_LENGTH = 64;
export const MAX_BANK_SWIFT_LENGTH = 32;
export const MAX_DECIMAL_SIGNIFICANT_DIGITS = 18;
export const MAX_DECIMAL_SCALE = 4;
export const LOW_CONFIDENCE_THRESHOLD = 0.75;

export const ALLOWED_CAPTURE_MIME_TYPES = [
  'image/jpeg',
  'image/png',
  'application/pdf',
] as const;

export interface CaptureFieldBox {
  page: number;
  x: number;
  y: number;
  width: number;
  height: number;
}

export interface CaptureLineField {
  description?: string | null;
  quantity?: string | null;
  unitAmount?: string | null;
  sourceLineTotal?: string | null;
  taxHint?: string | null;
  discount?: string | null;
  priceBasis?: PriceBasis | string | null;
  missingQuantityDefaulted?: boolean | null;
}

export interface CaptureSourceTotals {
  subtotal?: string | null;
  taxTotal?: string | null;
  grandTotal?: string | null;
  discountTotal?: string | null;
  additionalCharges?: string | null;
  priceBasis?: PriceBasis | string | null;
}

export interface CapturePaymentHints {
  paymentStatus?: PaymentHintStatus | string | null;
  amountPaid?: string | null;
  paymentMethod?: string | null;
}

export interface VendorCaptureFields {
  vendorName?: string | null;
  vendorInvoiceNo?: string | null;
  date?: string | null;
  dueDate?: string | null;
  referenceNo?: string | null;
  currency?: string | null;
  notes?: string | null;
  lines?: CaptureLineField[] | null;
  sourceTotals?: CaptureSourceTotals | null;
  paymentHints?: CapturePaymentHints | null;
  /** Fields an optional field mapper filled in, by path. A path that is absent was found by the rules. */
  fieldSources?: CaptureFieldSources | null;
}

/** Field path (for example `vendorName`, `sourceTotals.grandTotal`, `lines[2].description`) to where its value came from. */
export type CaptureFieldSources = Record<string, 'rules' | 'ai'>;

export interface SalesCaptureFields {
  customerName?: string | null;
  sourceInvoiceNo?: string | null;
  date?: string | null;
  dueDate?: string | null;
  referenceNo?: string | null;
  currency?: string | null;
  notes?: string | null;
  lines?: CaptureLineField[] | null;
  sourceTotals?: CaptureSourceTotals | null;
  fieldSources?: CaptureFieldSources | null;
}

export interface BankCaptureFields {
  name?: string | null;
  bankName?: string | null;
  accountNumber?: string | null;
  routingNumber?: string | null;
  iban?: string | null;
  swift?: string | null;
}

export interface AssistantCaptureFieldItem {
  label?: string | null;
  value?: string | null;
  confidence?: number | null;
  box?: CaptureFieldBox | null;
}

export interface AssistantCaptureFields {
  fields?: AssistantCaptureFieldItem[] | null;
}

export type AnyCaptureFields =
  | VendorCaptureFields
  | SalesCaptureFields
  | BankCaptureFields
  | AssistantCaptureFields;

export interface DocumentCaptureLinkView {
  targetType: 'bill' | 'invoice' | 'bank' | string;
  targetId: string;
  linkedAt: string;
  /** How the scanned tax was treated, derived by the server (for example `tax-rate:tax_…`, `in-cost`, `acknowledged`, `none`). */
  taxTreatment?: string | null;
  role?: 'source' | 'attachment' | string;
}

export type CaptureAiMappingStatus = 'pending' | 'done' | 'failed' | 'skipped';

export interface CaptureAiMappingView {
  status: CaptureAiMappingStatus;
  /** Field path to the value the rules found, for the fields the mapper changed. */
  ruleHints: Record<string, string>;
}

/** How often, and for how long, the review panel asks whether the background mapping has finished. */
export const AI_MAPPING_POLL_INTERVAL_MS = 1000;
export const AI_MAPPING_POLL_WINDOW_MS = 8000;

export interface DocumentCaptureView {
  id: string;
  kind: DocumentCaptureKind | string;
  schemaVersion: number;
  status: 'extracting' | 'ready' | 'failed' | 'accepted' | 'rejected' | string;
  contentType: string;
  byteCount: number;
  width: number | null;
  height: number | null;
  pageCount: number;
  fields: Record<string, unknown> | null;
  acceptedFields: Record<string, unknown> | null;
  warnings: string[] | null;
  errorCode: string | null;
  bytesAvailable: boolean;
  canAccept: boolean;
  canApply: boolean;
  providerName: string | null;
  providerVersion: string | null;
  sourceHash: string;
  contentHash: string;
  normalizationVersion: string;
  link: DocumentCaptureLinkView | null;
  /** Readable links for this capture, including role (`source` or `attachment`). */
  links?: DocumentCaptureLinkView[];
  /** The optional background field mapping. Absent or null when it isn't on for this capture. */
  aiMapping?: CaptureAiMappingView | null;
  createdAt: string;
  acceptedAt: string | null;
  rejectedAt: string | null;
}

export interface DocumentCaptureCapabilities {
  enabled: boolean;
  providerAvailable: boolean;
  kinds: DocumentCaptureKind[];
  schemaVersion: number;
  maxUploadBytes: number;
  maxPdfPages: number;
  maxImageWidth: number;
  maxImageHeight: number;
  maxImagePixels: number;
  allowedMimeTypes: string[];
}

export interface CaptureFieldDescriptor {
  key: string;
  label: string;
  type: 'text' | 'date' | 'currency' | 'textarea';
  maxLength?: number;
  requiredForAccept?: boolean;
  placeholder?: string;
}

export const KIND_LABELS: Record<DocumentCaptureKind, string> = {
  'expense-receipt': 'Expense receipt',
  'purchase-invoice': 'Supplier invoice',
  'sales-invoice': 'Sales reference invoice',
  bank: 'Bank letter or check',
  'assistant-attachment': 'Assistant attachment',
};

const VENDOR_FIELD_DESCRIPTORS_V1: readonly CaptureFieldDescriptor[] = [
  { key: 'vendorName', label: 'Vendor name', type: 'text', maxLength: MAX_TEXT_LENGTH, placeholder: 'Extracted vendor name' },
  { key: 'vendorInvoiceNo', label: "Vendor's bill #", type: 'text', maxLength: MAX_VENDOR_INVOICE_NO_LENGTH, placeholder: 'Invoice or receipt #' },
  { key: 'date', label: 'Document date', type: 'date', requiredForAccept: true },
  { key: 'dueDate', label: 'Due date', type: 'date' },
  { key: 'referenceNo', label: 'Reference / PO #', type: 'text', maxLength: MAX_REFERENCE_NO_LENGTH },
  { key: 'currency', label: 'Currency', type: 'currency', maxLength: 10, placeholder: 'e.g. USD' },
  { key: 'notes', label: 'Notes', type: 'textarea', maxLength: MAX_TEXT_LENGTH },
];

const SALES_FIELD_DESCRIPTORS_V1: readonly CaptureFieldDescriptor[] = [
  { key: 'customerName', label: 'Customer name', type: 'text', maxLength: MAX_TEXT_LENGTH },
  { key: 'sourceInvoiceNo', label: 'Source invoice #', type: 'text', maxLength: 200 },
  { key: 'date', label: 'Document date', type: 'date', requiredForAccept: true },
  { key: 'dueDate', label: 'Due date', type: 'date' },
  { key: 'referenceNo', label: 'Reference', type: 'text', maxLength: 200 },
  { key: 'currency', label: 'Currency', type: 'currency', maxLength: 10, placeholder: 'e.g. USD' },
  { key: 'notes', label: 'Notes', type: 'textarea', maxLength: MAX_TEXT_LENGTH },
];

const BANK_FIELD_DESCRIPTORS_V1: readonly CaptureFieldDescriptor[] = [
  { key: 'name', label: 'Account name', type: 'text', maxLength: MAX_TEXT_LENGTH },
  { key: 'bankName', label: 'Bank name', type: 'text', maxLength: MAX_TEXT_LENGTH },
  { key: 'accountNumber', label: 'Account number', type: 'text', maxLength: 64 },
  { key: 'routingNumber', label: 'Routing number', type: 'text', maxLength: 32 },
  { key: 'iban', label: 'IBAN', type: 'text', maxLength: 64 },
  { key: 'swift', label: 'SWIFT / BIC', type: 'text', maxLength: 32 },
];

export function isSupportedSchemaVersion(version: number | null | undefined): boolean {
  return typeof version === 'number' && SUPPORTED_SCHEMA_VERSIONS.includes(version);
}

export function getFieldDescriptors(kind: string, schemaVersion: number): readonly CaptureFieldDescriptor[] {
  if (!isSupportedSchemaVersion(schemaVersion)) return [];
  switch (kind) {
    case 'expense-receipt':
    case 'purchase-invoice':
      return VENDOR_FIELD_DESCRIPTORS_V1;
    case 'sales-invoice':
      return SALES_FIELD_DESCRIPTORS_V1;
    case 'bank':
      return BANK_FIELD_DESCRIPTORS_V1;
    default:
      return [];
  }
}

export function isFinancialKind(kind: string): kind is FinancialCaptureKind {
  return kind === 'expense-receipt' || kind === 'purchase-invoice' || kind === 'sales-invoice';
}

export function isVendorKind(kind: string): kind is 'expense-receipt' | 'purchase-invoice' {
  return kind === 'expense-receipt' || kind === 'purchase-invoice';
}

export function isSalesKind(kind: string): kind is 'sales-invoice' {
  return kind === 'sales-invoice';
}

export function isBankKind(kind: string): kind is 'bank' {
  return kind === 'bank';
}

export function isAssistantKind(kind: string): kind is 'assistant-attachment' {
  return kind === 'assistant-attachment';
}

/**
 * Formats a signed currency difference using the host page's currency formatter so
 * negative values render as "-$10.00" (never "$-10.00") and positive values as "+$10.00".
 */
export function formatSignedCurrencyDifference(
  diff: number,
  formatCurrency: (val: number) => string
): string {
  const absFormatted = formatCurrency(Math.abs(diff));
  if (diff > 0) return `+${absFormatted}`;
  if (diff < 0) return `-${absFormatted}`;
  return absFormatted;
}

/**
 * Produces a human-readable label for an already-linked capture target (vendor bill #, vendor,
 * source invoice #, or customer) without exposing the raw public ID in visible text.
 */
export function describeLinkedCaptureTarget(capture: DocumentCaptureView): string {
  const targetType = capture.link?.targetType ?? 'record';
  const raw = (capture.acceptedFields ?? capture.fields ?? {}) as Record<string, unknown>;
  const str = (key: string) => (typeof raw[key] === 'string' ? raw[key].trim() : '');

  if (targetType === 'bill') {
    const billNo = str('vendorInvoiceNo');
    const vendor = str('vendorName');
    if (billNo && vendor) return `bill #${billNo} from ${vendor}`;
    if (billNo) return `bill #${billNo}`;
    if (vendor) return `bill from ${vendor}`;
    return 'an existing bill';
  }

  if (targetType === 'invoice') {
    const invNo = str('sourceInvoiceNo');
    const customer = str('customerName');
    if (invNo && customer) return `invoice #${invNo} for ${customer}`;
    if (invNo) return `invoice #${invNo}`;
    if (customer) return `invoice for ${customer}`;
    return 'an existing invoice';
  }

  return `an existing ${targetType}`;
}

/**
 * Mirrors backend DocumentCaptureAccessPolicy for kind='bank':
 * requires banking.accounts.manage permission AND at least one of sales, purchases, or financials modules.
 */
export function canAccessBankCapture(
  hasPermission: (permission: string) => boolean,
  hasModule: (module: 'sales' | 'purchases' | 'financials') => boolean
): boolean {
  return (
    hasPermission('banking.accounts.manage') &&
    (hasModule('sales') || hasModule('purchases') || hasModule('financials'))
  );
}

export function isLowConfidence(confidence: number | null | undefined): boolean {
  return typeof confidence === 'number' && Number.isFinite(confidence) && confidence < LOW_CONFIDENCE_THRESHOLD;
}

export interface DecimalParseResult {
  valid: boolean;
  normalized: string | null;
  numeric: number | null;
  reason?: string;
}

/**
 * Strictly validates an invariant decimal string (no thousands separators, localized commas,
 * or currency symbols) so ambiguous OCR numbers are never silently reinterpreted.
 */
export function parseStrictDecimal(raw: string | null | undefined): DecimalParseResult {
  if (raw === null || raw === undefined) return { valid: true, normalized: null, numeric: null };
  const trimmed = raw.trim();
  if (trimmed === '') return { valid: true, normalized: null, numeric: null };

  if (/[,$€£¥\s]/.test(trimmed)) {
    return {
      valid: false,
      normalized: null,
      numeric: null,
      reason: 'Use a plain decimal number without commas or currency symbols (for example 1234.56).',
    };
  }

  if (!/^-?(?:0|[1-9][0-9]*)(?:\.[0-9]+)?$/.test(trimmed)) {
    return {
      valid: false,
      normalized: null,
      numeric: null,
      reason: 'Enter a valid decimal number (for example 12.50).',
    };
  }

  const unsigned = trimmed.startsWith('-') ? trimmed.slice(1) : trimmed;
  const dot = unsigned.indexOf('.');
  const fraction = dot >= 0 ? unsigned.slice(dot + 1) : '';
  if (fraction.length > MAX_DECIMAL_SCALE) {
    return {
      valid: false,
      normalized: null,
      numeric: null,
      reason: `At most ${MAX_DECIMAL_SCALE} decimal places are allowed.`,
    };
  }

  const digitsOnly = unsigned.replace('.', '').replace(/^0+/, '');
  if (digitsOnly.length > MAX_DECIMAL_SIGNIFICANT_DIGITS) {
    return {
      valid: false,
      normalized: null,
      numeric: null,
      reason: `At most ${MAX_DECIMAL_SIGNIFICANT_DIGITS} significant digits are allowed.`,
    };
  }

  const numeric = Number(trimmed);
  if (!Number.isFinite(numeric)) {
    return { valid: false, normalized: null, numeric: null, reason: 'Number is out of range.' };
  }

  return { valid: true, normalized: trimmed, numeric };
}

/** Validates an exact ISO calendar date (yyyy-MM-dd). Rejects ambiguous formats and invalid calendar dates. */
export function isValidIsoDate(value: string | null | undefined): boolean {
  if (!value) return false;
  const trimmed = value.trim();
  const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(trimmed);
  if (!match) return false;
  const year = Number(match[1]);
  const month = Number(match[2]);
  const day = Number(match[3]);
  const date = new Date(Date.UTC(year, month - 1, day));
  return (
    date.getUTCFullYear() === year &&
    date.getUTCMonth() === month - 1 &&
    date.getUTCDate() === day
  );
}

export function isValidCurrencyCode(value: string | null | undefined): boolean {
  if (!value || !value.trim()) return true;
  const trimmed = value.trim();
  return trimmed === 'unknown' || /^[A-Z]{3}$/.test(trimmed);
}

export interface EditableLineDraft {
  id: number;
  description: string;
  quantity: string;
  unitAmount: string;
  sourceLineTotal: string;
  taxHint: string;
  discount: string;
  priceBasis: string;
  missingQuantityDefaulted: boolean;
}

export interface VendorAcceptValidationResult {
  valid: boolean;
  errors: string[];
  payload: VendorCaptureFields;
}

/**
 * Validates and normalizes vendor review fields before POST /accept.
 * - Requires a valid ISO date.
 * - Defaults missing quantity to "1" with missingQuantityDefaulted = true.
 * - Refuses explicit zero/negative quantities.
 * - Flags amount-bearing lines with a blank description instead of silently dropping them.
 * - Removes only completely blank lines.
 * - Rejects ambiguous/localized numbers and overlong text.
 */
export function validateVendorFieldsForAccept(
  header: {
    vendorName: string;
    vendorInvoiceNo: string;
    date: string;
    dueDate: string;
    referenceNo: string;
    currency: string;
    notes: string;
  },
  lines: EditableLineDraft[],
  sourceTotals?: CaptureSourceTotals | null,
  paymentHints?: CapturePaymentHints | null
): VendorAcceptValidationResult {
  const errors: string[] = [];

  if (!isValidIsoDate(header.date)) {
    errors.push('Enter a valid document date in YYYY-MM-DD format before accepting.');
  }
  if (header.dueDate.trim() && !isValidIsoDate(header.dueDate)) {
    errors.push('Due date must be in YYYY-MM-DD format or left blank.');
  }
  if (header.vendorName.trim().length > MAX_TEXT_LENGTH) {
    errors.push(`Vendor name can be at most ${MAX_TEXT_LENGTH} characters.`);
  }
  if (header.vendorInvoiceNo.trim().length > MAX_VENDOR_INVOICE_NO_LENGTH) {
    errors.push(`Vendor's bill # can be at most ${MAX_VENDOR_INVOICE_NO_LENGTH} characters.`);
  }
  if (header.referenceNo.trim().length > MAX_REFERENCE_NO_LENGTH) {
    errors.push(`Reference / PO # can be at most ${MAX_REFERENCE_NO_LENGTH} characters.`);
  }
  if (header.notes.trim().length > MAX_TEXT_LENGTH) {
    errors.push(`Notes can be at most ${MAX_TEXT_LENGTH} characters.`);
  }
  const normalizedCurrency = header.currency.trim()
    ? header.currency.trim().toLowerCase() === 'unknown'
      ? 'unknown'
      : header.currency.trim().toUpperCase()
    : null;
  if (normalizedCurrency && !isValidCurrencyCode(normalizedCurrency)) {
    errors.push('Currency must be a 3-letter ISO code (for example USD) or "unknown".');
  }

  const keptLines: CaptureLineField[] = [];
  if (lines.length > MAX_FINANCIAL_LINES) {
    errors.push(`At most ${MAX_FINANCIAL_LINES} lines can be accepted.`);
  }

  lines.forEach((line, idx) => {
    const label = `Line ${idx + 1}`;
    const desc = line.description.trim();
    const qtyRaw = line.quantity.trim();
    const amtRaw = line.unitAmount.trim();
    const srcTotalRaw = line.sourceLineTotal.trim();
    const discRaw = line.discount.trim();
    const taxHint = line.taxHint.trim();

    const isBlankLine = !desc && !qtyRaw && !amtRaw && !srcTotalRaw && !discRaw && !taxHint;
    if (isBlankLine) return;

    if (!desc && (amtRaw || srcTotalRaw)) {
      errors.push(`${label} has an amount but no description. Add a description or clear the line.`);
    }
    if (desc.length > MAX_LINE_DESCRIPTION_LENGTH) {
      errors.push(`${label} description can be at most ${MAX_LINE_DESCRIPTION_LENGTH} characters.`);
    }

    let finalQty = '1';
    let missingQtyDefaulted = line.missingQuantityDefaulted;
    if (!qtyRaw) {
      finalQty = '1';
      missingQtyDefaulted = true;
    } else {
      const parsedQty = parseStrictDecimal(qtyRaw);
      if (!parsedQty.valid || parsedQty.numeric === null) {
        errors.push(`${label} quantity: ${parsedQty.reason ?? 'Enter a valid quantity.'}`);
      } else if (parsedQty.numeric <= 0) {
        errors.push(`${label} quantity must be greater than zero.`);
      } else {
        finalQty = parsedQty.normalized!;
      }
    }

    if (!amtRaw) {
      errors.push(`${label} requires a unit amount (no zero is invented automatically).`);
    }
    const parsedAmt = parseStrictDecimal(amtRaw);
    if (amtRaw && !parsedAmt.valid) {
      errors.push(`${label} unit amount: ${parsedAmt.reason}`);
    }

    const parsedSrcTotal = parseStrictDecimal(srcTotalRaw);
    if (srcTotalRaw && !parsedSrcTotal.valid) {
      errors.push(`${label} source line total: ${parsedSrcTotal.reason}`);
    }

    const parsedDisc = parseStrictDecimal(discRaw);
    if (discRaw && (!parsedDisc.valid || (parsedDisc.numeric !== null && (parsedDisc.numeric < 0 || parsedDisc.numeric > 100)))) {
      errors.push(`${label} discount must be between 0 and 100.`);
    }

    const normalizedLine: CaptureLineField = {};
    if (desc) normalizedLine.description = desc;
    normalizedLine.quantity = finalQty;
    if (parsedAmt.normalized !== null) normalizedLine.unitAmount = parsedAmt.normalized;
    if (parsedSrcTotal.normalized !== null) normalizedLine.sourceLineTotal = parsedSrcTotal.normalized;
    if (taxHint) normalizedLine.taxHint = taxHint.slice(0, 200);
    if (parsedDisc.normalized !== null) normalizedLine.discount = parsedDisc.normalized;
    if (line.priceBasis) normalizedLine.priceBasis = line.priceBasis;
    if (missingQtyDefaulted) normalizedLine.missingQuantityDefaulted = true;
    keptLines.push(normalizedLine);
  });

  const payload: VendorCaptureFields = {
    date: header.date.trim(),
  };
  if (header.vendorName.trim()) payload.vendorName = header.vendorName.trim();
  if (header.vendorInvoiceNo.trim()) payload.vendorInvoiceNo = header.vendorInvoiceNo.trim();
  if (header.dueDate.trim()) payload.dueDate = header.dueDate.trim();
  if (header.referenceNo.trim()) payload.referenceNo = header.referenceNo.trim();
  if (normalizedCurrency) payload.currency = normalizedCurrency;
  if (header.notes.trim()) payload.notes = header.notes.trim();
  if (keptLines.length > 0) payload.lines = keptLines;
  if (sourceTotals && Object.values(sourceTotals).some((v) => v !== null && v !== undefined && v !== '')) {
    const cleanTotals: CaptureSourceTotals = {};
    for (const key of ['subtotal', 'taxTotal', 'grandTotal', 'discountTotal', 'additionalCharges'] as const) {
      const val = sourceTotals[key];
      if (typeof val === 'string' && val.trim() !== '') {
        const parsed = parseStrictDecimal(val);
        if (parsed.valid && parsed.normalized !== null) cleanTotals[key] = parsed.normalized;
      }
    }
    if (sourceTotals.priceBasis) cleanTotals.priceBasis = sourceTotals.priceBasis;
    if (Object.keys(cleanTotals).length > 0) payload.sourceTotals = cleanTotals;
  }
  if (paymentHints && Object.values(paymentHints).some((v) => v !== null && v !== undefined && v !== '')) {
    const cleanHints: CapturePaymentHints = {};
    if (paymentHints.paymentStatus) cleanHints.paymentStatus = paymentHints.paymentStatus;
    if (typeof paymentHints.amountPaid === 'string' && paymentHints.amountPaid.trim() !== '') {
      const parsed = parseStrictDecimal(paymentHints.amountPaid);
      if (parsed.valid && parsed.normalized !== null) cleanHints.amountPaid = parsed.normalized;
    }
    if (paymentHints.paymentMethod) cleanHints.paymentMethod = paymentHints.paymentMethod.slice(0, 100);
    if (Object.keys(cleanHints).length > 0) payload.paymentHints = cleanHints;
  }

  return { valid: errors.length === 0, errors, payload };
}

export interface SalesAcceptValidationResult {
  valid: boolean;
  errors: string[];
  payload: SalesCaptureFields;
}

/**
 * Validates and normalizes sales-invoice review fields before POST /accept,
 * mirroring DocumentCaptureFieldSchemas.SalesFields exactly (no paymentHints).
 */
export function validateSalesFieldsForAccept(
  header: {
    customerName: string;
    sourceInvoiceNo: string;
    date: string;
    dueDate: string;
    referenceNo: string;
    currency: string;
    notes: string;
  },
  lines: EditableLineDraft[],
  sourceTotals?: CaptureSourceTotals | null
): SalesAcceptValidationResult {
  const errors: string[] = [];

  if (!isValidIsoDate(header.date)) {
    errors.push('Enter a valid document date in YYYY-MM-DD format before accepting.');
  }
  if (header.dueDate.trim() && !isValidIsoDate(header.dueDate)) {
    errors.push('Due date must be in YYYY-MM-DD format or left blank.');
  }
  if (header.customerName.trim().length > MAX_TEXT_LENGTH) {
    errors.push(`Customer name can be at most ${MAX_TEXT_LENGTH} characters.`);
  }
  if (header.sourceInvoiceNo.trim().length > MAX_SALES_SOURCE_INVOICE_NO_LENGTH) {
    errors.push(`Source invoice # can be at most ${MAX_SALES_SOURCE_INVOICE_NO_LENGTH} characters.`);
  }
  if (header.referenceNo.trim().length > MAX_SALES_REFERENCE_NO_LENGTH) {
    errors.push(`Reference can be at most ${MAX_SALES_REFERENCE_NO_LENGTH} characters.`);
  }
  if (header.notes.trim().length > MAX_TEXT_LENGTH) {
    errors.push(`Notes can be at most ${MAX_TEXT_LENGTH} characters.`);
  }
  const normalizedCurrency = header.currency.trim()
    ? header.currency.trim().toLowerCase() === 'unknown'
      ? 'unknown'
      : header.currency.trim().toUpperCase()
    : null;
  if (normalizedCurrency && !isValidCurrencyCode(normalizedCurrency)) {
    errors.push('Currency must be a 3-letter ISO code (for example USD) or "unknown".');
  }

  const keptLines: CaptureLineField[] = [];
  if (lines.length > MAX_FINANCIAL_LINES) {
    errors.push(`At most ${MAX_FINANCIAL_LINES} lines can be accepted.`);
  }

  lines.forEach((line, idx) => {
    const label = `Line ${idx + 1}`;
    const desc = line.description.trim();
    const qtyRaw = line.quantity.trim();
    const amtRaw = line.unitAmount.trim();
    const srcTotalRaw = line.sourceLineTotal.trim();
    const discRaw = line.discount.trim();
    const taxHint = line.taxHint.trim();

    const isBlankLine = !desc && !qtyRaw && !amtRaw && !srcTotalRaw && !discRaw && !taxHint;
    if (isBlankLine) return;

    if (!desc && (amtRaw || srcTotalRaw)) {
      errors.push(`${label} has an amount but no description. Add a description or clear the line.`);
    }
    if (desc.length > MAX_LINE_DESCRIPTION_LENGTH) {
      errors.push(`${label} description can be at most ${MAX_LINE_DESCRIPTION_LENGTH} characters.`);
    }

    let finalQty = '1';
    let missingQtyDefaulted = line.missingQuantityDefaulted;
    if (!qtyRaw) {
      finalQty = '1';
      missingQtyDefaulted = true;
    } else {
      const parsedQty = parseStrictDecimal(qtyRaw);
      if (!parsedQty.valid || parsedQty.numeric === null) {
        errors.push(`${label} quantity: ${parsedQty.reason ?? 'Enter a valid quantity.'}`);
      } else if (parsedQty.numeric <= 0) {
        errors.push(`${label} quantity must be greater than zero.`);
      } else {
        finalQty = parsedQty.normalized!;
      }
    }

    if (!amtRaw) {
      errors.push(`${label} requires a unit amount (no zero is invented automatically).`);
    }
    const parsedAmt = parseStrictDecimal(amtRaw);
    if (amtRaw && !parsedAmt.valid) {
      errors.push(`${label} unit amount: ${parsedAmt.reason}`);
    }

    const parsedSrcTotal = parseStrictDecimal(srcTotalRaw);
    if (srcTotalRaw && !parsedSrcTotal.valid) {
      errors.push(`${label} source line total: ${parsedSrcTotal.reason}`);
    }

    const parsedDisc = parseStrictDecimal(discRaw);
    if (discRaw && (!parsedDisc.valid || (parsedDisc.numeric !== null && (parsedDisc.numeric < 0 || parsedDisc.numeric > 100)))) {
      errors.push(`${label} discount must be between 0 and 100.`);
    }

    const normalizedLine: CaptureLineField = {};
    if (desc) normalizedLine.description = desc;
    normalizedLine.quantity = finalQty;
    if (parsedAmt.normalized !== null) normalizedLine.unitAmount = parsedAmt.normalized;
    if (parsedSrcTotal.normalized !== null) normalizedLine.sourceLineTotal = parsedSrcTotal.normalized;
    if (taxHint) normalizedLine.taxHint = taxHint.slice(0, 200);
    if (parsedDisc.normalized !== null) normalizedLine.discount = parsedDisc.normalized;
    if (line.priceBasis) normalizedLine.priceBasis = line.priceBasis;
    if (missingQtyDefaulted) normalizedLine.missingQuantityDefaulted = true;
    keptLines.push(normalizedLine);
  });

  const payload: SalesCaptureFields = {
    date: header.date.trim(),
  };
  if (header.customerName.trim()) payload.customerName = header.customerName.trim();
  if (header.sourceInvoiceNo.trim()) payload.sourceInvoiceNo = header.sourceInvoiceNo.trim();
  if (header.dueDate.trim()) payload.dueDate = header.dueDate.trim();
  if (header.referenceNo.trim()) payload.referenceNo = header.referenceNo.trim();
  if (normalizedCurrency) payload.currency = normalizedCurrency;
  if (header.notes.trim()) payload.notes = header.notes.trim();
  if (keptLines.length > 0) payload.lines = keptLines;
  if (sourceTotals && Object.values(sourceTotals).some((v) => v !== null && v !== undefined && v !== '')) {
    const cleanTotals: CaptureSourceTotals = {};
    for (const key of ['subtotal', 'taxTotal', 'grandTotal', 'discountTotal', 'additionalCharges'] as const) {
      const val = sourceTotals[key];
      if (typeof val === 'string' && val.trim() !== '') {
        const parsed = parseStrictDecimal(val);
        if (parsed.valid && parsed.normalized !== null) cleanTotals[key] = parsed.normalized;
      }
    }
    if (sourceTotals.priceBasis) cleanTotals.priceBasis = sourceTotals.priceBasis;
    if (Object.keys(cleanTotals).length > 0) payload.sourceTotals = cleanTotals;
  }

  return { valid: errors.length === 0, errors, payload };
}

export interface BankAcceptValidationResult {
  valid: boolean;
  errors: string[];
  payload: BankCaptureFields;
}

/**
 * Validates and normalizes bank review fields before POST /accept,
 * mirroring DocumentCaptureFieldSchemas.BankFields exactly (no opening balance or date).
 */
export function validateBankFieldsForAccept(header: {
  name: string;
  bankName: string;
  accountNumber: string;
  routingNumber: string;
  iban: string;
  swift: string;
}): BankAcceptValidationResult {
  const errors: string[] = [];

  const name = header.name.trim();
  const bankName = header.bankName.trim();
  const accountNumber = header.accountNumber.trim();
  const routingNumber = header.routingNumber.trim();
  const iban = header.iban.trim();
  const swift = header.swift.trim();

  if (name.length > MAX_TEXT_LENGTH) {
    errors.push(`Account name can be at most ${MAX_TEXT_LENGTH} characters.`);
  }
  if (bankName.length > MAX_TEXT_LENGTH) {
    errors.push(`Bank name can be at most ${MAX_TEXT_LENGTH} characters.`);
  }
  if (accountNumber.length > MAX_BANK_ACCOUNT_NUMBER_LENGTH) {
    errors.push(`Account number can be at most ${MAX_BANK_ACCOUNT_NUMBER_LENGTH} characters.`);
  }
  if (routingNumber.length > MAX_BANK_ROUTING_NUMBER_LENGTH) {
    errors.push(`Routing number can be at most ${MAX_BANK_ROUTING_NUMBER_LENGTH} characters.`);
  }
  if (iban.length > MAX_BANK_IBAN_LENGTH) {
    errors.push(`IBAN can be at most ${MAX_BANK_IBAN_LENGTH} characters.`);
  }
  if (swift.length > MAX_BANK_SWIFT_LENGTH) {
    errors.push(`SWIFT / BIC can be at most ${MAX_BANK_SWIFT_LENGTH} characters.`);
  }

  const payload: BankCaptureFields = {};
  if (name) payload.name = name;
  if (bankName) payload.bankName = bankName;
  if (accountNumber) payload.accountNumber = accountNumber;
  if (routingNumber) payload.routingNumber = routingNumber;
  if (iban) payload.iban = iban;
  if (swift) payload.swift = swift;

  return { valid: errors.length === 0, errors, payload };
}

export interface EditableAssistantFieldDraft {
  id: number;
  label: string;
  value: string;
  confidence: number | null;
  box: CaptureFieldBox | null;
}

export interface AssistantAcceptValidationResult {
  valid: boolean;
  errors: string[];
  payload: AssistantCaptureFields;
}

/**
 * Validates and normalizes assistant-attachment review fields before POST /accept,
 * mirroring DocumentCaptureFieldSchemas.ValidateAssistantFields (max 64 fields, label<=200, value<=2000).
 */
export function validateAssistantFieldsForAccept(
  items: EditableAssistantFieldDraft[]
): AssistantAcceptValidationResult {
  const errors: string[] = [];
  if (items.length > MAX_ASSISTANT_FIELDS) {
    errors.push(`At most ${MAX_ASSISTANT_FIELDS} attachment fields can be accepted.`);
  }

  const kept: AssistantCaptureFieldItem[] = [];
  items.forEach((item, idx) => {
    const label = item.label.trim();
    const value = item.value.trim();
    if (!label && !value) return;

    if (label.length > MAX_ASSISTANT_LABEL_LENGTH) {
      errors.push(`Field ${idx + 1} label can be at most ${MAX_ASSISTANT_LABEL_LENGTH} characters.`);
    }
    if (value.length > MAX_TEXT_LENGTH) {
      errors.push(`Field ${idx + 1} value can be at most ${MAX_TEXT_LENGTH} characters.`);
    }

    const normalized: AssistantCaptureFieldItem = {};
    if (label) normalized.label = label;
    if (value) normalized.value = value;
    if (typeof item.confidence === 'number' && Number.isFinite(item.confidence) && item.confidence >= 0 && item.confidence <= 1) {
      normalized.confidence = item.confidence;
    }
    if (item.box) {
      normalized.box = item.box;
    }
    kept.push(normalized);
  });

  return {
    valid: errors.length === 0,
    errors,
    payload: { fields: kept },
  };
}

export interface FinancialApplyGates {
  canApplyAll: boolean;
  canApplyMonetary: boolean;
  canApplyNonMonetary: boolean;
  unsupportedClass: boolean;
  multipleDocuments: boolean;
  foreignCurrency: boolean;
  ambiguousCurrency: boolean;
  unknownSchema: boolean;
  bytesMissing: boolean;
  sourceCurrency: string | null;
  priceBasis: PriceBasis;
  reasons: string[];
}

/**
 * Evaluates document-class, multi-document, currency, schema, and media gates
 * from the backend DocumentCaptureView warnings and canonical field values only.
 */
export function evaluateFinancialApplyGates(
  capture: DocumentCaptureView,
  orgCurrency: string | null | undefined,
  ambiguousCurrencyConfirmed = false
): FinancialApplyGates {
  const fields = (capture.acceptedFields ?? capture.fields ?? {}) as VendorCaptureFields;
  const warnings = new Set(capture.warnings ?? []);
  const reasons: string[] = [];

  const unknownSchema = !isSupportedSchemaVersion(capture.schemaVersion);
  if (unknownSchema) {
    reasons.push(`Unsupported capture schema version (${capture.schemaVersion}). Review only; automatic application is disabled.`);
  }

  const bytesMissing = !capture.bytesAvailable;
  if (bytesMissing) {
    reasons.push('The supporting file bytes are no longer available, so this capture cannot be applied.');
  }

  const unsupportedClass = warnings.has('UNSUPPORTED_DOCUMENT_CLASS');
  if (unsupportedClass) {
    reasons.push('This document appears to be a credit note, refund, or statement rather than an ordinary bill. Use Supplier Credits or Banking instead.');
  }

  const multipleDocuments = warnings.has('MULTIPLE_DOCUMENTS');
  if (multipleDocuments) {
    reasons.push('Multiple documents were detected in this file. Upload one document per bill rather than applying combined lines.');
  }

  const rawCurrency = typeof fields.currency === 'string' ? fields.currency.trim() : null;
  const normalizedOrg = orgCurrency?.trim().toUpperCase() || null;
  const sourceCurrency = rawCurrency ? (rawCurrency.toLowerCase() === 'unknown' ? 'unknown' : rawCurrency.toUpperCase()) : null;

  const foreignCurrency =
    warnings.has('FOREIGN_CURRENCY') ||
    (sourceCurrency !== null && sourceCurrency !== 'unknown' && normalizedOrg !== null && sourceCurrency !== normalizedOrg);
  if (foreignCurrency) {
    reasons.push(
      `Document currency (${sourceCurrency ?? 'foreign'}) does not match workspace currency (${normalizedOrg ?? 'default'}). Monetary amounts cannot be applied automatically; enter converted amounts manually.`
    );
  }

  const ambiguousCurrency =
    !foreignCurrency &&
    (warnings.has('AMBIGUOUS_CURRENCY') || sourceCurrency === 'unknown');
  if (ambiguousCurrency && !ambiguousCurrencyConfirmed) {
    reasons.push(`Document currency is ambiguous. Confirm that amounts are in ${normalizedOrg ?? 'your workspace currency'} before applying them.`);
  }

  const rawBasis = fields.sourceTotals?.priceBasis;
  const priceBasis: PriceBasis =
    rawBasis === 'inclusive' || rawBasis === 'exclusive' ? rawBasis : 'unknown';

  const blockedEntirely = unknownSchema || bytesMissing || unsupportedClass || multipleDocuments;
  const canApplyNonMonetary = !blockedEntirely;
  const canApplyMonetary = canApplyNonMonetary && !foreignCurrency && (!ambiguousCurrency || ambiguousCurrencyConfirmed);

  return {
    canApplyAll: canApplyMonetary,
    canApplyMonetary,
    canApplyNonMonetary,
    unsupportedClass,
    multipleDocuments,
    foreignCurrency,
    ambiguousCurrency,
    unknownSchema,
    bytesMissing,
    sourceCurrency,
    priceBasis,
    reasons,
  };
}

export type PartyMatchReason = 'alias' | 'identifier' | 'exact' | 'name';

export interface PartyMatchCandidate {
  partyId: string;
  name: string;
  reason: PartyMatchReason;
  score: number;
  archived: boolean;
}

export function describePartyMatchReason(reason: PartyMatchReason): string {
  switch (reason) {
    case 'alias':
      return 'matched before';
    case 'identifier':
      return 'same email/phone';
    case 'exact':
      return 'exact name';
    case 'name':
    default:
      return 'similar name';
  }
}

/**
 * Only a single non-archived alias, identifier, or exact-name candidate may be pre-selected.
 * Fuzzy/normalized name-only candidates and archived parties are never pre-selected (M-05, M-06, M-07).
 */
export function resolveAutoPreselectCandidate(
  candidates: readonly PartyMatchCandidate[]
): PartyMatchCandidate | null {
  const strong = candidates.filter(
    (c) => !c.archived && (c.reason === 'alias' || c.reason === 'identifier' || c.reason === 'exact')
  );
  return strong.length === 1 ? strong[0] : null;
}

export async function fetchPartyMatchCandidates(
  authFetch: (input: string, init?: RequestInit) => Promise<Response>,
  role: 'vendor' | 'customer',
  captureId: string
): Promise<PartyMatchCandidate[]> {
  if (!captureId) return [];
  try {
    const res = await authFetch(
      `/api/document-captures/party-match?role=${encodeURIComponent(role)}&captureId=${encodeURIComponent(captureId)}`
    );
    if (!res.ok) return [];
    const data = await res.json();
    if (!Array.isArray(data)) return [];
    return data
      .filter(
        (item): item is PartyMatchCandidate =>
          item &&
          typeof item.partyId === 'string' &&
          typeof item.name === 'string' &&
          (item.reason === 'alias' ||
            item.reason === 'identifier' ||
            item.reason === 'exact' ||
            item.reason === 'name') &&
          typeof item.score === 'number' &&
          typeof item.archived === 'boolean'
      )
      .slice(0, 3);
  } catch {
    return [];
  }
}

export interface CaptureDiscrepancy {
  sourceGrandTotal: number | null;
  formTotal: number;
  difference: number | null;
  hasDiscrepancy: boolean;
}

const round2 = (v: number) => Math.round((v + Number.EPSILON) * 100) / 100;

export function computeCaptureDiscrepancy(
  sourceTotals: CaptureSourceTotals | null | undefined,
  formTotal: number
): CaptureDiscrepancy {
  const parsed = parseStrictDecimal(sourceTotals?.grandTotal ?? null);
  if (!parsed.valid || parsed.numeric === null) {
    return { sourceGrandTotal: null, formTotal: round2(formTotal), difference: null, hasDiscrepancy: false };
  }
  const source = round2(parsed.numeric);
  const form = round2(formTotal);
  const diff = round2(form - source);
  return {
    sourceGrandTotal: source,
    formTotal: form,
    difference: diff,
    hasDiscrepancy: Math.abs(diff) > 0.005,
  };
}

export interface CaptureErrorPresentation {
  code: string;
  message: string;
  canRetryUpload: boolean;
  canCheckStatus: boolean;
  canRetryAccept: boolean;
}

/** Maps every §6 ProblemDetails error code to clear, safe, user-facing guidance. */
export function describeCaptureError(
  code: string | null | undefined,
  status?: number,
  retryAfterSeconds?: number | null
): CaptureErrorPresentation {
  const normalized = (code || '').trim();
  switch (normalized) {
    case 'DOCUMENT_EMPTY':
      return { code: normalized, message: 'The selected file is empty. Choose a valid JPEG, PNG, or PDF file.', canRetryUpload: false, canCheckStatus: false, canRetryAccept: false };
    case 'DOCUMENT_TOO_LARGE':
      return { code: normalized, message: 'The file exceeds the 8 MB limit (or the normalized image exceeds 1.5 MB). Choose a smaller file.', canRetryUpload: false, canCheckStatus: false, canRetryAccept: false };
    case 'DOCUMENT_UNSUPPORTED_TYPE':
      return { code: normalized, message: 'Unsupported file type, encrypted PDF, or PDF over 10 pages. Upload a JPEG, PNG, or unencrypted PDF (1–10 pages).', canRetryUpload: false, canCheckStatus: false, canRetryAccept: false };
    case 'DOCUMENT_TOO_COMPLEX':
      return { code: normalized, message: 'This document exceeds the reading limits. Split it into smaller files or enter the details manually.', canRetryUpload: false, canCheckStatus: false, canRetryAccept: false };
    case 'DOCUMENT_MALFORMED':
    case 'DOCUMENT_DIMENSIONS':
    case 'DOCUMENT_MEDIA_MISMATCH':
      return { code: normalized, message: 'The file is malformed or exceeds image dimension limits (max 8,000 px / 24 MP).', canRetryUpload: false, canCheckStatus: false, canRetryAccept: false };
    case 'DOCUMENT_UNREADABLE':
    case 'DOCUMENT_PROVIDER_OUTPUT_INVALID':
      return { code: normalized, message: 'The document could not be read clearly. You can retry extraction or enter the details manually.', canRetryUpload: true, canCheckStatus: true, canRetryAccept: false };
    case 'DOCUMENT_PROVIDER_TIMEOUT':
      return { code: normalized, message: 'Extraction timed out. Check status first to see if it finished, or retry manually.', canRetryUpload: true, canCheckStatus: true, canRetryAccept: false };
    case 'DOCUMENT_PROVIDER_UNAVAILABLE':
      return { code: normalized, message: 'The document reader is temporarily unavailable. Wait a moment and retry, or enter the details manually.', canRetryUpload: true, canCheckStatus: true, canRetryAccept: false };
    case 'DOCUMENT_CAPTURE_UNCONFIGURED':
    case 'DOCUMENT_CAPTURE_DISABLED':
      return { code: normalized, message: 'Document capture is currently unavailable for this workspace.', canRetryUpload: false, canCheckStatus: false, canRetryAccept: false };
    case 'DOCUMENT_BUSY':
      return { code: normalized, message: 'Document capture is busy right now. Wait a moment and retry manually.', canRetryUpload: true, canCheckStatus: true, canRetryAccept: false };
    case 'DOCUMENT_RATE_LIMITED':
      return {
        code: normalized,
        message: retryAfterSeconds
          ? `Too many capture attempts. Wait about ${retryAfterSeconds} seconds before retrying.`
          : 'Safety rate limit reached (20/hour or 60/day). Wait before trying another upload.',
        canRetryUpload: true,
        canCheckStatus: false,
        canRetryAccept: false,
      };
    case 'DOCUMENT_DECODE_BUSY':
      return { code: normalized, message: 'Image processing is temporarily busy. Wait a moment and retry manually.', canRetryUpload: true, canCheckStatus: false, canRetryAccept: false };
    case 'IDEMPOTENCY_CONFLICT':
      return { code: normalized, message: 'This upload key was already used for a different file or document type. Choose the file again to start a new upload.', canRetryUpload: false, canCheckStatus: false, canRetryAccept: false };
    case 'DOCUMENT_EXTRACTION_IN_PROGRESS':
      return { code: normalized, message: 'Extraction for this file is still in progress. Check status in a few seconds.', canRetryUpload: false, canCheckStatus: true, canRetryAccept: false };
    case 'DOCUMENT_KIND_UNKNOWN':
    case 'DOCUMENT_ACCEPT_INVALID':
    case 'DOCUMENT_IDEMPOTENCY_KEY_INVALID':
    case 'invalid_id':
      return { code: normalized, message: 'One or more fields are invalid. Check dates, numbers, and line items, then try again.', canRetryUpload: false, canCheckStatus: false, canRetryAccept: true };
    case 'DOCUMENT_BYTES_UNAVAILABLE':
      return { code: normalized, message: 'The stored source file is no longer available, so this capture cannot be accepted or applied.', canRetryUpload: false, canCheckStatus: false, canRetryAccept: false };
    case 'DOCUMENT_LINK_CONFLICT':
      return { code: normalized, message: 'This capture is already linked to another record.', canRetryUpload: false, canCheckStatus: true, canRetryAccept: false };
    case 'DOCUMENT_ACCEPT_CONFLICT':
    case 'DOCUMENT_STATE_CONFLICT':
      return { code: normalized, message: 'This capture was already accepted or updated elsewhere. Check status to load the canonical accepted values.', canRetryUpload: false, canCheckStatus: true, canRetryAccept: false };
    case 'DOCUMENT_REVIEW_REQUIRED':
      return { code: normalized, message: 'The bill total differs from the captured source total. Correct the bill or acknowledge the difference before approving.', canRetryUpload: false, canCheckStatus: false, canRetryAccept: false };
    case 'DOCUMENT_PERMISSION_REQUIRED':
      return { code: normalized, message: 'You do not have permission to perform this document capture action.', canRetryUpload: false, canCheckStatus: false, canRetryAccept: false };
    case 'unknown_id':
    case 'DOCUMENT_NOT_FOUND':
      return { code: normalized, message: 'This capture could not be found in the current workspace.', canRetryUpload: false, canCheckStatus: false, canRetryAccept: false };
    default:
      if (status === 504) {
        return { code: 'GATEWAY_TIMEOUT', message: 'The request timed out before a response arrived. Check status or retry manually.', canRetryUpload: true, canCheckStatus: true, canRetryAccept: false };
      }
      if (status === 429) {
        return { code: 'RATE_LIMITED', message: 'Too many requests. Wait a moment and retry manually.', canRetryUpload: true, canCheckStatus: false, canRetryAccept: false };
      }
      return { code: normalized || 'CAPTURE_ERROR', message: 'The document capture request failed. Your form entries were not changed.', canRetryUpload: true, canCheckStatus: true, canRetryAccept: true };
  }
}

const FIELD_SOURCE_PATH =
  /^(?:vendorName|customerName|vendorInvoiceNo|sourceInvoiceNo|date|dueDate|referenceNo|currency|notes|sourceTotals\.(?:subtotal|taxTotal|grandTotal|discountTotal|additionalCharges|priceBasis)|paymentHints\.(?:paymentStatus|amountPaid|paymentMethod)|lines\[\d{1,2}\]\.(?:description|quantity|unitAmount|sourceLineTotal|taxHint|discount|priceBasis|missingQuantityDefaulted))$/;

/**
 * Reads the per-field source map from accepted or extracted fields. Anything that isn't a known path with a `rules` or `ai` value is
 * ignored, and a missing or malformed map is an empty one, so a capture without it behaves exactly as before.
 */
export function parseFieldSources(raw: unknown): CaptureFieldSources {
  if (!raw || typeof raw !== 'object' || Array.isArray(raw)) return {};
  const sources: CaptureFieldSources = {};
  for (const [path, value] of Object.entries(raw as Record<string, unknown>)) {
    if (FIELD_SOURCE_PATH.test(path) && (value === 'rules' || value === 'ai')) sources[path] = value;
  }
  return sources;
}

/** True when an optional field mapper (not the rules) filled in this field. Nothing renders this yet. */
export const isAiAssistedField = (sources: CaptureFieldSources | null | undefined, path: string) => sources?.[path] === 'ai';

export function describeCaptureWarning(code: string): string {
  switch (code) {
    case 'OCR_PARTIAL_PAGES':
      return 'Some scanned pages were skipped. Check the full document before accepting.';
    case 'TOTALS_INCONSISTENT':
      return 'The extracted subtotal and tax do not match the total. Check the amounts before accepting.';
    case 'TOTAL_UNLABELED':
      return 'The reader could not find a labeled total. Verify the suggested amount before accepting.';
    case 'PARSE_TRUNCATED':
      return 'The reader could not finish checking all fields. Review the full document and fill in any missing details.';
    case 'LOW_CONFIDENCE':
      return 'Some extracted fields have low confidence — review carefully before accepting.';
    case 'MULTIPLE_DOCUMENTS':
      return 'Multiple documents detected in one file. Financial application is disabled.';
    case 'UNSUPPORTED_DOCUMENT_CLASS':
      return 'Document class is not an ordinary bill or invoice (e.g. credit note, refund, or statement).';
    case 'AMBIGUOUS_DATE':
      return 'Extracted date is ambiguous — confirm the exact YYYY-MM-DD date.';
    case 'AMBIGUOUS_CURRENCY':
      return 'Document currency symbol is ambiguous — confirm currency before applying amounts.';
    case 'FOREIGN_CURRENCY':
      return 'Document currency differs from workspace currency — monetary amounts cannot be auto-applied.';
    case 'PRICE_BASIS_UNKNOWN':
      return 'Price basis (tax-inclusive vs tax-exclusive) is unknown — verify tax before approving.';
    case 'PAYMENT_STATUS_UNKNOWN':
      return 'Payment status could not be determined from the document.';
    case 'AI_VALUE_NOT_IN_SOURCE':
      return 'A suggested value was not found in the document text, so the value read by the rules was kept.';
    case 'AI_TOTALS_INCONSISTENT':
      return 'Suggested amounts did not add up, so the totals or lines read by the rules were kept.';
    case 'AI_DISAGREES_WITH_RULES':
      return 'Some suggested values differ from what the rules read. Check them against the document.';
    case 'AI_UNAVAILABLE':
      return 'Suggestions were not available, so the fields read by the rules are shown.';
    default:
      return 'Review extracted values carefully before applying.';
  }
}

// ---------------------------------------------------------------------------------------------
// Scanned tax (rules T-09, T-10): match the document's tax to a workspace rate, then apply it per
// the workspace tax regime. Pure functions; the bill page owns the state.
// ---------------------------------------------------------------------------------------------

export type CaptureTaxRegime = 'None' | 'SalesTax' | 'VatGst';

export interface CaptureTaxCandidate {
  id: string;
  taxCode?: string | null;
  taxName: string;
  rate: number;
  isActive: boolean;
}

export type CaptureTaxMatch<T extends CaptureTaxCandidate = CaptureTaxCandidate> =
  | { kind: 'single'; tax: T; effectiveRate: number }
  | { kind: 'none'; effectiveRate: number | null }
  | { kind: 'ambiguous'; taxes: T[]; effectiveRate: number | null }
  | { kind: 'not-applicable' };

/** Rates within this many percentage points of the document's effective rate count as a match. */
export const CAPTURE_TAX_RATE_TOLERANCE = 0.05;

const isMatchableTax = (tax: CaptureTaxCandidate) =>
  tax.isActive !== false && (tax.taxCode ?? '').trim().toUpperCase() !== 'NONE' && tax.rate > 0;

/** Reads a scanned line's free-text tax hint as a percentage ("7%", "VAT 7", "exempt" → 0), or null when it says nothing. */
function parseTaxHintRate(hint: string | null | undefined): number | null {
  const text = (hint ?? '').trim().toLowerCase();
  if (!text) return null;
  if (/(exempt|non[- ]?tax|zero[- ]?rat|no tax|untaxed)/.test(text)) return 0;
  const match = text.match(/(\d+(?:\.\d+)?)\s*%?/);
  return match ? Number(match[1]) : null;
}

/**
 * Rule T-09: derives the document's effective tax rate and matches it to exactly one active workspace tax.
 * Exclusive or unknown basis: taxTotal / subtotal. Inclusive: taxTotal / (grandTotal - taxTotal).
 * Mixed-rate documents (scanned lines carry different tax hints) are never auto-assigned.
 */
export function matchCaptureTaxRate<T extends CaptureTaxCandidate>(
  sourceTotals: CaptureSourceTotals | null | undefined,
  lines: ReadonlyArray<{ taxHint?: string | null }> | null | undefined,
  taxes: readonly T[]
): CaptureTaxMatch<T> {
  const tax = parseStrictDecimal(sourceTotals?.taxTotal ?? null).numeric;
  if (tax === null || !(tax > 0)) return { kind: 'not-applicable' };

  const candidates = taxes.filter(isMatchableTax);
  const inclusive = sourceTotals?.priceBasis === 'inclusive';
  const subtotal = parseStrictDecimal(sourceTotals?.subtotal ?? null).numeric;
  const grand = parseStrictDecimal(sourceTotals?.grandTotal ?? null).numeric;

  let base: number | null = null;
  if (inclusive) {
    if (grand !== null && grand - tax > 0) base = grand - tax;
  } else if (subtotal !== null && subtotal > 0) {
    base = subtotal;
  } else if (grand !== null && grand - tax > 0) {
    base = grand - tax;
  }
  const effectiveRate = base !== null ? (tax / base) * 100 : null;

  const hintRates = new Set(
    (lines ?? []).map((l) => parseTaxHintRate(l.taxHint)).filter((r): r is number => r !== null)
  );
  if (hintRates.size > 1) {
    return { kind: 'ambiguous', taxes: candidates, effectiveRate };
  }

  if (effectiveRate === null) return { kind: 'none', effectiveRate: null };
  const matches = candidates.filter((t) => Math.abs(t.rate - effectiveRate) <= CAPTURE_TAX_RATE_TOLERANCE + 1e-9);
  if (matches.length === 1) return { kind: 'single', tax: matches[0], effectiveRate };
  if (matches.length > 1) return { kind: 'ambiguous', taxes: matches, effectiveRate };
  return { kind: 'none', effectiveRate };
}

const roundCents = (v: number) => Math.round((v + Number.EPSILON) * 100) / 100;

export interface CaptureTaxLine {
  quantity: number;
  /** Unit amount; null when the scan had no usable amount. */
  amount: number | null;
  discount: number;
}

const lineNet = (l: CaptureTaxLine) => roundCents(l.quantity * (l.amount ?? 0) * (1 - l.discount / 100));

/** Unit amounts are stored to the cent (decimal(18, 2)), so every unit amount returned here has at most 2 decimals. */
const unitFor = (target: number, l: CaptureTaxLine) => {
  const factor = l.quantity * (1 - l.discount / 100);
  return factor > 0 ? roundCents(target / factor) : null;
};

/** A line whose unit amount carries cents exactly: quantity 1 and no discount. */
const carriesCentsExactly = (l: CaptureTaxLine) => l.quantity === 1 && l.discount === 0;

/**
 * Turns per-line target totals into 2-decimal unit amounts. Lines that can't hit their target exactly (quantity above 1 or a
 * discount) leave a residual of a few cents; the given line (quantity 1, no discount) absorbs it. `accept` checks the result,
 * and `carrierShift` lets the caller try neighbouring cent shifts on the carrier. Returns null when no result is accepted.
 */
function unitsForTargets(
  lines: readonly CaptureTaxLine[],
  targets: readonly number[],
  carrier: number | null,
  accept: (achievedNets: number[]) => boolean,
  carrierShifts: readonly number[] = [0]
): number[] | null {
  const units: number[] = [];
  for (let i = 0; i < lines.length; i++) {
    const unit = i === carrier ? 0 : unitFor(targets[i], lines[i]);
    if (unit === null) return null;
    units.push(unit);
  }
  const achievedOthers = lines.reduce((sum, l, i) => (i === carrier ? sum : sum + lineNet({ ...l, amount: units[i] })), 0);
  const targetTotal = targets.reduce((a, b) => a + b, 0);
  for (const shift of carrier === null ? [0] : carrierShifts) {
    if (carrier !== null) units[carrier] = roundCents(targetTotal - achievedOthers + shift / 100);
    const nets = lines.map((l, i) => lineNet({ ...l, amount: units[i] }));
    if (accept(nets)) return [...units];
  }
  return null;
}

const carrierLine = (lines: readonly CaptureTaxLine[], nets: readonly number[]): number | null => {
  let best: number | null = null;
  lines.forEach((l, i) => {
    if (carriesCentsExactly(l) && (best === null || nets[i] >= nets[best])) best = i;
  });
  return best;
};

/**
 * Rule T-10 (SalesTax and None): spreads the document's tax over the lines in proportion to each line's net amount, so the
 * tax becomes part of the cost. Each share is rounded to the cent and the leftover cent goes to the largest line. Unit amounts
 * have at most 2 decimals; cents a multi-quantity or discounted line can't carry land on the largest line with quantity 1 and no
 * discount. Returns the new unit amounts, or null when the lines can't carry the tax exactly.
 */
export function spreadTaxIntoLineCosts(lines: readonly CaptureTaxLine[], taxTotal: number): number[] | null {
  if (!(taxTotal > 0) || lines.length === 0 || lines.some((l) => l.amount === null)) return null;
  const nets = lines.map(lineNet);
  const netSum = roundCents(nets.reduce((a, b) => a + b, 0));
  if (!(netSum > 0)) return null;

  const shares = nets.map((n) => roundCents((taxTotal * n) / netSum));
  const leftover = roundCents(taxTotal - shares.reduce((a, b) => a + b, 0));
  let largest = 0;
  nets.forEach((n, i) => {
    if (n > nets[largest]) largest = i;
  });
  shares[largest] = roundCents(shares[largest] + leftover);

  const targets = nets.map((n, i) => roundCents(n + shares[i]));
  const want = roundCents(netSum + taxTotal);
  return unitsForTargets(lines, targets, carrierLine(lines, nets), (achieved) => roundCents(achieved.reduce((a, b) => a + b, 0)) === want);
}

/**
 * Rule T-10 (VatGst, inclusive prices): converts each line's gross amount to net at the matched rate. The editor adds tax on
 * top per line (rounded to the cent), so a line with quantity 1 and no discount absorbs rounding cents until the net lines plus
 * that tax equal the document's grand total. Unit amounts have at most 2 decimals. Returns null when no result reproduces it.
 */
export function convertInclusiveLinesToNet(
  lines: readonly CaptureTaxLine[],
  ratePct: number,
  grandTotal: number
): number[] | null {
  if (lines.length === 0 || lines.some((l) => l.amount === null) || !(ratePct > 0)) return null;
  const targets = lines.map((l) => roundCents(lineNet(l) / (1 + ratePct / 100)));
  const want = roundCents(grandTotal);
  return unitsForTargets(
    lines,
    targets,
    carrierLine(lines, targets),
    (achieved) => roundCents(achieved.reduce((sum, n) => sum + n + roundCents((n * ratePct) / 100), 0)) === want,
    [0, 1, -1, 2, -2, 3, -3, 4, -4, 5, -5]
  );
}

export type CaptureTaxTreatment =
  | { kind: 'tax-rate'; tax: CaptureTaxCandidate; taxTotal: number; /** Bill tax minus document tax when per-line rounding makes them differ, else 0. */ roundingDifference: number }
  | { kind: 'added-to-cost'; taxTotal: number; unregistered: boolean }
  | { kind: 'needs-rate'; taxTotal: number; ambiguous: boolean }
  | { kind: 'unresolved'; taxTotal: number }
  /** A sales document whose workspace isn't set up for tax: lines stay untaxed, revenue is never grossed up. */
  | { kind: 'workspace-untaxed'; taxTotal: number }
  | { kind: 'none' };

export interface CaptureTaxPlan {
  treatment: CaptureTaxTreatment;
  /** New unit amounts per line, when the plan changes them. */
  unitAmounts: number[] | null;
  /** Tax to set on every applied line (VatGst single match), or '' to leave the lines untaxed. */
  taxId: string;
}

/**
 * Decides how a scanned bill's tax is applied for the workspace's tax regime (rules T-09 and T-10).
 * `regime` is null when the workspace policy couldn't be read: the tax is then left alone and the reconciliation gate holds approval.
 * `side` is 'purchase' for bills (sales tax and no-tax workspaces add the tax to cost) and 'sales' for sales invoices, where the
 * tax is output tax: VAT/GST and sales tax workspaces use a matching rate, and a workspace without tax is never grossed up.
 */
export function planCaptureTax<T extends CaptureTaxCandidate>(
  regime: CaptureTaxRegime | null,
  sourceTotals: CaptureSourceTotals | null | undefined,
  scannedLines: ReadonlyArray<{ taxHint?: string | null }>,
  lines: readonly CaptureTaxLine[],
  taxes: readonly T[],
  side: 'purchase' | 'sales' = 'purchase'
): CaptureTaxPlan {
  const taxTotal = parseStrictDecimal(sourceTotals?.taxTotal ?? null).numeric;
  const none: CaptureTaxPlan = { treatment: { kind: 'none' }, unitAmounts: null, taxId: '' };
  if (taxTotal === null || !(taxTotal > 0)) return none;
  if (regime === null) return { treatment: { kind: 'unresolved', taxTotal }, unitAmounts: null, taxId: '' };

  if (side === 'sales' && regime === 'None') {
    return { treatment: { kind: 'workspace-untaxed', taxTotal }, unitAmounts: null, taxId: '' };
  }

  if (regime === 'VatGst' || (side === 'sales' && regime === 'SalesTax')) {
    const match = matchCaptureTaxRate(sourceTotals, scannedLines, taxes);
    if (match.kind === 'single') {
      const grand = parseStrictDecimal(sourceTotals?.grandTotal ?? null).numeric;
      const inclusive = sourceTotals?.priceBasis === 'inclusive';
      const unitAmounts = inclusive && grand !== null ? convertInclusiveLinesToNet(lines, match.tax.rate, grand) : null;
      if (inclusive && unitAmounts === null) {
        // Can't reproduce the grand total exactly: leave the amounts alone and let the reconciliation gate speak.
        return { treatment: { kind: 'needs-rate', taxTotal, ambiguous: false }, unitAmounts: null, taxId: '' };
      }
      // The editor rounds tax per line, so the bill's tax can differ from the document's by a cent or two.
      const finalLines = lines.map((l, i) => ({ ...l, amount: unitAmounts ? unitAmounts[i] : l.amount }));
      const billTax = finalLines.reduce((sum, l) => sum + roundCents((lineNet(l) * match.tax.rate) / 100), 0);
      const roundingDifference = roundCents(billTax - taxTotal);
      return { treatment: { kind: 'tax-rate', tax: match.tax, taxTotal, roundingDifference }, unitAmounts, taxId: match.tax.id };
    }
    return {
      treatment: { kind: 'needs-rate', taxTotal, ambiguous: match.kind === 'ambiguous' },
      unitAmounts: null,
      taxId: '',
    };
  }

  const unitAmounts = spreadTaxIntoLineCosts(lines, taxTotal);
  if (unitAmounts === null) return { treatment: { kind: 'unresolved', taxTotal }, unitAmounts: null, taxId: '' };
  return {
    treatment: { kind: 'added-to-cost', taxTotal, unregistered: regime === 'None' },
    unitAmounts,
    taxId: '',
  };
}

/**
 * The bill form's tax for one document: true when the form total is short of the document's grand total by exactly the
 * document's tax (within one cent); `difference` is form total minus source total. Such a difference can't be acknowledged away; it needs a tax rate or the tax in the costs.
 */
export function isTaxOnlyDifference(
  sourceTotals: CaptureSourceTotals | null | undefined,
  difference: number | null
): boolean {
  const tax = parseStrictDecimal(sourceTotals?.taxTotal ?? null).numeric;
  if (tax === null || !(tax > 0) || difference === null) return false;
  return Math.abs(-difference - tax) <= 0.01 + 1e-9;
}

/** One line for the review panel saying where the document's tax went. */
export function describeCaptureTaxTreatment(treatment: CaptureTaxTreatment, side: 'purchase' | 'sales' = 'purchase'): string | null {
  switch (treatment.kind) {
    case 'tax-rate': {
      const base =
        side === 'sales'
          ? `Tax ${treatment.taxTotal.toFixed(2)} → output tax ${treatment.tax.taxName} ${treatment.tax.rate}%`
          : `Tax ${treatment.taxTotal.toFixed(2)} → input tax ${treatment.tax.taxName} ${treatment.tax.rate}% (recoverable)`;
      return treatment.roundingDifference === 0
        ? base
        : `${base}. Tax on the bill differs from the document by ${Math.abs(treatment.roundingDifference).toFixed(2)} (rounding)`;
    }
    case 'added-to-cost':
      return `Tax ${treatment.taxTotal.toFixed(2)} → added to line costs (not recoverable)`;
    case 'needs-rate':
    case 'unresolved':
      return `Tax ${treatment.taxTotal.toFixed(2)} → not on this ${side === 'sales' ? 'invoice' : 'bill'} yet`;
    case 'workspace-untaxed':
      return `Tax ${treatment.taxTotal.toFixed(2)} → not applied (workspace isn't set up for tax)`;
    default:
      return null;
  }
}

/**
 * One line for a linked capture on a bill or invoice, from the server's persisted tax treatment. `taxName` resolves a
 * `tax-rate:<id>` value to its label from the document's own lines. Metadata only, never document text.
 */
export function describeLinkedTaxTreatment(
  treatment: string | null | undefined,
  side: 'purchase' | 'sales',
  taxLabel: (taxPublicId: string) => string | null
): string | null {
  if (!treatment || treatment === 'none') return null;
  if (treatment === 'in-cost') return 'Tax from scan: included in line costs';
  if (treatment === 'acknowledged') return 'Tax from scan: not applied (acknowledged)';
  if (treatment === 'tax-rate:mixed') return `Tax from scan: ${side === 'sales' ? 'output' : 'input'} tax at mixed rates`;
  if (treatment.startsWith('tax-rate:')) {
    const label = taxLabel(treatment.slice('tax-rate:'.length));
    return `Tax from scan: ${side === 'sales' ? 'output' : 'input'} tax${label ? ` ${label}` : ''}`;
  }
  return null;
}

// ---------------------------------------------------------------------------------------------
// Live AI-assisted review updates: fold the mapper's improved fields into the review form without
// ever overwriting something the user has already touched.
// ---------------------------------------------------------------------------------------------

/** A field is untouched while it still equals what the review form was first filled with. */
export function mergeUntouched(
  current: Record<string, string>,
  baseline: Record<string, string>,
  next: Record<string, string>
): Record<string, string> {
  const merged: Record<string, string> = { ...current };
  for (const [key, value] of Object.entries(next)) {
    if ((current[key] ?? '') === (baseline[key] ?? '')) merged[key] = value;
  }
  return merged;
}

const LINE_FIELDS = [
  'description',
  'quantity',
  'unitAmount',
  'sourceLineTotal',
  'taxHint',
  'discount',
  'priceBasis',
  'missingQuantityDefaulted',
] as const;

const sameLine = (a: EditableLineDraft, b: EditableLineDraft) => LINE_FIELDS.every((field) => a[field] === b[field]);

/**
 * Lines the user hasn't edited at all are replaced by the improved ones. Once any line was edited, lines are merged field by field
 * only when the row count is unchanged; otherwise the user's lines stay exactly as they are.
 */
export function mergeUntouchedLines(
  current: EditableLineDraft[],
  baseline: EditableLineDraft[],
  next: EditableLineDraft[]
): EditableLineDraft[] {
  if (current.length === baseline.length && current.every((line, i) => sameLine(line, baseline[i]))) return next;
  if (current.length !== baseline.length || current.length !== next.length) return current;
  return current.map((line, i) => {
    const merged: EditableLineDraft = { ...line };
    for (const field of LINE_FIELDS) {
      if (line[field] === baseline[i][field]) (merged as unknown as Record<string, unknown>)[field] = next[i][field];
    }
    return merged;
  });
}

/** The value the rules found for a field the mapper changed, or null when there is none or it is what the form shows. */
export function ruleHintFor(
  aiMapping: CaptureAiMappingView | null | undefined,
  path: string,
  currentValue: string
): string | null {
  if (aiMapping?.status !== 'done') return null;
  const hint = aiMapping.ruleHints?.[path];
  return typeof hint === 'string' && hint !== currentValue ? hint : null;
}
