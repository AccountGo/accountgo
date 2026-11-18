# G01 / G02: credits, returns, refunds, advances and write-offs

Status: proposed design and implementation contract; no production changes authorized by this document.  
Prepared: 2026-11-18. Evidence baseline: core checkout `538167346b0bd834ded5405ad26c691b3c7dcd65`.  
Parent: ticket #65, G01/P1 and G02/P1.  
Companions: [implementation plan](credits-returns-refunds-plan.md) and [engineer handoff](credits-returns-refunds-engineer-prompt.md).

## 1. Product decision

Deliver customer credits/returns/refunds/write-offs and supplier credits/returns/advances/refunds as one coordinated accounting capability. Share settlement, reversal, money, authorization and audit rules. Keep sales and purchasing documents distinct because their business evidence, tax direction and inventory effects differ.

An issued invoice or approved bill remains intact. A credit changes consideration, an application settles an open item without cash, a refund records cash movement, a return records physical goods, and a write-off removes an uncollectible receivable. Each action has a dated source record and drill-through to its ledger entries. A paid document can receive a credit without undoing its payment.

### Goals and acceptance measures

- Complete every mandatory scenario in section 12 with exact agreement, to the supported currency precision, between source documents, settlements, GL, stock and bank evidence.
- Preserve original documents and payments in every adjustment and reversal journey.
- Prevent over-application, over-refund and over-return under concurrent requests and retries.
- Let an operator complete credit-to-application, credit-to-refund and advance-to-bill journeys from the originating party/document without a manual journal.
- Produce reproducible historical aging and party statements before and after later settlements and reversals.

These are release targets, not measured production outcomes. After launch, measure successful/failed commands and abandoned draft-to-post journeys without retaining sensitive free text; establish a baseline before setting adoption targets.

### Supported scope

Initial profile: domestic, single base currency, accrual source ledger; both existing accrual and cash report projections must remain accurate. Include non-stock services, partial/full adjustments, multiple applications across a party's documents, standalone supplier credits, supplier advances before any bill, and stock returns in the final delivery. Existing ledger currencies with unsupported precision must be rejected explicitly; do not silently assume every currency has two decimals.

All listed capabilities are required to close G01/G02. Phases are delivery slices, not permission to mark the stock or write-off requirements complete early. Keep the ticket priority P1; “required” below means required within this P1 scope, not a new backlog P0 classification.

### Boundaries

Foreign-currency settlements/revaluation (G10), a new procurement/warehouse/RMA platform, automated payment-provider refunds, chargebacks and general customer overpayment repair (G03), full opening-item migration (G04), general attachment storage (G05), and automatic historical stock recosting (G12) remain separate work. This feature records a refund already made externally; it does not send money. Reuse existing document/export/evidence facilities where verified.

Expected-return estimates, automated ECL models, statutory tax filing and universal jurisdiction compliance are not delivered by this operational workflow. Existing estimates must be linked and released correctly, or that case must be blocked with a specific accountant action. Do not reverse revenue, allowance or cost twice.

## 2. Verified implementation baseline

Code observations apply to this checkout; they are not deployed-workspace assurance.

| Surface | Verified behavior | Design consequence |
|---|---|---|
| `src/Services/Sales/ISalesService.cs`, `SalesService.cs` | Invoice/receipt/advance-receipt/allocation primitives exist; no complete customer credit-note lifecycle in the inspected surface. | Preserve advance receipt semantics; add a separately typed credit source. |
| `src/Core/Domain/Sales/SalesInvoiceHeader.cs` | Balance is total less customer allocations, clamped at zero; paid or allocated invoices cannot be voided. | Add typed settlement totals, preserve cash paid separately, and reject over-settlement before clamping. |
| `src/Services/Purchasing/IPurchasingService.cs`, `PurchasingService.cs` | Approve/void bill and pay/void payment exist; `PayBill` is bounded to an approved bill's balance. Bill posting permits some negative lines/quantities. | A negative bill line is not an independently available credit or advance. Introduce dedicated documents. |
| `src/Core/Domain/Purchases/Vendor.cs`, `PurchaseInvoiceHeader.cs` | Vendor balance derives from bills; bills retain payable account at posting. | Snapshot control accounts; include advances/credits as distinct balances. |
| `src/Services/Inventory/IStockService.cs`, `StockService.cs` | Moving-average Receive/Issue and full original-value Reverse; backdating does not recost later movements. | Add bounded partial source-linked returns/value adjustments; never call full Reverse for a partial return. |
| `src/Services/Financial/FinancialService.cs` | Header/line creation, validation, dated reversal; books lock/year checks and reconciled-period guard; reversal clears statement matches and calls reconciliation protection. | Use this posting seam. There is no verified `LedgerPostingService` class to call; a feature facade must delegate here, not create a second ledger engine. |
| `src/Api/Data/Repositories/EfRepository.cs` | `Insert` calls `SaveChanges` immediately. | A coordinator must open its transaction before the first repository write; a final controller save alone cannot provide atomicity. |
| `src/Api/Service/HistoricalAgingService.cs` | Cutoff balances use dated allocations/payments and voids. AP path excludes currently voided payments, even for an earlier cutoff; control IDs derive from current party settings. | Fix affected historical semantics and use posted account snapshots, not current party mappings. |
| `src/Services/Financial/CashBasisRecognition.cs` | Projection models customer allocations and vendor payments. | Extend by economic settlement type; credit applications and write-offs are not cash. |
| `src/Services/TaxSystem/TaxPeriodReportService.cs` | Tax detail enumerates invoices/bills and voids. | Add credit/reversal tax sources; GL-only posting will leave tax detail incomplete. |
| `src/Api/Data/ApiDbContext.cs`, `ApiDbContext.CloseGate.cs` | Company filters, tenant write checks, accounting revision inventory and close evidence exist. | Every new entity and relationship must join tenant enforcement, revision invalidation and close readiness. |
| `src/Core/Authorization/AccessOperationCatalog.cs`, existing controller attributes | Functional permission/module/policy infrastructure exists. | Extend catalogs and guards, including direct API calls, rather than relying on hidden buttons. |

