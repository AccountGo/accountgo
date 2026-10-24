# Company (Workspace) Onboarding — Requirements

**Status:** Approved; open decisions O1–O5 resolved to the proposed defaults (§11)  
**Owner:** Product + Finance architecture  
**Related:** [Implementation plan](./company-onboarding-implementation-plan.md) · [Multi-tenancy plan](../MULTI_TENANCY_PLAN.md) · [CoA register](../accounting/coa-register.md)  
**Standards lens:** IFRS / IFRS for SMEs · US GAAP (private / Non-PBE) · AICPA special purpose frameworks (cash / modified cash) · AccountGo SaaS double-entry engine

---

## 1. Purpose

Define the **minimum setup a new company (workspace) must complete before it is created**, and the **guided user journey** that collects those answers with low friction without sacrificing ledger quality.

This document is requirements and UX only. Implementation sequencing lives in [company-onboarding-implementation-plan.md](./company-onboarding-implementation-plan.md).

### 1.1 Naming (canonical)

| Layer | Term |
|---|---|
| UI copy, emails | workspace, organization |
| URL | `/org/:orgSlug/*` |
| Database / C# | `Company`, `CompanySetting` |

Do not introduce a separate Tenant / Organization schema entity.

---

## 2. Goals and non-goals

### 2.1 Goals

1. **Ledger-correct at birth** — Every new workspace is created with enough policy to post balanced journals under a chosen reporting framework and period calendar.
2. **Frictionless happy path** — Defaulted wizard completes in about 90 seconds; customized path under about 3 minutes.
3. **Progressive disclosure** — Ask only what changes the ledger at create time; defer commercial and polish settings.
4. **One create path** — Signup, Select Org, and Org Switcher all use the same wizard and the same provisioning service.
5. **Clean books** — Seed template CoA and operational defaults; **never** seed sample transactions.
6. **Enjoyable, production-grade UX** — Matches existing AccountGo design patterns (surfaces, tokens, Lucide, auth chrome).

### 2.2 Non-goals (v1)

- Opening balance / conversion wizard (tracked as post-create checklist item; feature may ship later).
- Multi-currency transactional FX setup at create time.
- Full tax registration / filing configuration.
- Live bank connections or payment processors.
- Per-account CoA editing inside the create wizard.
- Changing an existing workspace’s framework/currency after first posted journal (except via a future controlled migration path).

---

## 3. Current state (baseline)

As of the `feat/invoice-email-template-refine` / multi-tenant codebase:

| Area | Behavior today |
|---|---|
| Create UX | Single modal on `SelectOrg`: name, slug, currency |
| Signup | Optional workspace name; currency often omitted → `USD` |
| Provisioning | `CompanyProvisioningService`: full CoA from `coa.csv`, calendar FY Jan–Dec, payment terms, measurements, `CompanySetting` (currency + Accrual default), neutral tax `NONE` |
| Framework | Not stored |
| Fiscal year | Always current calendar year |
| Tax | Always “No tax” |
| Plan | Often hardcoded `accounting` on create |
| Post-create | Land on dashboard; Settings tabs for profile, taxes, invoices, etc. — **no setup checklist** |

**Gap:** Currency is asked; framework, fiscal calendar, tax posture, plan intent, and CoA template family are implicit. Several of those become costly to change after the first posting.

---

## 4. Accounting requirements (CFO / standards)

### 4.1 Hard gate — must be answered before `Company` is created

These decisions define the books. The create API must reject incomplete hard-gate payloads.

| # | Decision | Requirement | Standards / system rationale |
|---|---|---|---|
| H1 | **Workspace identity** | Required trading/display name; unique slug; creator becomes Owner | Tenant isolation; membership model |
| H2 | **Functional (books) currency** | Required ISO 4217 code; single functional currency for GL | IAS 21 / ASC 830 — all GL in one currency |
| H3 | **Reporting framework** | Required enum (see §5.1) | Full IFRS · IFRS for SMEs · US GAAP private · Cash / OCBOA |
| H4 | **Accounting basis** | Accrual or Cash, consistent with framework (see §4.3) | Recognition timing; already on `CompanySetting.AccountingBasis` |
| H5 | **Fiscal year shape + books start date** | FY pattern (calendar / Apr–Mar / Jul–Jun / custom start month) and earliest postable date | Period open/close; no posting before books start |
| H6 | **Plan / module intent** | `invoicing` \| `accounting` \| `enterprise` | Module seed and checklist depth |
| H7 | **Tax posture** | At least: None · Sales tax · VAT/GST (rates deferred) | Invoice/AP auto-post tax lines |
| H8 | **Inventory costing** | Required **only if** inventory modules are enabled for the plan: FIFO or weighted average | IAS 2 / ASC 330; **LIFO forbidden for IFRS / IFRS for SMEs** |

