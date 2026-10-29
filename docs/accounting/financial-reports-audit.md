# Financial Reports & Accounting Audit: `/org/ws-one/reports`

**Audit Target:** `/org/ws-one/reports` (Workspace: `WS One`, Slug: `ws-one`, Company ID: `3`, Plan: `enterprise`)  
**Auditors:** Senior Financial Accounting Officer & Senior Solution Architect  
**Audit Scope:** Data verification, accounting standards compliance, live data connectivity, security & tenancy isolation.

---

## 1. Executive Summary

A comprehensive financial and architectural audit of `/org/ws-one/reports` was performed.

### Key Verdict
The current route `/org/ws-one/reports` **does not provide a functional financial reporting suite**. It mounts a generic `PlaceholderView.tsx` component that displays raw, unnetted trial balance rows with several hardcoded/fabricated metrics and inoperable controls. 

While the underlying database transactions in Cloudflare D1 are balanced ($42,070 DR = $42,070 CR), the presentation fails basic accounting conventions (no net balances, no balancing totals row, gross amounts concatenated into strings) and lacks core financial statements (P&L, Balance Sheet, Aging schedules).

---

## 2. Live Data vs. Mock/Static Verification

Analysis of `src/Web/src/pages/PlaceholderView.tsx` (lines 187–230) reveals the breakdown between live data and hardcoded placeholders:

| Feature / Element | Implementation Status | Technical Detail |
| :--- | :---: | :--- |
| **Trial Balance Rows** | **Live** | Fetched from `/api/Financials/trialbalance`. Correctly scoped to tenant `ws-one`. |
| **Account Metadata** | **Live** | Account codes and names reflect active database records. |
| **Metric: Financial Year** | **Hardcoded** | Static string `'FY 2026'`. Does not query the active `FinancialYear` table. |
| **Metric: Trial Balance State** | **Hardcoded** | Static string `'Reconciled'`. Never validates that $\sum \text{Debits} = \sum \text{Credits}$. |
| **Metric: Audit Integrity** | **Hardcoded** | Static string `'Verified'`. Pure cosmetic badge with no verification logic. |
| **Metric: Income Statement Items** | **Misleading** | Displays `incomeStatement.length` (`39 Accounts`), counting all unposted COA accounts instead of accounts with period activity. |
| **Table: Category Column** | **Hardcoded** | Labeled "Category", but every row renders the static string `'Standard Ledger'`. |
| **Table: Status Column** | **Hardcoded** | Labeled "Status", but every row renders `'Reconciled'`. |
| **Action: Export Statement** | **Dead Control** | Button has no `onClick` handler or export generator. |
| **Action: Filters** | **Dead Control** | Button has no handler or filter drawer. |
| **Action: Row View** | **Dead Control** | Eye icon link has no navigation or drill-down modal. |

---

## 3. Financial Accounting Audit & Numbers Verification

### A. General Ledger Data for `ws-one` (Company ID: 3)
Verification against the live database confirms 15 General Ledger entries:
- **Sales Invoices:** Invoices #1, #4, and #5 posted; Invoices #2 and #3 voided with exact reversing GL entries.
- **Purchase Bills:** Bill #1 posted and partially paid (2 disbursements); Bills #2 and #3 voided with exact reversing GL entries.
- **Ledger Sums:** Total Debits = **42,070.00**, Total Credits = **42,070.00**, Out of Balance = **0.00**.

### B. Trial Balance Account Discrepancy
The raw endpoint `/api/Financials/trialbalance` groups transactions as follows:

