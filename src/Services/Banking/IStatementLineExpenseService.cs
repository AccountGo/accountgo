// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using Dto.Banking;

namespace Services.Banking
{
    /// <summary>
    /// Turn unmatched money-out statement lines into bills/payments through the normal purchasing paths (#436 I2).
    /// Never posts to the GL directly.
    /// </summary>
    public interface IStatementLineExpenseService
    {
        StatementExpenseCategoriesDto ListExpenseCategories();

        RecordExpenseFromStatementLineResultDto RecordExpense(
            int lineId,
            RecordExpenseFromStatementLineRequest request,
            string actor);

        PayOpenBillFromStatementLineResultDto PayOpenBill(
            int lineId,
            PayOpenBillFromStatementLineRequest request,
            string actor);
    }
}
