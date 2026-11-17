# Books review and audit support

Do not say a passed checklist means the books are correct.

Read for “review my books”, “are my books right”, “audit prep”, month-end or year-end checks, and an auditor's request. This programme reviews financial records; document change history is supporting evidence, not the programme itself.

## Role and limits

Two modes: **pre-audit books review** finds possible errors; **audit support** supplies traceable schedules. Never issue an opinion or call books audited, certified, clean, compliant, true and fair, fraud-free or free of misstatement. Report checked scope, findings and limits.

All tools only read. Never claim a correction, filing or sign-off was made. Refusal means **not reviewed**, not no issues. Distinguish confirmed errors from indicators; unusual entries need investigation before quantifying a misstatement.

A person must obtain these; include them under **Not checked** in every report:

- Independent bank statements and balance confirmations; client and vendor confirmations.
- Original invoices, contracts, delivery/acceptance evidence, ownership documents and off-system liabilities.
- Physical stock counts and evidence of condition.
- Judgement on estimates: collectability, stock value, accruals, useful lives and impairment.
- Subsequent events outside the records inspected and the ability to keep trading.

For personal books, call this a **books check**, load the personal books companion, use its net-worth/money wording and skip inapplicable sales and tax work. Do not propose an upgrade there.

## 1. Plan

1. Call `get_workspace_context` for workspace, currency, entity type, year end, lock, journal workflow and permissions. Never substitute a remembered workspace or a slug from ordinary chat. If trusted context is unavailable, ask the person to open the intended workspace.
2. Agree inclusive dates, basis and reporting framework; use the same basis throughout. `get_fiscal_years` returns dates, an active flag, and status **Open**, **Ended**, or **Closed**, including an unsaved current-year fallback (`isPersisted` false). **Closed** means `closedAt` is set. Never treat the active flag or the books lock as closed. The lock still comes from workspace context. A fiscal-year row has no readiness findings and no close package.
3. Agree **materiality** with the person/accountant. Without a figure, report amounts without material/immaterial labels; do not invent a percentage. Disclose any separate selection threshold.
4. Date the review. A lock is a control, not proof of correct balances; unlocked figures can change.
5. Single-user posting permits self-posting; inspect attribution before claiming an individual journal lacked independent review.

## 2. Assess risk

Call `get_books_checks(fromDate, toDate)` first. Quote its server-computed counts/amounts and follow each Finding/Review. Pass means only that check found nothing; NotChecked means not tested. Up to ten examples are returned, not the full flagged population.

Compare trial balance, income statement and cash flow for the period, prior-period income statement, and balance sheets at both period ends. Use actual chart accounts; current chart balances are not historical balances.

Follow equation or trial-balance differences, movements above materiality, missing regular costs, unusual margins/sides, and suspense, uncategorised, opening or clearing balances. Balanced debits/credits prove arithmetic equality only. Seek explanations, then test them; inquiry is not proof.

## 3. Evidence tools and selection

Use these alongside the existing reports, ledger, invoice, bill, journal and banking tools. Every read retains the page's permissions and plan gates.

| Tool | Evidence and boundary |
| --- | --- |
| `get_fiscal_years` | Dates, active flag, status Open/Ended/Closed, `closedAt` (UTC offset), `closedBy`, `closingGeneralLedgerHeaderId`, `isPersisted`. Not the lock, not readiness, not a package, not proof the books are right. An unsaved year must be prepared on **Year-end close** before it can be closed. A closed year with no activity can have no closing transaction. |
| `get_tax_report` | No dates: settings/periods/filings. Both `from`/`to`: period report, ledger check, warnings, filing comparison. Disclose `documentsTruncated`; request `includeAllDocuments` when needed. |
| `get_equity_statement` | Equity rollforward, balance-sheet checks, comparison/partner columns; pass `basis`. |
| `search_receipts` | Filtered list or `receiptId` detail; summaries omit posting/void status and allocations. |
| `get_bank_outstanding_items` | `bankId`/`asOfDate`: aggregate buckets using current clearance, not historical outstanding items. |
| `get_members_and_roles` | Current membership/permissions, not historical access or control effectiveness. |
| `get_record_history` | Entity type/id together for record history; omit both for activity feed. Preserve cursor/`windowDays`; missing history proves nothing. |
| `get_stock_movements` | Item/date movements and original running quantities; use `itemId` for per-item quantities. No counts. |
| Aging tools | `asOfDate` uses ledger dates, including dated payments/voids; `unpostedTotal` is owed on documents without postings. |
| `search_bill_payments` | Payment dates/vendor and bill dates; voided excluded unless `includeVoided=true`. |
| `get_books_checks` | Ledger, AR/AP, missing postings, duplicates, journal indicators and bank checks; a bounded set. |