Review current branch and production deployment independently before implementation/release. Parent-ticket observations can become stale: current code already has tax-period and close-readiness functionality.

## 3. Accounting policy and standards boundary

Design reference framework: **Full IFRS**, ongoing separate-entity reporting for a domestic SME, monthly operations and annual close. This is a system design, not a conclusion that a particular tenant applies Full IFRS or that its financial statements comply. IFRS for SMEs and local GAAP require their own policy mapping; reporting jurisdiction/framework and effective standard version must be recorded before external sign-off.

Facts: the ticket requests operational credits, returns, refunds, supplier advances and bad-debt settlement. Assumptions: base-currency transactions; commercial credits are supported by an agreed reduction in consideration; supplier credits represent an enforceable supplier acknowledgement; refunds recorded here have occurred outside AccountGo. Open release decisions appear in section 13.

### Source-backed accounting conclusions

- IFRS 15 distinguishes a refund obligation and the right to recover products from revenue. Material expected returns can require recognition before actual goods arrive. This design handles actual events; an existing estimate must be consumed through a linked schedule rather than posting another expense/revenue reversal. A warranty replacement needs separate classification. [XRB published IFRS-equivalent text, IFRS 15 B20–B27](https://standards.xrb.govt.nz/standards-navigator/nz-ifrs-15/).
- IFRS 9 distinguishes credit-loss allowance from derecognition when recovery is no longer reasonably expected. A write-off must consume attributable allowance; any shortfall is recognized as impairment, with evidence. This feature does not replace periodic allowance assessment. [XRB published IFRS-equivalent text, IFRS 9 5.4.4 and 5.5](https://standards.xrb.govt.nz/standards-navigator/nz-ifrs-9/).
- IAS 2 measures inventories with cost and recoverability constraints; purchase rebates reduce cost. Consequently, stock credits need cost allocation to remaining inventory and sold goods, and damaged returns need a supported recoverable value. [XRB published IFRS-equivalent text, IAS 2 9–11 and 34](https://standards.xrb.govt.nz/standards-navigator/nz-ias-2/).

The account structure, states and journals below are AccountGo implementation proposals derived from these principles. They are not prescribed software architecture in those standards. Tax recognition dates, required credit-note fields, advance tax points, and bad-debt tax relief depend on local law. Default bad-debt write-off does not reverse tax. Never advertise a tax profile without verified jurisdiction rules and acceptance examples.

### Recommended control-account structure

Use separate mapped accounts for available customer credits (liability), supplier credits/refunds receivable (asset), supplier advances (asset), sales returns/allowances (contra revenue), allowance for receivable losses (contra AR), impairment expense, recovery of written-off debts, supplier return clearing and return valuation differences. Existing customer advances remain separate. Supplier advances for goods/services and refundable cash claims can need different reporting classifications; retain source type and refundability.

This structure keeps unused credits outside invoice/bill aging and avoids manufacturing negative invoices. Apply a credit through a balanced reclassification into the destination's original AR/AP account. Original-document-linked credits offer default application to that document, shown in preview and confirmed with posting; standalone credits remain unapplied. Issuance and selected applications commit together when that option is selected.

All account IDs resolve through tenant configuration/account selection rules, then are snapshotted at posting. Never discover a new account by name/code at command execution. Existing workspaces require a reviewed mapping preview; never create/remap accounts silently. Preserve original tax/account snapshots for source corrections even if the tax configuration has since changed. An archived posting account requires an explicit approved replacement mapping, not use of a current default.

Gross AR, allowance, customer credits, gross AP, supplier credits and advances reconcile separately. A party's net commercial position may be shown as a clearly labeled summary, with its components. Financial-statement offset/reclassification requires an approved presentation policy; a matching party name alone does not establish a right of offset. Do not automatically net customer and vendor identities.

## 4. Posting contract

Amounts: `B` = net commercial adjustment, `T` = eligible tax adjustment, `G = B + T`, `A` = amount applied/refunded/advanced, `C` = inventory carrying value moved. Positive document amounts only; journal direction derives from document type. Tax examples assume recoverable input tax and authorized output-tax adjustment. Nonrecoverable input tax belongs to the original cost/expense allocation. No journal line is zero or negative.

| Business event | Debit | Credit | Cash/stock effect |
|---|---|---|---|
| Issue customer commercial credit | Sales returns/allowances B; output tax control T | Customer credits liability G | None |
| Apply customer credit to invoice | Customer credits liability A | Invoice's snapshotted AR A | No cash; dated invoice settlement |
| Record customer refund | Customer credits liability A | Selected bank/cash A | Outflow; consumes available credit |
| Receive saleable customer return, with no prior return estimate | Inventory C | Original sale's COGS C | Receive quantity once, at supported original cost |
| Recognize supplier credit, non-stock | Supplier credits asset G | Original expense/asset B; recoverable input tax T | None |
| Apply supplier credit | Bill's snapshotted AP A | Supplier credits asset A | No cash; dated bill settlement |
| Record supplier refund of credit | Bank/cash A | Supplier credits asset A | Inflow |
| Pay supplier advance, no advance-tax claim | Supplier advances A | Bank/cash A | Outflow, no bill required |
| Apply supplier advance | Bill's snapshotted AP A | Supplier advances A | No new cash or purchase expense |
| Receive unused supplier advance back | Bank/cash A | Supplier advances A | Inflow, not income |
| Transfer control of stock returned to supplier, before credit | Supplier return clearing C | Inventory C | Issue quantity at current carrying cost; see control-transfer rule below |
| Recognize supplier credit for dispatched goods | Supplier credits asset G | Return clearing C; input tax T; valuation difference B−C | Difference is debit if C exceeds B |
| Write off uncollectible invoice balance A, fully provided | Attributable loss allowance A | Original AR A | No cash, revenue or stock movement |
| Establish missing allowance before write-off | Impairment expense shortfall | Loss allowance shortfall | Same approved transaction as write-off |
| Recover a validly written-off amount A | Bank/cash A | Bad-debt recovery A | Bounded linked recovery; never sales revenue |

Reversal: exact opposite account/amount snapshot at a valid current correction date, linked to the original. A recovery of a genuine write-off is distinct from reversal of an erroneous write-off. Material prior-period errors require accountant-directed treatment; never hide them as a current-period commercial credit.

### Worked examples (illustrative 10% tax, two-decimal base currency)

**Customer, unpaid invoice.** Original invoice 1,000 + 100 tax, AR 1,100. Issue 200 + 20 credit: Dr returns 200, Dr output tax 20, Cr available customer credits 220. Apply 220: Dr credits 220, Cr AR 220. Invoice due 880, cash paid zero, credit remaining zero. Original sale and tax remain visible with linked adjustment.

**Customer, paid invoice.** Original receipt 1,100 remains intact. The same credit leaves AR zero and customer credits 220. Refund 150 leaves bank reduced by 150 and available credit 70. Applying the remaining 70 to a later invoice reduces that invoice's due without another cash receipt. For a partly paid invoice, default application is at most its current balance; surplus stays available for refund/later use.

**Supplier advance.** Pay 300: Dr advances 300, Cr bank 300. Approve bill 500 + 50: Dr expense 500, Dr input tax 50, Cr AP 550. Apply 300: Dr AP 300, Cr advances 300. Pay remaining 250: Dr AP 250, Cr bank 250. Expense is 500 once. An unapplied advance refund of 100 instead reduces advances and increases bank by 100.

**Supplier stock return.** Return two units with carrying value 70 each; supplier agrees a net credit 160 + 16 tax. Dispatch: Dr return clearing 140, Cr inventory 140. Credit: Dr supplier credits 176, Cr clearing 140, Cr input tax 16, Cr valuation difference 20. Apply/refund 176 with the entries above. The controller must classify the 20 difference by its cause and materiality under the costing policy; it is not miscellaneous sales income.

**Bad debt.** Invoice balance 400; attributable allowance 250. Approval of evidence-backed write-off posts Dr impairment 150/Cr allowance 150, then Dr allowance 400/Cr AR 400. Invoice is settled by write-off, not marked “Paid.” A later recovery of 50 posts Dr bank 50/Cr recovery 50 and leaves historical AR unchanged. No automatic output-tax relief.

## 5. User stories and operator journeys

### Customer credits and refunds

As a sales operator, I need to correct all or part of an issued sale without rewriting the invoice or its payment history.

1. Invoice detail → **Create credit note**. Retain customer, source invoice, issued seller/buyer details, currency, original line prices/discounts/tax and remaining creditable amounts. Permit paid invoices; reject drafts/void sources.
2. Choose **Price/service adjustment** or **Goods return**. Select original lines, positive quantity/net reduction, reason, credit date, source reference and evidence. Preview net, tax, gross and creditable limits. Shipping is an explicit component with its own tax allocation, never inferred from an item line.
3. For goods, link or create a return receipt and record physical receipt date, quantity and condition. “Credit without goods received” is allowed for an agreed concession, but does not increase stock. No one-click restock based only on a credit checkbox.
4. Preview shows original amount, cash paid, existing credits/write-offs, invoice still due, amount to apply now, unused credit and financial/tax/stock dates. Default “Apply to this invoice” up to its current due; operator can retain an unapplied credit deliberately.
5. Save draft or submit/post according to policy. Issuance takes a unique number; PDF preserves original references, reason, line/tax totals and issuer snapshots. Financial posting cannot depend on PDF/email delivery.
6. Posted credit detail → **Apply credit**. Select one or more eligible invoices for the same party/currency/workspace, dates and amounts; see available balance after the action. Explicit confirmation; never auto-allocate across unrelated invoices in the background.
7. Posted credit detail → **Record refund**. Enter date, bank/cash account, amount, external reference, method and evidence. Label: “Record a refund already sent.” Show available credit and remaining balance. Keep unused credit after a partial refund.

As an accountant, I need a separately authorized **Write off balance** action on an issued invoice. Show due, credits available, disputed amount and attributable allowance; require reason, collection evidence, amount/date and policy-based review. Commercial disputes/concessions route to a credit, not a write-off. Display “Settled: cash X, credits Y, written off Z” instead of treating all settlement as paid.

### Supplier credits, advances and refunds

As a purchasing operator, I need to record a supplier's credit even when no matching bill exists, and apply it across eligible bills later.

1. Bill detail → **Record supplier credit**, or supplier detail/Purchases → **Supplier credits → New**.
2. Capture supplier's credit number, our unique internal number, supplier, credit/effective dates, reason and evidence. Match bill lines when available. A standalone credit requires explicit expense/asset/tax classification and supplier acknowledgement; no unexplained debit to AP.
3. Goods return → create/link a supplier return dispatch. Select source receipt lines, eligible quantity, dispatch date and current carrying-value preview. Pending supplier acknowledgement remains a return-clearing claim, not available credit.
4. Preview tax reduction, expense/stock allocation, optional applications and unused credit. Post through authorized policy; then **Apply to bills** or **Record supplier refund** with source-specific available balance.
5. Supplier detail → **Pay advance**. Capture date, amount, payment account/reference and purpose, optionally a purchase-order reference; no invented bill. Show as an advance asset. On bill approval or detail, offer **Apply advance**; never apply automatically at bill creation.
6. **Record advance refund** consumes the selected advance rather than a supplier credit or income. Where one bank receipt refunds several sources, record one bank movement with multiple typed source allocations atomically.

### Lists, statements and failure behavior

- Sales navigation: Credit notes; Purchasing navigation: Supplier credits and Advances. Detail pages link invoices/bills, returns, applications, refunds, write-offs/recoveries, evidence and GL according to permission.
- Party statements show dated debits/credits and explicit unapplied balances. Document lists show open, partly settled, settled; detail shows settlement composition. Credit lists derive unused/partly used/used from applications and refunds.
- Explain empty states and why an action is unavailable (draft source, locked date, exhausted source, wrong currency, missing mapping, insufficient permission). A stale preview refreshes amounts; it never silently lowers or changes the submitted action.
- Errors preserve draft input and contain no cross-tenant names/IDs. Confirmation uses plain business amounts; journal detail is an accountant drill-through.
- Existing archived-party history remains readable. Permit settlement/refund/correction of existing obligations with permission; do not require creating a new sale/purchase. New standalone documents for archived parties require deliberate restoration under existing policy.

## 6. Domain model and state transitions

Proposed names below are implementation contracts; map to repository naming conventions during phase 0.

| Record | Minimum data and responsibility |
|---|---|
| `CustomerCreditNote` / lines | Company/customer, number, dates, currency, source invoice/line, reason, immutable party/line/tax/account snapshots, totals, lifecycle, posting/reversal IDs and version. |
| `SupplierCreditNote` / lines | Company/vendor, internal and supplier references, optional bill/line, classification evidence, equivalent immutable snapshots and posting metadata. |
| `SupplierAdvance` | Company/vendor, amount/date, currency, bank GL link, advance-account snapshot, purpose/refundability, version and reversal metadata. |
| `SettlementApplication` | Exactly one typed credit/advance source and one eligible invoice/bill destination, positive amount, effective date, original source/destination account IDs, posting ID, version. No polymorphic free-text ID without enforced referential integrity. |
| `SettlementApplicationReversal` | Original application, reversal date/reason/actor, exact amount and reverse GL. Original remains immutable; one full reversal per application in this release, then reapply desired amount. |
| `PartyRefund` / allocations | Customer payout or supplier inflow, one bank account/date/amount, typed credit/advance source allocations summing to bank amount, references/evidence, posting/reversal IDs. |
| `CustomerReturnReceipt`, `SupplierReturnDispatch` / lines | Original inventory movement/line, quantity, condition, stock cost snapshot, date, movement IDs, related credit quantities; cumulative financial/physical match tracking. |
| `ReceivableWriteOff` / `WriteOffRecovery` | Invoice, amount/date, reason/evidence, allocated allowance and shortfall, requester/reviewer/policy snapshot, GL/reversal references; recoveries bounded to written-off amount less previous recoveries. |
| `SettlementCommandReceipt` | Tenant, operation, idempotency key, canonical payload hash, persisted result/document IDs; same transaction as the financial action. |

Every new record is company owned, with tenant-scoped relationship validation. Separate tables or typed nullable FKs with a database exactly-one-source constraint are acceptable; preserve real foreign keys. Money uses decimal and explicit supported currency precision; quantity/unit cost follow existing four-decimal conventions. Allocate rounding residual to the final bounded line/application so totals are exact. Snapshots include cumulative credited amounts and rounding provenance, not merely current tax IDs.

Lifecycle: `Draft → PendingReview (when required) → Posted → Reversed`; rejected submissions return to editable draft with review history. Available/partly used/used and partly/full refunded are derived settlement states, not competing document lifecycles. Returns use Draft → Posted → Reversed, with physical dates independent of credits. Refunds/advances/write-offs obey policy review before posting. Draft deletion is audited and cannot delete any issued financial evidence.

Posted monetary fields, tax snapshots, stock links and settlement records are immutable. Any later permitted annotation is separate audit evidence. Reversing a credit requires reversing applications and refunds first, with actual bank facts respected. Reversing a physical return requires sufficient stock and valid clearing/credit dependencies; block or orchestrate all dependent reversals atomically. Never remove an original application from a collection to “unapply.”

### Critical invariants

1. `invoice due = original gross − cash settlements − credit applications − write-offs`, adjusted by dated reversals; `bill due = original gross − cash payments − supplier-credit applications − advance applications`, likewise. All terms are distinct; due cannot go below zero.
2. `available source = posted amount − active applications − active refunds`, with dated reversal events restoring capacity. Cumulative customer credit basis/quantities cannot exceed the original eligible sale; mixed quantity/price credits share one remaining monetary cap. Written-off value must be resolved explicitly before crediting the same receivable; prohibit double relief.
3. Linked supplier credits cannot exceed supported source amounts/quantities without a separately classified, evidenced standalone adjustment. Physical returns cannot exceed original delivered/received quantity less prior active returns. Financial credit quantities and physical quantities are tracked independently and reconciled.
4. Each action balances, preserves original documents, and commits its journal, capacity consumption, stock, audit and command receipt together.
5. Application date is on/after both source and destination effective dates; refund date is on/after source availability; reversal date is on/after its event. A credit's financial date may differ from physical return dates. Books start/locks/year close and tax-period rules apply to each posting date. No automatic backdating into a closed tax period.
6. Only same tenant, same counterparty and same currency may be settled. No user-supplied account or party ID bypasses relationship validation.
7. A settled invoice/bill cannot accept another payment, application or write-off. Refund availability is a source balance, not the original invoice's payment total.
8. Partial or failed bank transfers do not count as a completed refund. Record only confirmed actual amounts; canceled transfers have no financial posting here.

## 7. Inventory and tax controls

Customer returns use the original sale issue cost proportionally, capped by the remaining issued value/quantity, with final-return rounding consuming the exact remaining cost. Inspect condition and recoverability: saleable returns restore supported cost; damaged goods restore only supported value (possibly zero), with a separate evidenced write-down/loss where appropriate. Track returned quantity even when recoverable value is zero. Never restore inventory at sales price or today's moving average. If an expected-return recovery asset exists, consume that asset instead of crediting COGS again.

Supplier returns issue at current carrying cost under the existing moving-average method, with a dedicated return-clearing asset until acknowledgement. The accounting event requires evidence of control transfer and a supportable return claim under agreed terms. Shipping goods alone does not prove either: goods still controlled by the workspace remain owned inventory in transit, with an operational location/availability change rather than a credit to inventory. Capture physical dispatch and control-transfer dates separately; claims lacking recoverability need controller review. On credit recognition, clear the returned carrying value and classify the difference from supplier net credit. Link dispatch and credit allocations; do not issue stock again at credit posting. Physical return without supplier acknowledgement is never available credit.

For an acknowledged supplier credit before control transfer, debit supplier credits G and credit return clearing B/input tax T. At later control transfer, debit clearing B, credit inventory C and credit valuation difference B−C (debit when negative). This clears the original B exactly even if carrying cost changes between events. Until transfer, show the clearing credit as an unmatched return obligation. A supplier acknowledgement with no obligation to return goods is a price/rebate adjustment instead. Reverse each event using its own stored amounts, not a later cost estimate.

Supplier price/rebate credits with no physical movement reduce inventory value for the supported portion remaining on hand and COGS for the portion sold, with explicit allocation and value-only stock movements. For untraceable pooled historical purchases, require a controller-approved allocation with reconciliation evidence; never guess a purchase lot in moving-average stock. Standalone stock credits need an evidenced cost allocation or are blocked until mapped. No negative stock, negative stock value, or backdated return/value adjustment before the item's latest posted movement in this feature. A current-period correction retains the historical physical reference. Broader recosting remains G12.

Tax snapshots retain original rate, taxable basis, discount, inclusive/exclusive mode, recoverability and accounts. A price-only credit does not return quantity. Multiple partial credits consume remaining original net/tax/gross; the final full credit consumes exact residual tax rather than recomputing at today's rate. A changed or inactive tax code cannot rewrite source history. Standalone credits require valid supported tax evidence; “no tax” needs explicit classification.

Advance payments do not automatically claim input tax. Where advance-tax documents are required, use an explicit supported jurisdiction adapter with duplicate-claim protection at bill application. Unsupported advance tax and bad-debt tax relief remain blocked/manual accountant handoffs with source references. Amend/correct a recorded tax filing through existing filing controls; preserve its snapshot and show the later adjustment separately.

## 8. Architecture, atomicity and API contract

Business services own documents; a focused settlement service coordinates capacity and applications; the existing financial service owns ledger construction/validation/reversal. Stock operations remain in the stock service. Do not route operational documents through unrelated manual-journal approvals or introduce a parallel GL store. Extend existing workspace operating-policy evaluation to these operations; add a documented policy hook if current document review does not exist.

Audit repository save behavior before coding: methods may persist tracked changes before the high-level command finishes. Start an explicit database transaction around every multi-record command and ensure every participating repository uses that DbContext/transaction. Validation and idempotency checks happen inside it. No external email/PDF/payment request inside the financial transaction; use existing after-commit/outbox conventions if delivery is later added.

Capacity enforcement uses database-backed locking/serializable coordination or conditional versioned updates covering both source and destination plus original credit/return caps. Lock resources in a deterministic order; re-read after locking. A row version on only a new application does not prevent two writers consuming one source. Refund/apply races, payment/apply races and return/return races must use the same capacity coordination. Retry whole commands only when idempotent; expose a conflict when preview/version changed.

Logical API resources (adapt route style to existing controllers):

| Resource | Commands/queries |
|---|---|
| Customer and supplier credit notes | Create/update draft, preview, submit/review where required, post, list/detail, reverse, printable issued document. |
| Applications | Preview batch, apply batch, reverse application; batches atomic. |
| Supplier advances | Draft/preview/post payment, list/detail, apply, reverse. |
| Party refunds | Preview, submit/post record, list/detail, reverse. |
| Returns | Draft, preview, post receive/dispatch, link credit quantities, reverse. |
| Invoice write-offs/recoveries | Preview, request/review/post, reverse erroneous write-off, record recovery. |
| Party balances/statements | As-of components and event detail with source/GL drill-through. |

Write contracts include idempotency key, expected version, dates and business fields; ignore/reject client CompanyId and posting-state claims. Preview returns resolved snapshot/version, due/available before/after, tax/stock effects, policy requirements and errors. Server recomputes on execution. Reusing the same key/payload returns the original result; same key/different payload returns conflict. Enforce uniqueness within company/operation/key and persist the result before commit.

Use existing problem-details conventions with stable codes for stale balance/version, source exhausted, invalid state/date/currency, missing mapping/evidence, period locked, reconciliation protection and permission denial. Forbidden/missing foreign-tenant resources must not disclose their existence.

## 9. Authorization and review

Extend functional catalogs for customer credits, supplier credits, returns, supplier advances, customer/supplier refunds, write-offs/recoveries and reversals. Distinguish view, draft/create, post/approve and reverse; require bank-account selection access for cash actions. Read-only users cannot mutate through direct API. Inventory permissions accompany physical receipt/dispatch; posting permission accompanies financial credit, even when a combined UI submits both.

Honor the current workspace's solo/team operating policy. Team review, when required, prohibits self-approval and records policy/version/requester/reviewer. Solo workspaces use the explicitly permitted self-post path and audit it. A governance role alone grants no new financial permissions. Refunds, advances and write-offs cannot be approved by merely possessing journal-create permission. Configurable approval thresholds must come from approved policy, not invented hardcoded values.

New capability permissions default-deny until role mappings/presets are deliberately migrated. Existing plan/module visibility remains authoritative; map core sales/purchasing/accounting capability to actual catalog definitions in both community and hosted editions. Do not invent a paid tier or make basic correction workflows premium by accident. Evidence/export access retains its existing audit-history entitlement rules.

## 10. Reports, close and audit evidence

- AR aging includes outstanding invoices after dated cash, credit and write-off events; AP aging includes bills after cash, credits and advances. Unapplied credits and advances appear in separate schedules with their own GL controls. “Net party position” is not the AR/AP control reconciliation.
- As-of logic replays original events and effective reversals. A payment reversed in December still reduces November due. Apply this rule to existing payments as well as new records. Source date, event date, posting date and audit timestamp remain distinct.
- Snapshot AR/AP account IDs on new sources; include historical posting accounts in reconciliation even after current party settings change. Identify legacy unmapped accounts via evidence-backed backfill, with exceptions reported explicitly.
- Extend tax-period detail/control tie-out for credit tax and reversals; applications/refunds/advances without a tax event do not reduce tax again.
- Extend cash recognition by source lineage. Unpaid credit/write-off causes no cash recognition. Paid-sale refunds reduce previously recognized amounts once; unpaid supplier credits do not create income; advance application must not recognize the original outflow twice. Define advance cash-report timing under the supported cash policy and prove it with expected examples. This cash projection is not Full IFRS reporting or an assertion of local cash-tax eligibility.
- Update accrual P&L/BS/cash-flow classification, party cards, dashboard outstanding totals, CSV/PDF exports, stock valuation, bank matching, year-end readiness and evidence hashes. Noncash credits/write-offs must not appear as bank flows.
- Add new accounting records to `TouchesCloseReadiness` and tenant/append-only enforcement. Open return-clearing items, missing mappings, unposted approved events, and reconciliation differences surface in close with drill-through; material unexplained differences block sign-off.
- Retain actor, policy approval, event IDs, source references, reason/evidence, before/after balances, linked GL/stock/bank events and command key. Require at least a support reference/description; do not promise a file-upload system until storage/permissions are verified. Record data corrections separately.

## 11. Rollout and production ownership

Use additive schema, explicit mappings, compatibility defaults and a per-workspace feature gate. Enable posting only after reporting and reversal consumers are ready; a UI-only gate is insufficient. Existing invoices/bills/payments retain their IDs and journal history. No automatic rewrite of negative bill lines into credits; inventory them in a dry run for accountant review.

Release evidence must name API/Web SHAs, schema version, plan/role/profile and executed database provider. PostgreSQL is required for production-like capacity/race proof; SQLite tests alone are insufficient. SQL Server support is claimed only with executed provider evidence. Run migration upgrade from an existing database, not only empty-schema creation.

Disable new commands to roll back exposure; keep history, reads, reporting and authorized corrections available. Do not drop populated tables or deploy old report code that ignores new events. Retain a compatible build and tested backup/restore procedure. Restore is disaster recovery and requires whole-database consistency; do not restore only one subledger after cash has moved.

Monitor duplicate-command conflicts, capacity violations, failed/partial transactions (target zero persisted partial events), aged return clearing and differences for all new control schedules. Emit operational signals without customer/evidence text. Any persisted partial event, cross-tenant access or unexplained financial difference pauses rollout.

## 12. Mandatory acceptance scenarios

The engineer records exact journals, source balances, reports and bank/stock evidence per scenario. These are proposed checks, not executed results.

| ID | Given / when | Required result |
|---|---|---|
| C01 | Unpaid 1,100 invoice; 220 credit applied | AR 880, unused credit zero, cash zero; tax reduced 20 once. |
| C02 | Paid 1,100 invoice; credit 220, refund 150, apply 70 to later invoice | Original payment intact, AR on original zero, one bank outflow 150; credit zero. |
| C03 | Partly paid invoice with due 100; credit 220 | Default application 100, available credit 120, no negative AR invoice. |
| C04 | Mixed price/quantity partial credits and full final credit, inclusive/exclusive tax | Cumulative net/tax/gross capped to source; exact final residual; changed tax rate does not alter source tax. |
| C05 | Original stock issue cost 60/unit, current cost 80; receive two returned units | Restore supported original 120, not 160; one receipt; damaged/zero-value variant supported. |
| C06 | Credit issued before goods received; goods received later | No early inventory; one physical movement at actual receipt; reconcile credit/return mismatch. |
| C07 | Due 400, allowance 250; write off 400, recover 50 | Shortfall 150 then allowance use 400; AR zero; cash recovery 50; no revenue/tax reversal. |
| C08 | Attempt credit on already written-off portion | Block double relief and show accountant resolution; reversal/reclassification path has evidence. |
| S01 | Standalone supplier credit 330; apply 110 and 165 to two bills; refund 55 | Credit asset zero, AP reduced 275, bank inflow 55; original bills intact. |
| S02 | Advance 300 before bill 550, apply 300, pay 250 | Advance zero, AP zero, bank outflow 550, expense/tax once. |
| S03 | Advance 300, refund 100, apply 200 | Advance zero, one inflow 100; no income or duplicate purchase. |
| S04 | Dispatch two units carrying 140, supplier credit 176 including tax 16 | Inventory down 140, clearing zero after matching, difference 20 explained, credit asset 176. |
| S05 | Goods returned without supplier acknowledgement; include goods still controlled in transit | Clearing only for evidenced control transfer/supportable claim; otherwise owned inventory remains; no available supplier credit. |
| S06 | Stock rebate with goods partly sold, no physical return | Value-only inventory/COGS allocation agrees to approved cost policy; quantity unchanged. |
| X01 | Concurrent apply/refund on same source; concurrent payment/credit/write-off on same invoice; concurrent returns | One bounded consumption; deterministic conflict/retry, no negative capacity. Execute on PostgreSQL. |
| X02 | Lost response and retry; same key with altered payload | Original result once; altered payload conflict; no duplicated GL/stock/bank event. |
| X03 | Inject failure after journal/stock insertion but before audit/application commit | Entire action rolls back, including command receipt, capacity and sequence behavior. |
| X04 | Reverse application/refund/credit/return/advance/write-off in allowed dependency order | Exact dated opposing events, restored capacity, preserved source history. Reconciled bank events protected. |
| X05 | November settlement reversed in December; query November and December | November remains settled; December reopens; dated GL and subledger agree. |
| X06 | Change party AR/AP mapping after posting | Old and new controls reconcile using historical account snapshots; no lost balances. |
| X07 | Closed/locked/reconciled periods, books-start boundary, filed tax period | Same guards as existing postings; correction requires valid dates/policy; no silent override. |
| X08 | Cross-tenant IDs, wrong party/currency, revoked permission, direct API, team self-approval | Denied with no mutation/disclosure; solo-authorized path separately proved. |
| X09 | Accrual/cash/tax/stock/bank/party/close report pack at multiple cutoffs | Every source and reversal included once; separate controls tie exactly; no cash from noncash settlement. |
| X10 | Existing database upgraded; old negative-line bills and old payments preserved | No duplicate opening/posting; mapping exceptions explicit; supported providers validated. |
| X11 | Combined multi-source supplier refund and stale batch application | One bank movement, exact typed allocations; stale batch rejected atomically. |
| X12 | Expected-return estimate exists; post actual return/credit | Consume linked estimate once, or reject with specific resolution; never double revenue/COGS reversal. |
| X13 | Archived party, missing/archived mapping, inactive original tax, empty/huge/invalid precision amounts | History accessible; valid obligation settlement allowed by policy; invalid posting blocked before any commit. |

## 13. Decisions and sign-off

Proceed with the architectural defaults above for design/isolated implementation. Before enabling a workspace, record:

| Decision | Proposed default / unresolved fact | Owner / gate |
|---|---|---|
| Framework and jurisdiction | Full IFRS design reference; actual tenant framework/local tax rules not established by ticket. | CFO/tax owner; supported-profile release gate. |
| Control-account mappings | Separate credit liability/asset, supplier advances, loss allowance and return clearing. | CFO; review mapping dry run before posting. |
| Cash projection policy | Extend existing policy with explicit credit/refund/advance lineage and timing examples. | CFO + reporting engineer; gate phase 2 release. |
| Review policy | Honor solo/team rules; no invented approval threshold. | Product/governance owner; before role migration. |
| Stock variance and rebate allocation | Current carrying cost dispatch; supported original-cost customer returns; explained differences and evidenced on-hand/sold allocation. | Controller; stock release gate. |
| Expected-return/ECL schedules | No estimator in scope; link existing balances or block duplicate recognition. | CFO; affected-profile gate. |
| Packaging | Reuse actual module/plan catalog and verify all supported editions. | Product owner; entitlement acceptance gate. |
| Production evidence | Deployment, bank execution and restored recovery are not verified in this design. | Production owner; pilot/GA gate. |

CFO sign-off requires the posting matrix, policy decisions and dated reconciliations. Product sign-off requires the operator journeys and correct settlement labels. Production sign-off requires races, upgrade/rollback compatibility, monitoring and recovery evidence. G01 and G02 close separately only after each has its complete journey and both share passing controls; neither closes on the existence of an enum label, manual journal workaround or successful unit tests alone.
