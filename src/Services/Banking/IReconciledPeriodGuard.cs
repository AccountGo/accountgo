// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using Core.Domain.Financials;

namespace Services.Banking
{
    /// <summary>
    /// Warns before a new posting lands in a period a bank account has already reconciled (3E).
    /// One instance per request: the confirmation flag lives as long as the request.
    /// </summary>
    public interface IReconciledPeriodGuard
    {
        /// <summary>True once the caller sent <c>confirmReconciledPeriod=true</c> for this request.</summary>
        bool IsConfirmed { get; }

        void Confirm();

        /// <summary>
        /// Throws <see cref="ReconciledPeriodConfirmationRequiredException"/> when <paramref name="entry"/> posts to a bank
        /// account dated on or before that bank's latest finished statement end date and the request is not confirmed.
        /// Reversing (void) entries are skipped; they are governed by the reconciled-line check instead.
        /// Opening-balance entries are exempt (see <see cref="ReconciledPeriodGuard"/>).
        /// </summary>
        void EnsurePostingAllowed(GeneralLedgerHeader entry);
    }
}
