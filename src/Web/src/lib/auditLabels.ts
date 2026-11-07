/** Plain-word labels for the activity feed, record History and security log. Pure: no React, no locale lookups. */

const MONTH_NAMES = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'] as const;
const FULL_MONTH_NAMES = [
  'January', 'February', 'March', 'April', 'May', 'June',
  'July', 'August', 'September', 'October', 'November', 'December',
] as const;

const EMPTY_VALUE_TEXT = 'empty';
const DATE_PREFIX = /^(\d{4})-(\d{2})-(\d{2})(?:$|T)/;
const HAS_ZONE = /(?:Z|[+-]\d{2}:?\d{2})$/i;

function formatParts(year: number, monthIndex: number, day: number): string | null {
  const month = MONTH_NAMES[monthIndex];
  if (!month || !Number.isFinite(year) || !Number.isFinite(day)) return null;
  return `${day} ${month} ${year}`;
}

/**
 * Fixed `3 Nov 2026` format. Never uses a locale-dependent formatter, so a Thai Buddhist calendar
 * or a different default locale cannot change the year or month names.
 * ISO strings use their own calendar date (no time-zone shift); Date objects use local calendar parts.
 */
export function formatAuditDate(value: string | Date | null | undefined): string {
  if (value == null || value === '') return '';
  if (value instanceof Date) {
    if (Number.isNaN(value.getTime())) return '';
    return formatParts(value.getFullYear(), value.getMonth(), value.getDate()) ?? '';
  }
  const match = DATE_PREFIX.exec(value);
  if (!match) return value;
  return formatParts(Number(match[1]), Number(match[2]) - 1, Number(match[3])) ?? value;
}

/** `3 Nov 2026, 14:05` in the viewer's time zone. Server timestamps without a zone are UTC. */
export function formatAuditDateTime(value: string | null | undefined): string {
  if (!value) return '';
  const parsed = new Date(HAS_ZONE.test(value) ? value : `${value}Z`);
  if (Number.isNaN(parsed.getTime())) return value;
  const datePart = formatParts(parsed.getFullYear(), parsed.getMonth(), parsed.getDate());
  if (!datePart) return value;
  const hours = String(parsed.getHours()).padStart(2, '0');
  const minutes = String(parsed.getMinutes()).padStart(2, '0');
  return `${datePart}, ${hours}:${minutes}`;
}

// ---------------------------------------------------------------------------
// Record types
// ---------------------------------------------------------------------------

const BUSINESS_ENTITY_NAMES: Record<string, string> = {
  SalesInvoiceHeader: 'Invoice',
  SalesInvoiceLine: 'Invoice line',
  PurchaseInvoiceHeader: 'Bill',
  PurchaseInvoiceLine: 'Bill line',
  SalesReceiptHeader: 'Receipt',
  SalesReceiptLine: 'Receipt line',
  VendorPayment: 'Bill payment',
  CustomerAllocation: 'Payment allocation',
  Bank: 'Bank account',
  BankTransaction: 'Bank transaction',
  BankReconciliation: 'Bank reconciliation',
  BankStatementDescriptionRule: 'Bank rule',
  Account: 'Account',
  MainContraAccount: 'Contra account',
  JournalEntryHeader: 'Journal entry',
  JournalEntryLine: 'Journal entry line',
  Customer: 'Customer',
  Vendor: 'Vendor',
  Party: 'Contact',
  Tax: 'Tax',
  TaxGroup: 'Tax group',
  TaxGroupTax: 'Tax group line',
  PaymentTerm: 'Payment term',
  CompanyMember: 'Member',
  CompanyMemberSecurityRole: 'Member role',
  Company: 'Workspace settings',
  CompanySetting: 'Workspace settings',
  WorkspaceOperatingSettings: 'Workspace settings',
  GeneralLedgerSetting: 'Workspace settings',
  RecurringSchedule: 'Recurring invoice',
  RecurringScheduleLine: 'Recurring invoice line',
  FinancialYear: 'Fiscal year',
  BooksLockChange: 'Books lock',
};

/** Personal books never say "organization" or "workspace"; a journal entry is a transaction. */
const PERSONAL_ENTITY_NAMES: Record<string, string> = {
  JournalEntryHeader: 'Transaction',
  JournalEntryLine: 'Transaction line',
  Company: 'Settings',
  CompanySetting: 'Settings',
  WorkspaceOperatingSettings: 'Settings',
  GeneralLedgerSetting: 'Settings',
};

