# US GAAP (FASB ASC) Topic Map and Scope-First Routing

Use this reference to route transactions to the appropriate FASB Accounting Standards Codification (`ASC`) Topics, identify supporting standards, avoid US GAAP vs. IFRS traps, and evaluate Cash vs. Accrual (OCBOA) boundaries.

## Table of Contents

1. FASB Codification hierarchy and routing principles
2. Core ASC topic map (common transactions)
3. US GAAP vs. IFRS critical divergence matrix
4. Accrual US GAAP vs. OCBOA (Cash / Modified Cash / Tax Basis)
5. Multi-topic issue patterns
6. Industry-specific ASC topics (`ASC 905`–`ASC 995`)
7. Topic-specific analysis prompts

## 1) FASB Codification hierarchy and routing principles

- Under `ASC 105` (*Generally Accepted Accounting Principles*), the FASB Accounting Standards Codification is the single source of authoritative nongovernmental US GAAP.
- For Public Business Entities (PBEs) / SEC registrants, rules and interpretive releases of the SEC (e.g., Regulation S-X, Staff Accounting Bulletins / SABs) are also authoritative.
- Start with the **economic substance** and **enforceable rights and obligations** of the arrangement.
- Identify the **unit of account** before selecting the measurement rule.
- Test **scope and scope exceptions** (`Subtopic XX-10-15` in the Codification) before applying recognition and measurement.

## 2) Core ASC topic map (common transactions)

### Revenue and customer contracts

**Primary**
- `ASC 606` *Revenue from Contracts with Customers*
- `ASC 340-40` *Other Assets and Deferred Costs — Contracts with Customers* (incremental costs of obtaining a contract and fulfillment costs)
- `ASC 610-20` *Other Income — Gains and Losses from the Derecognition of Nonfinancial Assets*

**Common supporting**
- `ASC 310` / `ASC 326` (receivables and Current Expected Credit Losses / CECL)
- `ASC 460` (guarantees and product warranties)
- `ASC 450` (loss contingencies / onerous contract provisions where applicable)
- `ASC 830` (foreign currency considerations for contract assets vs. deferred revenue)

**Key routing questions**
- Does a contract with a customer exist with commercial substance and probable collectibility (`ASC 606-10-25-1`)?
- What are the distinct performance obligations (capable of being distinct + distinct within the context of the contract)?
- Is the entity a principal (gross revenue) or an agent (net revenue) based on control before transfer?
- Is there variable consideration subject to the "probable of no significant reversal" constraint?
- Is there a significant financing component (or does the 1-year practical expedient apply)?
- For software/SaaS: is it hosted cloud service (over time) or on-premise functional IP license (point in time)?
- Are sales commissions incremental costs of obtaining a contract (`ASC 340-40`) eligible for the <=1 year amortization practical expedient?

### Leases

**Primary**
- `ASC 842` *Leases*

**Common supporting**
- `ASC 360` (impairment of ROU assets as part of long-lived asset groups)
- `ASC 410-20` (Asset Retirement Obligations / lease restoration obligations)
- `ASC 820` (fair value measurements)
- `ASC 230` (cash flow classification of operating vs. finance lease payments)

**Key routing questions**
- Does the contract contain an identified asset (explicitly or implicitly) where the customer has the right to obtain substantially all economic benefits and direct its use?
- What is the lease classification (Finance Lease vs. Operating Lease under the 5 criteria in `ASC 842-10-25-2`)?
- Did the entity elect the practical expedient not to separate lease and non-lease components by class of underlying asset?
- Is it a short-term lease (<=12 months without a purchase option reasonably certain of exercise)?
- If Non-PBE (private company): did the entity elect the risk-free discount rate practical expedient by asset class, or common-control written terms arrangements?

### Inventory and cost of sales

**Primary**
- `ASC 330` *Inventory*
- `ASC 705` *Cost of Sales and Services*

