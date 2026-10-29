# General Ledger Redesign: `/org/:orgSlug/financials`

**Target route:** `/org/ws-one/financials` (and every tenant slug)  
**Audience:** Implementation (frontend + API)  
**Status:** Ready for implementation — no schema migration required for Phase 1–2  
**Related:** [COA & posting rules](coa-register.md) · [Double-entry tests](double-entry-tests.md) · [Reports audit / brief](financial-reports-audit.md)

---

## 1. Executive verdict

The current Financials page does **not** serve as a general ledger.

It mounts `PlaceholderView`, loads **trial balance by account**, and shows COA-count metrics. That duplicates **Reports** (TB / P&L / Balance Sheet) and ignores the books of record: **posted GL transactions**.

**Correct purpose of Financials:** a **journal / general ledger register** — chronological posted transactions, each expandable to balanced debit and credit lines, with links back to the source document.

| Surface | Job | Unit of work |
| :--- | :--- | :--- |
| **Financials (this redesign)** | Operational books & audit trail | One posted `GeneralLedgerHeader` + lines |
| **Chart of Accounts** `/financials/accounts` | Master data | Account codes, hierarchy, balances |
| **Reports** `/reports` | Period close & statements | Aggregates (TB, P&L, BS) |
| **Settings → Accounting** | Configuration | Fiscal years, cash banks, GL settings |

Do **not** re-present trial balance or COA on the Financials landing page.

---

## 2. Current state (codebase facts)

| Item | Location / behaviour |
| :--- | :--- |
| Route | `src/Web/src/App.tsx` → `path: 'financials'` → `<PlaceholderView />` |
| UI | `src/Web/src/pages/PlaceholderView.tsx` (`currentSection === 'financials'`) |
| Data loaded | `GET /api/Financials/trialbalance`, `GET /api/Financials/Accounts` |
| Primary action | “New Journal Entry” — **no handler** |
| Row action | “View” — **no handler** |
| Nav | Sidebar “Financials” badge `Ledger`; permission `financials.accounts.view` |
| COA page | Already exists: `/financials/accounts` → `ChartOfAccounts.tsx` |
| Reports page | Already exists: `/reports` → `Reports.tsx` (TB / P&L / BS) |

### Domain already correct

- **`GeneralLedgerHeader`** — `Date`, `DocumentType`, `Description`, `SourceDocumentId`, `ReversedGeneralLedgerHeaderId`, `GeneralLedgerLines`
- **`GeneralLedgerLine`** — `AccountId`, `DrCr`, `Amount`
- Auto-posting from sales, purchases, inventory, voids, manual journals (see `coa-register.md`)
- Append-only posted lines; corrections via **reversing** entries (`CreateReversingEntry`)

### API already partially built

| Endpoint | Role | Gaps for this redesign |
| :--- | :--- | :--- |
| `GET /api/Financials/GeneralLedger` | Groups lines by `TransactionNo` (= header Id) | Header thin; date filter unused; description / source / reversal omitted; no permission attribute |
| `GET /api/Financials/JournalEntries` | Manual vouchers (draft + posted) | Separate from posted GL register; keep out of Phase 1 default list |
| `GET …/JournalEntry`, `POST SaveJournalEntry`, `POST PostJournalEntry` | Manual JE lifecycle | Phase 3 UI only |
| `GetGeneralLedgerHeader` / `GetGeneralLedgerLines` | Service methods | No dedicated “header detail” HTTP shape for the SPA |

`MasterGeneralLedger` in `FinancialService` builds debit/credit line rows and includes `DocumentType`, but `BuildMasterGeneralLedger` in `FinancialsController` only sets `GroupId` on parents and drops document type / totals / description on the response tree.

---

## 3. Target UX

### 3.1 Page chrome

- Title: **General Ledger** (subtitle: Journal register / posted transactions)
- Use `PageHeader` + patterns from `Reports.tsx` / `Invoices.tsx` (not `PlaceholderView`)
- Permission gate: `financials.journal.view` (fallback: deny with `PermissionDeniedCard`)
- Module gate: unchanged (`RequiresModule module="financials"`)

### 3.2 Filters (required)

| Control | Behaviour |
| :--- | :--- |
| Period | Same presets as Reports where practical: This Month (default), Quarter-to-Date, Year-to-Date, All Time, Custom. Send `from` / `to` as ISO local bounds (`T00:00:00` / `T23:59:59.9999999`) |
| Document type | Optional multi or single select from `DocumentTypes` that appear in GL (SalesInvoice, SalesReceipt, PurchaseInvoice, PurchaseInvoicePayment, JournalEntry, InventoryAdjustment, …). Omit pure workflow types that never post if unused |
| Search | Client or server: transaction #, description contains, account code/name |
| Account | Optional `accountCode` — shows only transactions that touch that account (still expand full balanced entry) |

