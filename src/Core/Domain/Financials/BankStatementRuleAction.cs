// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

namespace Core.Domain.Financials
{
    /// <summary>The action suggested by a remembered statement description.</summary>
    public enum BankStatementRuleAction
    {
        Expense = 0,
        OwnerDrawing = 1,
        OwnerContribution = 2
    }
}