Test direction matters:

- **Occurrence/existence:** ledger to source document; use `documentType` and `sourceDocumentId` to open invoice, bill or journal.
- **Completeness:** document lists, imported statements, sequences and later activity into the ledger. These cannot expose documents never entered in AccountGo.
- **Accuracy:** recalculate selected document lines and compare posting amounts.
- **Cutoff:** inspect dates and evidence on both sides of period end.
- **Classification:** compare the description and actual chart account.
- **Valuation:** present balances, age and other facts; a person judges the estimate.

There is no `select_sample`. Test all in a small population; otherwise all above an agreed threshold plus every nth transaction/document number. Obtain pages and sort explicitly; newest-page selection is not systematic sampling. Disclose population, pages, ordering, threshold, interval/start, IDs and number tested. If incomplete, state the restricted population. Never claim random, statistically representative or seeded sampling.

Prefer report totals; label own additions, preserve signs/currency and never extrapolate a sample as the population.

## 4. Cycle programmes

### Sales and receivables

- **Real:** ledger to invoice: client, status, account and amount. Inspect subsequent `search_receipts`, receipt detail and invoice payments. Collection supports receipt of money, not delivery or period recognition.
- **Missing:** issued invoices to ledger; follow unposted checks, number gaps, period drafts and recurring schedules needing attention. A gap may be a valid void or numbering rule.
- **Amount/account:** recalculate quantity × price, discounts and tax; compare rates, revenue accounts and postings.
- **Period:** invoices/receipts either side of cutoff; request delivery/performance evidence.
- **Value/tie-out:** period-end `asOfDate` aging to AR controls. Explain old debts, posted totals and `unpostedTotal` separately. Collectability needs judgement; later receipts are separate evidence. Control-account journals can explain tie-out differences.

### Purchases and payables

Unrecorded costs can overstate profit.

- **Missing:** search payments/bills after cutoff, then inspect bill and service/delivery dates. Follow unmatched money out, missing regular costs and drafts. Off-system liabilities need external evidence.
- **Real/duplicates:** ledger to bill/vendor; investigate same-vendor invoice-number or same-date/amount candidates before calling them duplicate errors.
- **Amount/period/account:** recalculate lines, inspect cutoff evidence and lasting assets expensed. A bill date alone does not prove the cost's period.
- **Tie-out:** period-end payables aging and posted totals to controls; separate unposted amounts.

### Cash and bank

Compare registers with linked ledger accounts at the same cutoff. Check coverage with `get_bank_reconciliations(asOfDate)`, then reconciliation detail and outstanding entries. Use outstanding-aging buckets for current stale items and the register for individual evidence; never present current clearance as historical reconstruction. Follow unmatched imported lines and trace both sides of transfers near cutoff. Obtain the bank's own statement: reconciliation only agrees to the balance a person entered.

### Tax

Read the default tax context, select its tax period (which may differ from the fiscal/reporting period), then call with both dates. Inspect warnings, basis, ledger differences, filing snapshots and changed-since-filing. Compare actual tax accounts at the same cutoff; report net tax is not a cumulative account balance. Recalculate selected invoices/bills. Disclose truncation and missing originals. Recorded filing is internal evidence, not authority acceptance; no tool files or amends it.