/** Types stored as lines of another record; History groups them under "Line {id}". */
const LINE_ENTITY_TYPES = new Set([
  'SalesInvoiceLine', 'PurchaseInvoiceLine', 'JournalEntryLine', 'SalesReceiptLine',
  'RecurringScheduleLine', 'TaxGroupTax', 'CompanyMemberSecurityRole',
]);

export function isAuditLineEntity(entityType: string): boolean {
  return LINE_ENTITY_TYPES.has(entityType);
}

export function auditEntityName(entityType: string, personal: boolean): string {
  return (personal ? PERSONAL_ENTITY_NAMES[entityType] : undefined) ?? BUSINESS_ENTITY_NAMES[entityType] ?? entityType;
}

const BUSINESS_ONLY_FILTER_TYPES = new Set(['Customer', 'Vendor', 'CompanyMember']);
const FILTER_ENTITY_TYPES = [
  'SalesInvoiceHeader', 'PurchaseInvoiceHeader', 'SalesReceiptHeader', 'VendorPayment', 'Bank', 'BankTransaction', 'BankReconciliation', 'Account',
  'JournalEntryHeader', 'Customer', 'Vendor', 'Party', 'Tax', 'CompanyMember', 'CompanySetting',
  'RecurringSchedule', 'FinancialYear', 'BooksLockChange',
];

/** Record-type filter choices for the Activity list (one per plain name; line types ride with their document). */
export function auditEntityFilterOptions(personal: boolean): Array<{ value: string; label: string }> {
  return FILTER_ENTITY_TYPES
    .filter((type) => !(personal && BUSINESS_ONLY_FILTER_TYPES.has(type)))
    .map((type) => ({ value: type, label: auditEntityName(type, personal) }));
}

// ---------------------------------------------------------------------------
// Field labels (property names verified against the Core domain entities)
// ---------------------------------------------------------------------------

/** Applies to any record type unless an entry in ENTITY_FIELD_LABELS overrides it. */
export const AUDIT_FIELD_LABELS: Readonly<Record<string, string>> = {
  No: 'number',
  VendorInvoiceNo: 'vendor invoice number',
  ReferenceNo: 'reference number',
  Date: 'date',
  DueDate: 'due date',
  Status: 'status',
  CustomerId: 'customer',
  VendorId: 'vendor',
  PartyId: 'contact',
  Notes: 'notes',
  Description: 'description',
  Memo: 'memo',
  Posted: 'posted status',
  VoidedAt: 'void date',
  VoidReason: 'void reason',
  UndoReason: 'undo reason',
  Name: 'name',
  AccountName: 'name',
  AccountCode: 'code',
  ArchivedAt: 'archived date',
  IsActive: 'active status',
  BankName: 'bank name',
  Iban: 'IBAN',
  RoutingNumber: 'routing number',
  Swift: 'SWIFT code',
  Role: 'role',
  Currency: 'currency',
  FiscalYearStartMonth: 'fiscal year start',
  Quantity: 'quantity',
  Amount: 'amount',
  Discount: 'discount',
  TaxAmount: 'tax amount',
  Email: 'email',
  Phone: 'phone',
  TaxName: 'name',
  TaxCode: 'code',
  Rate: 'rate',
};

/** `Entity.Field` overrides: `Number` is an account number only on a bank account. */
const ENTITY_FIELD_LABELS: Readonly<Record<string, string>> = {
  'Bank.Number': 'account number',
};

export function auditFieldLabel(entityType: string, field: string): string | null {
  return ENTITY_FIELD_LABELS[`${entityType}.${field}`] ?? AUDIT_FIELD_LABELS[field] ?? null;
}

// Enums reach the log as numbers (System.Text.Json default); names mirror Core EnumTypes.
const SALES_INVOICE_STATUS = ['Draft', 'Open', 'Overdue', 'Closed', 'Void'];
const PURCHASE_INVOICE_STATUS = ['Draft', 'Open', 'Paid', 'Overdue', 'Void'];
const ENUM_VALUE_NAMES: Readonly<Record<string, Readonly<Record<number, string>>>> = {
  'SalesInvoiceHeader.Status': Object.fromEntries(SALES_INVOICE_STATUS.map((name, index) => [index, name])),
  'PurchaseInvoiceHeader.Status': Object.fromEntries(PURCHASE_INVOICE_STATUS.map((name, index) => [index, name])),
  'CompanyMember.Role': { 1: 'Owner', 2: 'Admin', 3: 'Member' },
  'CompanyMember.Status': { 1: 'Active', 2: 'Suspended' },
  'RecurringSchedule.Status': { 1: 'Active', 2: 'Paused', 3: 'Ended' },
  'BankReconciliation.Status': { 1: 'Draft', 2: 'Finished', 3: 'Undone' },
};