**Common supporting**
- `ASC 420` (exit or disposal cost obligations)
- `ASC 450` (firm purchase commitment losses)

**Key routing questions**
- What cost flow assumption is used (FIFO, Weighted/Moving Average, Specific Identification, or LIFO)?
- What subsequent measurement rule applies?
  - For FIFO or Average Cost: **Lower of Cost and Net Realizable Value (NRV)**.
  - For LIFO or Retail Inventory Method: **Lower of Cost or Market (LCM)** (where Market is replacement cost bounded by NRV ceiling and NRV less normal profit margin floor).
- Note: Under `ASC 330`, once inventory is written down below cost, the reduced amount becomes the new cost basis and **subsequent reversals are prohibited** (unlike IAS 2).

### Property, plant, equipment, intangibles, and software costs

**Primary**
- `ASC 360-10` *Property, Plant, and Equipment*
- `ASC 350-10` / `350-20` *Intangibles — Goodwill and Other* (Goodwill)
- `ASC 350-30` *General Intangibles Other Than Goodwill*
- `ASC 350-40` *Internal-Use Software* (including cloud computing arrangement / CCA implementation costs)
- `ASC 985-20` *Software — Costs of Software to Be Sold, Leased, or Otherwise Marketed*
- `ASC 730` *Research and Development*
- `ASC 835-20` *Capitalization of Interest*

**Key routing questions**
- Is software being developed for internal use / SaaS hosting (`ASC 350-40`: capitalize during the **application development stage**; expense preliminary project and post-implementation stages) or to be licensed/sold on-premise (`ASC 985-20`: capitalize only after **technological feasibility** is established)?
- For general R&D outside software rules (`ASC 730`): are costs expensed as incurred (unlike IAS 38 development capitalization)?
- For Goodwill (`ASC 350-20`): is the entity a PBE (annual/trigger-based single-step quantitative impairment test, with optional Step 0 qualitative screen) or a Private Company electing PCC goodwill amortization (amortize over <=10 years + trigger-based impairment at entity or reporting unit level)?

### Impairment of long-lived assets and credit losses

**Primary**
- `ASC 360-10-35` *Impairment or Disposal of Long-Lived Assets*
- `ASC 326` *Financial Instruments — Credit Losses (CECL)*

**Key routing questions**
- For held-and-used long-lived assets (`ASC 360`):
  - **Step 1 (Recoverability Test):** Is the carrying amount of the asset group greater than the **undiscounted** future net cash flows?
  - **Step 2 (Measurement):** If unrecoverable in Step 1, write down to **Fair Value** (`ASC 820`). Subsequent reversal of impairment is **strictly prohibited**.
- For trade receivables and contract assets (`ASC 326` CECL):
  - Are lifetime expected credit losses estimated using historical loss experience, current conditions, and reasonable and supportable forecasts (e.g., aging matrix / roll-rate / loss-rate methods)?

### Contingencies, commitments, and exit/restructuring costs

**Primary**
- `ASC 450` *Contingencies* (`450-20` Loss Contingencies, `450-30` Gain Contingencies)
- `ASC 420` *Exit or Disposal Cost Obligations*
- `ASC 410` *Asset Retirement and Environmental Obligations*
- `ASC 460` *Guarantees*

**Key routing questions**
- For loss contingencies (`ASC 450-20`):
  - Is the loss **Probable** (likely to occur — typically ~75%–80%+ in US GAAP practice, higher than IFRS "more likely than not" >50%), **Reasonably Possible**, or **Remote**?
  - Can the loss be reasonably estimated?
  - If a range of loss exists and no amount within the range is a better estimate than any other, accrue the **minimum** amount in the range (`ASC 450-20-30-1`) — unlike IAS 37 midpoint/expected value.
  - Gain contingencies (`ASC 450-30`) are not recognized until realized.

### Debt, equity, and financial instruments

