# Company (Workspace) Onboarding — Implementation Plan

**Status:** Ready for phased implementation  
**Requirements:** [company-onboarding-requirements.md](./company-onboarding-requirements.md)  
**Related:** [MULTI_TENANCY_PLAN.md](../MULTI_TENANCY_PLAN.md) · [coa-register.md](../accounting/coa-register.md) · `.agents/skills/saas-accounting-system/`

This plan is written for an implementing agent. Work **phase by phase**. Prefer **one PR per phase** on `feat/onboarding-phase-N-<name>`. Do not start the next phase until the previous PR’s acceptance checks pass.

**No partial Company rows:** the wizard collects state client-side (or server draft only if explicitly added later); `WorkspaceService.Create` remains the single atomic create + provision boundary.

---

## 0. Context

### 0.1 Problem

Workspace create today asks for name, slug, and currency, then seeds a one-size CoA, calendar FY, Accrual basis, and neutral tax. Ledger-defining choices (reporting framework, fiscal calendar, tax posture, plan, industry/CoA family) are implicit and expensive to fix after the first posting.

### 0.2 Target behaviour

1. All create entry points open a shared multi-step wizard.  
2. Hard-gate fields from the requirements doc are validated and persisted.  
3. `CompanyProvisioningService` seeds FY, CoA template family, tax posture, and settings from that payload.  
4. Dashboard shows a dismissible “Get books ready” checklist (soft gate).  
5. Currency / framework / statutory basis lock after the first posted journal entry.

### 0.3 Key code today (touch points)

| Concern | Path |
|---|---|
| Domain | `src/Core/Domain/Company.cs`, `CompanySetting.cs`, `Financials/FiscalYear.cs` (or `FinancialYear`), `GeneralLedgerSetting.cs` |
| Create API | `src/Api/Controllers/OrganizationsController.cs`, `AuthController.SignUp` |
| Services | `src/Api/Service/WorkspaceService.cs`, `CompanyProvisioningService.cs`, `Api/Data/Initializer.cs`, `Api/Data/coa.csv` |
| Web create | `src/Web/src/pages/SelectOrg.tsx`, `pages/Login.tsx`, `components/OrgSwitcher.tsx` |
| Settings | `src/Web/src/pages/Settings.tsx`, `TaxSettingsTab.tsx` |
| Tests | `test/AccountGo.ServicesTests/SignUpAndOrganizationTests.cs`, `test/AccountGo.BackendTests/MultiTenancyPhase3Tests.cs` |

### 0.4 Out of scope for this plan

Opening-balance posting engine, live FX, bank feeds, CoA visual editor inside the wizard, migrating **existing** workspaces’ frameworks automatically.

---

## Phase 1 — Domain model and persistence

**Branch:** `feat/onboarding-phase-1-domain`  
**Goal:** Persist hard-gate policy on the company without changing UX yet.

### 1.1 New enums / fields

Add to Core (names may match existing style in `EnumTypes.cs`):

```csharp
public enum ReportingFramework
{
    IfrsForSmes = 1,
    UsGaapPrivate = 2,
    Ifrs = 3,
    CashBasis = 4
}

public enum TaxRegime
{
    None = 0,
    SalesTax = 1,
    VatGst = 2
}

public enum FiscalYearPattern
{
    Calendar = 1,   // Jan–Dec
    AprMar = 2,
    JulJun = 3,
    Custom = 4
}

public enum IndustryTemplate
{
    Services = 1,
    ProductRetail = 2,
    Mixed = 3
}

public enum InventoryCostingMethod
{
    None = 0,
    Fifo = 1,
    WeightedAverage = 2
    // Do not add Lifo for IFRS tenants; reject at API if ever introduced
}
```

**`CompanySetting` (or dedicated `CompanyAccountingPolicy` if preferred — default: extend `CompanySetting`):**

| Column | Type | Notes |
|---|---|---|
| `ReportingFramework` | int / enum | Required after migration backfill |
| `TaxRegime` | int / enum | Default `None` |
| `FiscalYearPattern` | int / enum | Default `Calendar` |
| `FiscalYearStartMonth` | int? | 1–12; required when pattern needs it |
| `BooksStartDate` | date | Date-only semantics |
| `IndustryTemplate` | int / enum | Default `Services` |
| `InventoryCostingMethod` | int / enum | Default `None` |
| `CoaTemplateId` | string | e.g. `services-sme`, `trading-wa` |
| `OnboardingChecklistDismissedAt` | datetime? | Soft-gate UX |
| `OnboardingChecklistJson` | string? | Optional per-item completion |

