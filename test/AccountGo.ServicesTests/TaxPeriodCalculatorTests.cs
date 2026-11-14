// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Globalization;
using Core.Domain;
using Core.Domain.TaxSystem;
using Xunit;

namespace AccountGo.ServicesTests
{
    public sealed class TaxPeriodCalculatorTests
    {
        [Theory]
        [InlineData(1, 2026, 1, 15, 2026, 1, 1, 2026, 3, 31, "Jan\u2013Mar 2026")]
        [InlineData(1, 2026, 5, 20, 2026, 4, 1, 2026, 6, 30, "Apr\u2013Jun 2026")]
        [InlineData(1, 2026, 8, 1, 2026, 7, 1, 2026, 9, 30, "Jul\u2013Sep 2026")]
        [InlineData(1, 2026, 12, 31, 2026, 10, 1, 2026, 12, 31, "Oct\u2013Dec 2026")]
        [InlineData(2, 2026, 9, 15, 2026, 8, 1, 2026, 10, 31, "Aug\u2013Oct 2026")]
        [InlineData(2, 2026, 11, 1, 2026, 11, 1, 2027, 1, 31, "Nov 2026\u2013Jan 2027")]
        [InlineData(2, 2027, 1, 15, 2026, 11, 1, 2027, 1, 31, "Nov 2026\u2013Jan 2027")]
        [InlineData(3, 2026, 12, 10, 2026, 12, 1, 2027, 2, 28, "Dec 2026\u2013Feb 2027")]
        [InlineData(3, 2027, 2, 28, 2026, 12, 1, 2027, 2, 28, "Dec 2026\u2013Feb 2027")]
        [InlineData(3, 2024, 2, 29, 2023, 12, 1, 2024, 2, 29, "Dec 2023\u2013Feb 2024")]
        public void Quarterly_Containing_AllCyclesAndYearBoundary(
            int cycleStart, int year, int month, int day,
            int expectedStartY, int expectedStartM, int expectedStartD,
            int expectedEndY, int expectedEndM, int expectedEndD,
            string expectedLabel)
        {
            var period = TaxPeriodCalculator.Containing(
                new DateTime(year, month, day),
                TaxPeriodFrequency.Quarterly,
                cycleStart);

            Assert.Equal(new DateTime(expectedStartY, expectedStartM, expectedStartD), period.Start);
            Assert.Equal(new DateTime(expectedEndY, expectedEndM, expectedEndD), period.End);
            Assert.Equal(expectedLabel, period.Label);
            Assert.DoesNotContain("Q", period.Label, StringComparison.Ordinal);
        }

        [Fact]
        public void Monthly_LeapYearFebruary_Returns29Days()
        {
            var period = TaxPeriodCalculator.Containing(new DateTime(2024, 2, 15), TaxPeriodFrequency.Monthly, 1);

            Assert.Equal(new DateTime(2024, 2, 1), period.Start);
            Assert.Equal(new DateTime(2024, 2, 29), period.End);
            Assert.Equal("Feb 2024", period.Label);
        }

        [Theory]
        [InlineData(FiscalYearPattern.Calendar, null, 1)]
        [InlineData(FiscalYearPattern.AprMar, null, 1)]
        [InlineData(FiscalYearPattern.JulJun, null, 1)]
        [InlineData(FiscalYearPattern.Custom, 2, 2)]
        [InlineData(FiscalYearPattern.Custom, 3, 3)]
        [InlineData(FiscalYearPattern.Custom, 11, 2)]
        public void DefaultQuarterCycleStartMonth_MatchesFiscalPattern(
            FiscalYearPattern pattern, int? fiscalStartMonth, int expectedCycleStart)
        {
            var actual = TaxPeriodCalculator.DefaultQuarterCycleStartMonth(pattern, fiscalStartMonth);
            Assert.Equal(expectedCycleStart, actual);
        }

        [Fact]
        public void Quarterly_StraddlingFiscalYearEnd_IsIndependentOfFiscalYear()
        {
            var setting = new CompanySetting
            {
                FiscalYearPattern = FiscalYearPattern.Custom,
                FiscalYearStartMonth = 2, // FY Feb–Jan
                TaxPeriodFrequency = TaxPeriodFrequency.Quarterly,
                TaxQuarterCycleStartMonth = 1 // Cycle 1: Jan–Mar crosses FY boundary
            };

            var cycle = TaxPeriodCalculator.EffectiveCycleStart(setting);
            var period = TaxPeriodCalculator.Containing(new DateTime(2026, 2, 10), setting.TaxPeriodFrequency, cycle);

            Assert.Equal(1, cycle);
            Assert.Equal(new DateTime(2026, 1, 1), period.Start);
            Assert.Equal(new DateTime(2026, 3, 31), period.End);
            Assert.Equal("Jan\u2013Mar 2026", period.Label);
        }

        [Fact]
        public void IsWholePeriod_TrueAndFalseCases()
        {
            Assert.True(TaxPeriodCalculator.IsWholePeriod(
                new DateTime(2026, 8, 1), new DateTime(2026, 10, 31), TaxPeriodFrequency.Quarterly, 2));
            Assert.False(TaxPeriodCalculator.IsWholePeriod(
                new DateTime(2026, 8, 2), new DateTime(2026, 10, 31), TaxPeriodFrequency.Quarterly, 2));
            Assert.False(TaxPeriodCalculator.IsWholePeriod(
                new DateTime(2026, 8, 1), new DateTime(2026, 10, 30), TaxPeriodFrequency.Quarterly, 2));
            Assert.False(TaxPeriodCalculator.IsWholePeriod(
                new DateTime(2026, 8, 1), new DateTime(2026, 11, 30), TaxPeriodFrequency.Quarterly, 2));
            Assert.True(TaxPeriodCalculator.IsWholePeriod(
                new DateTime(2026, 10, 1), new DateTime(2026, 10, 31), TaxPeriodFrequency.Monthly, 1));
            Assert.False(TaxPeriodCalculator.IsWholePeriod(
                new DateTime(2026, 10, 15), new DateTime(2026, 10, 1), TaxPeriodFrequency.Monthly, 1));
        }

        [Fact]
        public void Recent_ReturnsNewestFirst()
        {
            var recent = TaxPeriodCalculator.Recent(new DateTime(2026, 11, 14), TaxPeriodFrequency.Quarterly, 2, 3);

            Assert.Equal(3, recent.Count);
            Assert.Equal("Nov 2026\u2013Jan 2027", recent[0].Label);
            Assert.Equal("Aug\u2013Oct 2026", recent[1].Label);
            Assert.Equal("May\u2013Jul 2026", recent[2].Label);
        }

        [Fact]
        public void Labels_AreUnchangedUnderThaiBuddhistCulture()
        {
            var previousCulture = CultureInfo.CurrentCulture;
            var previousUiCulture = CultureInfo.CurrentUICulture;
            try
            {
                var th = new CultureInfo("th-TH");
                CultureInfo.CurrentCulture = th;
                CultureInfo.CurrentUICulture = th;

                var monthly = TaxPeriodCalculator.Containing(new DateTime(2026, 10, 15), TaxPeriodFrequency.Monthly, 1);
                var quarterly = TaxPeriodCalculator.Containing(new DateTime(2026, 12, 15), TaxPeriodFrequency.Quarterly, 3);

                Assert.Equal("Oct 2026", monthly.Label);
                Assert.Equal("Dec 2026\u2013Feb 2027", quarterly.Label);
            }
            finally
            {
                CultureInfo.CurrentCulture = previousCulture;
                CultureInfo.CurrentUICulture = previousUiCulture;
            }
        }
    }
}
