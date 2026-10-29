// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System.Collections.Generic;
using Dto.Banking;

namespace Services.Banking
{
    public interface IBankReconciliationService
    {
        IReadOnlyList<BankReconciliationListItemDto> List(int bankId);
        BankReconciliationDto Get(int id);
        BankReconciliationDto Start(StartBankReconciliationRequest request, string actor);
        BankReconciliationDto UpdateItems(int id, UpdateBankReconciliationItemsRequest request);
        BankReconciliationDto AddAdjustment(int id, CreateReconciliationAdjustmentRequest request);
        BankReconciliationDto Finish(int id, FinishBankReconciliationRequest request, string actor);
        BankReconciliationDto Undo(int id, UndoBankReconciliationRequest request, string actor);
        /// <summary>Second-person sign-off on a finished reconciliation; the reviewer must differ from the preparer (3D).</summary>
        BankReconciliationDto Review(int id, string actor);
        void DiscardDraft(int id);
        /// <summary>Finished reconciliation report figures (statement + outstanding → books).</summary>
        BankReconciliationReportDto GetReport(int id);
    }
}