Keep existing `Currency`, `AccountingBasis`.

### 1.2 Schema

- Add SQL under `db/scripts/tables/` (and FK scripts if needed) following repo convention.  
- SQLite / D1: follow existing column-add process used for multi-tenancy (document in PR if `CreateTables` / sync list must change).  
- Backfill existing rows: `ReportingFramework = IfrsForSmes` (or product-confirmed O1), `TaxRegime = None`, `FiscalYearPattern = Calendar`, `BooksStartDate = FinancialYear.StartDate` or company `CreatedAt` date, `IndustryTemplate = Services`, `InventoryCostingMethod = None`, `CoaTemplateId = legacy-default`.

### 1.3 Validation helpers

Centralize in a small domain or application service, e.g. `CompanyAccountingPolicyRules`:

- CashBasis framework ⇒ `AccountingBasis.Cash`; else ⇒ Accrual.  
- Reject Cash basis paired with Ifrs / IfrsForSmes / UsGaapPrivate.  
- Reject `InventoryCostingMethod` other than None/Fifo/WeightedAverage.  
- If framework is Ifrs or IfrsForSmes, reject any future LIFO.  
- `BooksStartDate` required; FY dates derived must be coherent.

### 1.4 Acceptance

- [x] Migration / schema scripts apply cleanly on empty and existing DB.  
- [x] Existing companies readable; settings round-trip in tests.  
- [x] Unit tests for `CompanyAccountingPolicyRules` matrix.  
- [x] No UI change required in this PR.

**Status: Done on branch `feat/company-accounting-policy`, pending merge.** Added the policy fields, enums, EF migration `CompanyAccountingPolicy`, D1 `0022_company_accounting_policy.sql`, SQLite upgrader columns with `BooksStartDate` backfill, and `CompanyAccountingPolicyRules`.  
Deferred to Phase 2 (not yet implemented):
- Wire `CompanyAccountingPolicyRules` into `WorkspaceService.Create` and PATCH; it is currently only unit-tested.
- Reject LIFO (no LIFO enum value exists yet), undefined `TaxRegime` / `FiscalYearPattern`, and `Custom` without `FiscalYearStartMonth` in 1–12.
- `BooksStartDate` / fiscal-year coherence, which needs the FY derivation in §2.4.

Interim behaviour: new workspaces default `BooksStartDate` to today (UTC) while the fiscal year is still calendar from Jan 1. Phase 2 must set `BooksStartDate` to the FY start (or wizard value) no later than the Phase 4 posting guard.

---

## Phase 2 — Create API and provisioning

**Branch:** `feat/onboarding-phase-2-provision`  
**Goal:** Accept hard-gate payload; provision FY, tax posture, settings, and template id.

### 2.1 DTO

Extend create organization request (and signup org fields) roughly:

```json
{
  "name": "Acme Studio",
  "shortName": null,
  "slug": "acme-studio",
  "legalName": "Acme Studio LLC",
  "plan": "accounting",
  "currency": "USD",
  "reportingFramework": "UsGaapPrivate",
  "accountingBasis": "Accrual",
  "fiscalYearPattern": "Calendar",
  "fiscalYearStartMonth": null,
  "booksStartDate": "2026-10-24",
  "taxRegime": "None",
  "industryTemplate": "Services",
  "inventoryCostingMethod": "None"
}
```

Backward compatibility: if old clients send only `name` / `slug` / `currency` / `plan`, apply §5.2 defaults from the requirements doc and still succeed (log “legacy create payload”).

### 2.2 `WorkspaceService.Create`

1. Validate policy rules.  
2. Derive `CoaTemplateId` from industry + framework (+ plan).  
3. Create company + owner membership.  
4. Call `Provision(company, CreateProvisionOptions options)` — replace currency-only signature.

### 2.3 `CompanyProvisioningService` changes

