// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable

namespace Dto.Financial
{
    /// <summary>
    /// Additive fiscal-year read model shared by Settings and <c>get_fiscal_years</c>.
    /// Dates are <c>yyyy-MM-dd</c>. <see cref="ClosedAt"/> is a UTC timestamp with an explicit offset.
    /// </summary>
    public sealed class FiscalYearSummaryDto
    {
        public int Id { get; init; }
        public string FiscalYearCode { get; init; } = "";
        public string FiscalYearName { get; init; } = "";
        public string StartDate { get; init; } = "";
        public string EndDate { get; init; } = "";
        public bool IsActive { get; init; }

        /// <summary><c>Open</c>, <c>Ended</c>, or <c>Closed</c>. Closed means the year has a close timestamp.</summary>
        public string Status { get; init; } = "Open";

        public string? ClosedAt { get; init; }
        public string? ClosedBy { get; init; }
        public int? ClosingGeneralLedgerHeaderId { get; init; }
        public bool IsPersisted { get; init; }
    }
}