### 3.3 Transaction list (collapsed row)

One row per **posted** `GeneralLedgerHeader`, newest date first (then Id desc).

| Column | Source |
| :--- | :--- |
| Date | `GeneralLedgerHeader.Date` |
| Transaction # | `GeneralLedgerHeader.Id` |
| Type | Friendly label from `DocumentType` |
| Description | `Description` (empty → em dash) |
| Source | Link when `SourceDocumentId` + type resolve (see §5.3) |
| Debit total | Σ line debits |
| Credit total | Σ line credits (must equal debit total) |
| Flags | “Reversal” if `ReversedGeneralLedgerHeaderId` set; link to original txn # |

Expand/collapse chevron on the row. Expanding does **not** require a second fetch if lines are included in the list payload (preferred for Phase 1). Optional later: lazy-load lines by header Id.

### 3.4 Expanded lines

| Account code | Account name | Debit | Credit |
| :--- | :--- | ---: | ---: |
| … | … | … | … |
| | **Totals** | **Σ Dr** | **Σ Cr** |

- Amounts in tenant currency formatting (`currencySymbol` / existing helpers)
- Debit column only when `DrCr == Dr`; credit only when `Cr`
- Footer must show equality; if ever unequal, surface a visible error state (should not occur if engine validation holds)

### 3.5 Actions

| Action | Phase | Notes |
| :--- | :---: | :--- |
| Expand / collapse | 1 | Core |
| Open source document | 1 | When mappable |
| Refresh | 1 | Reload list |
| Export CSV (register) | 2 | One row per header; optional second sheet/section for lines |
| New Journal Entry | 3 | Requires `financials.journal.create`; post requires `financials.journal.post` |
| Reverse entry | Later | Uses `CreateReversingEntry`; needs product rules + permission |

Dead buttons are forbidden: do not show “New Journal Entry” until Phase 3 wires it.

### 3.6 Explicit non-goals for this page

- Trial Balance, P&L, Balance Sheet → stay on **Reports**
- COA tree / account master → stay on **`/financials/accounts`**
- Cash vs accrual statement toggle → Reports only (GL register is always the posted accrual ledger unless a future cash-basis ledger view is specified)
- Editing or deleting posted lines → never; corrections = reversing journals

---

## 4. Information architecture & navigation

1. Replace `PlaceholderView` on `financials` with a dedicated page component.
2. Keep `/financials/accounts` as Chart of Accounts (breadcrumb: Financials → Chart of Accounts).
3. Sidebar: keep path `/financials`; prefer label **General Ledger** or keep **Financials** with badge `Ledger`. Change required permission for the nav item from `financials.accounts.view` to **`financials.journal.view`** so ledger viewers need journal view (COA remains accounts.view on its own route).
4. Settings link “View Full Chart of Accounts” unchanged.
5. Optional Phase 4: from Reports TB account row → navigate to Financials with `?accountCode=…&from=…&to=…`.

---

## 5. Backend contract

### 5.1 Preferred list endpoint

**Evolve** `GET /api/Financials/GeneralLedger` (or add `GET /api/Financials/GeneralLedger/Transactions` and deprecate the awkward tree shape).

**Query parameters**

| Param | Type | Rules |
| :--- | :--- | :--- |
| `from` | `DateTime?` | Inclusive start; optional |
| `to` | `DateTime?` | Inclusive end; required when UI always sends an as-of/end date (match Reports validation: `from` must not be after `to`) |
| `accountCode` | `string?` | If set, return headers that have at least one line on that account; **include all lines** of those headers |
| `transactionNo` | `int?` | Exact header Id |
| `documentType` | `DocumentTypes?` or string | Optional filter |

**Authorization**

- Module: `financials` (already on controller; keep write endpoints financials-only)
- Permission: `[RequirePermission(SystemPermissions.FinancialsJournalView)]`

**Response:** Paged JSON object (not the legacy GroupId tree). Default `take=100`, maximum `take=500`, `skip>=0`.

```json
{
  "items": [
    {
      "id": 42,
      "date": "2026-03-15T00:00:00",
      "documentType": "SalesInvoice",
      "documentTypeLabel": "Sales Invoice",
      "description": "Invoice INV-5",
      "sourceDocumentId": 5,
      "sourcePath": "/sales/5",
      "reversedGeneralLedgerHeaderId": null,
      "totalDebit": 10000.00,
      "totalCredit": 10000.00,
      "lines": [
        {
          "id": 101,
          "accountId": 12,
          "accountCode": "10120",
          "accountName": "Accounts Receivable",
          "debit": 10000.00,
          "credit": 0
        },
        {
          "id": 102,
          "accountId": 40,
          "accountCode": "40100",
          "accountName": "Sales A/C",
          "debit": 0,
          "credit": 10000.00
        }
      ]
    }
  ],
  "totalCount": 1,
  "skip": 0,
  "take": 100
}
```

