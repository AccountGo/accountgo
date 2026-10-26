# Chart of Accounts — Import, Export & Lifecycle Requirements

**Status:** Approved (product decisions locked §11)  
**Owner:** Product + Finance architecture  
**Route:** `/org/:orgSlug/financials/accounts`  
**Related:** [CoA register & templates](./coa-register.md) · [Company onboarding requirements](../onboarding/company-onboarding-requirements.md) · [General ledger redesign](./general-ledger-redesign.md)  
**Standards lens:** IFRS / IFRS for SMEs · US GAAP (private) · competitor practice (Xero, QuickBooks Online, NetSuite) · AccountGo double-entry engine (`saas-accounting-system`)

---

## 1. Purpose

Define how AccountGo manages the **chart of accounts (COA)** for:

1. **New workspaces** — template seed, optional full replace while books are empty.  
2. **Existing workspaces** — add, edit, archive, limited delete, and bulk import/export.  
3. **Two personas** — SME owners doing light edits, and firm accountants doing bulk structure work.

This document is **requirements and UX only**. Implementation sequencing is §12.

### 1.1 Naming (canonical)

| Layer | Term |
|---|---|
| UI | Chart of Accounts, account, header account, archive |
| URL | `/org/:orgSlug/financials/accounts` |
| API | `Financials/Accounts`, `AddAccount`, `UpdateAccount`, `DeleteAccount`, future import/export endpoints |
| Database / C# | `Account`, `AccountClass`, `GeneralLedgerSetting`, `CompanySetting.CoaTemplateId` |
| Files | AccountGo Structure CSV (`AccountGo-COA-v1`), Snapshot CSV |

Do not conflate **user account deletion** (`AccountDeletionService`) with **ledger account** lifecycle.

---

## 2. Goals and non-goals

### 2.1 Goals

1. **Structure first** — Import/export manage account structure; balances are never written by COA import.  
2. **Safe for live books** — Default bulk path is append/upsert; destructive modes are gated and previewed.  
3. **Archive over delete** — Retire accounts with history via archive; hard delete only when never used.  
4. **Protect control accounts** — Mapped system/control accounts cannot be archived or deleted until remapped.  
5. **Round-trip Structure CSV** — Export structure can be edited and re-imported under defined modes.  
6. **Serve both personas** — Simple row actions for SMEs; import wizard + preview for accountants.  
7. **Extensible formats** — Ship AccountGo CSV first; keep an adapter seam for Xero/QBO later.  
8. **Align with onboarding** — New workspaces keep industry template seed; no full COA editor inside the create wizard.

### 2.2 Non-goals (this epic)

- Opening balances, conversion dates, or trial-balance import inside the COA CSV (separate epic; see §8).  
- Per-account currency (company currency remains authoritative in v1).  
- Per-account default tax codes in v1 import (tax remains on tax setup / document lines).  
- Re-seeding an industry template over a company that already has posted journals.  
- Silent delete or archive of accounts missing from an import file (must be explicit opt-in).  
- Per-account CoA editing inside the workspace create wizard (already deferred in onboarding requirements).

---

## 3. Current state (baseline)