**Primary**
- `ASC 470` *Debt* (`470-20` Debt with Conversion and Other Options, `470-50` Modifications and Extinguishments)
- `ASC 480` *Distinguishing Liabilities from Equity*
- `ASC 815` *Derivatives and Hedging* (`815-40` Contracts in Entity's Own Equity)
- `ASC 505` *Equity*
- `ASC 320` / `ASC 321` *Investments — Debt and Equity Securities*

**Key routing questions**
- Is a freestanding instrument mandatorily redeemable, settled in a variable number of shares, or indexed to an obligation to repurchase shares (`ASC 480` liability)?
- For SEC registrants: do conditional redemption features outside the issuer's control require classification in **Temporary / Mezzanine Equity** (`ASC 480-10-S99`)?
- For debt refinancing (`ASC 470-50`): does the 10% cash flow test indicate a troubled debt restructuring (`ASC 470-60`), a debt extinguishment (>=10% change in PV of cash flows), or a debt modification (<10% change)?

### Income taxes

**Primary**
- `ASC 740` *Income Taxes*

**Key routing questions**
- What are the book-to-tax temporary differences producing Deferred Tax Assets (DTAs) and Deferred Tax Liabilities (DTLs)?
- Does positive vs. negative weight of evidence require a **Valuation Allowance** (if it is more likely than not >50% that some or all of the DTA will not be realized)?
- For Uncertain Tax Positions (`ASC 740-10-25` / FIN 48 two-step model):
  - **Step 1 (Recognition):** Is the tax position more likely than not (>50%) to be sustained upon examination based solely on technical merits?
  - **Step 2 (Measurement):** Measure at the largest amount of benefit that is cumulatively >50% likely to be realized upon ultimate settlement.

### Stock-based compensation and employee benefits

**Primary**
- `ASC 718` *Compensation — Stock Compensation*
- `ASC 710` / `ASC 712` / `ASC 715` *Compensation — General, Nonretirement Postemployment, and Retirement Benefits*

## 3) US GAAP vs. IFRS critical divergence matrix

Always verify which standard applies before proposing journal entries or system rules:

| Topic | US GAAP (`FASB ASC`) | IFRS (`IAS / IFRS`) |
| :--- | :--- | :--- |
| **Inventory Costing (`ASC 330` vs. `IAS 2`)** | Permits FIFO, Average, Specific ID, and **LIFO**. Reversal of write-downs is **prohibited**. | **Prohibits LIFO**. Permits reversal of write-downs up to original write-down. |
| **Lessee Leases (`ASC 842` vs. `IFRS 16`)** | **Dual model:** Finance leases (interest + amortization) vs. Operating leases (single straight-line lease expense in P&L). | **Single model:** Almost all leases are treated like finance leases (front-loaded interest + amortization). |
| **Long-Lived Asset Impairment (`ASC 360` vs. `IAS 36`)** | **Two-step:** Step 1 undiscounted cash flow recoverability screen; Step 2 write down to Fair Value. **No reversals allowed.** | **One-step:** Compare carrying amount directly to Recoverable Amount (higher of VIU and FVLCD). Reversals allowed (except goodwill). |
| **R&D & Software (`ASC 730`/`350-40`/`985-20` vs. `IAS 38`)** | General R&D expensed (`ASC 730`); dedicated capitalization rules for internal-use software (`ASC 350-40`) and marketed software (`ASC 985-20`). | All development costs across industries must be capitalized once the 6 `IAS 38` criteria are met. |
| **Contingencies / Provisions (`ASC 450` vs. `IAS 37`)** | "Probable" means likely (~75%–80%+). Equal-probability range uses the **low end (minimum)** of the range; typically undiscounted. | "Probable" means more likely than not (>50%). Equal-probability range uses the **midpoint**; discounted if time value is material. |
| **Property Revaluation (`ASC 360` vs. `IAS 16`)** | Historical cost only; upward revaluation is **prohibited**. | Permits choice between Cost Model and Revaluation Model (fair value through OCI). |

## 4) Accrual US GAAP vs. OCBOA (Cash / Modified Cash / Tax Basis)

Under AICPA Professional Standards (`AU-C 800`), entities not required to issue US GAAP financial statements may report under a **Special Purpose Framework** (often called OCBOA — Other Comprehensive Basis of Accounting):

1. **Pure Cash Basis:**
   - Only cash receipts and cash disbursements are recorded. No A/R, A/P, Inventory, PP&E, or Accrued Liabilities exist.
   - Statements must be titled appropriately (e.g., *Statement of Assets and Liabilities Arising from Cash Transactions* and *Statement of Revenue Collected and Expenses Paid*) to avoid misleading readers into thinking they are GAAP Balance Sheets or Income Statements.
2. **Modified Cash Basis:**
   - Combines cash basis for operating working capital (revenues recognized when collected; operating expenses when paid) with logical accrual modifications having substantial support (such as capitalizing Inventory and Fixed Assets / PP&E and recording Depreciation and Loans Payable).
3. **SaaS ERP Implementation Rule (AccountGo Context):**
   - Keep the primary General Ledger on **Accrual Basis (US GAAP / IFRS)** so subledgers (`Accounts Receivable`, `Accounts Payable`, `Inventory`) remain accurate.
   - Generate **Cash Basis** P&L and Balance Sheet reports by substituting open `AR` and `AP` accruals with pro-rata revenue, expense, and tax lines determined by `CustomerAllocation` and `VendorPayment` settlement dates.

## 5) Multi-topic issue patterns

### Pattern A: SaaS subscription contract with upfront billing, implementation fees, and sales commissions
- `ASC 606`: Evaluate whether setup/implementation is a distinct performance obligation or an administrative/fulfillment activity integrated with the hosted SaaS service; record upfront billing as Deferred Revenue (Contract Liability).
- `ASC 340-40`: Capitalize incremental sales commissions to obtain the contract and amortize over the customer benefit period (including anticipated renewals unless commissions on renewals are commensurate).
- `ASC 326`: Assess trade receivables for expected credit losses.

### Pattern B: Internal software / AI platform build with cloud hosting
- `ASC 350-40`: Separate preliminary project stage (expense), application development stage (capitalize coding/testing), and post-implementation/maintenance stage (expense).
- Evaluate cloud computing arrangement (CCA) implementation costs under `ASC 350-40` (defer capitalized implementation costs and amortize over the hosting term into the same P&L line as hosting fees).

## 6) Industry-specific ASC topics (`ASC 905`–`ASC 995`)

Always check whether the entity operates in an industry with specialized US GAAP rules:
- `ASC 940` / `942` / `944` / `946` / `948`: Financial Services (Broker-Dealers, Depository/Lending, Insurance, Investment Companies, Mortgage Banking)
- `ASC 954`: Health Care Entities
- `ASC 958`: Not-for-Profit Entities (net assets with vs. without donor restrictions, contribution vs. exchange transactions)
- `ASC 970`: Real Estate
- `ASC 985`: Software

## 7) Topic-specific analysis prompts

### Revenue prompt (`ASC 606` / `ASC 340-40`)
- Confirm contract existence and collectibility threshold (`ASC 606-10-25-1`)
- Identify distinct performance obligations and principal vs. agent role
- Determine transaction price (fixed, variable + constraint, financing, noncash, consideration payable to customer)
- Allocate transaction price via standalone selling prices (SSP)
- Determine recognition pattern (over time vs. point in time)
- Evaluate contract costs (`ASC 340-40`) and contract asset / contract liability presentation

### Lease prompt (`ASC 842`)
- Confirm identified asset and right to control use
- Identify lease and non-lease components (and practical expedient election)
- Determine lease term, lease payments, and discount rate (IBR vs. implicit rate vs. Non-PBE risk-free rate)
- Classify as Finance Lease vs. Operating Lease
- Compute initial ROU asset and lease liability, plus subsequent straight-line (operating) vs. effective-interest/amortization (finance) schedules