| Area | Change |
|---|---|
| CoA | Select template source by `CoaTemplateId`. **Phase 2 minimum:** keep single `coa.csv` but store template id; **Phase 2b / Phase 5:** variant CSVs or filtered account sets. |
| GL settings | Map control accounts from template codes (fail fast if codes missing). |
| FY | Build `FinancialYear` from pattern + `BooksStartDate` (see §2.4). Do not always use `DateTime.UtcNow.Year` + Jan 1. |
| CompanySetting | Persist all new fields + currency + basis + legal name. |
| Tax | `None` → existing NONE group. `SalesTax` / `VatGst` → create appropriately named empty/active group **without** foreign jurisdiction rates; optional zero stub tax with `IsActive = false` forcing checklist. |
| Plan / modules | Honor requested plan. |
| Sample data | Still none. |

### 2.4 Fiscal year derivation algorithm

Given `BooksStartDate` = D and pattern P:

1. Determine FY start month M (Calendar → 1, AprMar → 4, JulJun → 7, Custom → `FiscalYearStartMonth`).  
2. Find the fiscal year period that contains D (start = most recent M/1 on or before D in the FY sense; end = day before next FY start).  
3. Set `IsActive = true` on that year.  
4. Optionally create the prior FY as inactive **only if** product wants comparative shells — **default v1: create only the current open FY** containing D.  
5. Enforce posting ≥ `BooksStartDate` in period validation (wire in Phase 4 if not already enforced).

Document edge cases in tests: D = Feb 15 calendar; D = May 1 Apr–Mar; custom month = 10.

### 2.5 Signup path

- If signup includes organization name: **do not** create workspace in `SignUp` until wizard completes **or** accept full payload from a post-OTP wizard call.  
- Preferred: SignUp creates user only; client routes to `/create-org` with name prefills.  
- If keeping create-on-signup: require same DTO fields / defaults and share `WorkspaceService.Create`.

### 2.6 Lock helpers (service stubs)

Add `CompanyAccountingPolicyService.IsLedgerFrozen(companyId)` → true if any posted JE exists for company. Used by Settings PATCH in Phase 4.

### 2.7 Acceptance

- [x] `POST /api/organizations` with full payload provisions correct FY dates (test matrix).  
- [x] Legacy minimal payload still works with documented defaults.  
- [x] Tax regime None vs SalesTax/VatGst seed differs; no PH/US rates forced.  
- [x] IFRS* + LIFO rejected (400) — LIFO has no enum value, so any undefined inventory costing value is rejected.  
- [x] CashBasis + Accrual rejected; Ifrs + Cash rejected.  
- [x] No journal lines after provision.  
- [x] Update `SignUpAndOrganizationTests` / backend tests accordingly.

**Status: Done on branch `feat/onboarding-phase-2-provision`, pending merge.**  
- `POST /api/organizations` accepts the hard-gate payload (enums as strings or numbers). Omitted fields use legacy defaults: IFRS for SMEs, Accrual (Cash when the framework is CashBasis), calendar year, books start = Jan 1 of the current year, no tax, Services, no inventory costing.  
- Policy is validated by `CompanyAccountingPolicyRules.Validate(CompanyProvisionOptions, today)` (400 `Invalid accounting policy`) and again in `WorkspaceService.Create`. The fiscal year is derived by `FiscalYearCalculator`; `CoaTemplateId` by `CoaTemplates.Resolve` (all ids seed the same CoA until Phase 5).  
- SalesTax / VatGst seed an inactive zero-rate placeholder tax so the owner must set real rates; None keeps the active NONE tax.  
- `CompanyAccountingPolicyService.IsLedgerFrozen` is a stub, not enforced until Phase 4.  
- Signup still creates its workspace with legacy defaults through the shared `WorkspaceService.Create`; policy fields are not yet on the signup DTO (Phase 3 routes signup to the wizard).

---

## Phase 3 — Web wizard UX

**Branch:** `feat/onboarding-phase-3-wizard`  
**Goal:** Production create journey per requirements §6.

### 3.1 Routing

- Add tenant-free route: `/create-org` (and keep `?action=new` redirect).  
- Entry points: `SelectOrg`, `OrgSwitcher`, post-signup redirect with query prefills (`?name=`).  
- Remove or retire cramped single-modal create as primary path (modal may deep-link to `/create-org`).

### 3.2 Component structure (suggested)