### 4.2 Soft gate — before first real commercial document (non-blocking at create)

Shown as a post-create checklist; does not block workspace creation.

| # | Item | Purpose |
|---|---|---|
| S1 | Confirm or refine tax groups/rates if posture ≠ None | Correct first invoice tax |
| S2 | Identify / rename primary cash or bank account | Cash position and payment posting |
| S3 | Skim seeded chart of accounts (accounting/enterprise) | Control-account awareness |
| S4 | Legal name + business contact for invoices | Commercial documents |
| S5 | Optional: invite teammate | Collaboration |
| S6 | Optional: opening balances (when feature exists) | Conversion from prior books |

### 4.3 Framework ↔ basis consistency rules

| Reporting framework | Default basis | Allowed at create |
|---|---|---|
| Full IFRS | Accrual | Accrual only as statutory default |
| IFRS for SMEs | Accrual | Accrual only as statutory default |
| US GAAP (private / Non-PBE) | Accrual | Accrual only as statutory default |
| Cash basis (OCBOA) | Cash | Cash |

**Product rule:** A “Cash” report toggle on Accrual books (management view) is **not** the same as selecting Cash basis framework. Do not conflate them in the wizard.

### 4.4 Explicitly deferred (must not block create)

- Full postal address, tax IDs, registration numbers  
- Detailed tax rates and multi-jurisdiction schedules  
- Bank feeds / payment integrations  
- Opening balances / prior-year conversion  
- Cost centres / dimensions  
- Custom CoA account codes and GL mapping editors  
- Whitelabel / branding  
- Payroll, fixed assets detail registers, lease modules  

### 4.5 Immutability and change control

| Field | Books empty (no posted JE) | After first posted JE |
|---|---|---|
| Currency | Editable with strong warning | **Locked** (future migration path only) |
| Reporting framework | Editable with strong warning | **Locked** / change-control workflow |
| Accounting basis (statutory) | Editable with consistency checks | **Locked** / change-control |
| Fiscal year calendar | Adjustable if period unused | Closed periods immutable; open period edits controlled |
| CoA | Customize accounts; do not re-seed template | Same |
| Tax posture / rates | Freely refine | Do not delete taxes with history |

---

## 5. Product requirements

### 5.1 Data captured at create (logical model)

| Field | Required | Notes |
|---|---|---|
| `Name` (trading) | Yes | Maps to `Company.Name` |
| `Slug` | Yes | Unique globally; reserved after soft-delete |
| `LegalName` | No | `CompanySetting.LegalName` |
| `Currency` | Yes | `CompanySetting.Currency` |
| `ReportingFramework` | Yes | New persisted field |
| `AccountingBasis` | Yes | Existing; enforce §4.3 |
| `FiscalYearPattern` | Yes | Calendar · Apr–Mar · Jul–Jun · Custom |
| `FiscalYearStartMonth` | When custom / pattern needs it | 1–12 |
| `BooksStartDate` | Yes | Earliest allowed posting date; drives first open FY |
| `Plan` | Yes | `invoicing` \| `accounting` \| `enterprise` |
| `TaxRegime` | Yes | `None` \| `SalesTax` \| `VatGst` |
| `IndustryTemplate` | Yes | Drives CoA template family: Services · Product/retail · Mixed |
| `InventoryCostingMethod` | Conditional | `None` \| `Fifo` \| `WeightedAverage`; never `Lifo` for IFRS* |
| `CoaTemplateId` | Derived | Not free-typed by user; derived from industry + framework (+ plan) |

### 5.2 Smart defaults (frictionless quality)

| Decision | Default |
|---|---|
| Plan | User-selected on intent step; no silent hardcode of `accounting` without UI |
| Framework | Locale/currency heuristic: `USD` → US GAAP private; otherwise IFRS for SMEs — always editable |
| Basis | Accrual (except Cash basis framework → Cash) |
| FY | Calendar year containing `BooksStartDate` |
| Books start | Today (UTC date or org-local date — implementation plan decides) |
| Tax | None |
| Industry | Services |
| Inventory method | None unless product/mixed industry or inventory module on → Weighted average |
| Currency | Last-used currency for the user, else product default (resolve PHP vs USD doc mismatch separately) |