Date filters: inclusive. A date-only / midnight `to` expands to the last tick of that calendar day (same intent as the SPA’s `T23:59:59.9999999`). Manual journal drafts may split multiple lines to the same account.

**Implementation notes**

- Authoritative source: `GeneralLedgerHeader` + `GeneralLedgerLine` + `Account` (tenant-scoped).
- Apply `from` / `to` on `GeneralLedgerHeader.Date` in the query (fix commented-out filter in `MasterGeneralLedger`).
- Compute totals server-side; assert debit ≈ credit within money rounding (reject or flag if not).
- Sort: `Date` descending, then `Id` descending.
- Prefer DB filtering over `.AsEnumerable()` for production scale; Phase 1 may refactor `MasterGeneralLedger` or replace it with a dedicated method `GetGeneralLedgerTransactions(...)`.
- Include `Description` from the header (today missing from Master GL DTO mapping).
- Include `SourceDocumentId` and `ReversedGeneralLedgerHeaderId`.

### 5.2 Optional detail endpoint

`GET /api/Financials/GeneralLedger/{id}` — single header + lines (same DTO). Useful for deep links and Phase 3+; optional if list always embeds lines.

### 5.3 Source document deep links

Map `DocumentType` + `SourceDocumentId` to SPA paths under `/org/:slug/…`:

| `DocumentTypes` | SPA path (relative to org) |
| :--- | :--- |
| `SalesInvoice` | `/sales/{id}` |
| `SalesReceipt` | Prefer invoice detail if receipt ties to invoice; else omit link until receipt UI exists |
| `PurchaseInvoice` | `/purchases/{id}` |
| `PurchaseInvoicePayment` | `/purchases/{billId}` when payment resolves to a bill |
| `JournalEntry` | Phase 3 journal detail route, or omit until then |
| `InventoryAdjustment` | `/items/inventory` or item detail when available |
| Void / reversal headers | Same type as original; `ReversedGeneralLedgerHeaderId` points at original GL Id |

Server may return `sourcePath` as org-relative path **without** the `/org/{slug}` prefix so the client prefixes the slug. If mapping is unknown, omit `sourcePath` and show type + id as text only.

### 5.4 Manual journals (Phase 3 — contract reminder)

Reuse existing:

- `GET /api/Financials/JournalEntries`
- `GET /api/Financials/JournalEntry?id=`
- `POST /api/Financials/SaveJournalEntry` → `financials.journal.create`
- `POST /api/Financials/PostJournalEntry` → `financials.journal.post`

Posted manual journals already create `GeneralLedgerHeader` with `DocumentTypes.JournalEntry` and appear in the Phase 1 register. Unposted drafts belong on a separate “Journal entries” tab or sub-route, **not** mixed into the default posted GL list without a Draft filter.

### 5.5 Leave unchanged

- Reporting endpoints (`trialbalance`, `IncomeStatement`, `BalanceSheet`) and `Reports.tsx`
- Account CRUD / `ChartOfAccounts.tsx`
- Posting logic in Sales / Purchasing / Inventory services
- No D1 / SQLite schema change for Phase 1–2 (`SourceDocumentId` / reversal columns already exist)

---

## 6. Frontend implementation brief

### 6.1 Files to add / change

| File | Change |
| :--- | :--- |
| `src/Web/src/pages/GeneralLedger.tsx` | **New** — register UI |
| `src/Web/src/App.tsx` | Mount `GeneralLedger` on `financials` instead of `PlaceholderView` |
| `src/Web/src/components/Sidebar.tsx` | Label / permission for Financials link |
| `src/Web/src/components/MobileBottomNav.tsx` | Match sidebar permission if applicable |
| `src/Api/Controllers/FinancialsController.cs` | New/updated GL transactions response + permission + date validation |
| `src/Services/Financial/FinancialService.cs` (+ interface) | `GetGeneralLedgerTransactions` (or fixed `MasterGeneralLedger`) |
| `src/Dto/Financial/…` | New DTO e.g. `GeneralLedgerTransaction` / line DTO (prefer clear names over overloading `MasterGeneralLedger`) |
| Tests | API: date filter, grouping, balance, permission, tenant scope. Web: optional component/query helper tests |

Preserve existing Tailwind tokens and `PageHeader` patterns. No new npm packages without approval.

### 6.2 Suggested TypeScript types