```
src/Web/src/pages/CreateOrg/
  CreateOrgPage.tsx          # shell, progress, submit
  steps/IntentStep.tsx
  steps/IdentityStep.tsx
  steps/MoneyTimeStep.tsx
  steps/ReportingStep.tsx
  steps/ReviewStep.tsx
  hooks/useCreateOrgWizard.ts
  lib/fyPreview.ts           # pure FY label/date helpers (unit-testable)
  lib/policyDefaults.ts
```

Reuse existing input/button/token classes from `SelectOrg` / Settings. No new UI library.

### 3.3 Behavior

- Client-side step validation before Next.  
- Slug availability: debounce existing check endpoint if present; else validate on submit.  
- Review calls `POST /api/organizations` with full DTO.  
- Loading state during provision.  
- Success → `setPersistedOrgSlug` → `/org/{slug}/dashboard`.  
- Plan cards map to `invoicing` / `accounting` / `enterprise`.  
- Reporting step hides inventory method unless industry is Product/Mixed or plan implies inventory.  
- Invoicing plan: collapse framework to advanced disclosure with defaults applied.

### 3.4 Motion

- Progress bar width by step index.  
- FY preview chip updates from `fyPreview.ts`.  
- Light success check (CSS), then navigate.

### 3.5 Acceptance

- [x] Keyboard-accessible step flow; mobile usable (native radio/inputs, focus moves to step heading; layout stacks on small screens — not yet checked on a device).  
- [x] Cannot submit without hard gates (`validateStep`/`validateAll`, unit-tested).  
- [x] Visual match with SelectOrg / Settings (same tokens, radii, Lucide).  
- [x] Signup no longer creates a company; after OTP it routes to `/create-org?name=&slug=`, which prefills Identity.  
- [x] Manual QA script below (to be run against a live API before merge).

**Status:** Done on branch, pending merge. `pnpm build` and `test/createOrg.test.mjs` (7 tests) pass; no browser run yet.

**Implementation notes**

- `SelectOrg` create modal removed; `?action=new` redirects to `/create-org`. `OrgSwitcher` links there.
- No slug-availability endpoint exists, so slug conflicts surface as a 409 on submit and return the user to the Identity step.
- Invitation signups are unchanged.

**Manual QA script**

1. Happy path: `/select-org` → Create → pick Accounting → name + slug → USD, books start 1 Jan current year, Calendar FY → IFRS for SMEs, no tax, Services → Create. Expect dashboard at `/org/{slug}/dashboard`.
2. Apr–Mar: choose Apr–Mar FY with books start 15 Feb; preview chip must read `FY2025-26 … Open`; confirm the created FY via API/Settings.
3. VAT posture: choose VAT/GST; confirm an inactive zero-rate placeholder tax exists after creation.
4. Invoicing plan: framework is collapsed under advanced disclosure with defaults; payload uses defaults.
5. Gates: leave name empty / Custom FY without month / duplicate slug and confirm Next or submit is blocked with a clear message.
6. Signup: new signup with an org name → OTP → wizard opens with the name prefilled; no company exists before submit.

---

## Phase 4 — Post-create checklist, Settings locks, posting guard

**Branch:** `feat/onboarding-phase-4-checklist-locks`  
**Goal:** Soft gate UX + immutability after first post.

### 4.1 Checklist UI

- Dashboard card / banner: “Get books ready” with items S1–S6 from requirements (hide opening balances until feature exists).  
- Persist dismissal: `OnboardingChecklistDismissedAt` or per-item JSON via `PATCH /api/organizations/current` or dedicated endpoint.  
- Deep links: Settings taxes, financials, general, members, CoA route.  
- Shorter list for invoicing plan.

### 4.2 Settings surfacing

- Show read-only or editable: framework, basis, FY pattern, books start, tax regime, inventory method.  
- If `IsLedgerFrozen`: currency, framework, statutory basis = read-only with explanation.  
- If books empty: allow edit with confirm dialog; PATCH updates settings + may adjust open FY only when safe (no posted activity).

### 4.3 Posting guard

- Enforce `entry_date >= BooksStartDate` (and open FY) in journal post path.  
- Tests: reject post before books start; accept on/after.

### 4.4 Acceptance

- [ ] Checklist appears for new orgs; dismiss persists.  
- [ ] Frozen policy fields cannot PATCH after first JE.  
- [ ] Books-start enforcement covered by automated tests.

