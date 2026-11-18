# Implementation engineer handoff: G01 / G02

Use this as one work assignment for the implementation engineer or repository coding agent. The linked design and plan form part of the assignment; keep them available with the repository checkout.

```text
Act as AccountGo's implementation engineer. Implement the coordinated G01/P1 and
G02/P1 scope from commercial parent issue #65:
https://github.com/AccountGo/accountgo-commercial/issues/65

Read and follow:
- AGENTS.md and any applicable local instructions.
- docs/accounting/credits-returns-refunds-design.md
- docs/accounting/credits-returns-refunds-plan.md

Treat the design's posting matrix, invariants and scenario table as the acceptance
contract. The plan defines delivery order. Record and explain repository drift from
the reviewed baseline 538167346b0bd834ded5405ad26c691b3c7dcd65 before changing code.
Keep ticket priority P1; shared financial integrity controls are mandatory.

Implement both sides with shared settlement controls and separate sales/purchasing
documents. Deliver customer credits/returns/refunds, authorized bad-debt write-offs
and recoveries; supplier credits/returns/advances/refunds; partial/full and multi-bill
applications; dated reversals; UI, reports, exports, permissions and audit evidence.

Begin by tracing current posting, repositories/SaveChanges, policies, account/tax
snapshots, report consumers and bank guards. Then implement phase 1 and continue in
reviewable vertical slices. Each slice needs UI/API/accounting/reporting/reversal
coverage before activation. Keep later slices gated until their consumers are ready.
Do not stop at an enum, API scaffold, generic journal or frontend-only implementation.

Non-negotiable accounting behavior:
1. Preserve issued invoices, approved bills, original payments and posted journals.
   Correct them with linked documents and dated reversing events.
2. Separate commercial credits, physical returns, noncash applications, cash refunds,
   supplier advances and uncollectible-receivable write-offs. Never label all settled
   amounts paid. Record refunds already executed outside AccountGo; do not send funds.
3. Use mapped customer-credit liability, supplier-credit asset and advance asset
   controls as designed. Applications reclassify to the destination's original AR/AP.
   Snapshot posting accounts and reconcile unapplied balances separately.
4. Consume attributable loss allowance for write-offs; recognize a documented
   shortfall as impairment in the same approved transaction. No automatic revenue,
   tax or stock reversal. Recoveries have bounded links and recovery classification.
5. Use original tax evidence for credits and exact final residuals. No tax change on
   application/refund. Local advance-tax and bad-debt-relief rules require a verified
   adapter; never invent jurisdiction rules or claim universal IFRS/tax compliance.
6. Customer stock returns use supported original issue cost and actual received
   condition/quantity. Supplier control transfer uses carrying cost with return
   clearing; physical dispatch alone can leave goods as owned inventory in transit.
   Never move stock twice, apply full Reverse to a partial return, guess stock rebate
   allocation, allow new negative stock/value, or silently backdate through later
   moving-average movements. Honor existing estimates or block duplicate recognition.

Non-negotiable engineering behavior:
- Use existing IFinancialService construction/validation/reversal and IStockService;
  a focused coordinator may delegate to them. Do not build a parallel ledger engine.
- Transactionally commit source, GL, stock, applications, audit, capacity updates and
  durable idempotency receipt. Inspect repository saves: an early SaveChanges must
  remain within the same explicit transaction.
- Protect both source and destination capacity against payment/apply/refund/write-off
  and return races, including real PostgreSQL proof. Idempotency is tenant/operation
  scoped with payload hashing; retry returns the original result, altered reuse conflicts.
- Enforce company-owned FKs, date/currency/counterparty rules, immutable posting
  snapshots, append-only event history, original-account reconciliation, books locks,
  year-end revision invalidation and bank reconciliation protection.
- Extend real permission/operation catalogs, solo/team policy, role migrations and
  module/plan guards. Do not infer financial permission from governance role, allow
  team self-approval, invent plan tiers, or rely on UI hiding.
- Update every due/status/aging/party/dashboard/export consumer, tax detail, cash
  recognition, inventory valuation, cash flow and close evidence. A later reversal
  must not rewrite earlier cutoff reports. Fix affected existing AP payment cutoff
  handling rather than copying its current-state IsVoid filter.
- Cash projection must distinguish cash-backed lineage from credits and write-offs,
  and must not recognize an advance again on application. Establish exact reviewed
  policy examples before enabling the slice.
- Add compatible SQLite/PostgreSQL migrations and test an existing database upgrade.
  Report any provider without executed evidence; do not claim support from compilation.
- Use existing UI patterns. Confirmations show business amounts and remaining credit;
  refunds say they record an external payment/receipt. Preserve form input on errors.

Execute all mandatory scenarios C01-C08, S01-S06 and X01-X13, with meaningful tests
and a synthetic-data browser acceptance pack. Expected examples include:
- Invoice 1,100, credit 220: unpaid due 880, or paid invoice unchanged with credit 220.
- Refund 150 of that credit, then apply 70: credit zero; one bank outflow 150.
- Advance 300, bill 550, apply 300, pay 250: AP/advance zero; expense counted once.
- Supplier return carrying 140, credit 160 plus tax 16: inventory down 140,
  clearing zero after matching and explained valuation difference 20.
- Write off 400 against allowance 250: shortfall impairment 150 then allowance use
  400; later recovery 50 is cash/recovery, not sales revenue.

Run focused tests during each slice, then the relevant backend, frontend and hosted
checks in the plan. Prove retries, rollback, tenant/permission denials, PostgreSQL
races, historical cutoffs, changed mappings and all control-account reconciliations.
Record commands, SHAs, providers, results and skipped/missing evidence honestly.

Use additive gated rollout. Prepare mapping dry-run, operational signals, compatible
rollback/read/report behavior and isolated restore evidence. Do not deploy, mutate
production, send communications or execute actual bank/payment-provider transfers
without separate authorization. Framework/jurisdiction/mapping/review-policy facts
that are missing gate affected activation; continue independent implementation work.

Do not implement G03/G04/G05/G10/G12 as unrelated projects. Reuse existing primitives
and isolate only prerequisites necessary for the promised invariants. Do not rewrite
historical negative bill lines or generate unapproved account mappings.

For user-visible code commits, add a public-safe Unreleased changelog entry in the
same commit. Keep Author/Committer as the human's configured identity; no agent/tool
branding or attribution trailers. After any commit, inspect git log -1 --format=%B.

Deliver: reviewable code/PR slices; schema and mapping notes; updated account/posting
rules and user documentation; executed accounting/UI/provider evidence; rollout and
rollback runbook; remaining decisions with named owner roles. State exactly which
G01/G02 acceptance gates pass and which remain. Do not declare either gap closed
before its full promised scope and shared financial controls pass.
```
