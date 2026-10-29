# Judgment Framework for US GAAP Accounting Analysis

Use this reference when an issue involves material judgment, estimation uncertainty, private-company accounting elections, incomplete evidence, or `ASC 250` accounting changes and error corrections.

## Table of Contents

1. Purpose and mindset
2. Judgment protocol
3. Materiality framework (quantitative + qualitative / SAB 99)
4. Estimates and uncertainty framework (`ASC 275`)
5. Evidence quality grading
6. `ASC 250` change classification (principle vs. estimate vs. error)
7. Confidence calibration and escalation triggers
8. Audit-ready documentation checklist

## 1) Purpose and mindset

Produce US GAAP accounting analysis that is:
- grounded in the FASB Accounting Standards Codification (`ASC`),
- explicit about Public Business Entity (PBE) vs. Private Company (Non-PBE) elections,
- transparent about assumptions and evidence quality,
- auditable under AICPA or PCAOB standards.

When facts are incomplete, deliver a structured conditional view rather than false certainty.

## 2) Judgment protocol (apply explicitly)

For each significant judgment:

### Step 1: Define the judgment decision
State the exact question being judged:
- whether promised goods/services are distinct under `ASC 606`
- whether the entity is principal or agent
- whether software costs qualify for the application development stage (`ASC 350-40`) or technological feasibility (`ASC 985-20`)
- whether a loss contingency is probable, reasonably possible, or remote (`ASC 450-20`)
- whether an instrument is liability, mezzanine equity, or permanent equity (`ASC 480` / `ASC 815-40`)
- whether a valuation allowance is required on deferred tax assets (`ASC 740`)

### Step 2: Explain why judgment is required
- Identify the specific `ASC` criteria that cannot be resolved mechanically.

### Step 3: Identify outcome-sensitive facts
Separate:
- verified facts
- working assumptions
- open questions that could flip the conclusion

### Step 4: Evaluate competing views
For each plausible view, document:
- supporting facts and Codification rationale
- counterarguments and audit risks
- financial statement and footnote consequences

### Step 5: State the recommended position and reassessment triggers
State why the recommended position is preferable and what future events require reassessment.

## 3) Materiality framework (quantitative + qualitative / SAB 99)

Under FASB Concepts Statement No. 8 and SEC Staff Accounting Bulletin No. 99 (`SAB 99`), an omission or misstatement is material if there is a substantial likelihood that a reasonable user would view it as having significantly altered the total mix of information.

### Quantitative considerations
- percentage of pre-tax income, operating income, EBITDA, revenue, gross margin, total assets, net working capital, or total equity
- cumulative effect of uncorrected misstatements across periods ("iron curtain" vs. "rollover" evaluation under `SAB 108` / `ASC 250`)

### Qualitative considerations (can make a quantitatively small item material)
- masks a change in earnings or revenue trend
- hides a failure to meet analyst consensus or internal SaaS KPIs (ARR/gross margin)
- changes a loss into income or vice versa
- affects compliance with loan covenants or regulatory capital requirements
- increases management compensation or bonuses
- involves related-party transactions (`ASC 850`) or unlawful acts

## 4) Estimates and uncertainty framework (`ASC 275`)

Under `ASC 275` (*Risks and Uncertainties*), entities must evaluate and disclose certain significant estimates where it is reasonably possible that the estimate will change in the near term (within one year) and the effect would be material.

### Key analysis elements for estimates:
1. **Nature of the estimate:** e.g., `ASC 606` variable consideration constraint, `ASC 326` CECL allowance, `ASC 820` Level 3 fair value inputs, `ASC 360`/`350` impairment cash flows, `ASC 740` valuation allowance, or `ASC 718` common stock fair value (409A / option pricing inputs).
2. **Input observability (`ASC 820` hierarchy where applicable):**
   - **Level 1:** Quoted prices in active markets for identical assets/liabilities
   - **Level 2:** Observable market-corroborated inputs
   - **Level 3:** Unobservable inputs reflecting management's own assumptions
