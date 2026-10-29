# Phase 4 (P4) Implementation Plan: Format-Adapter Interface & Documented Stubs

**Status:** Done (implemented)

## 1. Context & Objectives
- **Reference Spec:** `docs/accounting/coa-import-export-requirements.md` §7.5, §7.7, §11 D7, §12 P4.
- **Scope:**
  - Build the extensible format adapter interface:
    - `ICoaFormatAdapter` (`FormatKey`, `DisplayName`, `IsInstalled`, `CanHandle`, `MapToCanonical`)
    - `ICoaFormatAdapterRegistry` (discovers adapters, detects format from CSV content, routes to adapter or returns actionable "adapter not installed" error)
  - Refactor `AccountService.ValidateImportAsync` and `CommitImportAsync` to use `ICoaFormatAdapterRegistry` (`Detect` → `MapToCanonical` → shared validation & atomic commit).
  - Implement `AccountGoV1CoaFormatAdapter` as the primary production adapter for `# AccountGo-COA-v1`.
  - Implement detection stubs for `XeroCoaFormatAdapter` and `QboCoaFormatAdapter` (`IsInstalled = false`) that detect their respective CSV headers and return standard "adapter not installed / see documentation" messages.
  - Create `docs/accounting/format-adapters.md` with complete column mapping specifications, account class translations, and integration guidance for Xero and QuickBooks Online.
  - Add backend integration tests verifying adapter detection, rejection of unknown formats, stub detection for Xero & QBO, and round-trip AccountGo v1 processing.
  - Mark **P4** as **Done** in `docs/accounting/coa-import-export-requirements.md` §12.

---

## 2. Architecture & Design

### 2.1 File & Namespace Structure
- **Core / Service Interfaces (`src/Api/Service/Import/`):**
  - `ICoaFormatAdapter.cs`: Defines adapter contract.
  - `CoaCanonicalRow.cs`: Canonical data model produced by adapters.
  - `CoaAdapterResult.cs`: Container for parsed rows, file errors, and notices.
  - `ICoaFormatAdapterRegistry.cs`: Registry and detection seam.
- **Implementations (`src/Api/Service/Import/`):**
  - `CoaFormatAdapterRegistry.cs`: Scans lines for markers and column signatures; routes to matching adapter or yields standard error.
  - `AccountGoV1CoaFormatAdapter.cs`: Encapsulates AccountGo-COA-v1 parsing, reserved column warnings, and row extraction.
  - `XeroCoaFormatAdapter.cs`: Stub adapter detecting Xero headers (`*Account Code`, `*Account Name`, `*Account Type`).
  - `QboCoaFormatAdapter.cs`: Stub adapter detecting QuickBooks Online headers (`Account`, `Type`, `Detail Type`).
- **Documentation (`docs/accounting/`):**
  - `format-adapters.md`: Technical documentation of adapter seam, plus complete Xero and QuickBooks Online column & account class mapping matrices.
- **Backend Tests (`test/AccountGo.BackendTests/Financial/`):**
  - `ChartOfAccountsFormatAdapterTests.cs`: Tests adapter detection, stub behaviors, and canonical mapping.

### 2.2 Dependency Injection
- Register `ICoaFormatAdapterRegistry` and adapter classes in ASP.NET DI.
- Injected into `AccountService`.

---

## 3. Verification & Acceptance Criteria
1. `dotnet test`: All 34 existing ChartOfAccounts tests pass without regression.
2. New adapter tests pass:
   - AccountGo v1 parses cleanly into canonical rows.
   - Xero header signatures detected & returned as "adapter not installed; refer to docs/accounting/format-adapters.md".
   - QuickBooks Online header signatures detected & returned as "adapter not installed; refer to docs/accounting/format-adapters.md".
   - Unknown format / missing marker rejected with descriptive error.
   - Snapshot marker continues to be rejected with conversion journal notice.
3. `pnpm --dir src/Web test`: 53/53 tests pass.
4. `docs/accounting/coa-import-export-requirements.md` §12 marked Done for P4.
