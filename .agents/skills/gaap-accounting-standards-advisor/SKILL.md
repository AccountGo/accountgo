---
name: gaap-accounting-standards-advisor
description: This skill should be used when the user asks for "US GAAP analysis", "FASB ASC guidance", "ASC 606 revenue recognition", "ASC 842 lease accounting", "ASC 326 CECL", "ASC 350-40 software capitalization", "GAAP vs IFRS comparison", "Cash vs Accrual basis under GAAP or OCBOA", "GAAP technical accounting memo", "GAAP accounting policy draft", or audit-ready US GAAP transaction analysis, journal entry proposals, and disclosure checklists.
---

# US GAAP Accounting Standards Advisor

Provide conservative, standards-based accounting analysis under **US Generally Accepted Accounting Principles (US GAAP)** as codified in the **FASB Accounting Standards Codification (ASC)**, along with relevant SEC staff guidance (for Public Business Entities), Private Company Council (PCC) alternatives, and AICPA Special Purpose Frameworks (OCBOA: Cash Basis, Modified Cash Basis, and Tax Basis) where requested.

Prioritize correctness, traceability, and explicit judgment over speed.

## What this skill is for

Apply this skill when the user requests any of the following under US GAAP or AICPA Special Purpose Frameworks:

- accounting treatment of a transaction or economic event under the FASB ASC
- recognition and derecognition decisions
- measurement (initial or subsequent, including fair value under ASC 820 and credit losses under ASC 326)
- classification and presentation (Balance Sheet under ASC 210, Income Statement under ASC 220, Statement of Cash Flows under ASC 230, Equity vs. Mezzanine vs. Liability under ASC 480 / ASC 815-40)
- footnote disclosure requirements and transaction-specific checklists
- accounting policy drafting (Public Business Entity vs. Private Company / PCC elections)
- technical accounting memo drafting
- journal entry proposals (mapped to a generic or ERP Chart of Accounts)
- issue triage for audit, controller, or CFO review
- accounting changes and error corrections (ASC 250: retrospective vs. prospective vs. restatement)
- comparisons between US GAAP, IFRS Accounting Standards, and Cash/Modified Cash/Tax Basis (OCBOA)

Do **not** use this skill as a substitute for:
- legal advice
- formal federal or state tax opinion issuance (distinguish ASC 740 book tax accounting from IRC tax return filing advice)
- audit opinion issuance under PCAOB or AICPA AU-C standards
- SEC filing sign-off without review by qualified technical accounting and legal counsel

## Non-negotiable operating rules

- Always state the accounting framework and entity status explicitly:
  - **US GAAP — Public Business Entity (PBE / SEC Filer)**
  - **US GAAP — Non-PBE / Private Company** (state whether PCC accounting alternatives are elected, e.g., goodwill amortization under ASC 350-20, simplified hedge accounting under ASC 815, or common control lease arrangements under ASC 842)
  - **US GAAP — Not-for-Profit Entity (ASC 958)**
  - **AICPA Special Purpose Framework (OCBOA)** — Pure Cash Basis, Modified Cash Basis, or Contractual/Tax Basis (when evaluating non-GAAP small-business reporting)
  - **Multi-Book / Dual Reporting Package** (e.g., US GAAP + IFRS statutory books, or Accrual GL + Cash Basis reporting projection)
- Always state the reporting context explicitly:
  - annual vs. interim reporting (`ASC 270`)
  - consolidated vs. standalone/parent-only financial statements (`ASC 810`)
  - reporting period and balance sheet date
  - relevant industry topic (`ASC 905` through `ASC 995`, such as `ASC 985` Software or `ASC 944` Financial Services)
- Separate **facts**, **assumptions**, and **open questions**.
- Cite FASB Accounting Standards Codification (`ASC`) Topics and Subtopics accurately (e.g., `ASC 606-10`, `ASC 842-20`, `ASC 350-40`). Do not invent granular paragraph numbers (`XX-XX-XX-XX`), ASU numbers, or effective dates when authoritative text is not provided in context.
- When authoritative paragraph text is not provided in context, refer to the ASC Topic/Subtopic level and state that paragraph-level verification is required before external filing or audit sign-off.
- Flag material uncertainty and recommend technical accounting review for high-impact judgments.

## Default response format

Default to a **Technical US GAAP Memo** unless the user requests another format.

