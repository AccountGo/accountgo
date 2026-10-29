# Fact Pattern Question Bank for US GAAP Accounting Analysis

Use this reference to request the minimum outcome-determinative facts needed for a reliable US GAAP conclusion.
Ask only the questions that could change the accounting treatment.

## Table of Contents

1. Universal minimum fact set
2. Revenue and contract costs (`ASC 606` / `ASC 340-40`)
3. Leases (`ASC 842`)
4. Software, R&D, and intangibles (`ASC 350-40` / `ASC 985-20` / `ASC 730`)
5. Inventory and cost of sales (`ASC 330` / `ASC 705`)
6. Impairment and credit losses (`ASC 360` / `ASC 350` / `ASC 326`)
7. Contingencies, commitments, and guarantees (`ASC 450` / `ASC 460`)
8. Debt, equity, and financing instruments (`ASC 470` / `ASC 480` / `ASC 815`)
9. Business combinations and consolidation (`ASC 805` / `ASC 810`)
10. Income taxes (`ASC 740`) and `ASC 250` accounting changes

## 1) Universal minimum fact set (ask first if missing)

1. Is the reporting entity a **Public Business Entity (PBE)**, a **Non-PBE Private Company** (eligible for PCC alternatives and practical expedients), or a **Not-for-Profit (`ASC 958`)**?
2. Is the entity reporting under full **Accrual US GAAP**, or under an **AICPA Special Purpose Framework (Cash Basis, Modified Cash Basis, or Tax Basis)**?
3. What is the reporting period and balance sheet date?
4. What are the key contract terms, dates, and dollar amounts involved?
5. How is the transaction currently recorded in the General Ledger (if at all)?
6. What deliverable is needed (technical memo, journal entries, policy draft, or ERP posting rule design)?

## 2) Revenue and contract costs (`ASC 606` / `ASC 340-40`)

- Are there multiple promised goods or services (e.g., software license, SaaS hosting, implementation/setup, hardware, support)?
- For software/technology: does the customer have the contractual right to take possession of the software and run it on their own infrastructure (`ASC 606-10-55-54` functional IP), or is it a hosted SaaS arrangement?
- Is there variable consideration (usage tiers, rebates, SLA credits, milestone bonuses, right of return)?
- Does the entity control the good or service before transfer to the customer (Principal = Gross) or arrange for another party to provide it (Agent = Net)?
- What are the billing and payment terms relative to performance (upfront annual prepayment creating Deferred Revenue vs. unbilled Contract Asset)?
- Are incremental sales commissions paid to obtain the contract (`ASC 340-40`), and is the amortization period <=1 year?

## 3) Leases (`ASC 842`)

- Does the contract identify a specific physical asset and convey the right to control its use?
- What is the noncancellable lease term, and are any renewal or termination options *reasonably certain* of exercise?
- Do any of the 5 Finance Lease criteria (`ASC 842-10-25-2`: title transfer, reasonably certain purchase option, major part of economic life >=75%, substantially all fair value >=90%, or specialized asset with no alternative use) apply, or is it an Operating Lease?
- Has the entity elected the practical expedient not to separate lease and non-lease components?
- If a Non-PBE private company: has the entity elected the risk-free discount rate expedient instead of the Incremental Borrowing Rate (IBR)?

## 4) Software, R&D, and intangibles (`ASC 350-40` / `ASC 985-20` / `ASC 730`)

- Is the software being developed for **internal use / cloud hosting / SaaS delivery** (`ASC 350-40`) or to be **sold, leased, or marketed on-premise** (`ASC 985-20`)?
- If under `ASC 350-40` (internal-use / SaaS platform):
  - Has the preliminary project stage completed and has management authorized/committed funding for the project (**application development stage**)?
  - Which costs are direct coding/testing/configuration (capitalizable) vs. data conversion, training, and maintenance (expensed)?
- If a Cloud Computing Arrangement (CCA) where the entity is the customer: what implementation costs are incurred during the application development stage?

## 5) Inventory and cost of sales (`ASC 330` / `ASC 705`)

- Which cost flow method does the entity use (FIFO, Moving/Weighted Average, Specific Identification, or LIFO)?
- Are there obsolete, slow-moving, or damaged items where Net Realizable Value (or Market for LIFO) has fallen below cost?
- Are freight-in, import duties, and direct production overhead properly included in inventory cost rather than expensed immediately?

## 6) Impairment and credit losses (`ASC 360` / `ASC 350` / `ASC 326`)

- **Long-lived assets (`ASC 360`):** What is the lowest level of identifiable cash flows (asset group)? Do undiscounted expected future cash flows exceed carrying value (Step 1)?
- **Goodwill (`ASC 350-20`):** Has the private company elected PCC goodwill amortization (<=10 years), or is it performing annual/trigger-based impairment testing?
- **Receivables (`ASC 326` CECL):** What historical loss data, aging buckets, and reasonable/supportable forecast adjustments are used to estimate expected credit losses?

## 7) Contingencies, commitments, and guarantees (`ASC 450` / `ASC 460`)

- Did the underlying obligating event occur on or before the balance sheet date?
- Is an unfavorable outcome **Probable**, **Reasonably Possible**, or **Remote**?
- Can a single best estimate be determined, or only a range of loss (where the minimum of the range is accrued under `ASC 450-20-30-1`)?
- Are there any gain contingencies or insurance recoveries (`ASC 450-30` / `ASC 410-30` — note that recoveries exceeding recognized losses are gain contingencies and cannot be recognized until realized)?

## 8) Debt, equity, and financing instruments (`ASC 470` / `ASC 480` / `ASC 815`)

- What are the exact redemption, conversion, warrant, or liquidation preference terms?
- Can the holder force cash redemption (or redemption upon an event outside the issuer's control)?
- For debt modifications/refinancings (`ASC 470-50`): is the lender the same, and do the present values of cash flows differ by >=10%?

## 9) Business combinations and consolidation (`ASC 805` / `ASC 810`)

- Does the acquired set fail the `ASC 805` "screen test" (where substantially all fair value of gross assets acquired is concentrated in a single identifiable asset or group of similar assets, making it an **asset acquisition** rather than a business combination)?
- Are there contingent consideration (earnout) arrangements, and are any tied to continuing employment of selling shareholders (compensation vs. purchase price)?
- Does the entity hold a variable interest in a Variable Interest Entity (`ASC 810` VIE) where it is the primary beneficiary?

## 10) Income taxes (`ASC 740`) and `ASC 250` accounting changes

- **Income taxes (`ASC 740`):** Does the entity have cumulative 3-year pre-tax book losses (strong negative evidence requiring a DTA valuation allowance)?
- **Accounting changes (`ASC 250`):** Did the adjustment arise from new information (estimate change — prospective), a voluntary/mandatory change between acceptable GAAP methods (principle change — retrospective), or a mistake/omission in applying GAAP to facts that existed in a prior period (error correction — restatement)?