| Code | Account Name | Account Class | Total Debit | Total Credit | Net Balance (True TB) | Current Page Rendering |
| :--- | :--- | :---: | :---: | :---: | :---: | :---: |
| **10111** | Regular Checking Account | Asset (1) | ₱5,000.00 | ₱480.00 | **₱4,520.00 Dr** | `DR: ₱5,000.00` / `CR: ₱480.00` |
| **10120** | Accounts Receivable | Asset (1) | ₱25,000.00 | ₱15,000.00 | **₱10,000.00 Dr** | `DR: ₱25,000.00` / `CR: ₱15,000.00` |
| **13302** | Plants & Equipments | Asset (1) | ₱360.00 | ₱180.00 | **₱180.00 Dr** | `DR: ₱360.00` / `CR: ₱180.00` |
| **20110** | Accounts Payable | Liability (2) | ₱1,035.00 | ₱1,035.00 | **₱0.00** | `DR: ₱1,035.00` / `CR: ₱1,035.00` |
| **40100** | Sales A/C | Revenue (4) | ₱10,000.00 | ₱25,000.00 | **₱15,000.00 Cr** | `DR: ₱10,000.00` / `CR: ₱25,000.00` |
| **50200** | Purchase A/C | Expense (5) | ₱675.00 | ₱375.00 | **₱300.00 Dr** | `DR: ₱675.00` / `CR: ₱375.00` |

#### Accounting Deficiencies:
1. **Gross Turnover vs. Net Balances:** A standard trial balance reports the net balance in either the Debit or Credit column. Displaying gross cumulative debit and credit strings confuses transaction volume with ending balances.
2. **Missing Zero-Balance Filtering:** Accounts with zero net balance (e.g., Accounts Payable `20110` at ₱0.00) are listed as unreconciled lines rather than cleared balances.
3. **Absence of Footing / Totals Check:** There is no footer row displaying total debits vs total credits ($\sum = ₱15,000.00$).

---

## 4. Accountant Requirements Gap Analysis

A professional accounting officer requires the following capabilities under `/reports`:

1. **Multi-Statement Navigation:**
   - **Balance Sheet (Statement of Financial Position):** Assets = Liabilities + Equity (incorporating unclosed period net profit per IFRS IAS 1).
   - **Income Statement (Profit & Loss):** Revenues − Cost of Goods Sold = Gross Profit − Operating Expenses = Operating Profit − Tax = Net Profit.
   - **Trial Balance:** Formatted with Beginning Balance, Period Activity (Dr/Cr), and Ending Balance.
   - **Aging Schedules:** Accounts Receivable (A/R) and Accounts Payable (A/P) aged by 0–30, 31–60, 61–90, 90+ days.
2. **Date & Period Filtering:**
   - Date range selector (This Month, This Quarter, Year-to-Date, Custom Period).
   - Comparative periods (Current Period vs Prior Period with Variance and % Variance).
   - Cash vs. Accrual accounting toggle.
3. **Drill-Down Capabilities:**
   - Clicking an account row opens the detailed General Ledger ledger transactions for that account.
4. **Export Utilities:**
   - Export to formatted CSV/Excel (`.xlsx`) and print-ready PDF statements.

---

## 5. Solution Architecture & Security Analysis

1. **Module Gating Inconsistency:**
   - Frontend route `src/Web/src/App.tsx` requires `<RequiresModule module="reports">`.
   - Backend `Api/Controllers/FinancialsController.cs` has `[RequireModule("financials")]`. If a tenant subscribes to `reports` without `financials`, all API requests fail with HTTP 403 Forbidden.
   - **Fix:** Update controller attribute to `[RequireModule("financials", "reports")]`.
2. **Granular RBAC Bypass:**
   - `Core/Domain/Security/SystemModule.cs` defines `SystemPermissions.ReportsView`.
   - Endpoints `/api/Financials/trialbalance`, `/BalanceSheet`, and `/IncomeStatement` do not declare `[RequirePermission(SystemPermissions.ReportsView)]`.
   - **Fix:** Add `[RequirePermission(SystemPermissions.ReportsView)]` to reporting endpoints.
