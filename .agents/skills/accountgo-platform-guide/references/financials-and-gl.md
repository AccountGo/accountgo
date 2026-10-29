# General Ledger, Chart of Accounts & Financial Reports Guide

This reference covers all user workflows under **General Ledger** (`/org/{orgSlug}/financials`) and **Reports** (`/org/{orgSlug}/reports`).

## Module & Permission Requirements

- **Required Modules**:
  - `financials` — Required for General Ledger, Manual Journals, and Chart of Accounts. (If disabled, clicking Dashboard or General Ledger redirects to Invoices).
  - `reports` — Required for the Financial Reports section (`/org/{orgSlug}/reports`).
- **Permissions** (`Owner` and `Admin` have full access; `Member` users require these granted via a Security Role):
  - `financials.journal.view` — View the General Ledger posted transaction register and manual journal entry list.
  - `financials.journal.create` — Draft manual journal entries.
  - `financials.journal.post` — Post manual journal entries to the General Ledger.
  - `financials.accounts.view` — View the Chart of Accounts (`/org/{orgSlug}/financials/accounts`).
  - `financials.accounts.manage` — Add, edit, archive, or delete accounts, and import/export COA.
  - `reports.view` — View and export financial reports (Trial Balance, P&L, Balance Sheet, Cash Flow).

---

## 1. General Ledger: Posted Transactions Register (`/org/{orgSlug}/financials`)

The General Ledger displays every posted financial transaction across the system with complete debit and credit account lines.

### Sub-Navigation Bar
At the top of the General Ledger screen, you can switch between three views:
1. **Posted transactions** (`/org/{orgSlug}/financials`)
2. **Manual journals / drafts** (`/org/{orgSlug}/financials/journals`)
3. **Chart of Accounts** (`/org/{orgSlug}/financials/accounts`) *(requires `financials.accounts.view`)*

### Filtering & Paging
- **Period Preset**: Choose **This Month**, **Quarter-to-Date**, **Year-to-Date**, **All Time**, or **Custom Range** (`From` and `To` dates).
- **Document Type**: Filter by transaction source: `Sales Delivery`, `Sales Invoice`, `Sales Receipt`, `Purchase Receipt`, `Purchase Invoice`, `Purchase Invoice Payment`, `Journal Entry`, `Customer Allocation`, or `Inventory Adjustment`.
- **Account Code**: Filter transactions that touch a specific general ledger account (e.g., `10100`, `40100`).
- **Transaction #**: Search for an exact sequential transaction number.
- **Search Box**: Free-text search matching transaction numbers, descriptions, account codes, or account names.
- **Pagination**: Default 100 transactions per page (up to 500 max).

### Inspecting Transactions
- Each transaction row displays the **Date**, **Transaction #**, **Document Type**, **Description**, **Source Document ID**, and total debits and credits.
- Expand any row to see its constituent journal lines: **Account code**, **Account name**, **Debit**, **Credit**, and an **In balance** badge verifying that `Total Debits = Total Credits`.
- **Export CSV**: Click **Export CSV** in the top-right header to download a complete spreadsheet of the filtered register.

---

## 2. Manual Journal Entries (`/org/{orgSlug}/financials/journals`)

Use manual journal entries to record adjustments, accruals, depreciations, opening balances, or corrections that do not originate from sales invoices or vendor bills.

### Creating a Manual Journal Entry
1. Navigate to **General Ledger** → click the **Manual journals / drafts** link, or click the **New Journal Entry** button (`/org/{orgSlug}/financials/journals?new=1`).
2. Fill in the header details:
   - **Journal date** *(Required)*: The effective posting date.
   - **Voucher type**: Select one:
     - *Opening balances*
     - *Closing entries*
     - *Adjustment entries*
     - *Correction entries*
     - *Transfer entries*
     - *Other*
   - **Reference**: Optional external identifier or document number.
   - **Memo**: Description of the entry's business purpose.
3. Add debit and credit lines:
   - **Account**: Select a postable leaf account (`canPost: true`). Parent rollup accounts cannot receive direct postings.
   - **Side**: Select **Debit** or **Credit**.
   - **Amount**: Enter a positive numeric amount.
   - **Line memo**: Optional description for the specific account line.
   - Click **Add line** to insert additional rows (minimum 2 lines required).
