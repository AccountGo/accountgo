import assert from 'node:assert/strict';
import { after, test } from 'node:test';
import { createServer } from 'vite';

const server = await createServer({ server: { middlewareMode: true, hmr: false, ws: false }, appType: 'custom' });
after(() => server.close());

const labels = await server.ssrLoadModule('/src/lib/auditLabels.ts');
const {
  describeChange,
  formatAuditDate,
  formatAuditDateTime,
  formatAuditValue,
  auditEntityName,
  auditEntityFilterOptions,
  auditFieldLabel,
  auditActorLabel,
  securityEventLabel,
  securityOutcomeLabel,
  securityEventFilterOptions,
  SECURITY_EVENT_LABELS,
  AUDIT_FIELD_LABELS,
  isAuditLineEntity,
} = labels;

const modified = (entityType, changes) => ({ entityType, entityId: 1, action: 'Modified', changes });

test('dates use a fixed day-month-year format regardless of the runtime locale', () => {
  const original = {
    toLocaleDateString: Date.prototype.toLocaleDateString,
    toLocaleString: Date.prototype.toLocaleString,
    toLocaleTimeString: Date.prototype.toLocaleTimeString,
  };
  // A Thai Buddhist-calendar runtime would return 2569 here; the formatter must not call these.
  Date.prototype.toLocaleDateString = () => '3 พ.ย. 2569';
  Date.prototype.toLocaleString = () => '3 พ.ย. 2569 14:05';
  Date.prototype.toLocaleTimeString = () => '14:05';
  try {
    assert.equal(formatAuditDate('2026-11-03'), '3 Nov 2026');
    assert.equal(formatAuditDate('2026-11-03T00:00:00'), '3 Nov 2026');
    assert.equal(formatAuditDate('2026-11-03T23:30:00Z'), '3 Nov 2026');
    assert.equal(formatAuditDate(new Date(2026, 10, 3, 9, 0)), '3 Nov 2026');
    assert.match(formatAuditDateTime('2026-11-03T08:15:00Z'), /^\d{1,2} (Nov|Oct|Dec) 2026, \d{2}:\d{2}$/);
    assert.equal(
      describeChange(modified('SalesInvoiceHeader', { DueDate: ['2026-11-03T00:00:00', '2026-11-10T00:00:00'] }), false)[0],
      'Changed the due date from 3 Nov 2026 to 10 Nov 2026',
    );
  } finally {
    Object.assign(Date.prototype, original);
  }
});

test('invalid or empty dates are returned safely', () => {
  assert.equal(formatAuditDate(''), '');
  assert.equal(formatAuditDate(null), '');
  assert.equal(formatAuditDate(new Date('nope')), '');
  assert.equal(formatAuditDate('not a date'), 'not a date');
  assert.equal(formatAuditDateTime(''), '');
  assert.equal(formatAuditDateTime('garbage'), 'garbage');
});

test('describeChange: one sentence per changed field', () => {
  const sentences = describeChange(
    modified('SalesInvoiceHeader', { Notes: ['a', 'b'], DueDate: ['2026-11-03T00:00:00', '2026-11-10T00:00:00'] }),
    false,
  );
  assert.deepEqual(sentences, [
    'Changed the notes from a to b',
    'Changed the due date from 3 Nov 2026 to 10 Nov 2026',
  ]);
});

test('describeChange: set, cleared and empty values', () => {
  assert.deepEqual(
    describeChange(modified('Bank', { BankName: [null, 'First Bank'] }), false),
    ['Set the bank name to First Bank'],
  );
  assert.deepEqual(
    describeChange(modified('Bank', { BankName: ['First Bank', ''] }), false),
    ['Cleared the bank name (was First Bank)'],
  );
  assert.equal(formatAuditValue('Bank', 'BankName', null), 'empty');
  assert.equal(formatAuditValue('Bank', 'BankName', ''), 'empty');
});

test('describeChange: masked values are shown as they are', () => {
  assert.deepEqual(
    describeChange(modified('Bank', { Number: ['****1234', '****5678'], Iban: [null, '****9012'] }), false),
    ['Changed the account number from ****1234 to ****5678', 'Set the IBAN to ****9012'],
  );
});