3. **Sensitivity and range of reasonably possible outcomes**
4. **Reassessment cadence and internal control review**

## 5) Evidence quality grading

Grade the underlying evidence to calibrate conclusion confidence:

- **Grade A (Strong / Audit-Ready):** Executed contracts, bank statements, third-party valuation reports, reconciled subledgers (`AR`, `AP`, `Inventory`), board resolutions.
- **Grade B (Corroborated Internal):** Reviewed management schedules, historical cohort/loss-rate tables, unsigned final-form term sheets.
- **Grade C (Preliminary / Unverified):** Verbal descriptions, unverified estimates, incomplete contract summaries.

## 6) `ASC 250` change classification (principle vs. estimate vs. error)

Always classify any change to prior accounting under `ASC 250` (*Accounting Changes and Error Corrections*):

### A. Change in Accounting Principle (`ASC 250-10-45-1` to `45-16`)
- Change from one generally accepted accounting principle to another (e.g., changing inventory cost flow from FIFO to Average Cost, or adopting a newly issued ASU / PCC alternative).
- Requires **preferability justification** (unless mandated by a new ASU) and **retrospective application** to all prior periods presented unless impracticable.
- Note: Changing from an non-GAAP method (such as Cash Basis) to Accrual US GAAP is treated as a **correction of an error** if the prior statements purported to be US GAAP, or a change in reporting framework if transitioning from OCBOA to US GAAP.

### B. Change in Accounting Estimate (`ASC 250-10-45-17` to `45-20`)
- Results from new information or new developments (e.g., revising useful lives of equipment, updating bad debt reserve percentages, or adjusting warranty accrual rates).
- Accounted for **prospectively** in the period of change and future periods (including a change in depreciation/amortization method, which `ASC 250` classifies as a *change in estimate effected by a change in accounting principle*).

### C. Correction of an Error in Previously Issued Financial Statements (`ASC 250-10-45-22` to `45-28`)
- Mathematical mistakes, mistakes in applying GAAP, or oversight/misuse of facts that existed when the financial statements were prepared.
- Requires **restatement** of prior-period financial statements with an adjustment to opening Retained Earnings:
  - **"Big R" Restatement:** Error is material to previously issued financial statements; requires reissuance/restatement and non-reliance notice.
  - **"Little r" Revision:** Error is immaterial to prior periods individually and in the aggregate, but correcting it out-of-period in the current period would materially misstate the current period; revise comparative columns the next time statements are issued.

## 7) Confidence calibration and escalation triggers

### Confidence levels
- **High Confidence:** Explicit ASC guidance, complete Grade A/B facts, low judgment ambiguity.
- **Moderate Confidence:** Bounded assumptions, standard multi-topic interaction, pending documentary verification.
- **Preliminary:** Missing key contract terms, complex financing/equity features, or Grade C evidence.

### Escalation triggers (recommend specialist CPA / audit review)
- Material restatement ("Big R" vs. "little r") evaluations under `ASC 250`
- Complex debt/equity/warrant contracts under `ASC 480` and `ASC 815-40`
- Business combinations (`ASC 805`) and Variable Interest Entity (`ASC 810` VIE) consolidations
- Uncertain tax positions and valuation allowances (`ASC 740`)
- Revenue arrangements with complex variable consideration, licensing, or principal-agent splits (`ASC 606`)

## 8) Audit-ready documentation checklist

- [ ] Accounting issue and unit of account defined
- [ ] Entity type (PBE vs. Non-PBE) and framework (US GAAP vs. OCBOA) stated
- [ ] Facts separated from assumptions
- [ ] Primary and supporting `ASC` Topics mapped
- [ ] Scope and scope exceptions verified
- [ ] Recognition, measurement, presentation, and disclosure analyzed
- [ ] Significant judgments and `ASC 275` estimates documented
- [ ] Confidence level and open validation items listed
