// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Globalization;

namespace Services.Banking
{
    /// <summary>
    /// A new posting hits a bank account on or before its latest finished statement end date. The caller must
    /// resend with <c>confirmReconciledPeriod=true</c>. Controllers map this to HTTP 409
    /// <c>RECONCILED_PERIOD_CONFIRM_REQUIRED</c> (same shape as <see cref="BankArchiveConfirmationRequiredException"/>).
    /// </summary>
    public sealed class ReconciledPeriodConfirmationRequiredException : InvalidOperationException
    {
        public const string ErrorCode = "RECONCILED_PERIOD_CONFIRM_REQUIRED";

        public ReconciledPeriodConfirmationRequiredException(string bankName, DateTime statementEndDate, DateTime postingDate)
            : base(
                $"{bankName} is reconciled through {statementEndDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}. "
                + $"Posting on {postingDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)} changes a reconciled period. Confirm to continue.")
        {
            BankName = bankName;
            StatementEndDate = statementEndDate.Date;
            PostingDate = postingDate.Date;
        }

        public string BankName { get; }
        public DateTime StatementEndDate { get; }
        public DateTime PostingDate { get; }
    }
}
