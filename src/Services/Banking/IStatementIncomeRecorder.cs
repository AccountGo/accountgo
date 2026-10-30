// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using Dto.Banking;

namespace Services.Banking
{
    /// <summary>
    /// Seam for recording other income from a statement line (#436 I5).
    /// v1 creates a one-line invoice issued and paid on the statement date; a later sales-receipt
    /// implementation can replace this without changing the statement-line API or UI.
    /// </summary>
    public interface IStatementIncomeRecorder
    {
        StatementIncomeCategoriesDto ListIncomeCategories();

        RecordOtherIncomeFromStatementLineResultDto Record(
            int lineId,
            RecordOtherIncomeFromStatementLineRequest request,
            string actor);
    }
}
