# P3 Implementation Plan: Replace Mode, Control Remapping, Owner Gate & Empty-Books Banner

This implementation plan defines the design and execution steps for **Phase 3 (P3)** of the Chart of Accounts import/export specification ([`docs/accounting/coa-import-export-requirements.md`](./coa-import-export-requirements.md) §§5–7, §9–10, §11 D2–D4/D9, §12 P3, §13 AC 5–7).

---

## 1. Scope & Core Objectives (P3 Only)

| Feature | Spec Reference | Description |
|---|---|---|
| **Empty-Books Detection** | §7.4, §7.5, §13 AC 5 | Determine if workspace has 0 posted transactions (no general ledger entries, no journal entries, no invoice lines, no payments). |
| **Owner Role Gate** | §7.4, §9, §11 D9, §13 AC 7 | Replace mode and Retire-Missing require workspace **Owner** role + `financials.accounts.manage`. Non-Owners receive HTTP 403 Forbidden. |
| **Replace Mode (Empty Books)** | §7.4, §7.6, §11 D2, §13 AC 5 | Replaces the company's chart from file. Atomic transaction. Old accounts without postings are removed only after required system controls are repointed. |
| **Control Remapping Grid** | §6.1, §7.5, §11 D4, §13 AC 5 | Every required system control role (`GeneralLedgerSetting` keys: AP, clearing, discounts, shipping, retained earnings, income summary; active `Taxes`; active `Banks`) must be remapped to an account code in the new chart before commit. |
| **Retire-Missing Option** | §7.4, §13 AC 6 | Checkbox (default **OFF**; Owner only). In Upsert mode, archives codes present in books but absent from file. Hard-blocks if an omitted account is a protected control account. Never deletes. |
| **Empty-Books Banner** | §7.5, §10 | Alert banner on `/financials/accounts`: *"Books not started — Replace chart is available."* Disables Replace mode with explanatory reason once transactions exist. |

> [!NOTE]
> **Out of Scope for P3**:
> - Format adapters for Xero / QBO (P4).
> - Opening balances / conversion journal import (separate epic per §8).
> - Deleting accounts in Replace mode if any transactions exist (always hard-blocked).

---

## 2. Schema Choice & Alignment (§5.4 / §12)

- **Persistence Choice**: We maintain **`ArchivedAt`** (`DateTime?` on `Account`), established in P1 & P2, matching `Customer.ArchivedAt`, `Vendor.ArchivedAt`, and `Item.ArchivedAt`.
- **DTO & CSV Alignment**:
  - DTO exposes `Archived` (boolean) and `ArchivedAt` (ISO timestamp).
  - CSV exposes `Status` column (`Active` vs `Archived`), defaulting to `Active`.
  - Retire-Missing sets `ArchivedAt = DateTime.UtcNow` for omitted non-protected accounts.

---

## 3. Architecture & Data Contracts

### 3.1 DTO Additions (`src/Dto/Financial/Account.cs`)

```csharp
public class ChartOfAccountsStatusDto
{
    public bool IsEmptyBooks { get; set; }
    public int PostedTransactionCount { get; set; }
    public string? ReplaceBlockedReason { get; set; }
    public bool CanReplace { get; set; }
    public List<ControlRoleMappingDto> RequiredControlRoles { get; set; } = new();
}

public class ControlRoleMappingDto
{
    public string RoleKey { get; set; } = string.Empty; // e.g. "PayableAccount", "SalesTaxPayable"
    public string RoleName { get; set; } = string.Empty; // Human-friendly label
    public string SourceType { get; set; } = string.Empty; // "GeneralLedgerSetting", "Tax", "Bank"
    public int? SourceEntityId { get; set; } // Tax.Id or Bank.Id if applicable
    public int? CurrentAccountId { get; set; }
    public string? CurrentAccountCode { get; set; }
    public string? CurrentAccountName { get; set; }
    public string? TargetAccountCode { get; set; } // Mapped code in new chart
    public bool IsRequired { get; set; } = true;
}

public class CoaReplaceRequestDto
{
    public string CsvContent { get; set; } = string.Empty;
    public string Mode { get; set; } = "replace";
    public bool RetireMissing { get; set; } = false;
    public Dictionary<string, string> ControlMappings { get; set; } = new(); // RoleKey -> TargetAccountCode
}
```

### 3.2 Service Layer (`IAccountService` & `AccountService`)

1. **`Task<ChartOfAccountsStatusDto> GetChartStatusAsync()`**:
   - Queries `_context.GeneralLedgerLines`, `_context.JournalEntryLines`, `_context.SalesInvoiceLines`, `_context.PurchaseInvoiceLines`, `_context.SalesReceiptHeaders`, `_context.VendorPayments`.
   - Returns whether books are empty (`PostedTransactionCount == 0`), along with currently mapped control roles.
2. **`Task<List<ControlRoleMappingDto>> GetRequiredControlRolesAsync()`**:
   - Gathers all control roles:
     - `GeneralLedgerSetting`: `PayableAccountId`, `PurchaseDiscountAccountId`, `GoodsReceiptNoteClearingAccountId`, `SalesDiscountAccountId`, `ShippingChargeAccountId`, `PermanentAccountId`, `IncomeSummaryAccountId`.
     - `Taxes`: Active tax sales & purchasing accounts.
     - `Banks`: Active bank GL accounts.
