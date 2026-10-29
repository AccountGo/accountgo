---
name: saas-accounting-system
description: Use when designing double-entry accounting inside SaaS, including automated postings, ledgers, controls, reconciliations, and reports.
metadata:
  portable: true
  compatible_with:
  - claude-code
  - codex
---

## Platform Notes

- Optional helper plugins may help in some environments, but they must not be treated as required for this skill.

# SaaS Accounting System
Acknowledgement: Shared by Peter Bamuhigire, techguypeter.com, +256 784 464178.


## Required Inputs

| Input | Required | Use |
|---|---|---|
| Tenant, product, and lifecycle scope | yes | Bound the SaaS decision |
| Current architecture, plans, policies, and constraints | yes | Preserve enforceable behaviour |
| Production data or verified evidence | conditional | Validate thresholds and migrations |

## Capability and permission contract

Default to read-only analysis. Change configuration, billing, identity, tenant data, infrastructure, or customer communications only with explicit authority, least-privilege credentials, tenant scope, rollback, and auditable approval. Never expose secrets or cross tenant boundaries.

## Degraded mode

If production access, policy, telemetry, or authoritative records are unavailable, produce a labelled design or dry-run plan. Do not claim deployment, reconciliation, deletion, delivery, or measured outcomes; list missing evidence and verification.

## Decision rules

| Condition | Action | Stop condition |
|---|---|---|
| Tenant isolation, money, identity, or deletion is affected | Require approval and rollback evidence | Scope or authority is ambiguous |
| Evidence supports a reversible change | Stage, test, and record it | Acceptance checks fail |
| Only partial context is available | Return assumptions and validation | A production claim cannot be verified |

## Domain Anti-Patterns

- Applying one tenant's policy or data to another. Fix: enforce tenant scope at every boundary.
- Mutating production from an advisory request. Fix: remain read-only until authority is explicit.
- Inventing limits, prices, metrics, or compliance claims. Fix: use authoritative records or mark them unresolved.
- Shipping without rollback and audit evidence. Fix: stage and retain before/after proof.
- Treating a missing dependency as successful. Fix: name the blocked verification.


<!-- dual-compat-start -->
## Use When

- Implement a complete double-entry accounting system inside any SaaS app. Users enter transactions naturally (sales, expenses, inventory) while the system auto-posts journal entries under the hood. Produces both user-friendly reports and technical...

## Evidence Produced

| Category | Artifact | Format | Example |
|----------|----------|--------|---------|
| Correctness | Double-entry accounting test plan | Markdown doc covering journal balance, period close, and reconciliation scenarios | `docs/accounting/double-entry-tests.md` |
| Data safety | Chart of accounts and posting rules register | Markdown doc per `skill-composition-standards/references/entity-model-template.md` covering accounts, posting rules, and audit-trail requirements | `docs/accounting/coa-register.md` |

## References

- Use the `references/` directory for deep detail after reading the core workflow below.
- Use companion skill `accounting-finance-controller` when the implementation also needs IFRS-aware reporting, management accounting, cost accounting, financial controls, projections, or CFO-grade review beyond the core double-entry engine.
<!-- dual-compat-end -->
## Overview

## Deprecation Note

For new products that handle money, prefer the stricter companion skill `accounting-engine`. This file remains useful as legacy implementation detail, but any guidance here must be read subject to these hard rules:

- All ledger writes go through one `LedgerPostingService`.
- Posted journal lines are append-only and are never updated, deleted, or soft-deleted.
- Corrections use reversing journals.
- Materialized balances are rebuildable caches, not authoritative records.
- Business modules never hardcode account codes or write ledger tables directly.
- IFRS/IFRS for SMEs tenants never use LIFO.

This skill implements a **hidden accounting engine** inside business applications. End users never see debits, credits, or journal entries — they enter sales, record payments, move inventory. Under the hood, every transaction auto-posts balanced journal entries that accountants and auditors can query at any time.

**Philosophy:** Users think in business terms. The system thinks in double-entry.

## Core Architecture

