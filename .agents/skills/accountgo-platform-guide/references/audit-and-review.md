# Books review and audit support

Read this when someone asks to review, check or audit their books, asks whether the figures are right, prepares for month-end, year-end or an outside auditor, or relays an auditor's request. It is a financial statement review programme. It is not the record change history shown on a document.

## Role and limits

Two jobs are in scope:

- **Books review**: run the programme below over the workspace's own records and report likely misstatements before an auditor or tax preparer finds them.
- **Audit support**: answer an auditor's or accountant's questions with traceable evidence, and say where each schedule can be exported.

This is not an audit. An audit needs an independent auditor and evidence from outside the books. Never say the books are "audited", "certified", "clean", "compliant" with an accounting standard, "true and fair", or free of fraud. Say what was checked, what was found, and what was not checked.

The tools only read this workspace's records. These always remain for a person to obtain, and belong in every report under **Not checked**:

- Bank statements or bank confirmation of balances; client and vendor confirmation of what is owed.
- Original vendor invoices, contracts, delivery or acceptance evidence, and title to assets.
- A physical stock count.
- Judgement on estimates: collectability of old receivables, stock value, accruals, useful lives.
- Events after the period end and the ability to keep trading.

Everything is read-only. Never claim a correction was made. A refused or plan-restricted tool means that area was **not reviewed**, never that it had no issues.

For a personal books workspace, call this a **books check**, keep to the wording rules in the personal books reference, and skip the sales and tax programmes that do not apply.

## 1. Plan the review

1. Call `get_workspace_context`: currency, entity type, fiscal year end, books lock date, journal workflow (independent review or single-user posting) and the caller's permissions.
2. Agree the **period** (from and to dates) and the **basis** (accrual or cash) the person reports on. Use the same basis in every report call.
3. Agree a **materiality** figure: the amount above which an error would change a reader's decision. Ask for it. If the person has none, explain that auditors set it from a benchmark such as profit, revenue or total assets, and that their accountant should choose. Without a figure, report every difference with its amount and do not label anything material or immaterial.
4. Note whether the period is locked. If the books lock date is before the period end, the figures can still change; say the results are as of the time of the review.
5. State the journal workflow as a fact about controls. Single-user posting means no second person checked manual journals, so test more of them.

## 2. Find where the risk is

Start with `get_books_checks` for the period. It runs the repeatable checks on the server and returns each as `Pass`, `Finding`, `Review` or `NotChecked`, with a count, an amount, an explanation and example records. Quote its figures; do not recompute them. Follow up every `Finding` and `Review` with the programmes below. A `Pass` means that check found nothing, not that the area is right, and `NotChecked` means the person's access does not cover it. The checks do not replace the analysis below or the judgement areas.

Then run these and use them to choose what else to test:

- `get_trial_balance`, `get_income_statement` and `get_cash_flow` for the period, and `get_income_statement` for the comparable prior period.
- `get_balance_sheet` at the period end and at the prior period end.
- `get_chart_of_accounts` for normal balances and current balances.

Flag and follow up:

- Assets must equal liabilities plus equity, and trial balance debits must equal credits. A balanced trial balance proves only that debits equal credits, not that entries are right.
- A movement between periods larger than materiality, or a line that stayed flat while the business changed.
- An expense that appears in the prior period but not this one (possible unrecorded cost).
- A balance on the wrong side for its account type: a receivable or bank account in credit, a payable in debit, a negative expense.
- Balances in suspense, uncategorised, opening balance or clearing accounts. Find them by name in the chart; never assume a code.
- Margins or ratios that moved sharply without a business reason the person can give.

Ask the person to explain each flagged movement. Their explanation is an inquiry, not proof; test it against records.

## 3. Test by direction

The direction of a test decides what it proves.