```ts
export type GlDocumentType =
  | 'SalesInvoice'
  | 'SalesReceipt'
  | 'PurchaseInvoice'
  | 'PurchaseInvoicePayment'
  | 'JournalEntry'
  | 'InventoryAdjustment'
  | string;

export interface GlLine {
  id: number;
  accountId: number;
  accountCode: string;
  accountName: string;
  debit: number;
  credit: number;
}

export interface GlTransaction {
  id: number;
  date: string;
  documentType: GlDocumentType;
  documentTypeLabel: string;
  description: string | null;
  sourceDocumentId: number | null;
  sourcePath: string | null;
  reversedGeneralLedgerHeaderId: number | null;
  totalDebit: number;
  totalCredit: number;
  lines: GlLine[];
}
```

Money: treat API decimals as major units (consistent with existing invoice/report pages that do not use minor-units for GL). Match whatever the controller currently returns for other financial DTOs; do not invent a second unit system on this page.

### 6.3 Accessibility

- Expandable rows: `aria-expanded`, keyboard toggle (Enter/Space)
- Table captions / column headers for debit and credit
- Status text for empty period and load errors (same tone as Reports)

---

## 7. Phased delivery

### Phase 1 — Posted GL register (MVP) — **implement first**

1. Backend transaction list DTO + date filters + `FinancialsJournalView`
2. Replace PlaceholderView with `GeneralLedger.tsx`
3. Expandable debit/credit lines, period filters, search, source links where mapped
4. Sidebar permission update
5. Automated tests for API contract and balance

**Exit criteria:** Accountant can open `/org/ws-one/financials`, see posted transactions for a period, expand any row, verify Dr = Cr, and open linked invoices/bills.

### Phase 2 — Export & polish

- CSV export of headers (and optional line detail)
- Document-type filter
- Deep-link query params `accountCode`, `from`, `to`, `transactionNo`
- Empty / error / out-of-balance edge states

### Phase 3 — Manual journal entry UI

- Create / edit draft JE, post to GL
- Optional drafts list (tab or `/financials/journals`)
- Do not enable the primary CTA until this phase ships

### Phase 4 — Report → ledger drill-down

- From Reports TB (or COA) navigate to GL filtered by account + period

### Out of scope / ignore for this work

- In-progress cash-basis branch behaviour on Financials page
- Aging schedules, bank reconciliation UI, PDF statements
- Changing auto-posting rules

---

## 8. Acceptance checklist

- [x] `/org/:orgSlug/financials` no longer renders trial-balance-by-account or PlaceholderView metrics
- [x] Default view is posted GL transactions for the current month (or active FY month preset)
- [x] Each row expands to show account lines with Debit and Credit columns and matching totals
- [x] `from` / `to` change the API query and the visible set
- [x] `financials.journal.view` enforced on API and page
- [x] Tenant isolation: ws-one never sees another company’s headers
- [x] Source links work for Sales Invoice and Purchase Invoice at minimum
- [x] Reversal relationship visible when `ReversedGeneralLedgerHeaderId` is set
- [x] COA remains at `/financials/accounts`; Reports unchanged for TB / P&L / BS
- [x] `dotnet test` and `pnpm --dir src/Web build` pass
- [x] No posted line update/delete introduced

---

## 9. Verification scenarios (manual)

Using workspace `ws-one` (or equivalent seeded tenant):

1. Post or use existing sales invoice → GL row type Sales Invoice → expand → AR Dr / Revenue (and tax) Cr.
2. Void invoice → original + reversing GL rows; reversal flag / link present.
3. Bill + vendor payment → two (or more) GL transactions; payment touches cash/bank and AP.
4. Filter period that excludes a known posting → row disappears; widen period → returns.
5. Filter `accountCode` for cash account → only transactions that touch cash; expand still shows full entry.
6. User without `financials.journal.view` → permission denied; COA still reachable if they have `financials.accounts.view`.

Align automated coverage with guarantees in [double-entry-tests.md](double-entry-tests.md) (entry balance, void = reverse).

---

## 10. Implementation order (suggested PR sequence)

1. **API:** DTO + service method + controller + permission + backend tests  
2. **Web:** `GeneralLedger.tsx` + `App.tsx` route swap + sidebar permission  
3. **Follow-ups:** Phase 2 export / Phase 3 journals as separate PRs  

Create a dedicated branch (e.g. `feat/general-ledger-register`) from current mainline; do not couple this work to the cash-basis feature branch.

---

## 11. Decision log

| Decision | Choice | Rationale |
| :--- | :--- | :--- |
| Primary entity on Financials | Posted `GeneralLedgerHeader` | Books of record; includes auto-postings, not only manual JEs |
| Expand pattern | Inline expand lines | Matches controller review workflow; user request |
| Trial balance on Financials | Remove | Belongs on Reports |
| COA on Financials landing | Remove | Exists at `/financials/accounts` |
| Default list includes drafts | No | Drafts are not books until posted |
| Schema migration | None for MVP | Header/line/source/reversal columns already present |