```
┌─────────────────────────────────────────────────┐
│  USER LAYER (Friendly)                          │
│  Sales, Purchases, Payments, Inventory, Expenses│
│  → User sees: "Sale #1042 to Customer X: $500"  │
└──────────────────────┬──────────────────────────┘
                       │ auto-posts
┌──────────────────────▼──────────────────────────┐
│  ACCOUNTING ENGINE (Hidden)                     │
│  Journal Entries, Ledger Postings, Trial Balance│
│  → Engine posts: DR Accounts Receivable $500    │
│                  CR Sales Revenue      $500     │
└──────────────────────┬──────────────────────────┘
                       │ aggregates
┌──────────────────────▼──────────────────────────┐
│  REPORTING LAYER (Dual)                         │
│  User Reports: Sales Summary, Aging, P&L Simple │
│  Accountant Reports: Trial Balance, BS, IS, CF  │
└─────────────────────────────────────────────────┘
```

## The Golden Rule

**Every transaction MUST produce balanced journal entries. No exceptions.**

```
SUM(debits) = SUM(credits)  — ALWAYS
```

This is enforced at the database level via stored procedure or trigger. If entries don't balance, the entire transaction rolls back.

## Chart of Accounts (COA)

The COA is the backbone. Every account has a type that determines its normal balance.

| Type | Code Range | Normal Balance | Examples |
|------|-----------|----------------|----------|
| **Asset** | 1000-1999 | Debit | Cash, Bank, AR, Inventory, Equipment |
| **Liability** | 2000-2999 | Credit | AP, Loans, Tax Payable, Unearned Revenue |
| **Equity** | 3000-3999 | Credit | Owner's Equity, Retained Earnings |
| **Revenue** | 4000-4999 | Credit | Sales, Service Income, Interest Income |
| **COGS** | 5000-5999 | Debit | Cost of Goods Sold, Direct Materials |
| **Expense** | 6000-6999 | Debit | Rent, Salaries, Utilities, Marketing |

**Setup rules:**
- Tenant gets a default COA on creation (seeded from template)
- Accounts are tenant-scoped (`franchise_id`)
- Accounts cannot be deleted if they have posted entries
- Sub-accounts supported (e.g., 1100 Cash → 1101 Petty Cash, 1102 Main Bank)

See: `references/chart-of-accounts.md` and `references/accounting-bookkeeping-erp-patterns.md`.

## Auto-Posting Rules

Users never create journal entries manually. Every business transaction has a posting rule.

### Transaction → Journal Entry Map

| User Action | Debit Account | Credit Account |
|------------|---------------|----------------|
| **Record Sale (Invoice)** | Accounts Receivable | Sales Revenue |
| **Record Sale + Tax** | AR + Tax Receivable | Sales Revenue + Tax Payable |
| **Receive Payment (Cash)** | Cash/Bank | Accounts Receivable |
| **Record Purchase** | Inventory/Expense | Accounts Payable |
| **Pay Supplier** | Accounts Payable | Cash/Bank |
| **Record Expense** | Expense Account | Cash/Bank or AP |
| **Inventory Sale (COGS)** | Cost of Goods Sold | Inventory |
| **Stock Adjustment (+)** | Inventory | Inventory Adjustment (Income) |
| **Stock Adjustment (-)** | Inventory Adjustment (Expense) | Inventory |
| **Salary Payment** | Salary Expense | Cash/Bank |
| **Loan Received** | Cash/Bank | Loan Payable |
| **Loan Repayment** | Loan Payable + Interest Exp | Cash/Bank |
| **Depreciation** | Depreciation Expense | Accumulated Depreciation |
| **Customer Refund** | Sales Returns | Cash/Bank or AR |

**Multi-line entries:** A single sale with tax and discount creates 3+ journal lines, all in one entry. The entry MUST balance.

See: `references/journal-posting-rules.md` and `references/accounting-bookkeeping-erp-patterns.md`.

## Void & Reversal Mechanics

**Rule:** Never delete a posted journal entry. Always create a reversing entry.

### Void Process

```
1. User clicks "Void" on a transaction (e.g., Sale Invoice #1042)
2. System marks the original transaction as VOIDED (status change)
3. System auto-creates a REVERSING journal entry:
   - Same accounts, opposite directions
   - Reference: "REVERSAL of JE-{original_id}"
   - Same date OR current date (configurable)
4. Original entry + reversal entry net to ZERO
5. All sub-ledger balances update automatically
```

### Reversal Entry Example

```
Original (Sale Invoice #1042):
  DR  Accounts Receivable    500.00
  CR  Sales Revenue           500.00

Reversal (Void of #1042):
  DR  Sales Revenue           500.00
  CR  Accounts Receivable    500.00

Net effect: ZERO
```

### Partial Void Rules

- Partially paid invoices: Void remaining balance only
- Payment already received: Must void payment first, then invoice
- Inventory already delivered: Must reverse stock movement first
- Cascade protection: System warns if dependent transactions exist

