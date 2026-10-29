# Output Templates for US GAAP Accounting Skill

Use these templates to produce consistent, audit-ready US GAAP deliverables.
Adapt depth to materiality, entity status (PBE vs. Non-PBE), and user intent.

## Table of Contents

1. Technical US GAAP Memo (default)
2. Accounting Policy Draft (with PBE vs. Non-PBE election notes)
3. Transaction Review Checklist
4. Journal Entry Proposal (mapped to AccountGo / generic COA)
5. Executive Summary (management-facing)
6. Audit-Prep Question List
7. US GAAP vs. IFRS / Cash Basis Comparison Matrix

## 1) Technical US GAAP Memo (Default)

# Technical US GAAP Memo

## 1. Issue
- **Question:** [State the accounting question precisely]
- **Requested output:** [recognition / measurement / classification / disclosures / policy / JE / memo]
- **Unit of account:** [performance obligation / lease component / reporting unit / asset group / instrument / entity]

## 2. Applicable Framework and Entity Context
- **Framework:** [US GAAP (FASB ASC) / OCBOA Cash Basis / OCBOA Modified Cash Basis]
- **Entity status:** [Public Business Entity (PBE) / Non-PBE Private Company (note PCC elections) / Not-for-Profit (`ASC 958`)]
- **Reporting period/date:** [YYYY-MM-DD or fiscal period]
- **Financial statements:** [Consolidated / Standalone]
- **Industry context (if relevant):** [e.g., SaaS / Software (`ASC 985`), Manufacturing, Retail]

## 3. Facts Considered
1. ...
2. ...
3. ...

## 4. Assumptions (if any)
1. ...
2. ...

## 5. Relevant Authoritative Literature (FASB ASC)
### Primary ASC Topics / Subtopics
- [e.g., `ASC 606` *Revenue from Contracts with Customers*]
### Supporting ASC Topics
- [e.g., `ASC 340-40` *Other Assets and Deferred Costs — Contracts with Customers*, `ASC 326` *Credit Losses*]
### Notes on citation certainty
- [e.g., Topic/Subtopic level verified; granular paragraph verification required before audit sign-off]

## 6. Analysis

### 6.1 Scope
- [Why the transaction is within the scope of the primary ASC Topic and not excluded]
- [Component separation / unbundling across ASC Topics]

### 6.2 Recognition / Derecognition
- [Recognition criteria, trigger, and timing]

### 6.3 Initial Measurement
- [Measurement basis, allocation of consideration, capitalizable vs. expensed costs]

### 6.4 Subsequent Measurement / Remeasurement / Impairment
- [Amortization/depreciation pattern, remeasurement rules, impairment or CECL model]

### 6.5 Presentation / Classification
- [Balance Sheet (`ASC 210`): current vs. non-current, asset vs. contra-asset, liability vs. mezzanine vs. equity]
- [Income Statement (`ASC 220`): gross vs. net, COGS vs. operating expense vs. non-operating]
- [Statement of Cash Flows (`ASC 230`): operating, investing, financing, or noncash supplemental disclosure]

### 6.6 Footnote Disclosure (`ASC 235`, `ASC 275`, and Section 50)
- [Required qualitative and quantitative footnote disclosures]
- [PBE vs. Non-PBE disclosure accommodations]

### 6.7 Accounting Changes / Transition / Comparative Considerations (`ASC 250`)
- [Prospective vs. retrospective vs. error restatement treatment]

### 6.8 Significant Judgments and Estimates
- [Key judgments and `ASC 275` estimation uncertainties]

## 7. Conclusion
- **Recommended US GAAP treatment:** [clear statement]
- **Confidence level:** [High / Moderate / Preliminary]
- **Rationale:** [brief summary]

## 8. Private Company (PCC) Alternatives / Supportable Alternative Views
- [Applicable Non-PBE practical expedients or PCC elections]

## 9. Information Still Needed / Validation Points
1. ...
2. ...