test('describeChange: booleans are Yes / No', () => {
  assert.deepEqual(
    describeChange(modified('JournalEntryHeader', { Posted: [false, true] }), false),
    ['Changed the posted status from No to Yes'],
  );
  assert.equal(formatAuditValue('Account', 'IsActive', true), 'Yes');
  assert.equal(formatAuditValue('Account', 'IsActive', false), 'No');
});

test('describeChange: unmapped fields show their raw names', () => {
  assert.deepEqual(
    describeChange(modified('SalesInvoiceHeader', { SellerJson: ['{}', '{"a":1}'], ShippingHandlingCharge: [1, 2] }), false),
    ['Changed SellerJson from {} to {"a":1}', 'Changed ShippingHandlingCharge from 1 to 2'],
  );
  assert.equal(auditFieldLabel('SalesInvoiceHeader', 'SellerJson'), null);
});

test('describeChange: enum numbers read as names', () => {
  assert.deepEqual(
    describeChange(modified('SalesInvoiceHeader', { Status: [1, 4] }), false),
    ['Changed the status from Open to Void'],
  );
  assert.deepEqual(
    describeChange(modified('PurchaseInvoiceHeader', { Status: [1, 2] }), false),
    ['Changed the status from Open to Paid'],
  );
  assert.deepEqual(
    describeChange(modified('CompanyMember', { Role: [3, 2], Status: [1, 2] }), false),
    ['Changed the role from Member to Admin', 'Changed the status from Active to Suspended'],
  );
  assert.equal(formatAuditValue('CompanySetting', 'FiscalYearStartMonth', 4), 'April');
});

test('describeChange: Added and Deleted rows lead with the record, then mapped fields only', () => {
  const added = describeChange({
    entityType: 'Bank', entityId: 3, action: 'Added',
    changes: { Id: 3, CompanyId: 1, Name: 'Main', Number: '****1234', Swift: null, Unmapped: 'x', IsActive: true },
  }, false);
  assert.deepEqual(added, [
    'Created a bank account',
    'Set the name to Main',
    'Set the account number to ****1234',
    'Set the active status to Yes',
  ]);
  const deleted = describeChange({ entityType: 'Account', entityId: 9, action: 'Deleted', changes: { AccountCode: '1010' } }, false);
  assert.deepEqual(deleted, ['Deleted an account', 'The code was 1010']);
  assert.deepEqual(
    describeChange({ entityType: 'CompanySetting', entityId: 1, action: 'Added', changes: {} }, false),
    ['Created workspace settings'],
  );
});

test('describeChange: tolerates missing or malformed changes', () => {
  assert.deepEqual(describeChange(modified('Bank', null), false), []);
  assert.deepEqual(describeChange(modified('Bank', 'oops'), false), []);
  assert.deepEqual(describeChange(modified('Bank', []), false), []);
});

test('describeChange: before/after objects are read like pairs', () => {
  assert.deepEqual(
    describeChange(modified('Account', { AccountName: { old: 'Cash', new: 'Petty cash' } }), false),
    ['Changed the name from Cash to Petty cash'],
  );
});

test('record names: business and personal wording', () => {
  const business = {
    SalesInvoiceHeader: 'Invoice', PurchaseInvoiceHeader: 'Bill', Bank: 'Bank account', Account: 'Account',
    JournalEntryHeader: 'Journal entry', Customer: 'Customer', Vendor: 'Vendor', Party: 'Contact', Tax: 'Tax',
    CompanyMember: 'Member', CompanySetting: 'Workspace settings', RecurringSchedule: 'Recurring invoice',
    FinancialYear: 'Fiscal year', BooksLockChange: 'Books lock',
  };
  for (const [type, name] of Object.entries(business)) assert.equal(auditEntityName(type, false), name, type);

  assert.equal(auditEntityName('PurchaseInvoiceHeader', true), 'Bill');
  assert.equal(auditEntityName('Bank', true), 'Bank account');
  assert.equal(auditEntityName('Account', true), 'Account');
  assert.equal(auditEntityName('JournalEntryHeader', true), 'Transaction');
  assert.equal(auditEntityName('CompanySetting', true), 'Settings');
  assert.equal(auditEntityName('Company', true), 'Settings');
  assert.equal(auditEntityName('SomethingNew', false), 'SomethingNew');
  assert.ok(isAuditLineEntity('SalesInvoiceLine'));
  assert.ok(!isAuditLineEntity('SalesInvoiceHeader'));
});

