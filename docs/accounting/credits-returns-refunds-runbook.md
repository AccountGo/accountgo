# G01 / G02 Rollout, Mapping Dry-Run, and Rollback Runbook

## Pre-activation gates (must be recorded before enabling a workspace)
1. **Framework & Jurisdiction Profile**: Confirm tenant reporting framework and local tax rules (advance tax, bad-debt tax relief, credit-note statutory fields).
2. **Control-Account Mappings**: Run `GET /api/sales/credits/mappings/preview` for the workspace, review suggested accounts, and save active mappings via `PUT /api/sales/credits/mappings`. Never silently create or remap accounts.
3. **Cash-Projection Policy**: Confirm cash-recognition treatment for advances, refunds, and credit rollovers.
4. **Operating Policy**: Verify solo vs. team operating settings (`WorkspaceOperatingSettings`) and reviewer role assignments.

## Feature gate control
- New commands are gated **off by default** via `CompanySetting.CreditsReturnsRefundsEnabled = false`.
- Enable only for approved pilot workspaces after completing the pre-activation gates above.

## Monitoring signals
- Track `SettlementCommandException` counts by `Code` (`stale_version`, `source_exhausted`, `idempotency_conflict`, `missing_mapping`, `period_locked`, `self_approval_forbidden`).
- Alert on any failed/partial transaction or reconciliation variance on customer credits liability, supplier credits asset, supplier advances, loss allowance, or return clearing.
- Monitor aged items in `SupplierReturnDispatch` where `HasControlTransferred = true` and `SupplierCreditNoteId IS NULL`.

## Rollback procedure
1. Set `CompanySetting.CreditsReturnsRefundsEnabled = false` for the affected workspace(s) to immediately halt new mutations while preserving read access, reporting, and authorized reversals.
2. Do **not** drop populated tables (`CustomerCreditNote`, `SupplierCreditNote`, `SupplierAdvance`, `SettlementApplication`, `PartyRefund`, `ReceivableWriteOff`, etc.) or deploy older report code that ignores posted settlement events.
3. For disaster recovery after cash movement, restore the entire database consistently rather than restoring a single subledger.