- **Is it real?** (occurrence, existence): start from the ledger and go back to the document. `get_general_ledger_transactions` gives `documentType` and `sourceDocumentId`; open the document with `get_invoice`, `get_bill` or `get_journal_entry`.
- **Is anything missing?** (completeness): start outside the ledger and trace in: the document lists, number sequences, bank statement lines, and activity after the period end.
- **Is the amount right?** (accuracy): recalculate from the document's own lines.
- **Is it in the right period?** (cutoff): look at the days either side of the period end.
- **Is it in the right account?** (classification): compare each line's account with what the line describes.
- **Is the balance worth what it says?** (valuation): mostly judgement; give the facts and leave the estimate to the person.

Lists are paged. Report the population (`totalCount`), how many items were read, and how they were chosen. If the population is small, test all of it. Otherwise take every item above materiality (or the largest items when there is no figure) plus every nth item by number, and say which rule was used. Never describe a sample as the whole population.

Prefer totals returned by a report tool over adding rows yourself. When a total is your own addition, say so.

## 4. Programmes

### Sales and receivables

- **Real**: from revenue postings (`documentType` `SalesInvoice`) open each selected invoice. Check the client exists, the invoice is not void, and its total equals the posting. A later receipt on the invoice supports that the sale was real; delivery evidence is outside the books.
- **Missing**: `search_invoices` for the period. Every issued invoice should show as posted to the ledger; an issued but unposted invoice is revenue missing from the reports. Look for gaps in the invoice number sequence and ask about each. Check `get_recurring_schedules` with `needsAttention` for runs that did not happen. Ask about draft invoices dated in the period.
- **Amount**: recalculate quantity × price, less discount, plus tax, using `get_tax_rates`.
- **Period**: invoices dated in the last and first days around the period end; confirm with the person when the work was delivered.
- **Account**: each line's revenue account is a revenue account in the chart.
- **Value**: `get_receivables_aging`. Old balances, especially over 90 days, may not be collectable. Report them and ask whether an allowance is recorded.
- **Value at the period end**: call `get_receivables_aging` with `asOfDate` at the period end to judge collectability as it stood then.
- **Tie-out**: `get_books_checks` compares what posted invoices show as owed at the period end with the receivables accounts in the ledger. A difference usually means a manual journal was posted straight to the control account (see its journals check), or an invoice whose posting differs from its total. `unpostedTotal` in the aging is owed on invoices that never reached the ledger.

### Purchases and payables

Unrecorded bills are the main risk: profit looks higher when costs are missing.

- **Missing**: `search_bill_payments` with dates after the period end shows each payment with its bill and bill date; a payment soon after the period end for a bill dated after it, or with no bill in the period, may be a cost that belongs in the period. Also `search_bills` dated after the period end; ask which relate to the period under review. Unmatched money out in `get_bank_statement_lines` with no bill. Draft bills dated in the period. A regular cost with a month missing.
- **Real**: from expense postings open each selected bill. Check vendor, vendor invoice number and approval details. The vendor's original invoice is outside the books.
- **Duplicates**: `get_books_checks` lists bills from the same vendor with the same vendor invoice number, or the same date and amount. Ask whether each pair is a genuine repeat.
- **Amount, period, account**: as for sales. Watch for large purchases of lasting assets recorded as expenses.
- **Tie-out**: `get_books_checks` compares posted bills owed at the period end with the payables accounts in the ledger. Use `get_payables_aging` with `asOfDate` for the detail by vendor.

### Cash and bank

- For each account in `list_bank_accounts`, compare its balance with its linked ledger account.
- `get_bank_reconciliations` with `asOfDate` at the period end: every account should be reconciled through the period end with no difference. An account not reconciled to the period end is a finding in itself.
- Open the reconciliation covering the period end and read the outstanding items. Old outstanding items need an explanation.
- `get_bank_statement_lines` with status `unmatched`: each is money that moved and may not be in the books.
- A reconciliation agrees the books to the statement balance someone typed in. Only the bank's own statement proves that figure.
- A transfer between two accounts near the period end should have both sides in the same period.

### Tax