test('actor labels by kind', () => {
  assert.equal(auditActorLabel({ actorKind: 'staff', actorName: 'Sam' }), 'AccountGo support');
  assert.equal(auditActorLabel({ actorKind: 'job', actorName: null }), 'Automatic');
  assert.equal(auditActorLabel({ actorKind: 'system' }), 'System');
  assert.equal(auditActorLabel({ actorKind: 'STAFF' }), 'AccountGo support');
  assert.equal(auditActorLabel({ actorKind: 'user', actorName: 'Ana Cruz' }), 'Ana Cruz');
  assert.equal(auditActorLabel({ actorKind: 'user', actorName: '  ' }), 'Unknown user');
  assert.equal(auditActorLabel({ actorKind: 'user' }), 'Unknown user');
  assert.equal(auditActorLabel({}), 'Unknown user');
});

// Every event type the API writes to the security log (AuthController, SessionService, EmailOtpService,
// AccessDeniedAudit, WorkspaceMembersController, InvitationsController, OrganizationsController,
// AdministrationController, PublicInvoicePdfController, AuditDataAccess, CliOAuthService).
const PR3_EVENT_LABELS = {
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

test('every security event type has a plain-word label', () => {
  for (const [type, text] of Object.entries(PR3_EVENT_LABELS)) {
    assert.equal(securityEventLabel(type), text, type);
    assert.notEqual(securityEventLabel(type), type);
  }
  assert.deepEqual(Object.keys(SECURITY_EVENT_LABELS).sort(), Object.keys(PR3_EVENT_LABELS).sort());
  assert.equal(securityEventLabel('something.new'), 'something.new');
  assert.equal(securityEventFilterOptions(false).length, Object.keys(PR3_EVENT_LABELS).length);
});

test('security outcomes read as plain words', () => {
  assert.equal(securityOutcomeLabel('success'), 'Succeeded');
  assert.equal(securityOutcomeLabel('failure'), 'Failed');
  assert.equal(securityOutcomeLabel('denied'), 'Denied');
  assert.equal(securityOutcomeLabel('other'), 'other');
  assert.equal(securityOutcomeLabel(null), '');
});

test('at least 20 fields have plain labels', () => {
  assert.ok(Object.keys(AUDIT_FIELD_LABELS).length >= 20);
  assert.equal(auditFieldLabel('Bank', 'Number'), 'account number');
  assert.equal(auditFieldLabel('SalesInvoiceHeader', 'Number'), null);
});

test('personal-books output never says upgrade, organization or profit', () => {
  const rows = [
    modified('JournalEntryHeader', { Memo: ['a', 'b'], Posted: [false, true], Date: ['2026-11-03T00:00:00', '2026-11-04T00:00:00'] }),
    modified('CompanySetting', { Currency: ['USD', 'THB'], FiscalYearStartMonth: [1, 4] }),
    modified('Company', { Name: ['Old', 'New'] }),
    { entityType: 'CompanySetting', entityId: 1, action: 'Added', changes: { Currency: 'USD' } },
    { entityType: 'JournalEntryHeader', entityId: 2, action: 'Deleted', changes: { Memo: 'x' } },
    modified('PurchaseInvoiceHeader', { Status: [0, 1] }),
    modified('Bank', { Number: ['****1', '****2'] }),
  ];
  const text = [
    ...rows.flatMap((row) => describeChange(row, true)),
    ...Object.keys(PR3_EVENT_LABELS).map((type) => securityEventLabel(type, true)),
    ...auditEntityFilterOptions(true).map((option) => option.label),
    ...securityEventFilterOptions(true).map((option) => option.label),
    ...['Invoice', 'Bill', 'Bank', 'Account', 'JournalEntryHeader', 'CompanySetting', 'Company', 'CompanyMember']
      .map((type) => auditEntityName(type, true)),
  ].join(' | ');
  assert.ok(!/upgrade|organi[sz]ation|profit/i.test(text), text);
  assert.ok(!/workspace/i.test(text), text);
  assert.ok(text.includes('Transaction'));
  assert.ok(text.includes('Settings'));
});

test('personal books drop business-only record filters', () => {
  const personal = auditEntityFilterOptions(true).map((option) => option.value);
  assert.ok(!personal.includes('Customer'));
  assert.ok(!personal.includes('CompanyMember'));
  assert.ok(personal.includes('JournalEntryHeader'));
  assert.ok(auditEntityFilterOptions(false).map((option) => option.value).includes('Customer'));
});