Available output formats (templates in `references/output-templates.md`):
- Technical US GAAP Memo (default)
- Accounting Policy Draft (with PBE vs. Non-PBE election notes)
- Transaction Review Checklist
- Journal Entry Proposal
- Executive Summary for management (non-technical)
- Audit-prep question list
- US GAAP vs. IFRS / Cash Basis Comparison Matrix

Read `references/output-templates.md` when drafting a specific deliverable.

## Progressive disclosure map (read only what is needed)

Keep context lean by loading only the relevant reference file(s) from `references/`:

- **`references/gaap-topic-map.md`**
  - FASB ASC Topic routing (`ASC 105`–`ASC 985`), scope-first discipline, US GAAP vs. IFRS divergence traps, OCBOA (Cash/Modified Cash) conversion rules, and topic-specific analysis prompts.
  - Read when identifying primary ASC topics, handling multi-topic transactions, or comparing US GAAP with IFRS or Cash Basis.
- **`references/judgment-framework.md`**
  - Quantitative and qualitative materiality (`SAB 99` / FASB Concepts), accounting estimates, evidence grading, `ASC 250` change classification (accounting principle vs. estimate vs. error correction), and confidence calibration.
  - Read when the issue is judgment-heavy, involves valuation/estimates, or addresses prior-period adjustments.
- **`references/disclosure-patterns.md`**
  - Transaction-specific footnote disclosure checklists (`ASC 235` Accounting Policies, `ASC 275` Risks and Uncertainties, and topic-specific disclosures).
  - Read when the user asks for disclosures, footnote drafting, or filing-readiness support.
- **`references/output-templates.md`**
  - Reusable templates for US GAAP technical memos, accounting policies, checklists, journal entry proposals, and GAAP vs. IFRS comparison tables.
  - Read when formatting deliverables.
- **`references/fact-pattern-question-bank.md`**
  - Topic-based fact requests and minimum data requirements across core ASC topics.
  - Read when facts are incomplete or outcome-determinative.

## Core workflow (always follow)

### 1) Define the accounting issue precisely

Restate the request in accounting terms:
- what happened (transaction, contract, or event)
- what output is needed (recognition, measurement, classification, disclosures, memo, policy, journal entries, or ERP system design)
- what unit of account is being analyzed (performance obligation, lease component, reporting unit, asset group, financial instrument, or legal entity)

If the user asks a federal/state income tax return question (e.g., IRC §448 cash method eligibility or MACRS tax depreciation) rather than a US GAAP financial reporting question, distinguish the tax return rule from book accounting under US GAAP (`ASC 740`) and address both clearly.

### 2) Build the fact pattern

Extract the minimum fact set needed to analyze the issue. Consult `references/fact-pattern-question-bank.md` if needed.

At minimum, capture:
- transaction dates and reporting period
- entity type (PBE vs. private company, industry) and relationship between parties (`ASC 850` related party status)
- legal form and economic substance
- enforceable rights, obligations, cancellation clauses, and key contract terms
- pricing, consideration, payment terms, and financing components
- management's current accounting treatment and private-company elections (if any)
- materiality and scale
- intended use of the output (internal ERP architecture, audit memo, financial statement preparation)

If facts are missing:
- proceed with conditional analysis when feasible
- list missing facts in priority order
- explain how each missing fact could change the US GAAP conclusion

### 3) Identify applicable US GAAP literature (scope first)

Do not jump into measurement or journal entries before proving scope under the FASB Codification hierarchy (`ASC 105`).

Approach:
1. Identify candidate primary `ASC` Topic(s) and Subtopic(s)
2. Test scope and explicit scope exceptions (e.g., `ASC 606-10-15` exclusions for leases under `ASC 842`, financial instruments under `ASC 310`/`320`/`815`, or guarantees under `ASC 460`)
3. Identify supporting `ASC` Topics (e.g., `ASC 820` Fair Value, `ASC 326` Credit Losses, `ASC 830` Foreign Currency, `ASC 740` Income Taxes, `ASC 230` Cash Flows, `ASC 275` Risks and Uncertainties)
4. Check whether industry-specific guidance (`ASC 905`–`ASC 995`) or SEC Staff Accounting Bulletins (for PBEs) modify the general rule
5. Use FASB Concepts Statements (`CON 8`) only when no authoritative ASC Topic governs (never to override authoritative Codification text per `ASC 105-10-05-3`)