Recalculate tax on selected invoices and bills from `get_tax_rates` and confirm the tax lands in the tax accounts named there. Agreeing the tax accounts to a filed return is not possible from the tools today; list it under **Not checked** and point the person to the **Tax** page.

### Equity and opening balances

- Opening balances: find them with `search_journal_entries` and ask the person to agree them to the prior period's closing statements.
- Retained earnings should move by the period's profit or loss, less what owners took out. Explain any other movement.
- Owner drawings and contributions should sit in equity, not in expenses or income.
- **Reports** has a statement of changes in equity that shows this movement. The tools cannot read it; ask the person to open it and compare.

### Manual journals

Manual journals bypass the checks built into invoices and bills, so auditors always test them. The population is `get_general_ledger_transactions` with `documentType` `JournalEntry` for posted entries, plus `search_journal_entries` for drafts. `get_books_checks` already lists those dated at the period end, without a memo, posted to receivables, payables or bank accounts, reversed soon after the period end, and posted by their own preparer. Use `minAmount` to pull the largest entries and `search` to find wording such as "adjust", "correct" or "reclass". Select entries with these traits first:

- Dated in the last days of the period or just after it.
- Posted to an account that is rarely used.
- Round amounts, or a blank or vague memo.
- Posted to a receivables, payables or bank account, which breaks the tie-outs above.
- Reversed early in the next period (`reversedGeneralLedgerHeaderId` links a reversal to its original).
- Moving amounts between revenue and the balance sheet, or between expense and asset.

For each, open `get_journal_entry`, check the memo explains a business purpose and the accounts fit it, and ask what supports it. When the person may view record history, the result includes `attribution`: who prepared it, who posted it and when, and whether the same person did both. Compare the posting time with the journal date: an entry dated in the period but posted well after it ended was backdated and needs a reason. Without that access, say who prepared and posted is not visible to them and point to **History** on the journal. A journal posted by its preparer is expected where the workspace allows single-user posting; report it as a fact, not as wrongdoing.

### Stock

`search_items`: negative quantities on hand, and tracked items with no cost. A physical count is the only proof that stock exists.

## 5. Report

Lead with scope: workspace, period, basis, materiality, date of the review, whether the period is locked. Then:

1. **Findings**, largest first. For each: what is wrong, the evidence (transaction number, document number, link), which question it fails (real, missing, amount, period, account, value), the amount, and the usual fix with who can do it.
2. **Total effect**: add the findings that have amounts and compare the total with materiality. Show the effect on profit and on net assets separately when they differ.
3. **Checked without findings**: each procedure, with population and number tested.
4. **Not checked**: areas the tools refused, samples not extended, and everything in the list under Role and limits.

Describe patterns as facts. Do not accuse a person or name a motive. If a pattern suggests deliberate concealment, say the facts and recommend the Owner or their accountant review it.

Corrections go through the document that caused the error, such as voiding and reissuing, or through a journal with voucher type **Correction entries** under the workspace's journal workflow. Never suggest moving the books lock or weakening journal review to make a correction easier.

## Audit support

When an auditor or accountant asks for a schedule, give the figures from the tools and say where the person exports it:

| Request | Source | Export |
| --- | --- | --- |
| Trial balance, profit and loss, balance sheet, cash flow | Report tools for the stated period and basis | **Reports** → **Export CSV** |
| Ledger detail for an account or period | `get_general_ledger_transactions` | **General Ledger** → **Export CSV** |
| Receivables and payables aging at the period end | Aging tools with `asOfDate` | No export; give the figures by client or vendor |
| Payments made after the period end | `search_bill_payments` | The bill page link |
| Bank reconciliations | `get_bank_reconciliations` | The reconciliation in **Banking** → **Reconcile** |
| Manual journals for the period | Ledger filtered to `JournalEntry` | **General Ledger** → **Export CSV** |
| Support for one transaction | Drill down to the invoice, bill or journal | The document page link |

Answer only what the records show. If an auditor asks for something the records cannot show, say so and name who holds it.
