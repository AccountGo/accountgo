# G01 / G02 implementation plan

Status: proposed engineering work breakdown, 2026-11-18.  
Authority: [joint design and acceptance contract](credits-returns-refunds-design.md).  
Parent: [commercial #65](https://github.com/AccountGo/accountgo-commercial/issues/65).  
Handoff: [implementation engineer prompt](credits-returns-refunds-engineer-prompt.md).

## Delivery strategy

Implement a shared foundation followed by end-to-end vertical slices for both sales and purchases. Each slice includes UI, API, accounting, historical reporting, authorization and reversals. Do not expose a new financial command until all its consumers understand it. Service-only work and hidden navigation are insufficient acceptance.

No calendar estimate is asserted before phase 0. This touches financially material state, report projections and production race conditions. The implementation lead estimates each work package after repository reconciliation and records dependencies; split PRs by the work below rather than one large release.

## Phase 0 — confirm contracts and lock the baseline

Owner: implementation lead with CFO/product review.

1. Re-read repository `AGENTS.md`, linked design and current sources. Record branch/SHA, current core/commercial ownership, routes, all balance consumers and existing policy hooks. The design baseline is `538167346b0bd834ded5405ad26c691b3c7dcd65`; resolve drift rather than blindly following old line numbers.
2. Trace an invoice issue/payment, bill approval/payment, stock issue/receipt, GL reversal, bank match, tax-period report, cash recognition and year-end close. Inspect repository `SaveChanges` behavior and transaction boundaries. Record which shared DbContext coordinates the new command.
3. Propose exact entities/FKs, account-role configuration, permission keys and role defaults. Inventory original source tax/account snapshots; specify what can be evidenced/backfilled for legacy documents and which cases must be blocked.
4. Confirm supported base-currency precision and database providers. Produce mapping/legacy-data dry-run output without changing ledger data. Include negative bill lines, existing advances, unattributed allowances and historical mapping changes.
5. Freeze the supplied posting examples as expected-result fixtures; define additional exact cash-projection examples for advance timing, refund, credit rollover and mixed partial settlement.

Exit: reviewable contract delta, account mapping preview, report-consumer inventory and test/PR sequence. Framework/tax decisions unresolved by the ticket block activation of the affected profile, not read-only analysis or isolated implementation.

## Phase 1 — shared financial controls and schema

Owner: backend/domain lead. Dependency: phase 0 contracts.

- Add company-owned customer/supplier credit documents, advances, typed application/reversal events, refund headers/allocations, returns, write-offs/recoveries and command receipts. Enforce referential integrity, positive values, unique per-tenant document numbers and exactly one eligible source/destination.
- Add tenant-configured account roles and posting snapshots; use explicit mapping UI/preview for existing workspaces. Keep account classification/normal balance consistent with existing COA conventions and custom/imported charts.
- Introduce a focused financial command coordinator around existing `IFinancialService` and `IStockService`. Ensure all repository saves join one transaction, capacity locks have a stable order, and the command receipt commits with the financial action.
- Add durable idempotency, version/preview conflict behavior, audit events and immutable posted-source enforcement. Register all new entity relationships with tenant checks and accounting revision/close readiness.
- Add permissions and policy hooks for draft, approve/post and reverse, with explicit solo/team behavior, server-side module/plan checks and migration of role presets. Keep new operations gated off.
- Produce matching SQLite and PostgreSQL migrations/snapshots; validate any additional provider claimed by the project. Use additive migrations and tested defaults; no ledger rewrite.

Exit: migration upgrade passes, transaction failure tests leave no partial financial fact, cross-tenant relationships are rejected, and real PostgreSQL races cannot overspend source/destination capacities. Do not publish financial UI yet.

## Phase 2 — non-stock credits, applications and reports for both sides

Owner: sales/purchasing implementation lead with reporting owner. Dependency: phase 1.

- Deliver draft/preview/review/post/read/reverse customer and supplier credits, including standalone supplier credits, independent numbering, source references, snapshots and printable customer credit note.
- Deliver partial/full and batch applications, application reversal and optional atomic issue-and-apply. Extend current invoice/bill payment validation so payments compete with credits/write-offs/advances for the same remaining due.
- Add invoice/bill detail actions and separate credit lists/detail pages; party views show gross due, available credits and net commercial position with clear labels. Preserve original payment history and expose settlement composition.
- Update current/as-of due calculations, existing AP payment reversal cutoff behavior, dashboard/party totals, historical aging/control account discovery, tax details, cash projections and close evidence. All new sources must be represented once; credits alone are not cash.
- Use original tax rates/bases for linked credits, exact final residuals and explicit classification of standalone credits. No tax relief inferred from a future application/refund.

Exit: C01/C03/C04/S01 application portion plus X05–X10/X13 pass, with journal and report assertions. Pilot non-stock credit issuance only after reports understand every enabled command. Cash-projection policy examples are reviewed before activation.

## Phase 3 — cash refunds and supplier advances

Owner: cash/banking implementation lead. Dependency: phase 2 reporting foundation.

- Deliver supplier advance creation/payment, later application and unused-advance refund without requiring a bill.
- Deliver customer credit refunds and supplier credit refunds, including one bank movement allocated across multiple typed supplier sources. Treat records as externally executed payments/receipts; do not call payment providers.
- Add refund/advance review, list/detail, bank reference/evidence, complete reversal path and reconciliation guards. Match existing bank statement lines to the new actual cash movement; never record another cash journal when matching.
- Extend cash report lineage for advance timing, applications, credit rollover and refunds; exclude noncash applications and avoid recognizing one advance twice. Extend accrual cash-flow classification by underlying source purpose.
- Ensure refund/apply races share capacity coordination, and bank account changes/currency cannot evade source validation.

Exit: C02/S01–S03/X01–X04/X09/X11 pass, including locked/reconciled bank reversal and mixed-source bank receipt. Control schedules for credit liabilities/assets and advances tie to GL.

## Phase 4 — write-offs and recoveries

Owner: AR/accounting implementation lead. Dependency: shared capacities, policy and report infrastructure.

- Deliver invoice write-off request, evidence and allowance attribution, reviewer decision, posting, erroneous-write-off reversal and bounded recovery.
- Consume attributable allowance; post and document any shortfall as impairment in the same approved command. Do not consume a global allowance balance without attribution or controller evidence.
- Keep commercial concessions separate. Reject overlap with credits/payments and require an explicit accountant resolution for credits against already written-off amounts. Preserve collection/recovery history.
- Make status/read models distinguish cash paid, credited and written off. Tax remains unchanged unless a supported local relief workflow separately records it.
- Integrate cash projections, recovery classification, statements, close schedules and as-of reversals.

Exit: C07/C08 plus relevant X01–X09 pass. CFO accepts allowance and recovery treatment; neither “Paid” nor reduced sales revenue is used as a write-off shortcut.

## Phase 5 — physical returns and inventory value adjustments

Owner: inventory lead with controller review. Dependency: phases 1/2; refunds reuse phase 3.

- Add source-linked customer return receipts and supplier return dispatches, separate physical dates and quantity caps. Posted stock changes use stock service extensions for partial original-cost returns, current-cost supplier dispatch and value-only rebates.
- Track condition, cost recovery, cumulative original cost and exact residual rounding. Block new negative stock/value and backdating before the item's latest movement. Handle zero-value damaged returns as physical evidence without zero-value GL lines.
- Integrate supplier return clearing, acknowledged credit allocations, unmatched return/credit schedule and difference classification, including credit before control transfer. Distinguish physical dispatch from control transfer: goods still controlled remain owned inventory in transit. Credit posting never repeats physical stock movement.
- Implement price/rebate credits for on-hand and sold inventory with evidenced allocation. Surface cases lacking reliable attribution for controller review instead of guessing.
- Detect existing expected-return assets/refund liabilities. Consume linked balances through an explicit adapter, or reject the affected action with an accountant resolution path; never silently duplicate earlier recognition.

Exit: C05/C06/S04–S06/X12 plus all capacity/date/reversal/report controls pass. Inventory valuation and GL reconcile after every step. G01/G02 remain open for stock support until this gate passes.

## Phase 6 — production acceptance and controlled activation

Owner: production owner, CFO and product owner. Dependency: required slices complete.

1. Run all mandatory design scenarios on a production-like PostgreSQL environment, retaining expected/actual journals, source events, control schedules and UI evidence. Use synthetic data and real authorization paths.
2. Run migrations from an existing representative database. Review mapping exceptions and compatibility with older sources; verify no duplicate posting or unsupported conversion of historical negative lines.
3. Execute browser journeys for sales and purchasing, both solo and team review, desktop and a narrow mobile layout. Validate permission-denied and stale-preview behavior; retain actual API/Web SHAs.
4. Reconcile AR, AP, customer credits, supplier credits, advances, allowances, return clearing, tax, stock and bank at multiple cutoffs. Produce accrual/cash statements and close preview; prove a later mutation invalidates prior close evidence.
5. Test disabling new commands while preserving history/reporting/corrections, and restore a consistent backup in an isolated environment. Record the compatible rollback build and operating procedure.
6. Enable a small set of approved workspaces with explicit profile, mappings and policy. Watch financial differences, duplicate/idempotency conflicts, aged clearing and failed commands; expand only with reviewed evidence.

Exit: CFO accounting acceptance, product journey acceptance and production evidence acceptance recorded. Mark G01/G02 complete only for the profiles actually proven; keep parent #65's broader readiness work open.

## Proposed reviewable PR sequence

These are suggested work packages, not created issues or assigned people. One implementation engineer can own the sequence; “owner” above describes review responsibility, not a request to spawn agents.

| PR | Deliverable | Depends on |
|---|---|---|
| 1 | Contract delta, schema/account mappings and tenant-safe idempotent capacity foundation | Phase 0 |
| 2 | Customer credit/apply slice plus due/aging/tax/cash/close consumers | 1 |
| 3 | Supplier credit/apply slice plus standalone classification and reports | 1, shared report changes in 2 |
| 4 | Refunds, supplier advances, bank/reversal and cash projection integration | 2–3 |
| 5 | Write-offs/recoveries and allowance attribution | 2, shared cash path in 4 |
| 6 | Physical returns, inventory valuation and matching/clearing | 2–3, refund integration in 4 |
| 7 | Upgrade, full-cycle acceptance pack, runbook and gated activation readiness | 1–6 |

Each feature PR carries user-facing documentation and an Unreleased changelog line when applicable. Internal-only test/design work does not need a public changelog entry. Do not add agent attribution to commits or PR bodies.

## Engineering touchpoints

| Layer | Existing entry points to inspect/extend |
|---|---|
| Domain | `src/Core/Domain/Sales`, `src/Core/Domain/Purchases`, `src/Core/Domain/Items`, document/numbering types, GL settings. |
| Services | `src/Services/Sales/SalesService.cs`, `InvoicePaymentRecorder.cs`, `Purchasing/PurchasingService.cs`, `Inventory/StockService.cs`, `Financial/FinancialService.cs`. |
| Report/close | `src/Api/Service/HistoricalAgingService.cs`, `src/Services/Banking/BankAccountService.Aging.cs`, `Financial/CashBasisRecognition.cs`, `Financial/YearEndCloseService.Readiness.cs`, `TaxSystem/TaxPeriodReportService.cs`. |
| Persistence | `src/Api/Data/ApiDbContext.cs`, `ApiDbContext.CloseGate.cs`, `Repositories/EfRepository.cs`, both migration trees and model snapshots. |
| Authorization | `src/Core/Authorization/AccessOperationCatalog.cs`, permission catalog/seeds, workspace operating policy, API permissions/module guards; hosted/community catalog consumers. |
| API/DTO | Existing Sales/Purchasing controller conventions, financial/bank controllers for drill-through/matching, `src/Dto/Sales`, `src/Dto/Purchasing`. |
| Web | `src/Web/src/pages/InvoiceDetail.tsx`, `BillDetail.tsx`, customer/vendor details, list pages, `layouts/OrgLayout.tsx`, existing payment dialogs and report components. |
| Tests | Existing sales/integrity/stock, banking aging, cash recognition, tax-period HTTP, close, access and PostgreSQL race fixtures in both test projects; `src/Web/test` and `test-ui`. |

Re-search consumers rather than treating this table as exhaustive. Avoid a broad ledger-engine rewrite; if foundational defects block an invariant, isolate the smallest prerequisite and record it in the handoff.

## Verification contract

Use focused expected-journal domain/service tests, API integration tests for policy/tenant behavior, PostgreSQL integration tests for isolation and races, and UI interaction tests for money-moving confirmation flows. Tests must prove financial outcomes and failure rollback, not just that a method was called. Reuse existing fixtures; do not substitute an in-memory EF provider for relational constraints/concurrency.

Run the repository's current checks, adapting only if scripts have changed:

```sh
dotnet test test/AccountGo.ServicesTests
dotnet test test/AccountGo.BackendTests
```

From `src/Web`:

```sh
npm test
npm run build
```

Use the existing `ACCOUNTGO_TEST_POSTGRES` fixture mechanism for backend integration/race suites; provide credentials through the approved local/test environment and never print them. Run hosted module tests when permissions/entitlements/plan catalog are touched. Record exact commands, provider, skipped tests and results. Missing PostgreSQL evidence is a release gap, not a passed check.

The design-only preparation has not run these implementation tests. Final engineering delivery must link executed evidence and residual limitations; production deployment and real fund transfers need the separately authorized release/operations workflow.
