# Chart of Accounts Import (P2: Append & Upsert) — Plan of Record

**Status:** Approved & Locked for P2 Implementation  
**Spec Reference:** `docs/accounting/coa-import-export-requirements.md` (§7.1–7.5, §9, §11 D1–D3/D5–D7/D10, §12 P2, §13 Criteria 1–2, 7–8)  
**Parent Requirements:** [coa-import-export-requirements.md](./coa-import-export-requirements.md)

---

## 1. Scope & Boundaries (P2 Only)
- **Included:**
  - Format detection: `# AccountGo-COA-v1` required.
  - Snapshot / Balance rejection: `# AccountGo-COA-Snapshot-v1` or `Balance` column strictly rejected with §8 conversion journal guidance.
  - Reserved columns: `DefaultTaxCode`, `Currency` ignored + warned in preview; does not fail file.
  - **Append Mode**: creates missing account codes; existing codes skipped with clear note.
  - **Upsert Mode**: creates missing account codes; updates existing account fields (`AccountName`, `Description`, `ParentAccountId`, `IsContraAccount`, `IsCash`, `ArchivedAt`).
  - Class immutability hard-guard: cannot alter `AccountClass` if account has GL history.
  - Protection rules on archive-via-import: cannot set `Status=Archived` if account is a protected control account.
  - Parent resolution & cycle detection: missing parent = row error; circular ancestry = error; reparent under archived parent blocked unless unarchiving parent in the same batch.
  - Soft warnings: code/class recommended range mismatch; protected account rename.
  - Server-side validate & preview endpoint + server-side re-validating commit endpoint in atomic transaction.
  - Web UI: Multi-step Import Wizard modal on `/org/:orgSlug/financials/accounts` with upload, live preview (Create/Update/Skip/Error badges), error report, and treegrid refresh.
- **Strictly Excluded from P2:**
  - Replace mode (empty books), Retire-missing mode, Owner-only gates, control remapping grid, opening balances, Xero/QBO format adapters.
  - Never delete via import in P2.

---

## 2. API Contracts & DTOs
- `POST /api/Financials/ImportAccounts/Validate`: Validates file against tenant DB, returns summary counts & row-level preview.
- `POST /api/Financials/ImportAccounts/Commit`: Re-validates full batch within an atomic database transaction (`BeginTransactionAsync`), applies creates & updates in topological parent order, and returns commit summary.
- Gated by: `[RequirePermission(SystemPermissions.FinancialsAccountsManage)]` and `[RequireModule("financials")]`.

---

## 3. UI Wizard
- Modal on `ChartOfAccounts.tsx` for users with `financials.accounts.manage`.
- Step 1: Upload CSV, choose Append vs Upsert, link to sample Structure template.
- Step 2: Validation preview with summary metrics, filterable table, action badges (`Create`, `Update`, `Skip`, `Error`), inline messages, and downloadable error report.
- Step 3: Transactional commit and immediate treegrid refresh.