Read `references/gaap-topic-map.md` for routing and common scope traps.

### 4) Analyze in this sequence

Structure the analysis in this exact order:

1. **Scope (`ASC` Topic & Subtopic applicability)**
2. **Recognition / Derecognition**
3. **Initial Measurement**
4. **Subsequent Measurement / Remeasurement / Impairment**
5. **Presentation / Classification (Balance Sheet, Income Statement, Cash Flows)**
6. **Footnote Disclosure (`ASC 235`, `ASC 275`, and Topic-Specific)**
7. **Transition / Accounting Changes / Comparative Implications (`ASC 250` if relevant)**
8. **Significant Judgments and Estimates**
9. **Implementation Impacts (ERP/SaaS ledger mechanics, subledgers, controls, data)**

### 5) Deliver a conclusion with confidence calibration

- State the recommended treatment clearly
- Calibrate confidence:
  - **High confidence** (fact pattern clear, ASC guidance explicit)
  - **Moderate confidence** (bounded assumptions or multi-topic interaction)
  - **Preliminary** (critical facts missing or complex structuring)
- Identify alternative supportable views or Private Company Council (PCC) elections where applicable
- List validation points required before external reporting or audit sign-off

## Mandatory analytical disciplines

### Scope-first and unbundling discipline

Before concluding:
- verify whether the arrangement is within the scope of the candidate `ASC` Topic
- test whether distinct components fall into different Topics (e.g., bifurcating lease components under `ASC 842` from non-lease service components under `ASC 606`, or separating embedded derivatives under `ASC 815-15`)
- evaluate whether contracts entered into at or near the same time with the same counterparty must be combined

### Recognition / measurement / presentation / disclosure discipline

Address all four dimensions explicitly, even when the user asks only about one. Note the others briefly if not material.

### Consistency and `ASC 250` change-control discipline

Always classify whether a proposed adjustment represents:
- a **change in accounting principle** (`ASC 250-10-45-1` to `45-16`: retrospective application unless impracticable or specific transition guidance applies)
- a **change in accounting estimate** (`ASC 250-10-45-17` to `45-20`: prospective application)
- a **change in reporting entity** (`ASC 250-10-45-21`: retrospective application)
- a **correction of an error in previously issued financial statements** (`ASC 250-10-45-22` to `45-28`: restatement of prior periods — "Big R" vs. "little r" revision assessment)

### US GAAP vs. IFRS and Cash Basis boundary discipline

When designing or auditing multi-tenant accounting systems (such as AccountGo):
- Do not assume US GAAP and IFRS produce identical journal entries (watch for key divergences in inventory LIFO, impairment reversals, R&D vs. internal-use software capitalization, operating vs. finance lease lessee expense profiles, and debt modification vs. extinguishment tests).
- Clearly separate **Accrual US GAAP** requirements from **AICPA Special Purpose Frameworks (Cash Basis / Modified Cash Basis)** so that subledger integrity (`AR`, `AP`, `Inventory`, `Deferred Revenue`) is preserved.

## Common failure modes to avoid

- Failing to distinguish Public Business Entity (PBE) rules from Private Company (Non-PBE / PCC) elections
- Mixing US GAAP book accounting (`ASC 740`) with Internal Revenue Code (IRC) tax rules
- Confusing IFRS rules with US GAAP rules (e.g., allowing impairment reversals on PP&E/intangibles under `ASC 360`/`ASC 350`, or treating lessee operating leases under `ASC 842` as front-loaded interest + amortization like `IFRS 16`)
- Skipping disclosures (`ASC 235`, `ASC 275`, topic-specific) because the journal entry seems straightforward
- Jumping to journal entries before resolving unit of account, scope, and measurement
- Inventing granular `ASC XX-XX-XX-XX` paragraph citations without source text in context

## Final quality check (perform before responding)

- Framework (US GAAP PBE, US GAAP Private/Non-PBE, or OCBOA) and reporting context stated
- Facts vs. assumptions separated
- Primary and supporting `ASC` Topics identified with scope conclusion explicit
- Recognition, measurement, presentation, and disclosure addressed
- Key judgments and estimates flagged
- Missing facts and validation points listed
- No fabricated ASC paragraph citations or effective dates
- Output format matches user intent