/** One value as plain text: masked values unchanged, empty → "empty", booleans → Yes/No, dates in a fixed format. */
export function formatAuditValue(entityType: string, field: string, value: unknown): string {
  if (value === null || value === undefined || value === '') return EMPTY_VALUE_TEXT;
  if (typeof value === 'boolean') return value ? 'Yes' : 'No';
  if (typeof value === 'number') {
    const named = ENUM_VALUE_NAMES[`${entityType}.${field}`]?.[value];
    if (named) return named;
    if (field === 'FiscalYearStartMonth' && value >= 1 && value <= 12) return FULL_MONTH_NAMES[value - 1];
    return String(value);
  }
  if (typeof value === 'string') {
    if (value.startsWith('****')) return value;
    return DATE_PREFIX.test(value) ? formatAuditDate(value) : value;
  }
  return JSON.stringify(value);
}

// ---------------------------------------------------------------------------
// Change sentences
// ---------------------------------------------------------------------------

export interface AuditChangeRow {
  entityType: string;
  entityId?: number;
  action: string;
  /** `{ Field: [old, new] }` for Modified; `{ Field: value }` for Added/Deleted. */
  changes: unknown;
}

interface ParsedChange {
  field: string;
  oldValue: unknown;
  newValue: unknown;
  /** True for a before/after pair; false for an Added/Deleted snapshot value. */
  isPair: boolean;
}

function parseChanges(changes: unknown): ParsedChange[] {
  if (!changes || typeof changes !== 'object' || Array.isArray(changes)) return [];
  return Object.entries(changes as Record<string, unknown>).map(([field, raw]) => {
    if (Array.isArray(raw) && raw.length >= 2) return { field, oldValue: raw[0], newValue: raw[1], isPair: true };
    if (raw && typeof raw === 'object' && 'old' in raw && 'new' in raw) {
      const pair = raw as { old: unknown; new: unknown };
      return { field, oldValue: pair.old, newValue: pair.new, isPair: true };
    }
    return { field, oldValue: raw, newValue: raw, isPair: false };
  });
}

const NO_ARTICLE_NAMES = new Set(['Settings', 'Workspace settings']);

function withArticle(name: string): string {
  const lower = name.charAt(0).toLowerCase() + name.slice(1);
  if (NO_ARTICLE_NAMES.has(name)) return lower;
  return `${/^[aeiou]/i.test(lower) ? 'an' : 'a'} ${lower}`;
}

function isEmptyValue(value: unknown): boolean {
  return value === null || value === undefined || value === '';
}

/** Hidden from sentences: row identity, not something a person changed. */
const IDENTITY_FIELDS = new Set(['Id', 'CompanyId']);

/**
 * One plain sentence per changed field, e.g. "Changed the due date from 3 Nov 2026 to 10 Nov 2026".
 * Added/Deleted rows lead with a sentence for the record, then list the fields that have plain labels.
 * Fields without a plain label show their raw name.
 */