**Time budgets**

- Happy path with defaults: ≤ 90 seconds  
- Customized FY / tax / framework: ≤ 3 minutes  

### 5.3 Entry points (must share one wizard)

1. Post-signup when user opted to name a workspace (do **not** create Company until wizard completes).  
2. `SelectOrg` → New organization.  
3. Org switcher → Create workspace.  
4. Deep link: `/select-org?action=new` or dedicated `/create-org`.

Invitation-only signup continues to skip workspace creation.

### 5.4 Success criteria (product)

| Metric | Target (directional) |
|---|---|
| Create → first invoice | Shorter than current after checklist exists |
| Framework / FY changed in Settings within 7 days | Low (wizard captured correctly) |
| Support tickets: wrong currency / FY | Near zero for new workspaces |
| Abandoned wizard | No partial `Company` rows |

---

## 6. UX / UI requirements

### 6.1 Design system constraints

Follow existing AccountGo Web patterns:

- React + Tailwind tokens: `--bg-canvas`, `--bg-surface`, `--brand-primary`, `--brand-ring`, `--border-subtle`
- Surfaces: `rounded-2xl` / `rounded-xl`; input height `h-11`
- Icons: Lucide  
- Auth / select-org chrome: full-page, no sidebar (`OrgLayout` only after create)  
- Primary CTA: existing `btn-primary` / brand fill  
- Overlays: `bg-black/60–75` when any modal remains for edge cases  

**Replace** the single create modal with a **multi-step create flow** so accounting choices are not cramped.

### 6.2 Journey structure

| Step | Screen | Job to be done | Required inputs |
|---|---|---|---|
| 0 | Intent (optional micro-step) | Choose plan / depth | Plan card |
| 1 | Identity | Name the workspace | Name, slug; legal name optional |
| 2 | Money & time | Currency, books start, FY pattern | Currency, books start, FY |
| 3 | How you report | Framework, basis, tax posture, industry (+ inventory if needed) | H3–H4, H7–H8, industry |
| 4 | Review & create | Confirm and provision | Summary; CTA Create workspace |
| 5 | Post-create checklist | Soft gates | Non-blocking banner/card |

**Invoicing plan:** Same shell; shorten Screen 3 (framework can stay defaulted with less prominence) and Checklist (skip deep CoA).

### 6.3 Screen-level requirements

#### Screen 0 — Intent

- Three cards: Invoicing · Full accounting · Enterprise  
- One short supporting line each  
- Selection sets `Plan` and which later fields appear  

#### Screen 1 — Identity

- Brand-first header: “Create your workspace”  
- Trading name (required) with live slug preview and availability  
- Legal name optional (“shown on invoices”)  
- Progress indicator: Step *n* of *N*  
- **No** address, phone, or logo upload  

#### Screen 2 — Money & time

- Currency searchable select  
- Helper: “This becomes your books currency”  
- Books start date  
- FY segmented control: Calendar · Apr–Mar · Jul–Jun · Custom  
- Live preview chip: e.g. `FY2026 · 1 Jan 2026 – 31 Dec 2026 · Open`  
- Note: “Opening balances can be added later”  

#### Screen 3 — How you report

- Framework as large selectable cards with one-line plain English + “Recommended for …”  
- Basis control with §4.3 enforcement and inline explanation  
- Tax posture radio cards: None · Sales tax · VAT/GST  
- Industry cards: Services · Product/retail · Mixed  
- Inventory method only when relevant; **do not offer LIFO** for IFRS / IFRS for SMEs  

#### Screen 4 — Review & create

- Summary of all hard-gate choices  
- Primary: Create workspace  
- Secondary: Back  
- Loading copy: “Setting up your chart of accounts…”  
- On success: navigate to `/org/{slug}/dashboard` with checklist visible  

#### Screen 5 — Get books ready (in-product)

- Dismissible checklist for Owner/Admin  
- Links into existing Settings tabs (Taxes, Financials, General, Members)  
- Depth varies by plan (§5.2 / Screen 0)  

### 6.4 Motion (minimum)