See: `references/void-reversal-patterns.md`

## Database Schema (Core Tables)

```sql
-- Chart of Accounts
accounts (id, franchise_id, code, name, type, parent_id,
          is_active, normal_balance, created_at)

-- Journal Entries (Header)
journal_entries (id, franchise_id, entry_date, reference_type,
                 reference_id, narration, is_reversal, reversed_entry_id,
                 posted_by, status, created_at)

-- Journal Entry Lines (Detail)
journal_entry_lines (id, journal_entry_id, account_id,
                     debit_amount, credit_amount, narration,
                     franchise_id, created_at)

-- Fiscal Periods
fiscal_periods (id, franchise_id, period_name, start_date,
                end_date, status, closed_by, closed_at)

-- Account Balances (Materialized for performance)
account_balances (id, franchise_id, account_id, period_id,
                  opening_balance, debit_total, credit_total,
                  closing_balance, updated_at)
```

**Integrity constraints:**
- `journal_entry_lines.debit_amount` and `credit_amount` are DECIMAL(15,2)
- CHECK constraint: Each line has debit OR credit, never both, never both zero
- Stored procedure validates SUM(debit) = SUM(credit) per entry
- `franchise_id` on every table (multi-tenant isolation)
- `status` enum: DRAFT, POSTED, VOIDED

See: `references/schema-design.md`

## Subledger and Control Account Discipline

Every finance module must reconcile to the general ledger through control accounts:

- Accounts receivable aging must reconcile to the AR control account.
- Accounts payable aging must reconcile to the AP control account.
- Inventory valuation must reconcile to inventory control accounts.
- Fixed asset register net book value must reconcile to asset cost less accumulated depreciation.
- Bank reconciliation must explain the difference between bank statement and book balance.
- Tax schedules must reconcile tax payable/receivable accounts to return-ready evidence.

Do not let users post directly to control accounts except through controlled adjustment workflows with reason, approval, and audit trail.

## Management Accounting Layer

When the system supports manufacturing, services, projects, schools, clinics, retail, or ERP management reporting, add cost-centre, profit-centre, project, branch, department, and product dimensions to postings. Use these dimensions for budgets, flexible budget reports, standard-cost variance, contribution margin, activity-based costing, and management packs without corrupting statutory GL structure.

## Dual Reporting System

### User-Friendly Reports (Non-Accountant)

| Report | What User Sees | Data Source |
|--------|---------------|-------------|
| Sales Summary | Total sales by period, customer, product | Sales transactions |
| Outstanding Invoices | Who owes what, how old | AR sub-ledger |
| Expense Report | Spending by category | Expense transactions |
| Profit & Loss (Simple) | Revenue minus expenses | Income/expense accounts |
| Cash Position | Money in bank/cash | Cash/bank accounts |
| Inventory Value | Stock on hand with cost | Inventory sub-ledger |

### Accountant/Auditor Reports (Technical)

| Report | What It Shows | Source |
|--------|-------------|--------|
| Trial Balance | All account balances (DR/CR columns) | General Ledger |
| Balance Sheet | Assets = Liabilities + Equity | GL (type 1-3) |
| Income Statement | Revenue - COGS - Expenses = Net Income | GL (type 4-6) |
| Cash Flow Statement | Operating + Investing + Financing | Cash account entries |
| General Ledger Detail | Every entry per account | Journal entries |
| Journal Register | All journal entries chronologically | Journal entries |
| Aged Receivables | AR aging (30/60/90/120 days) | AR sub-ledger |
| Aged Payables | AP aging | AP sub-ledger |
| Audit Trail | Who posted what, when | Journal entries + audit log |

See: `references/financial-statements.md`

## Implementation Checklist

### Phase 1: Foundation (Must Complete First)

- [ ] Create `accounts` table with COA seed data
- [ ] Create `journal_entries` and `journal_entry_lines` tables
- [ ] Create `fiscal_periods` table
- [ ] Build balance validation stored procedure
- [ ] Build `postJournalEntry()` service function
- [ ] Write tests: balanced entry passes, unbalanced rejects

### Phase 2: Auto-Posting Integration

- [ ] Wire sales invoice → auto-post AR/Revenue entry
- [ ] Wire payment received → auto-post Cash/AR entry
- [ ] Wire purchase → auto-post Inventory or Expense/AP entry
- [ ] Wire supplier payment → auto-post AP/Cash entry
- [ ] Wire inventory movement → auto-post COGS/Inventory entry
- [ ] Write tests: each transaction type posts correct entries