## 10. Implementation Notes (ERP / SaaS Accounting Engine Mechanics)
- [Chart of Accounts mapping, subledger controls, auto-posting rules, reversal mechanics]

## 2) Accounting Policy Draft

# US GAAP Accounting Policy: [Topic]

## 1. Purpose and Scope
- Governed transactions and applicable FASB ASC Topic(s):
- Excluded transactions:

## 2. Entity Elections and Practical Expedients
- PBE vs. Non-PBE status:
- Practical expedients and PCC alternatives elected:

## 3. Recognition and Measurement Policy
- **Initial recognition:**
- **Initial measurement:**
- **Subsequent measurement and impairment:**

## 4. Presentation and Classification
- **Balance Sheet (`ASC 210`):**
- **Income Statement (`ASC 220`):**
- **Cash Flows (`ASC 230`):**

## 5. Footnote Disclosures (`ASC 235` / Topic Section 50)
- Required disclosures and rollforwards:

## 6. Internal Controls and Audit Evidence
- Required approvals, subledger reconciliations, and supporting schedules:

## 3) Transaction Review Checklist

# US GAAP Transaction Review Checklist: [Transaction Name]

- [ ] Executed contract, amendments, and side letters obtained
- [ ] Entity status (PBE vs. Non-PBE) and practical expedients confirmed
- [ ] Primary and supporting `ASC` Topics verified (`Section 15` Scope)
- [ ] Distinct units of account / performance obligations / lease components identified
- [ ] Initial and subsequent measurement schedules reconciled to the General Ledger
- [ ] Balance Sheet, Income Statement, and Cash Flow classifications verified
- [ ] Footnote disclosures and `ASC 855` subsequent events reviewed

## 4) Journal Entry Proposal

# US GAAP Journal Entry Proposal: [Transaction]

## 1. Context
- **Transaction summary:**
- **Accounting basis:** [US GAAP Accrual / OCBOA Cash Basis comparison]
- **Posting date(s):**

## 2. Proposed Balanced Entries (`SUM(Dr) = SUM(Cr)`)

### Entry 1: [Event Description] — [Date]
| Account Code | Account Name | Account Class | Debit | Credit |
| :--- | :--- | :--- | ---: | ---: |
| `1200` | Accounts Receivable | Asset (1) | $X,XXX.XX | |
| `4000` | Sales / Subscription Revenue | Revenue (4) | | $X,XXX.XX |

**US GAAP Basis (`ASC` Reference):** [Explanation]

## 5) Executive Summary (Management-Facing)

# US GAAP Executive Summary: [Topic]

- **What happened:** [1–3 sentences]
- **Financial statement & KPI impact:** [Impact on Revenue, EBITDA, Net Income, Working Capital, Covenants]
- **Recommended US GAAP treatment:** [Plain-English summary]
- **Private company options (if applicable):** [PCC / Non-PBE expedients]
- **Next steps before audit sign-off:** [Actionable list]

## 6) Audit-Prep Question List

# Auditor Discussion Prep (US GAAP): [Topic]

- **Management's position (`ASC` basis):**
- **Top 5 auditor challenge questions & prepared responses:**
- **PBC (Prepared-by-Client) audit evidence package:**

## 7) US GAAP vs. IFRS / Cash Basis Comparison Matrix

# Multi-Framework Comparison: [Transaction / System Feature]

| Dimension | US GAAP (`FASB ASC`) | IFRS (`IAS / IFRS`) | Cash / Modified Cash Basis (`OCBOA`) |
| :--- | :--- | :--- | :--- |
| **Authoritative Literature** | `ASC XXX` | `IFRS X` / `IAS X` | AICPA `AU-C 800` / IRC §448 |
| **Recognition Trigger** | | | |
| **Measurement Rule** | | | |
| **Journal Entry / GL Impact** | | | |
| **Financial Statement Presentation** | | | |
| **ERP System / Subledger Impact** | | | |
