# General Ledger, Chart of Accounts & Financial Reports Guide

All paths are relative to `/org/{orgSlug}`. Use the active workspace. Owner/Admin does not grant functional permissions.

## Access

General Ledger, journals and Chart of Accounts require `financials`; Reports requires `reports` and `reports.view`. Ledger/journal read: `financials.journal.view`; draft: `financials.journal.create`; post: `financials.journal.post`; accounts read: `financials.accounts.view`; manage/import/export: `financials.accounts.manage`. Year-end close requires `financials` and either `financials.periods.close` or `financials.accounts.view`; closing/reopening needs `financials.periods.close`.

## 1. Posted transactions (`/financials`)

Switch among **Posted transactions**, **Manual journals / drafts** (`/financials/journals`), and **Chart of Accounts** (`/financials/accounts`). The register contains posted entries and expandable debit/credit lines.

Filters: This Month, Quarter-to-Date, Year-to-Date, All Time, Custom Range (From/To), Document Type, Account Code, exact Transaction #, and free text matching numbers/descriptions/account codes/names. Default page size 100, maximum 500. Document types include Sales Delivery/Invoice/Receipt, Purchase Receipt/Invoice/Invoice Payment, Journal Entry, Customer Allocation, Inventory Adjustment.

Rows show date, transaction number, type, description, source ID and debit/credit totals. Expand for account code/name and debit/credit lines. **In balance** means equal totals, not correct accounting. **Export CSV** exports the filtered register. Follow source-document links/read tools when investigating documents behind a ledger line.

## 2. Manual journals (`/financials/journals`)

For adjustments, accruals, depreciation, opening balances and corrections that do not duplicate document postings:

1. Click **New Journal Entry** (`?new=1`). Enter required **Journal date**, **Voucher type**, optional **Reference** and **Memo**.
2. Types: Opening balances, Adjustment entries, Correction entries, Transfer entries, Other, Prior-period adjustment. **Closing entries** is system-only: it cannot be selected/saved manually, but can label an existing system journal.
3. Use at least two lines: current postable leaf **Account** (`canPost: true`), **Debit**/**Credit**, positive **Amount**, optional **Line memo**. **Add line** adds rows. Parents cannot receive direct postings. Read the actual chart before naming accounts.
4. Check **In balance**/**Out of balance** and debit/credit totals. **Save draft** assigns a journal ID, remains editable, and does not affect posted books. Save before **Post journal**. Posting assigns a GL transaction number and prevents editing.
5. If independent review applies, a different eligible reviewer must approve first. Close does not waive ordinary-journal review or add its own separate second approver.

## 3. Chart of Accounts (`/financials/accounts`)

Classes and normal sides: Assets 1 debit, Liabilities 2 credit, Equity 3 credit, Revenue 4 credit, Expenses 5 debit, Closing accounts 6. Permanent closing equity is Class 3; retained earnings is not Class 6. Contra accounts may have the opposite side.

Properties: code/name, optional parent, contra/cash flags, `canPost`. Template codes are not the workspace's actual configuration. Parents roll up children; only leaves post. System posting accounts are protected from accidental deletion.

**New account** needs manage access: code/name/class, optional parent and contra/cash flags. Pencil edits allowed properties. **Archive** removes accounts from ordinary future use; **Delete** requires no posted history. **Import COA** supports CSV append/upsert, and replace for empty books; **Export COA** downloads the chart. Follow import preview/restrictions; do not promise populated books can be replaced.

## 4. Reports (`/reports`)

Statement buttons: **Trial Balance**, **Profit & Loss**, **Balance Sheet**, **Changes in Equity**, **Cash Flow**, **Receivables & Payables Aging**. Personal books use the personal-books guide's labels.

Trial Balance compares debits/credits; a balanced result is not proof of correct entries. Balance Sheet is cumulative and includes unclosed earnings. Changes in Equity shows the rollforward. Cash Flow shows operating/investing/financing movements and beginning/ending cash.

Normal controls: This Month, Quarter-to-Date, Year-to-Date, All Time / As-of Date, Custom Range, From/To, **Accounting Basis** (Accrual/Cash), **Hide zero balances**, CSV/PDF where available. Accrual recognizes issued invoices/approved bills; cash projects recorded payments. Cash presentation does not replace the accrual ledger used for closing.

Profit & Loss excludes system closing/reversal journals, preserving historical operating results. A cumulative Trial Balance includes them and can verify nominal balances cleared. Re-run statements after correction/reopen. The retained close record is a separate year-end download.

Aging: `/reports?statement=aging&to=YYYY-MM-DD`. One **As-of Date**, no basis selector. Heading: **Outstanding balances as of [date]; GL tie-out on accrual ledger basis**. Aggregate receivables/payables buckets, document count, posted/unposted totals, ledger control/difference; denied sections are labelled, not zero. **Export CSV** uses the cutoff. No aging PDF, party/document table or drilldown. Use `get_receivables_aging`/`get_payables_aging` with `asOfDate`, then source-document read tools for the detail they return. Do not promise party-level UI exports.

## 5. Year-end close


Close is not a manual journal. Open **Year-end close** (`/org/{orgSlug}/financials/year-end`) when the person has `financials.periods.close` or `financials.accounts.view`. An Owner or Admin can also use the same card under **Settings** → **Financial Rules & Banking** (`/org/{orgSlug}/settings?tab=financials`). Personal books use **Settings** → **Books**. The card title is **Year-end close**.