| Area | Today |
|---|---|
| UI | Read-only tree: search, class filter, hide zero balances, expand/collapse, refresh |
| Export | Client-side CSV: Code, Account, Class, Level, Normal balance, Header, Contra, Cash, Balance — **not round-trippable** |
| Import | None |
| CRUD API | `AddAccount`, `UpdateAccount`, `DeleteAccount` exist; page does not expose manage actions |
| Permissions | `financials.accounts.view`, `financials.accounts.manage` (manage unused on this page) |
| Account model | No archive/inactive flag; header vs postable derived from children (`CanPost`) |
| New workspace | Seed from `coa.csv` + `CoaTemplateId` (see [coa-register §6](./coa-register.md#6-chart-of-accounts-templates)) |
| Delete rules | Block if children, GL/JE history, or hard item/category use; clears some config FKs |

---

## 4. Personas and journeys

### 4.1 SME owner / bookkeeper

- Skim seeded chart after create (onboarding checklist).  
- Rename bank/cash; add a few expense or income accounts.  
- Archive unused leaves.  
- Export Snapshot for the accountant.  
- Rarely uses Replace or Retire-missing.

### 4.2 Firm accountant / advisor

- Export Structure; edit in spreadsheet; Append or Upsert.  
- Empty books: Replace chart from a firm template, remap controls.  
- Prefer Archive; use Delete only for never-posted mistakes.  
- Expect preview diffs and downloadable error reports.  
- Later: map legacy Xero/QBO exports via adapters (not v1 parsers).

### 4.2 Workspace lifecycle (CFO order)

```
Create workspace
  → industry template seed (unchanged)
  → optional Replace (empty books only) OR light manual edit
  → optional opening balances (separate epic)
  → live operations: Append / Upsert / Archive
```

After the **first posted journal entry**, Replace is unavailable. Customize accounts; do not re-seed the template (consistent with [onboarding immutability](../onboarding/company-onboarding-requirements.md#45-immutability-and-change-control)).

---

## 5. Account lifecycle rules

### 5.1 Allowed

| Action | Rule |
|---|---|
| Add | Unique `AccountCode` per company; class required; optional parent |
| Edit name / description | Anytime |
| Reparent | Anytime if no cycles; parent becomes header (non-postable) when it gains children |
| Recode | Allowed if new code unique; soft-warn after first posting |
| Set Contra / Cash | Allowed with validation (cash typically under cash/bank hierarchy) |
| Archive | Default retire path; hides from new-transaction pickers; retained on historical reports |
| Delete | Only if: never in GL/JE (or other transaction history), no children, not protected |
| Hierarchy depth | **Unlimited**; cycle detection required |

### 5.2 Forbidden / hard-block

- Delete accounts with transaction history.  
- Archive or delete **protected** accounts while still mapped (§6).  
- Post to header (parent) accounts.  
- Change **account class** after the account has posting history.  
- Re-seed industry template over a posted company.  
- Import that deletes or archives missing rows without explicit **Retire missing** confirmation.  
- Import that writes balances or opening balances.  
- Duplicate account codes within a company.

### 5.3 Soft-warn

- Account code looks inconsistent with class recommended ranges (see [coa-register §2](./coa-register.md#2-chart-of-accounts-coa-structure) and seeded `1xxxx`… style).  
- Recoding after first posting.  
- Upsert that renames a protected/control account.  
- Very deep trees (guidance only; no hard cap).

### 5.4 Archive vs delete

| | Archive (default) | Delete |
|---|---|---|
| History | Kept | N/A (no history allowed) |
| Pickers | Hidden from new docs | Removed |
| Reports | Historical lines still resolve | Account gone |
| Restore | Supported (unarchive) | Not applicable |
| UI default | Primary action | Secondary; confirm; offer Archive first when eligible for both |

### 5.5 Account codes and classes

- **Uniqueness:** `(CompanyId, AccountCode)` unique.  
- **Class is authoritative** for financial statement placement and normal balance.  
- **Numbering:** Templates and docs recommend hierarchical numeric codes aligned with class ranges; **not hard-enforced** so firm charts and migrations can use their schemes. Soft-warn on class/range mismatch.  
- Codes may be alphanumeric; trim; case-sensitive uniqueness as stored (normalize consistently in import validation).

---

## 6. Protected (control / system) accounts

An account is **protected** while any of the following hold:

1. Referenced by `GeneralLedgerSetting` (e.g. payable, purchase/sales discount, goods-received clearing, shipping, permanent / retained earnings, income summary, and any future control mappings).  
2. Referenced as a tax sales or purchasing account.  
3. Linked as a bank’s GL account.  
4. Has child accounts.  
5. Explicitly flagged as system/control in product configuration (if introduced).

### 6.1 Rules

- Cannot **Archive** or **Delete** while protected. UI must explain: remap in Settings (or move children) first.  
- **Replace** (empty books) must include a **control remapping** step: every required control role must resolve to a code present in the new chart before commit.  
- Delete/update paths must **never silently null** required control foreign keys.  
- Protected accounts may be **renamed** (soft-warn on Upsert).

Best practice matches Xero locked system accounts and QBO control-account care: structure stays editable; integrity of subledger/control links does not.

---

## 7. Import and export

### 7.1 Two CSV contracts

| Contract | Direction | Purpose |
|---|---|---|
| **Structure** (`AccountGo-COA-v1`) | Export + Import | Round-trip chart structure |
| **Snapshot** | Export only | Audit / review with balances |

Import of a file that includes a **Balance** (or opening-balance) column must be **rejected** with a clear message pointing to the future conversion epic.

### 7.2 Structure columns (`AccountGo-COA-v1`)

| Column | Required | Notes |
|---|---|---|
| `AccountCode` | Yes | Unique per company |
| `AccountName` | Yes | |
| `ParentAccountCode` | No | Empty = root under class; must exist or appear earlier/same file |
| `AccountClass` | Yes | `1`–`6` or stable names: Assets, Liabilities, Equity, Revenue, Expenses, Closing accounts |
| `Contra` | No | `TRUE`/`FALSE`; default false |
| `Cash` | No | `TRUE`/`FALSE`; default false |
| `Description` | No | |
| `Status` | No | `Active` / `Archived`; default Active |

**Reserved (ignored in v1, may warn “unsupported”):** `DefaultTaxCode`, `Currency`.

**Format marker:** file must identify `AccountGo-COA-v1` (metadata row, header comment, or agreed first-column sentinel) so future adapters can coexist.

### 7.3 Snapshot columns (export only)

Structure columns plus: `Level`, `NormalBalance`, `Header` (yes/no), `Balance`.  
Balances are informational; signed per existing COA page conventions.

### 7.4 Import modes

| Mode | Availability | Behavior |
|---|---|---|
| **Append** | Always (with manage permission) | Create rows whose codes do not exist; conflict policy on duplicates: skip or error (chosen in preview) |
| **Upsert** | Always (with manage permission) | Create missing; update name, parent, description, contra, cash, status for existing; **do not** change class if account has history; **never** delete |
| **Replace** | **No posted JE** + Owner (+ manage) | Replace company chart from file; control remapping required; atomic transaction |
| **Retire missing** | Explicit checkbox, **default off**; Owner (+ manage) | Archive codes present in books but absent from file; never hard-delete via import |

Parents must be creatable in dependency order (parents before children, or multi-pass resolution). Cycles fail validation.

### 7.5 Import UX (wizard)

1. Download sample Structure template.  
2. Upload CSV.  
3. Detect format version (AccountGo v1; unknown → reject or “adapter not installed”).  
4. Validate rows.  
5. **Preview diff:** create / update / skip / archive (if retire-missing) / error.  
6. For Replace: control remapping grid.  
7. Confirm → commit atomically.  
8. Download error/skip report.

Empty-books banner on the COA page: “Books not started — Replace chart is available.”  
After first post: Replace disabled with reason.

### 7.6 Export UX

- **Export Structure** — default for round-trip / firm templates.  
- **Export Snapshot** — current balances (evolves today’s export).  
- Prefer **server-side** generation for consistency, tenancy, and audit (client-only export may remain interim only during early phases).

### 7.7 Future format adapters

- v1 ships **AccountGo Structure only**.  
- Design import pipeline behind a **format adapter** interface (`Detect` → `MapToCanonical` → shared validate/commit).  
- Xero / QuickBooks Online column maps are **documented stubs** in a later phase; full parsers are not required to close this epic.

---

## 8. Opening balances (out of scope; best-practice boundary)

Per competitor practice (QBO, Xero, NetSuite) and migration guidance:

1. Finalize **COA structure** first.  
2. Then load **opening balances** via a dedicated conversion journal / trial-balance import at `BooksStartDate`.  
3. Never mix balances into Structure import.

Track as a **separate epic**. COA import must reject balance-bearing files.

---

## 9. Permissions

| Action | Permission / role |
|---|---|
| View chart, Snapshot export | `financials.accounts.view` |
| Add, Edit, Archive, Unarchive, Structure export, Append/Upsert import | `financials.accounts.manage` |
| Delete (when eligible) | `financials.accounts.manage` + confirm |
| Replace, Retire missing | **Owner** (or equivalent elevated workspace role) **and** `financials.accounts.manage` |

Module gate: `financials` (consistent with existing Financials mutations).

---

## 10. UI requirements (`/financials/accounts`)

Retain: hierarchy tree, search, class chips, hide zero balances, keyboard treegrid behavior, ledger deep-link on balances where permitted.

Add for manage permission:

- **Add account**  
- Row actions: Edit · Archive / Unarchive · Delete (only when eligible; Archive preferred)  
- Badges: Contra, Cash, Header, Archived, Protected/Control  
- **Import** wizard entry  
- **Export** menu: Structure | Snapshot  
- Empty-books Replace affordance  
- Block reasons when Archive/Delete unavailable  

Progressive disclosure: SME sees Add / Archive / Export; Import and Replace sit as explicit actions without cluttering the default viewport.

---

## 11. Locked product decisions

| # | Decision |
|---|---|
| D1 | Personas: **both** SME and firm accountant on one page (progressive disclosure). |
| D2 | Import modes: **Append**, **Upsert**, and **Replace** (empty books only). |
| D3 | **Archive** is the default retire action; delete only when never used and unprotected. |
| D4 | Control accounts: remappable in Settings; protected while mapped; Replace requires remapping step. |
| D5 | Codes unique per tenant; class authoritative; numeric ranges **recommended**, soft-warn only. |
| D6 | Hierarchy depth: **unlimited**, with cycle checks. |
| D7 | Format: **AccountGo-COA-v1** first; adapter seam for Xero/QBO later. |
| D8 | Opening balances: **separate epic**; reject balance columns on COA import. |
| D9 | Replace / Retire-missing: **Owner** + manage; day-to-day manage for Append/Archive. |
| D10 | v1: **no** per-account currency or tax defaults in CSV (reserved columns ignored). |

---

## 12. Implementation phases

| Phase | Deliverable | Done |
|---|---|---|
| **P0** | Structure export + sample template + `AccountGo-COA-v1` marker; Snapshot export (balances) | |
| **P1** | `Archived` (or equivalent) on `Account`; filter pickers; Add / Edit / Archive / Delete UI; protected-account messaging | |
| **P2** | Append + Upsert import with validate/preview/commit; harden delete so required controls are never nulled silently | |
| **P3** | Replace (empty books) + control remapping; Owner gate; empty-books banner | |
| **P4** | Format-adapter interface + stub docs for Xero/QBO column maps | |
| **Later** | Opening-balance / conversion import epic | |

Mark a phase **Done** in this table with a two-line summary when that phase ships.

---

## 13. Acceptance criteria (epic-level)

1. Structure export downloads `AccountGo-COA-v1` without balances and re-imports cleanly under Append on a fixture company.  
2. Snapshot export includes balances and is rejected by the import API/UI.  
3. Archived accounts do not appear in new journal/invoice account pickers but remain on historical GL views.  
4. Delete is refused for posted, child, or protected accounts with actionable errors.  
5. Replace succeeds only with zero posted JEs and complete control mappings; fails closed otherwise.  
6. Retire-missing is off by default and requires Owner confirmation.  
7. Users without manage cannot mutate; non-Owners cannot Replace.  
8. No COA import path writes GL balances.

---

## 14. Open implementation notes (non-blocking)

- Exact persistence name: `IsActive` vs `ArchivedAt` — implementer’s choice; behavior per §5.4.  
- Whether interim P0 keeps client-side Snapshot until server export lands.  
- Full list of “required” control roles for Replace validation should be derived from provisioning + `GeneralLedgerSetting` + tax/bank links and kept in sync with [coa-register §3](./coa-register.md#3-general-ledger-settings-mapping).  
- Alphanumeric code collation and normalization rules to document in API OpenAPI examples when built.