### Phase 3: Void & Reversal

- [ ] Build void transaction service (creates reversing entry)
- [ ] Handle partial void scenarios
- [ ] Handle cascade dependencies (warn before void)
- [ ] Write tests: void produces net-zero, balances correct

### Phase 4: Reporting

- [ ] Build Trial Balance report (all accounts, DR/CR totals)
- [ ] Build Balance Sheet (Assets = Liabilities + Equity)
- [ ] Build Income Statement (Revenue - Expenses)
- [ ] Build user-friendly summary reports
- [ ] Build audit trail report
- [ ] Write tests: reports match expected values from test data

### Phase 5: Period Management

- [ ] Build period open/close functionality
- [ ] Prevent posting to closed periods
- [ ] Build year-end closing entry (Revenue/Expense → Retained Earnings)
- [ ] Write tests: closed period rejects entries

## Cross-Skill Integration

| Area | Skill | How It Applies |
|------|-------|---------------|
| Database schema | `mysql-best-practices` | DECIMAL(15,2), indexes, FK constraints |
| API endpoints | `api-error-handling` | Consistent error responses for failed postings |
| Multi-tenancy | `multi-tenant-saas-architecture` | franchise_id on all tables |
| UI reports | `webapp-gui-design` | DataTables for ledger, charts for P&L |
| Mobile reports | `jetpack-compose-ui` | Report screens with tables |
| PDF export | `android-pdf-export`, `report-print-pdf` | Financial statement PDFs |
| Security | `vibe-security-skill` | Protect financial data, audit trail |
| Auth | `vibe-security-skill` | Permission: who can post/void/view reports |
| Inventory link | `inventory-costing-and-stock-accounting` (chwezi-accounting-doctrine) | COGS posting on stock movements |
| Testing | `sdlc-testing` | Test every posting rule, every reversal |
| Implementation | `implementation-status-auditor` (`../../sdlc-meta/implementation-status-auditor/references/plan-implementation.md`) | Execute accounting phases with TDD |
| Audit | `implementation-status-auditor` | Verify accounting system completeness |

## Anti-Patterns

| Don't | Do Instead |
|-------|-----------|
| Let users enter journal entries directly | Auto-post from business transactions |
| Use FLOAT for money | Use DECIMAL(15,2) always |
| Delete journal entries | Create reversing entries (void) |
| Skip balance validation | Enforce DR=CR in stored procedure |
| Store calculated balances only | Store individual entries, calculate on demand |
| Mix accounting with business logic | Separate accounting engine as its own service layer |
| Hard-code account codes | Use configurable COA with tenant-specific accounts |
| Skip audit trail | Log every posting with user, timestamp, IP |
| Allow posting to closed periods | Enforce period status check before posting |
| Show debits/credits to end users | Show friendly labels (Income, Payment, etc.) |

## Accuracy Guarantee

Every implementation MUST pass these tests:

1. **Balance test:** `SELECT SUM(debit) - SUM(credit) FROM journal_entry_lines` = 0.00
2. **Entry test:** Every journal entry has SUM(debit) = SUM(credit)
3. **Trial Balance test:** Total debits = Total credits
4. **Balance Sheet test:** Assets = Liabilities + Equity
5. **Reversal test:** Voided transaction + reversal nets to zero
6. **Period test:** No entries in closed periods
7. **Tenant test:** No cross-tenant data leakage in any query

## See Also

- `references/chart-of-accounts.md` — COA templates and setup
- `references/journal-posting-rules.md` — Complete posting rules per transaction
- `references/void-reversal-patterns.md` — Void mechanics and edge cases
- `references/financial-statements.md` — Report SQL queries and formats
- `references/schema-design.md` — Complete database schema with constraints
- `references/accounting-bookkeeping-erp-patterns.md` — bookkeeping, subledger control, ERP finance configuration, cost accounting, management reporting, and acceptance tests
## Quality Standards

No posting design passes unless entries balance, source events are idempotent, periods and tenants are isolated, reversals preserve history, and control evidence reconciles to reports.

## Outputs

| Artefact | Consumer | Acceptance condition |
|---|---|---|
| SaaS accounting architecture and posting contract | Finance controller and engineering team | Every business event maps to balanced entries, tenant and period controls are enforced, reversals are traceable, and reconciliation evidence is defined |
