# General Ledger, Chart of Accounts & Financial Reports Guide



This reference covers all user workflows under **General Ledger** (`/org/{orgSlug}/financials`) and **Reports** (`/org/{orgSlug}/reports`).

## Module & Permission Requirements

- **Required Modules**:
  - `financials` — Required for General Ledger, Manual Journals, and Chart of Accounts. (If disabled, clicking Dashboard or General Ledger redirects to Invoices).
  - `reports` — Required for the Financial Reports section (`/org/{orgSlug}/reports`).
- **Permissions** (the workspace role does not grant these: `Owner`, `Admin` and `Member` each need them through a functional access profile or Security Role):
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
- **Account Code**: Filter transactions that touch a specific general ledger account (use a code from this workspace's chart).
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
     - *Adjustment entries*
     - *Correction entries*
     - *Transfer entries*
     - *Other*
     - *Prior-period adjustment*
     - **Closing entries** is not in this list. The year-end close creates that voucher, and a person cannot save one by hand. An existing system closing journal still displays the label **Closing entries**.
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
  - If this workspace requires another person to approve journals, posting still waits for that approval. Year-end close does not bypass that rule for ordinary journals, and it does not add a second approver of its own.

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
- **Account Code**: Unique numeric code. Read the actual codes from this workspace's chart; do not quote defaults.
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
1. **Trial Balance**: Lists each account's debit or credit balance for the period and shows whether total debits equal total credits. A balanced trial balance does not prove the entries are correct.
2. **Profit & Loss (Income Statement)**: Calculates total revenues, total expenses, and resulting Net Income.
3. **Balance Sheet**: Cumulative statement of financial position showing Total Assets = Total Liabilities + Total Equity (including unclosed current-period net income).
4. **Cash Flow Statement**: Breaks down cash movements into Operating Activities, Investing Activities, and Financing Activities, detailing starting cash, net change in cash, and ending cash.
5. **Receivables & Payables Aging**: outstanding customer and vendor balances as of one date. Open it from the statement buttons or `/org/{orgSlug}/reports?statement=aging&to=YYYY-MM-DD`. The screen has an as-of date and no cash/accrual switch. The heading is **Outstanding balances as of [date]; GL tie-out on accrual ledger basis**. A section the person cannot view says it is denied and is not shown as zero. Export CSV uses that same date. There is no aging PDF. `get_receivables_aging` and `get_payables_aging` remain available with `asOfDate`.

### Report Controls
- **Statements Selector**: Click the statement buttons at the top to toggle between **Trial Balance**, **Profit & Loss**, **Balance Sheet**, **Cash Flow**, and **Receivables & Payables Aging**.
- **Period Filter**: Select from **This Month**, **Quarter-to-Date**, **Year-to-Date**, **All Time**, or **Custom Range** (`From` and `To` dates).
- **Accounting Basis Toggle**:
  - **Accrual Basis** (Default): Recognizes revenue when earned (issued invoices) and expenses when incurred (approved bills).
  - **Cash Basis**: Recognizes revenue when payment is received and expenses when bills are paid.
- **Export CSV**: Click **Export CSV** in the top-right header to download the active report to a spreadsheet. Aging CSV comes from the same as-of date. Aging has no PDF.

Profit and loss omits system closing journals, so a closed year's revenue and expense remain visible. A cumulative trial balance includes those journals, so nominal accounts move to equity after the close. Run the report again after a reopen. The retained close package is a separate download on the year-end screen.

---

## 5. Year-end close

Close is not a manual journal. Open **Year-end close** (`/org/{orgSlug}/financials/year-end`) when the person has `financials.periods.close` or `financials.accounts.view`. An Owner or Admin can also use the same card under **Settings** → **Financial Rules & Banking** (`/org/{orgSlug}/settings?tab=financials`). Personal books use **Settings** → **Books**. The card title is **Year-end close**.

1. If the year is not listed, a person with close permission uses **Prepare fiscal years through next year**. Reading the year list does not create the year. `get_fiscal_years` may include that unsaved year with `isPersisted` false, status **Open**, and no closer or closing transaction. Tell the person to prepare it before closing. Do not invent a closing journal for a year that had nothing to close: a saved closed year can have `closingGeneralLedgerHeaderId` null.
2. Status on that list and on `get_fiscal_years` is **Open**, **Ended**, or **Closed**. **Closed** means the year was closed (`closedAt` is set). The active flag is not closed and is not open. Do not infer **Closed** from the books lock date.
3. **Review & close** is shown for an **Ended** year when the person has close permission. It opens the preview: blockers, warnings, and the closing lines the system will post. The button in the dialog is also **Review & close**.
4. The lines use this workspace's configured equity accounts. Do not tell every entity to close into retained earnings or code 30300. Read the chart when you name an account. Defaults differ by entity: owner's capital, partners' capital (with each partner's share on the preview), retained earnings, retained surplus, net assets, or accumulated net worth for personal books. The preview is the list that will post.
5. Warnings require the checkbox **I acknowledge these warnings and want to proceed** and a written **Reason for closing with warnings**. If the preview is out of date, the screen asks the person to use **Refresh review** and does not close. A clean close needs close permission. A warning close in a workspace that requires independent journal review also needs an Owner or Admin who has close permission.
6. A successful close posts the system closing journal when the lines are not zero, records who closed it and when, and moves the books lock forward to the year end when the lock was earlier. A zero-activity close can have no closing transaction.
7. Corrections:
   - The latest closed year can be reopened with **Reopen**, then **Reason (required)**, then **Reopen year**, when the statements are recorded as not issued. An issued year cannot be reopened from this screen. Reopening reverses the system closing journal and rolls the lock back only when this close imposed it. Correct the books in the open year, then close again with **Review & close**. **Download close package** saves the retained record for a year closed with this feature. A year closed earlier says **No retained close package: closed before this feature**.
   - An earlier closed year stays closed. Post a manual journal with voucher type **Prior-period adjustment** in the current open year: memo required, the closed year selected, lines on balance-sheet accounts including the prior-period adjustments account (confirm code 30820 on this workspace's chart). Income and expense accounts are refused. Say that this records the adjustment in the books. The accountant still handles any external restatement or disclosure. It does not reopen the old year.
   - A change in estimate is an ordinary journal in the current open year, not a prior-period adjustment.
8. `get_fiscal_years` does not list blockers, warnings, or a close package, and it does not say the books are right. The books lock comes from `get_workspace_context`. Close checklist links and amounts are what the **Review & close** preview shows for this workspace.