1. If the year is not listed, a person with close permission uses **Prepare fiscal years through next year**. Reading the year list does not create the year. `get_fiscal_years` may include that unsaved year with `isPersisted` false, status **Open**, and no closer or closing transaction. Tell the person to prepare it before closing. Do not invent a closing journal for a year that had nothing to close: a saved closed year can have `closingGeneralLedgerHeaderId` null.
2. Status on that list and on `get_fiscal_years` is **Open**, **Ended**, or **Closed**. **Closed** means the year was closed (`closedAt` is set). The active flag is not closed and is not open. Do not infer **Closed** from the books lock date.
3. **Review & close** is shown for an **Ended** year when the person has close permission. It opens the preview: blockers, warnings, and the closing lines the system will post. The button in the dialog is also **Review & close**.
4. The lines use this workspace's configured equity accounts. Do not tell every entity to close into retained earnings or code 30300. Read the chart when you name an account. Defaults differ by entity: owner's capital, partners' capital (with each partner's share on the preview), retained earnings, retained surplus, net assets, or accumulated net worth for personal books. The preview is the list that will post.
5. Warnings require the checkbox **I acknowledge these warnings and want to proceed** and a written **Reason for closing with warnings**. If the preview is out of date, the screen asks the person to use **Refresh review** and does not close. A clean close needs close permission. A warning close in a workspace that requires independent journal review also needs an Owner or Admin who has close permission.
6. A successful close posts the system closing journal when the lines are not zero, records who closed it and when, and moves the books lock forward to the year end when the lock was earlier. A zero-activity close can have no closing transaction.
7. Corrections:
   - The latest closed year can be reopened with **Reopen**, then **Reason (required)**, then **Reopen year**, when eligible and the statements were not externally issued/filed. An issued year cannot be reopened. Reopening reverses the system closing journal and can roll the lock backward; the dialog shows the current/proposed dates and may require an explicit acknowledgement for an independent lock. A later independent lock is not automatically restored. Correct the books in the open year, then close again with **Review & close**. **Download close package** downloads the latest retained record as JSON. Older closes may have no retained record; do not promise a successful package download for them.
   - An earlier closed year stays closed. Post a manual journal with voucher type **Prior-period adjustment** in the current open year: memo required, the closed year selected, lines on balance-sheet accounts including the prior-period adjustments account (confirm code 30820 on this workspace's chart). Income and expense accounts are refused. Say that this records the adjustment in the books. The accountant still handles any external restatement or disclosure. It does not reopen the old year.
   - A change in estimate is an ordinary journal in the current open year, not a prior-period adjustment.
8. `get_fiscal_years` does not list blockers, warnings, or a close package, and it does not say the books are right. The books lock comes from `get_workspace_context`. Close checklist links and amounts are what the **Review & close** preview shows for this workspace.

### Verified workflow limits

Use these boundaries when answering a closing question. Do not present the intended accounting workflow as delivered software, or claim the assistant can close/reopen books: its tools only read.

- Before closing, guide the accountant to finish cutoffs/reconciliations and review period reports, and to consider any needed depreciation, inventory, accrual, prepayment, payroll and tax adjustments. These are accountant preparation steps, not an automated checklist the close screen completes or a certification of the books.
- The review currently shows dates, blockers/warnings and individual closing lines. It does not display the full framework/basis/books-start context, financial-effect summary, external-adjustments reminder, balanced totals footer or close-lock impact. For a zero-activity year the lines can be empty without an explanatory message. Do not invent those controls or a six-stage wizard.
- The draft-invoice **Review** link currently opens an invalid invoice page. If encountered, use **Invoices & Sales** (`/org/{orgSlug}/sales`), choose **Drafts**, and review the year's invoice dates there. Do not link to `/sales/invoices`. The journals and bills resolution lists have scoped date/status links; there is no dedicated return-to-close control.
- The year list labels ended years **Ended — ready to close** before it has checked their blockers. That wording is not readiness approval. If fiscal-year ranges overlap after a calendar change, stop the close walkthrough and have the setup corrected; do not recommend closing each overlapping row.
- The application currently records **Unissued** automatically at a first close. That is not an accountant's declaration or evidence that financial statements or returns were never externally issued/filed. Confirm the real external status before recommending reopening. The UI cannot record an **Issued** declaration. If statements were filed/issued, use the correction policy for a prior-period adjustment and arrange appropriate authorized support rather than using **Reopen** just because the button is enabled. A displayed ineligibility reason still applies even if **Reopen year** becomes clickable; the server rejects it.
- The downloaded JSON close record contains a digest, revision, check outcomes, closing lines and aggregate statement/aging figures. It is not a complete financial statement or party-level evidence pack: the export currently omits the written warning exception reason, workspace/year identity, actor/time and lock/journal references. Do not claim those are in the download or that it proves external filing status. Preserve supporting reports and the accountant's exception rationale separately. The UI has no close-package CSV selector, closing-transaction link or historical-run selector. It can also show a package button to a read-only reviewer whose download is denied.
- A partnership can have partner allocations in the preview when already configured. The **Partners** settings tab may be missing even for a saved Partnership; do not promise that setup can be completed through that absent tab or invent another screen.
- Historical aging currently shows one aggregate table per receivables/payables section, posted/unposted totals, ledger control and difference. It has no party/document rows or drilldown. A later payment does not reduce an earlier cutoff balance. The report may incorrectly say **No active financial year available** while the workspace has a current year; that heading does not prove the year is missing.