4. Review the balance indicator at the bottom of the table:
   - If Total Debits match Total Credits, the status indicates **In balance** in green.
   - If unbalanced, it warns **Out of balance** in red and displays the difference.

### Saving vs. Posting
- **Save draft**: Saves the journal entry with an assigned Journal ID. Drafts remain editable and do **not** affect the general ledger or financial statements (`financials.journal.create`).
- **Post journal**: Posts the entry to the live General Ledger (`financials.journal.post`).
  - *Rule*: You must save the draft first before posting.
  - Once posted, the journal becomes permanent, receives a sequential GL Transaction #, and cannot be edited.

---

## 3. Chart of Accounts (COA) (`/org/{orgSlug}/financials/accounts`)

The Chart of Accounts organizes all assets, liabilities, equity, revenues, and expenses into a structured tree hierarchy.

### Account Classifications
1. **Assets** (Class 1) — Normal balance: Debit (Dr)
2. **Liabilities** (Class 2) — Normal balance: Credit (Cr)
3. **Equity** (Class 3) — Normal balance: Credit (Cr)
4. **Revenue** (Class 4) — Normal balance: Credit (Cr)
5. **Expenses** (Class 5) — Normal balance: Debit (Dr)
6. **Closing Accounts** (Class 6) — Year-end retained earnings and closing summaries.

### Key Account Properties
- **Account Code**: Unique numeric code (e.g., `10100` Cash, `12000` Accounts Receivable, `20100` Accounts Payable).
- **Account Name**: Descriptive title.
- **Parent Account**: If selected, the account is a sub-account. Parent accounts display rolled-up balances of their children.
- **Can Post (`canPost`)**: Only leaf accounts without sub-accounts can be posted to directly on invoices, bills, or journals.
- **Contra Account**: Flagged when an account offsets its class (e.g., Accumulated Depreciation under Assets, or Sales Discounts under Revenue).
- **Cash Account**: Flagged for bank accounts and petty cash funds.
- **Protected Accounts**: System accounts required for automatic postings (e.g., Accounts Receivable, Accounts Payable, Retained Earnings) are protected from accidental deletion.

### Adding & Managing Accounts
- **New Account**: Click the **New account** button in the header (requires `financials.accounts.manage`). Enter Code, Name, Class, optional Parent Account, Contra flag, and Cash flag.
- **Edit**: Click the **Pencil icon** on any account row to adjust its name, description, or parent relationship.
- **Archive / Delete**: An account can be archived if it is no longer used. It can only be deleted if it has never had any posted transactions.
- **Import / Export**: Use **Import COA** to upload accounts via CSV (supports `append`, `upsert`, or `replace` mode for empty books) or **Export COA** to download the current chart of accounts.

---

## 4. Financial Reports (`/org/{orgSlug}/reports`)

Available when the workspace has the `reports` module enabled.

### Available Reports
1. **Trial Balance**: Verifies that total debit balances equal total credit balances across all active accounts for the period.
2. **Profit & Loss (Income Statement)**: Calculates total revenues, total expenses, and resulting Net Income.
3. **Balance Sheet**: Cumulative statement of financial position showing Total Assets = Total Liabilities + Total Equity (including unclosed current-period net income).
4. **Cash Flow Statement**: Breaks down cash movements into Operating Activities, Investing Activities, and Financing Activities, detailing starting cash, net change in cash, and ending cash.

### Report Controls
- **Statements Selector**: Click the statement buttons at the top to toggle between **Trial Balance**, **Profit & Loss**, **Balance Sheet**, and **Cash Flow**.
- **Period Filter**: Select from **This Month**, **Quarter-to-Date**, **Year-to-Date**, **All Time**, or **Custom Range** (`From` and `To` dates).
- **Accounting Basis Toggle**:
  - **Accrual Basis** (Default): Recognizes revenue when earned (issued invoices) and expenses when incurred (approved bills).
  - **Cash Basis**: Recognizes revenue when payment is received and expenses when bills are paid.
- **Export CSV**: Click **Export CSV** in the top-right header to download the active report to a spreadsheet.
