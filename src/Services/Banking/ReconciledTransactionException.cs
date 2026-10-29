// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;

namespace Services.Banking
{
    /// <summary>
    /// Thrown when a caller tries to change a GL document whose lines are stamped by a finished reconciliation.
    /// Controllers map this to HTTP 409 with <c>code = "RECONCILED"</c>.
    /// </summary>
    public sealed class ReconciledTransactionException : InvalidOperationException
    {
        public const string ErrorCode = "RECONCILED";

        public ReconciledTransactionException(string bankName, DateTime statementEndDate)
            : base($"This transaction is reconciled. Undo the reconciliation for {bankName} to {statementEndDate:yyyy-MM-dd} first.")
        {
            BankName = bankName;
            StatementEndDate = statementEndDate.Date;
        }

        public string BankName { get; }
        public DateTime StatementEndDate { get; }
    }
}