3. **`ValidateImportAsync` Extensions**:
   - When `mode == "replace"`:
     - Fails closed if books are not empty (`"Cannot replace chart of accounts because the company has posted transactions."`).
     - Fails closed if caller is not `Owner` (`"Replace mode requires workspace Owner role."`).
     - Validates control role mappings: every required control role must resolve to an active account code in the new file.
   - When `retireMissing == true`:
     - Fails closed if caller is not `Owner` (`"Retire Missing requires workspace Owner role."`).
     - Identifies all existing accounts missing from the file.
     - Hard-blocks if any missing account is protected (`"Cannot retire missing account '{code}' because it is a protected control account ({reason})."`).
4. **`CommitImportAsync` Extensions**:
   - For `mode == "replace"`:
     - Re-verifies Owner role and empty books inside `await _context.Database.BeginTransactionAsync()`.
     - Creates/wires all accounts from the new CSV file.
     - Repoints `GeneralLedgerSetting`, `Taxes`, and `Banks` to the newly created account IDs according to the validated mapping dictionary.
     - Removes old accounts that are not in the new chart (leaf accounts first, then parents).
     - Commits transaction atomically.
   - For `retireMissing == true` in Upsert:
     - Sets `ArchivedAt = DateTime.UtcNow` on all omitted non-protected accounts.

### 3.3 Controller Layer (`FinancialsController`)

- `GET /api/Financials/ChartStatus`: Gated with `financials.accounts.view`. Returns `ChartOfAccountsStatusDto`.
- `POST /api/Financials/ImportAccounts/Validate`: Accepts `mode=replace`, `retireMissing=bool`, and `controlMappings` in body or form data.
- `POST /api/Financials/ImportAccounts/Commit`: Enforces Owner verification via `ITenantContext.Role == CompanyMemberRole.Owner` when `mode == "replace"` or `retireMissing == true`.

---

## 4. Frontend UX Enhancements (`src/Web/src/pages/ChartOfAccounts.tsx`)

1. **Empty-Books Banner**:
   - Placed above the accounts table when `chartStatus.isEmptyBooks && canManage`.
   - Visual: Clean info/discovery card with icon and description:
     *"Books not started — Replace chart is available. You have no posted journal entries or transactions. You can safely replace the default chart with your own structure."*
   - CTA button: **Replace Chart** (opens Import Wizard with Replace mode pre-selected).
2. **Import Wizard Modal Updates**:
   - Mode Selection:
     - Card 1: **Append** (Safe Default)
     - Card 2: **Upsert**
     - Card 3: **Replace** (Available only if `isEmptyBooks` and user is `Owner`; otherwise disabled with explanation tooltip/badge).
   - **Retire-Missing Checkbox**:
     - Visible in Upsert mode if user is Owner (default **OFF**).
     - Label: *"Retire missing accounts (archive accounts not present in CSV)"*.
   - **Step 2a: Control Remapping Grid** (When mode is Replace):
     - Displays table of all required control accounts (`Role`, `Current Account`, `Target Account in New Chart`).
     - Dropdown select for each role populated with matching accounts from the parsed CSV.
     - Auto-matches by code or role name.
     - Shows validation status (e.g. green check if mapped, red badge if unmapped).
     - Must be 100% mapped before the user can proceed to Commit.

---

## 5. Implementation Task List

| Task | Component | Details |
|---|---|---|
| **T1** | DTOs | Add `ChartOfAccountsStatusDto`, `ControlRoleMappingDto`, and update `CoaImportPreviewDto` / `CoaImportCommitResultDto`. |
| **T2** | Backend Service | Implement `GetChartStatusAsync`, `GetRequiredControlRolesAsync`, Replace validation, control remapping checks, Retire-Missing checks, and atomic Replace commit in `AccountService.cs`. |
| **T3** | API Controller | Add `GET /api/Financials/ChartStatus` and update validate/commit actions in `FinancialsController.cs` with Owner role verification. |
| **T4** | Backend Tests | Create `test/AccountGo.BackendTests/Financial/ChartOfAccountsReplaceTests.cs` testing: 1) Replace blocked when posted transactions exist; 2) Replace blocked for non-Owners; 3) Replace blocked when controls unmapped; 4) Replace succeeds on empty books with remapping; 5) Retire-missing requires Owner; 6) Retire-missing archives omitted accounts; 7) Retire-missing blocks on protected controls. |
| **T5** | Web UI Banner | Add Empty-Books Banner and load `chartStatus` on `/financials/accounts`. |
| **T6** | Web UI Wizard | Update Import Wizard with Replace mode, Retire-Missing toggle, and Control Remapping step. |
| **T7** | Web Tests | Update `src/Web/test/chartOfAccounts.test.mjs` for banner and Replace mode rendering. |
| **T8** | Spec Doc | Mark §12 P3 as Done in `docs/accounting/coa-import-export-requirements.md`. |

---

## 6. File Touch List

- `src/Dto/Financial/Account.cs` (DTO definitions)
- `src/Api/Service/IAccountService.cs` (Interface additions)
- `src/Api/Service/AccountService.cs` (Replace, empty-books, control remapping, retire-missing)
- `src/Api/Controllers/FinancialsController.cs` (API endpoints and Owner verification)
- `test/AccountGo.BackendTests/Financial/ChartOfAccountsReplaceTests.cs` (Integration tests)
- `src/Web/src/pages/ChartOfAccounts.tsx` (Banner, Replace mode, Control Remapping grid)
- `src/Web/test/chartOfAccounts.test.mjs` (UI tests)
- `docs/accounting/coa-import-export-requirements.md` (Mark P3 Done)
