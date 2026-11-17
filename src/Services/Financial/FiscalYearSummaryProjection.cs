// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Globalization;
using Core.Domain.Financials;
using Dto.Financial;

namespace Services.Financial
{
    /// <summary>
    /// One status rule for year-end lists and the fiscal-year read contract.
    /// Closed means <c>ClosedAt</c> is set. <c>IsActive</c> is never closed or open.
    /// </summary>
    public static class FiscalYearCloseStatusResolver
    {
        public static FiscalYearCloseStatus Resolve(DateTime? closedAt, DateTime endDate, DateTime workspaceToday)
        {
            if (closedAt != null)
                return FiscalYearCloseStatus.Closed;
            if (endDate.Date < workspaceToday.Date)
                return FiscalYearCloseStatus.Ended;
            return FiscalYearCloseStatus.Open;
        }

        public static string ToWireStatus(FiscalYearCloseStatus status) => status switch
        {
            FiscalYearCloseStatus.Open => "Open",
            FiscalYearCloseStatus.Ended => "Ended",
            FiscalYearCloseStatus.Closed => "Closed",
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown fiscal year status.")
        };
    }

    public static class FiscalYearSummaryProjection
    {
        public static FiscalYearSummaryDto Project(FinancialYear year, DateTime workspaceToday, bool isPersisted)
        {
            var closedAt = isPersisted ? year.ClosedAt : null;
            var status = FiscalYearCloseStatusResolver.Resolve(closedAt, year.EndDate, workspaceToday);
            return new FiscalYearSummaryDto
            {
                Id = isPersisted ? year.Id : 0,
                FiscalYearCode = year.FiscalYearCode,
                FiscalYearName = year.FiscalYearName,
                StartDate = FormatDate(year.StartDate),
                EndDate = FormatDate(year.EndDate),
                IsActive = year.IsActive,
                Status = FiscalYearCloseStatusResolver.ToWireStatus(status),
                ClosedAt = isPersisted ? FormatUtc(year.ClosedAt) : null,
                ClosedBy = isPersisted ? year.ClosedBy : null,
                ClosingGeneralLedgerHeaderId = isPersisted ? year.ClosingGeneralLedgerHeaderId : null,
                IsPersisted = isPersisted
            };
        }

        public static string FormatDate(DateTime value) =>
            value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        /// <summary>UTC timestamp with an explicit numeric offset. Stored close times are UTC instants.</summary>
        public static string? FormatUtc(DateTime? value)
        {
            if (value == null)
                return null;
            var utc = value.Value.Kind == DateTimeKind.Local
                ? value.Value.ToUniversalTime()
                : DateTime.SpecifyKind(value.Value, DateTimeKind.Utc);
            return new DateTimeOffset(utc).ToString("yyyy-MM-dd'T'HH:mm:ss.fffffffzzz", CultureInfo.InvariantCulture);
        }
    }
}