3. **Database Performance & In-Memory Grouping:**
   - `FinancialService.TrialBalance()` currently executes `.AsEnumerable()` before grouping, loading raw ledger rows into memory.
   - **Fix:** Refactor into database-level `IQueryable` aggregations (`GROUP BY` in SQL).

---

## 6. Implementation Action Plan

- **Step 1:** Synchronize backend permissions and module gating in `FinancialsController.cs`.
- **Step 2:** Refactor backend services to support active `from` and `to` date filtering on `TrialBalance`, `IncomeStatement`, and `BalanceSheet`.
- **Step 3:** Implement dedicated `Reports.tsx` component replacing `PlaceholderView` at route `/org/:orgSlug/reports`.
- **Step 4:** Add statement tabs (*Trial Balance*, *Profit & Loss*, *Balance Sheet*), date range picker, net balance formatting, and CSV export.

---

## 7. Financial Reporting Suite Implementation Brief

### Objective and target state

Replace the placeholder at `/org/:orgSlug/reports` with
`src/Web/src/pages/Reports.tsx`, mounted by `src/Web/src/App.tsx`.

- **Trial Balance:** Sort accounts by code, show each net balance in either Debit
  or Credit, and display Total Debits, Total Credits, Difference, and a computed
  in-balance indicator.
- **Profit & Loss:** Categorize revenue, cost of goods sold, and operating
  expenses; calculate Gross Profit, Operating Expenses, and Net Profit.
  Hide zero balances by default.
  The existing API has no separate COGS classification: use chart-of-accounts
  names (Cost of Goods Sold, Cost of Sales, Direct Costs, Purchase/Purchases)
  and their descendants to identify direct costs; other expenses are operating
  expenses. No account-code ranges are assumed.
- **Balance Sheet:** Categorize Assets, Liabilities, and Equity, including
  unclosed net income under Retained Surplus / Accumulated Profit. Losses reduce
  equity; contra accounts retain their signed class balances.
- Provide This Month, Quarter-to-Date, Year-to-Date, All Time, and custom/as-of
  date controls, sending `from` and `to` query parameters. Trial Balance and P&L
  report movement in the selected range. Balance Sheet is cumulative through
  `to`, preserving opening balances regardless of `from`.
- Export the selected statement to client-generated CSV, including totals.
- Read fiscal-year metadata from the active `FinancialYear`, without fabricated
  fiscal years, reconciliation, audit verification, or ledger-category badges.

### Backend contract

- Gate `FinancialsController` with `[RequireModule("financials", "reports")]`.
- Preserve `financials` module requirements on write actions so reports-only
  workspaces cannot modify journals, accounts, or banks.
- Require `SystemPermissions.ReportsView` on reporting actions.
- Bind and validate nullable `from`/`to` parameters on all three statements.
- Apply inclusive ledger date bounds before database-level Trial Balance
  aggregation; derive the other statements from those ledger totals.
- Assets and Expenses are debit-normal; Liabilities, Equity, and Revenues are
  credit-normal. Do not replace a negative equity balance with a positive loss.

### Scope and constraints

Implementation is limited to `Reports.tsx`, `App.tsx`,
`FinancialsController.cs`, `FinancialService.cs`, and report tests under
`test/AccountGo.BackendTests/` or `src/Web/test/`, plus this requested document.
Preserve existing Tailwind tokens and styling. Do not modify transaction
processing services, database schemas, D1 migrations, or Docker configuration.
Do not delete files or add npm/NuGet packages without approval. No external
charting dependencies are needed.

### Acceptance and verification

- `/org/ws-one/reports` renders Trial Balance, Profit & Loss, and Balance Sheet.
- Trial Balance presents net debit/credit columns and a balancing footer.
- Date controls change statement API query parameters.
- Every statement can download its currently displayed data as a clean CSV.
- Reports-only module access works and `reports.view` is enforced.
- Run `dotnet test` and `pnpm --dir src/Web build`; add focused report tests.
- Create a new branch before implementation and report completed steps.