---

## Phase 5 — CoA template families (quality hardening)

**Branch:** `feat/onboarding-phase-5-coa-templates`  
**Goal:** Industry/framework templates beyond a single undifferentiated seed.

### 5.1 Templates

| `CoaTemplateId` | Intent |
|---|---|
| `services-sme` | Lean P&L; minimal inventory accounts |
| `trading-wa` / `trading-fifo` | Inventory + COGS emphasis; method metadata only |
| `mixed-sme` | Union of services + trading essentials |
| `legacy-default` | Current `coa.csv` for backfilled companies |

Implementation options (choose one in PR):

- A) Multiple CSV files under `src/Api/Data/coa/`  
- B) One CSV with `Template` column filter  
- C) Base CSV + additive overlay accounts  

Control account codes in `GeneralLedgerSetting` must exist in the chosen template.

### 5.2 Acceptance

- [ ] Services template omits unused manufacturing depth (or leaves inactive).  
- [ ] Provisioning fails clearly if control account codes missing.  
- [ ] Document templates in `docs/accounting/coa-register.md` (short section).  
- [ ] Backend tests provision each template id once.

---

## Phase 6 — Polish, analytics, docs

**Branch:** `feat/onboarding-phase-6-polish`

- Empty/error copy pass; a11y audit.  
- Optional: localStorage draft of wizard state.  
- Telemetry events (if stack exists): `onboarding_step_view`, `onboarding_create_success`, `onboarding_create_fail`, `checklist_item_click`, `checklist_dismiss`.  
- Update `docs/MULTI_TENANCY_PLAN.md` cross-link; `docs/README.md` nav; Web README create-org note.  
- Resolve currency default doc mismatch (PHP vs USD) in product copy/defaults explicitly.

### Acceptance

- [ ] Docs linked and consistent with shipped behavior.  
- [ ] No open P0 UX bugs from Phase 3 QA list.

---

## Cross-cutting test plan

| Layer | Cases |
|---|---|
| Unit | Policy matrix; FY derivation; slug sanitize; defaults |
| API | Create full/legacy; conflict slug; framework/basis/tax/inventory rejects; provision FY; tax seed; freeze after JE |
| Web | Wizard step validation; submit payload shape; checklist dismiss (component or e2e if available) |
| Accounting integrity | After provision: zero JE; trial balance N/A; CoA tenant-scoped |

Run existing multi-tenancy and organization tests each phase.

---

## Suggested PR order and dependencies

```
Phase 1 domain
    └── Phase 2 API + provision
            └── Phase 3 wizard UI
            └── Phase 4 checklist + locks  (can parallelize with late Phase 3 after API stable)
                    └── Phase 5 CoA templates
                            └── Phase 6 polish
```

Phase 5 may be deferred if product accepts single CoA + stored `CoaTemplateId` for an interim release; requirements hard gates are still met by Phases 1–4.

---

## Rollout / risk

| Risk | Mitigation |
|---|---|
| Old mobile/clients break | Legacy payload defaults in Phase 2 |
| Wrong FY for existing orgs | Backfill only; do not rewrite FY rows without migration note |
| Users pick Cash by mistake | Card copy + confirm on Review; Settings editable while books empty |
| Long provision time | Keep seed lean; loading copy; consider async only if measured slow |
| D1/SQLite column drift | Follow established sync/create-table checklist in PR |

---

## Implementation checklist (summary)

- [x] Phase 1 — Domain + schema + policy rules  
- [x] Phase 2 — API + provisioning + tests  
- [x] Phase 3 — CreateOrg wizard UI  
- [ ] Phase 4 — Checklist + locks + books-start guard  
- [ ] Phase 5 — CoA template families  
- [ ] Phase 6 — Polish + docs  

Mark each phase **Done** in this file with a two-line summary when merged.

---

## Document history

| Date | Change |
|---|---|
| 2026-10-24 | Initial phased implementation plan |
| 2026-10-25 | Phase 2 marked done on branch |
| 2026-10-25 | Phase 3 wizard UI implemented on branch, pending merge and live QA |
| 2026-10-25 | Phase 1 marked done on branch; LIFO and FY-coherence checks deferred to Phase 2 |