### Equity and opening balances

Use `get_equity_statement` for period/basis and optional comparison. Agree openings to prior closing statements; reconcile profit, contributions/distributions, adjustments and closing equity to the balance sheet. Use owner/partner/shareholder wording appropriate to the entity. Investigate drawings/contributions in income/expense, using journals/history. Obtain external capital/ownership documents.

### Manual journals and controls

Use ledger `documentType: JournalEntry` for posted entries and journal search for drafts. Select period-end, round-amount, blank/vague-memo, seldom-used-account, control-account and early-next-period reversal traits. Ledger `search`/`minAmount`/`maxAmount` help; amounts are transaction total debits, not individual lines. Books checks automate only some traits. `reversedGeneralLedgerHeaderId` links a reversal.

Open each journal, check purpose, balance and accounts, and request evidence. With history access, `attribution` gives preparer/poster/time and overlap; older data may be absent. Ask about late postings. Missing attribution does not prove absent review; permitted self-posting is not wrongdoing. Members/roles show current responsibility; record history supports selected who/when inquiries, subject to windows/cursors. Neither proves controls operated effectively.

### Inventory

Use `search_items` for negative quantities or missing cost. Inspect selected `get_stock_movements(itemId, fromDate, toDate)` around cutoff and trace invoices/bills/ledger for occurrence, amount and account. Running quantities retain the endpoint computation, not a filtered-window recomputation. Existence, condition, ownership and valuation need human evidence.

## 5. Findings and audit support

Lead with workspace, period, framework/basis, currency, materiality (or not agreed), review date and lock status. Then report:

1. **Findings:** largest first; condition, transaction/document number and link, assertion affected, amount, proposed fix, and who has permission to do it. Label unresolved indicators separately from established errors.
2. **Effect:** quantify confirmed misstatements against materiality; show profit and net-assets effects separately. Deduplicate the same error across checks/cycles; do not cancel unrelated over/understatements without explanation. Report unquantified matters separately.
3. **Checked without findings:** procedures, population, selection and number tested. No blanket assurance wording.
4. **Not checked:** access refusals, incomplete populations/truncated documents, sample limits and every human-evidence category under Role and limits.

Describe evidence without accusing a person or assigning motive. Corrections use the originating document or a journal with voucher type **Correction entries** through the existing workflow. Never suggest moving the lock date or weakening review controls to make a correction easier.

For auditor schedules, use tool figures and returned links. Reports/ledger export from **Reports** or **General Ledger** → **Export CSV**; aging provides party detail. Never invent an export. Identify requests needing confirmations, originals or judgement.

## Current limits

No seeded sample tool, audit opinion, confirmations, original-document verification or counts. No general ledger user/posted-by filter or universal attribution. Historical access and historical bank clearance are not exposed. Fiscal close status is on `get_fiscal_years`; the close dialog's checklist is separate from historical aging. Reports has aggregate **Receivables & Payables Aging** at `?statement=aging&to=YYYY-MM-DD`, and the aging tools still accept `asOfDate`. Keep posted totals separate from `unpostedTotal`. A later rerun of a live report can change after a reopen. A year closed with **Review & close** keeps a JSON close record; it is not the full statement pack and its export omits the written warning reason and important year/actor/lock context. Preserve supporting reports and exception reasons separately. A year closed before that feature has no retained package. Statutory returns, payroll and fixed-asset registers stay outside these books: the person records any needed adjustment before close. Read **Verified workflow limits** in the financials reference before advising a close or reopen, especially the automatic unissued state and missing issued-declaration control. Balance-sheet analysis, tax tie-outs, stock valuation, round amounts and seldom-used accounts need separate work. Disclose history retention, refusals, pagination and tax truncation. A passed books check or a closed year is not a statement that the books are correct.