export function describeChange(row: AuditChangeRow, personal: boolean): string[] {
  const entityName = auditEntityName(row.entityType, personal);
  const parsed = parseChanges(row.changes).filter((change) => !IDENTITY_FIELDS.has(change.field));
  const phrase = (field: string): string => {
    const label = auditFieldLabel(row.entityType, field);
    return label ? `the ${label}` : field;
  };

  if (row.action === 'Added' || row.action === 'Deleted') {
    const deleted = row.action === 'Deleted';
    const sentences = [`${deleted ? 'Deleted' : 'Created'} ${withArticle(entityName)}`];
    for (const change of parsed) {
      if (!auditFieldLabel(row.entityType, change.field)) continue;
      const value = deleted ? change.oldValue : change.newValue;
      if (isEmptyValue(value)) continue;
      const text = formatAuditValue(row.entityType, change.field, value);
      sentences.push(deleted
        ? `${capitalize(phrase(change.field))} was ${text}`
        : `Set ${phrase(change.field)} to ${text}`);
    }
    return sentences;
  }

  return parsed.map((change) => {
    if (row.action === 'Modified' && !change.isPair && (row.entityType === 'BankTransaction' || row.entityType === 'BankReconciliation')) {
      if (change.field === 'BankId') return `Bank account #${change.newValue}`;
      if (change.field === 'StatementEndDate') return `Statement ending ${formatAuditValue(row.entityType, change.field, change.newValue)}`;
    }
    if (row.entityType === 'BankTransaction' && change.field === 'VoidGeneralLedgerHeaderId'
      && isEmptyValue(change.oldValue) && !isEmptyValue(change.newValue)) return 'Voided bank transaction';
    const subject = phrase(change.field);
    const before = formatAuditValue(row.entityType, change.field, change.oldValue);
    const after = formatAuditValue(row.entityType, change.field, change.newValue);
    if (isEmptyValue(change.oldValue) && !isEmptyValue(change.newValue)) return `Set ${subject} to ${after}`;
    if (!isEmptyValue(change.oldValue) && isEmptyValue(change.newValue)) return `Cleared ${subject} (was ${before})`;
    return `Changed ${subject} from ${before} to ${after}`;
  });
}

function capitalize(text: string): string {
  return text.charAt(0).toUpperCase() + text.slice(1);
}

// ---------------------------------------------------------------------------
// Who
// ---------------------------------------------------------------------------

export interface AuditActor {
  actorKind?: string | null;
  actorName?: string | null;
}

export function auditActorLabel(actor: AuditActor): string {
  switch ((actor.actorKind ?? '').toLowerCase()) {
    case 'staff': return 'AccountGo support';
    case 'job': return 'Automatic';
    case 'system': return 'System';
    default: return actor.actorName?.trim() || 'Unknown user';
  }
}

// ---------------------------------------------------------------------------
// Security log
// ---------------------------------------------------------------------------

/** Every event type the API writes to the security log, in plain words. */
export const SECURITY_EVENT_LABELS: Readonly<Record<string, string>> = {
  'auth.signin.success': 'Signed in',
  'auth.signin.failure': 'Sign-in failed',
  'auth.signup': 'Account sign-up',
  'auth.logout': 'Signed out',
  'auth.password.change': 'Password changed',
  'auth.session.revoke_others': 'Signed out other devices',
  'auth.otp.locked': 'Sign-in code locked after too many attempts',
  'auth.refresh.reuse': 'Reused sign-in session blocked',
  'access.denied': 'Access denied',
  'member.role.change': 'Member role changed',
  'member.remove': 'Member removed',
  'member.ownership.transfer': 'Ownership transferred',
  'approval.grant': 'Approval granted',
  'approval.revoke': 'Approval revoked',
  'invitation.accept': 'Invitation accepted',
  'workspace.delete': 'Workspace deleted',
  'platform.role.change': 'Platform role changed',
  'invoice.pdf.public': 'Invoice opened from a shared link',
  'data.report.view': 'Report viewed',
  'data.export': 'Data exported',
  cli_oauth_request_created: 'Command-line sign-in requested',
  cli_oauth_denied: 'Command-line sign-in denied',
  cli_oauth_code_issued: 'Command-line sign-in approved',
  cli_oauth_token_exchanged: 'Command-line access granted',
  cli_oauth_refresh_reuse: 'Reused command-line session blocked',
  cli_oauth_revoked: 'Command-line access revoked',
};

const PERSONAL_SECURITY_EVENT_LABELS: Readonly<Record<string, string>> = {
  'workspace.delete': 'Books deleted',
};

export function securityEventLabel(eventType: string, personal = false): string {
  return (personal ? PERSONAL_SECURITY_EVENT_LABELS[eventType] : undefined)
    ?? SECURITY_EVENT_LABELS[eventType]
    ?? eventType;
}

export function securityEventFilterOptions(personal: boolean): Array<{ value: string; label: string }> {
  return Object.keys(SECURITY_EVENT_LABELS).map((value) => ({ value, label: securityEventLabel(value, personal) }));
}

const OUTCOME_LABELS: Readonly<Record<string, string>> = {
  success: 'Succeeded',
  failure: 'Failed',
  denied: 'Denied',
};

export function securityOutcomeLabel(outcome: string | null | undefined): string {
  if (!outcome) return '';
  return OUTCOME_LABELS[outcome.toLowerCase()] ?? outcome;
}
