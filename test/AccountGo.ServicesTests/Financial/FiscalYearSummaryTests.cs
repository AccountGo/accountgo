#nullable enable
using System;
using System.Globalization;
using Core.Domain;
using Core.Domain.Financials;
using Services.Financial;

namespace AccountGo.ServicesTests.Financial;

public sealed class FiscalYearSummaryTests
{
    [Theory]
    [InlineData("2026-01-01", "2026-12-31", false, "2026-06-01", "Open")]
    [InlineData("2026-01-01", "2026-12-31", false, "2026-12-31", "Open")]
    [InlineData("2026-04-01", "2027-03-31", false, "2027-04-01", "Ended")]
    [InlineData("2026-01-01", "2026-12-31", true, "2026-06-01", "Closed")]
    [InlineData("2026-01-01", "2026-12-31", true, "2027-01-01", "Closed")]
    public void Status_UsesCloseTimestampThenWorkspaceDate(string start, string end, bool closed, string today, string expected)
    {
        var year = Year(start, end, closedAt: closed ? DateTime.Parse(today) : null, isActive: false);
        var status = FiscalYearCloseStatusResolver.Resolve(year.ClosedAt, year.EndDate, Date(today));
        Assert.Equal(expected, FiscalYearCloseStatusResolver.ToWireStatus(status));
    }

    [Fact]
    public void InactiveOpenYear_IsNotClosed()
    {
        var today = new DateTime(2026, 6, 1);
        var year = Year("2026-01-01", "2026-12-31", closedAt: null, isActive: false);
        var summary = FiscalYearSummaryProjection.Project(year, today, isPersisted: true);
        Assert.False(summary.IsActive);
        Assert.Equal("Open", summary.Status);
        Assert.True(summary.IsPersisted);
        Assert.Null(summary.ClosedAt);
        Assert.Null(summary.ClosedBy);
        Assert.Null(summary.ClosingGeneralLedgerHeaderId);
    }

    [Fact]
    public void ZeroActivityClosedYear_HasNoClosingJournal()
    {
        var closedAt = new DateTime(2020, 1, 15, 18, 30, 0, DateTimeKind.Utc);
        var year = Year("2019-01-01", "2019-12-31", closedAt, isActive: false);
        year.ClosedBy = "ada@example.test";
        year.ClosingGeneralLedgerHeaderId = null;
        var summary = FiscalYearSummaryProjection.Project(year, new DateTime(2026, 11, 17), isPersisted: true);
        Assert.Equal("Closed", summary.Status);
        Assert.Equal("2019-01-01", summary.StartDate);
        Assert.Equal("2019-12-31", summary.EndDate);
        Assert.Equal("ada@example.test", summary.ClosedBy);
        Assert.Null(summary.ClosingGeneralLedgerHeaderId);
        Assert.Equal("2020-01-15T18:30:00.0000000+00:00", summary.ClosedAt);
    }

    [Fact]
    public void UnpersistedYear_DoesNotInventAClose()
    {
        var year = Year("2026-01-01", "2026-12-31", closedAt: new DateTime(2026, 12, 31), isActive: true);
        year.Id = 44;
        year.ClosedBy = "should-not-leak";
        year.ClosingGeneralLedgerHeaderId = 9;
        var summary = FiscalYearSummaryProjection.Project(year, new DateTime(2026, 6, 1), isPersisted: false);
        Assert.Equal(0, summary.Id);
        Assert.False(summary.IsPersisted);
        Assert.Equal("Open", summary.Status);
        Assert.Null(summary.ClosedAt);
        Assert.Null(summary.ClosedBy);
        Assert.Null(summary.ClosingGeneralLedgerHeaderId);
    }

    [Fact]
    public void LocalMidnight_UsesTheWorkspaceDateNotUtc()
    {
        var clock = new FixedClock(new DateTimeOffset(2026, 1, 2, 7, 30, 0, TimeSpan.Zero));
        var losAngeles = WorkspaceBusinessDate.Today(clock, "America/Los_Angeles");
        Assert.Equal(new DateTime(2026, 1, 1), losAngeles);
        var endedOnUtc = FiscalYearCloseStatusResolver.Resolve(null, new DateTime(2026, 1, 1), clock.GetUtcNow().UtcDateTime.Date);
        var openLocally = FiscalYearCloseStatusResolver.Resolve(null, new DateTime(2026, 1, 1), losAngeles);
        Assert.Equal(FiscalYearCloseStatus.Ended, endedOnUtc);
        Assert.Equal(FiscalYearCloseStatus.Open, openLocally);

        var afterMidnight = new FixedClock(new DateTimeOffset(2026, 1, 1, 11, 30, 0, TimeSpan.Zero));
        var auckland = WorkspaceBusinessDate.Today(afterMidnight, "Pacific/Auckland");
        Assert.Equal(new DateTime(2026, 1, 2), auckland);
        Assert.Equal(FiscalYearCloseStatus.Ended,
            FiscalYearCloseStatusResolver.Resolve(null, new DateTime(2026, 1, 1), auckland));
    }

    [Fact]
    public void DaylightSavingBoundary_FollowsTheZone()
    {
        // 07:30 UTC is 00:30 PDT on 2 July, and still 1 July if the zone is left on standard time.
        var summer = WorkspaceBusinessDate.InZone(
            new DateTimeOffset(2026, 7, 2, 7, 30, 0, TimeSpan.Zero), "America/Los_Angeles");
        Assert.Equal(new DateTime(2026, 7, 2), summer);
        Assert.Equal(new DateTime(2026, 3, 7),
            WorkspaceBusinessDate.InZone(new DateTimeOffset(2026, 3, 8, 7, 30, 0, TimeSpan.Zero), "America/Los_Angeles"));
        Assert.Equal(new DateTime(2026, 3, 8),
            WorkspaceBusinessDate.InZone(new DateTimeOffset(2026, 3, 8, 10, 30, 0, TimeSpan.Zero), "America/Los_Angeles"));
    }

    [Fact]
    public void UnknownTimezone_FallsBackToUtcDate()
    {
        var today = WorkspaceBusinessDate.InZone(
            new DateTimeOffset(2026, 5, 2, 1, 0, 0, TimeSpan.Zero), "Not/AZone");
        Assert.Equal(new DateTime(2026, 5, 2), today);
    }

    private static DateTime Date(string value) =>
        DateTime.ParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static FinancialYear Year(string start, string end, DateTime? closedAt, bool isActive) => new()
    {
        Id = 7,
        FiscalYearCode = "FY",
        FiscalYearName = "FY",
        StartDate = Date(start),
        EndDate = Date(end),
        IsActive = isActive,
        ClosedAt = closedAt
    };

    private sealed class FixedClock : TimeProvider
    {
        private readonly DateTimeOffset _utc;
        public FixedClock(DateTimeOffset utc) => _utc = utc;
        public override DateTimeOffset GetUtcNow() => _utc;
    }
}