1. Progress bar fills with brand color between steps  
2. FY preview chip updates as date/month changes  
3. Brief success confirmation before dashboard  

Avoid heavy confetti or decorative clutter.

### 6.5 States and accessibility

| State | Requirement |
|---|---|
| Slug conflict | Inline error; preserve other fields |
| Validation | Per-step; cannot advance with invalid required fields |
| API failure on create | Remain on Review; show error; retry safe (idempotent slug check) |
| Abandon mid-wizard | No partial Company; local draft optional (nice-to-have) |
| Keyboard | Full keyboard navigation; focus management on step change |
| Mobile | Usable on small viewports; same step model |

---

## 7. Provisioning requirements

On successful create, `CompanyProvisioningService` (or equivalent single path) must:

1. Create `Company` + Owner `CompanyMember`  
2. Seed CoA from **selected template family** (not only one global CSV forever — may start as filtered/annotated variants of current seed)  
3. Seed `GeneralLedgerSetting` control accounts consistent with template  
4. Create financial year(s) from FY pattern + books start (not always Jan 1 of UTC year)  
5. Persist `CompanySetting`: currency, basis, legal name if provided, **plus new framework / tax regime / inventory / template metadata**  
6. Seed payment terms, sequence numbers, measurements (as today)  
7. Tax:  
   - `None` → existing neutral `NONE` group  
   - `SalesTax` / `VatGst` → seed posture-appropriate **placeholder** group(s) with inactive or zero starter rates **or** empty group + checklist emphasis (implementation plan chooses; rates never assumed from another jurisdiction)  
8. Apply plan / `ActiveModulesJson`  
9. **No sample transactions**

Idempotent re-setup (`Administration/Setup`) must respect already-chosen framework/FY and not overwrite user policy with calendar defaults blindly.

---

## 8. API / contract requirements (logical)

Extend authenticated create (and signup-when-creating-workspace) to accept the hard-gate payload.

- Reject unknown framework / tax regime / inventory method combinations  
- Reject LIFO when framework is IFRS or IFRS for SMEs  
- Reject Cash basis with IFRS / US GAAP as statutory pair  
- Slug uniqueness and sanitization unchanged in spirit from today  
- Response: created organization summary + slug for navigation  

Exact DTO shapes and migration steps: see implementation plan.

---

## 9. Security, tenancy, and audit

- Create remains authenticated (except invitation flows that skip create)  
- Creator is Owner  
- All seeded rows carry `CompanyId`  
- Provisioning runs under tenant impersonation for the new company (existing pattern)  
- Record create audit: who, when, key policy fields (framework, currency, FY, plan)  
- Soft-deleted slugs remain reserved  

---

## 10. Acceptance criteria (requirements level)

1. User cannot create a workspace without completing hard-gate fields (H1–H8 as applicable).  
2. Happy path with defaults creates a workspace and lands on dashboard with checklist.  
3. Provisioned FY matches selected pattern and books start date.  
4. `CompanySetting` (and new fields) persist framework, basis, currency, tax regime.  
5. IFRS / IFRS for SMEs workspaces never receive LIFO as a selectable or stored inventory method.  
6. No sample journal entries exist after create.  
7. Invoicing vs accounting plans see appropriately scoped Screen 3 and checklist.  
8. Signup optional-name path does not create Company until wizard finishes.  
9. UI matches AccountGo design patterns (§6.1).  
10. Existing Settings remain the place for soft-gate completion (taxes, business details, CoA).  

---

## 11. Open decisions (product)

Decisions below adopt the defaults proposed in §5.2 and are the basis for implementation:

| # | Question | Decision |
|---|---|---|
| O1 | Primary market framework when currency does not decide | IFRS for SMEs |
| O2 | Cash basis: first-class framework vs report-only toggle | First-class OCBOA framework card |
| O3 | Opening balances in v1 checklist | Checklist item linking to “coming soon” or hide until feature exists |
| O4 | Signup: wizard-before-create vs create-then-force-wizard | Wizard-before-create |
| O5 | SalesTax/VatGst seed strategy | Placeholder group + checklist to set rates (no foreign jurisdiction rates) |

---

## 12. Document history

| Date | Change |
|---|---|
| 2026-10-24 | Initial requirements from CFO / architect / PM / UX planning session |
| 2026-10-25 | Open decisions O1–O5 resolved to proposed defaults; status set to Approved |
