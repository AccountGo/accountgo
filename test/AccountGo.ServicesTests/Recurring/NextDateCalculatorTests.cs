using System;
using Core.Domain.Recurring;
using Core.Domain.Recurring.Calendar;
using Xunit;

namespace AccountGo.ServicesTests.Recurring
{
    public class NextDateCalculatorTests
    {
        #region Day 31 Monthly Schedule Tests

        [Fact]
        public void Day31_Monthly_NonLeapYear_FebruaryIs28()
        {
            // Arrange: Start Jan 31, 2025 (non-leap year)
            var startDate = new DateTime(2025, 1, 31);

            // Act
            var dates = NextDateCalculator.ComputeNextDates(
                RecurringCadence.Monthly,
                interval: 1,
                startDate: startDate,
                dayOfMonth: 31,
                count: 3);

            // Assert: Jan 31, Feb 28, Mar 31
            Assert.Equal(3, dates.Count);
            Assert.Equal(new DateTime(2025, 1, 31), dates[0]);
            Assert.Equal(new DateTime(2025, 2, 28), dates[1]); // Feb has 28 days in non-leap year
            Assert.Equal(new DateTime(2025, 3, 31), dates[2]);
        }

        [Fact]
        public void Day31_Monthly_LeapYear_FebruaryIs29()
        {
            // Arrange: Start Jan 31, 2024 (leap year)
            var startDate = new DateTime(2024, 1, 31);

            // Act
            var dates = NextDateCalculator.ComputeNextDates(
                RecurringCadence.Monthly,
                interval: 1,
                startDate: startDate,
                dayOfMonth: 31,
                count: 3);

            // Assert: Jan 31, Feb 29, Mar 31
            Assert.Equal(3, dates.Count);
            Assert.Equal(new DateTime(2024, 1, 31), dates[0]);
            Assert.Equal(new DateTime(2024, 2, 29), dates[1]); // Feb has 29 days in leap year
            Assert.Equal(new DateTime(2024, 3, 31), dates[2]);
        }

        [Fact]
        public void Day31_Monthly_AprilIs30()
        {
            // Arrange: Start Mar 31, 2025
            var startDate = new DateTime(2025, 3, 31);

            // Act
            var dates = NextDateCalculator.ComputeNextDates(
                RecurringCadence.Monthly,
                interval: 1,
                startDate: startDate,
                dayOfMonth: 31,
                count: 2);

            // Assert: Mar 31, Apr 30
            Assert.Equal(2, dates.Count);
            Assert.Equal(new DateTime(2025, 3, 31), dates[0]);
            Assert.Equal(new DateTime(2025, 4, 30), dates[1]); // April has 30 days
        }

        [Fact]
        public void Day30_Monthly_FebruaryIs28()
        {
            // Arrange: Start Jan 30, 2025
            var startDate = new DateTime(2025, 1, 30);

            // Act
            var dates = NextDateCalculator.ComputeNextDates(
                RecurringCadence.Monthly,
                interval: 1,
                startDate: startDate,
                dayOfMonth: 30,
                count: 3);

            // Assert: Jan 30, Feb 28 (last day since 30 > 28), Mar 30
            Assert.Equal(3, dates.Count);
            Assert.Equal(new DateTime(2025, 1, 30), dates[0]);
            Assert.Equal(new DateTime(2025, 2, 28), dates[1]); // Feb only has 28 days
            Assert.Equal(new DateTime(2025, 3, 30), dates[2]);
        }

        #endregion

        #region Weekly Cadence Tests

        [Fact]
        public void Weekly_SameWeekday_EveryWeek()
        {
            // Arrange: Start on a Monday
            var startDate = new DateTime(2025, 1, 6); // Monday

            // Act
            var dates = NextDateCalculator.ComputeNextDates(
                RecurringCadence.Weekly,
                interval: 1,
                startDate: startDate,
                dayOfMonth: null,
                count: 4);

            // Assert: Every Monday for 4 weeks
            Assert.Equal(4, dates.Count);
            Assert.Equal(DayOfWeek.Monday, dates[0].DayOfWeek);
            Assert.Equal(DayOfWeek.Monday, dates[1].DayOfWeek);
            Assert.Equal(DayOfWeek.Monday, dates[2].DayOfWeek);
            Assert.Equal(DayOfWeek.Monday, dates[3].DayOfWeek);
            Assert.Equal(7, (dates[1] - dates[0]).Days);
        }

        [Fact]
        public void Weekly_Every2Weeks()
        {
            // Arrange: Start on a Friday
            var startDate = new DateTime(2025, 1, 3); // Friday

            // Act
            var dates = NextDateCalculator.ComputeNextDates(
                RecurringCadence.Weekly,
                interval: 2,
                startDate: startDate,
                dayOfMonth: null,
                count: 3);

            // Assert: Every 2 weeks on Friday
            Assert.Equal(3, dates.Count);
            Assert.Equal(DayOfWeek.Friday, dates[0].DayOfWeek);
            Assert.Equal(DayOfWeek.Friday, dates[1].DayOfWeek);
            Assert.Equal(DayOfWeek.Friday, dates[2].DayOfWeek);
            Assert.Equal(14, (dates[1] - dates[0]).Days);
            Assert.Equal(14, (dates[2] - dates[1]).Days);
        }

        #endregion

        #region Quarterly Cadence Tests

        [Fact]
        public void Quarterly_SameDayOfMonth()
        {
            // Arrange: Start Jan 15
            var startDate = new DateTime(2025, 1, 15);

            // Act
            var dates = NextDateCalculator.ComputeNextDates(
                RecurringCadence.Quarterly,
                interval: 1,
                startDate: startDate,
                dayOfMonth: 15,
                count: 4);

            // Assert: Jan 15, Apr 15, Jul 15, Oct 15
            Assert.Equal(4, dates.Count);
            Assert.Equal(new DateTime(2025, 1, 15), dates[0]);
            Assert.Equal(new DateTime(2025, 4, 15), dates[1]);
            Assert.Equal(new DateTime(2025, 7, 15), dates[2]);
            Assert.Equal(new DateTime(2025, 10, 15), dates[3]);
        }

        #endregion

        #region Yearly Cadence Tests

        [Fact]
        public void Yearly_SameDayOfMonth()
        {
            // Arrange: Start Feb 29, 2024 (leap year)
            var startDate = new DateTime(2024, 2, 29);

            // Act
            var dates = NextDateCalculator.ComputeNextDates(
                RecurringCadence.Yearly,
                interval: 1,
                startDate: startDate,
                dayOfMonth: 31, // Requesting day 31, Feb will get last day
                count: 3);

            // Assert: Feb 29 2024, Feb 28 2025 (non-leap), Feb 28 2026
            Assert.Equal(3, dates.Count);
            Assert.Equal(new DateTime(2024, 2, 29), dates[0]); // Leap year
            Assert.Equal(new DateTime(2025, 2, 28), dates[1]); // Non-leap year
            Assert.Equal(new DateTime(2026, 2, 28), dates[2]); // Non-leap year
        }

        #endregion

        #region End Date and Max Occurrences Tests

        [Fact]
        public void EndDate_StopsGeneratingAfterEndDate()
        {
            // Arrange: Monthly schedule with end date
            var startDate = new DateTime(2025, 1, 15);
            var endDate = new DateTime(2025, 3, 31);

            // Act
            var dates = NextDateCalculator.ComputeNextDates(
                RecurringCadence.Monthly,
                interval: 1,
                startDate: startDate,
                dayOfMonth: 15,
                count: 10,
                endDate: endDate);

            // Assert: Only 3 dates (Jan, Feb, Mar)
            Assert.Equal(3, dates.Count);
        }

        [Fact]
        public void MaxOccurrences_StopsAfterMaxOccurrences()
        {
            // Arrange: Monthly schedule with max 2 occurrences
            var startDate = new DateTime(2025, 1, 15);

            // Act
            var dates = NextDateCalculator.ComputeNextDates(
                RecurringCadence.Monthly,
                interval: 1,
                startDate: startDate,
                dayOfMonth: 15,
                count: 10,
                maxOccurrences: 2);

            // Assert: Only 2 dates
            Assert.Equal(2, dates.Count);
        }

        #endregion

        #region Failed/Skipped Period Tests

        [Fact]
        public void FailedPeriod_DoesNotShiftLaterPeriods()
        {
            // Arrange: Monthly on day 15
            var startDate = new DateTime(2025, 1, 15);

            // Act: Compute next 4 dates
            var dates = NextDateCalculator.ComputeNextDates(
                RecurringCadence.Monthly,
                interval: 1,
                startDate: startDate,
                dayOfMonth: 15,
                count: 4);

            // Assert: Dates are fixed regardless of failures
            Assert.Equal(new DateTime(2025, 1, 15), dates[0]);
            Assert.Equal(new DateTime(2025, 2, 15), dates[1]);
            Assert.Equal(new DateTime(2025, 3, 15), dates[2]);
            Assert.Equal(new DateTime(2025, 4, 15), dates[3]);

            // Verify: Calling ComputeNextDate after Feb 15 with startDate still gives Mar 15
            var nextAfterFeb = NextDateCalculator.ComputeNextDate(
                RecurringCadence.Monthly,
                interval: 1,
                afterDate: new DateTime(2025, 2, 15),
                dayOfMonth: 15,
                startDate: startDate);
            Assert.Equal(new DateTime(2025, 3, 15), nextAfterFeb);
        }

        [Fact]
        public void RetryOnLaterDate_DoesNotSkipPeriod()
        {
            // Arrange: Monthly on day 15, starting Jan 15
            var startDate = new DateTime(2025, 1, 15);

            // Act: Retry happens on Mar 2 (after a Feb run that got delayed)
            // The next date should still be Mar 15, not Apr 15
            var nextDate = NextDateCalculator.ComputeNextDate(
                RecurringCadence.Monthly,
                interval: 1,
                afterDate: new DateTime(2025, 3, 2),
                dayOfMonth: 15,
                startDate: startDate);

            // Assert: Mar 15, not Apr 15 (series stays anchored to start)
            Assert.Equal(new DateTime(2025, 3, 15), nextDate);
        }

        #endregion

        #region First Date Must Be On Or After Start Date

        [Fact]
        public void FirstDate_Monthly_DayOfMonthBeforeStartDay_AdvancesOneMonth()
        {
            // Arrange: Start Mar 15 with DayOfMonth = 1
            // The first occurrence should be Apr 1, NOT Mar 1 (which is before the start)
            var startDate = new DateTime(2025, 3, 15);

            // Act
            var dates = NextDateCalculator.ComputeNextDates(
                RecurringCadence.Monthly,
                interval: 1,
                startDate: startDate,
                dayOfMonth: 1,
                count: 3);

            // Assert: First occurrence is Apr 1, not Mar 1
            Assert.Equal(3, dates.Count);
            Assert.Equal(new DateTime(2025, 4, 1), dates[0]);
            Assert.Equal(new DateTime(2025, 5, 1), dates[1]);
            Assert.Equal(new DateTime(2025, 6, 1), dates[2]);
        }

        [Fact]
        public void FirstDate_Monthly_DayOfMonthEqualsStartDay_ReturnsSameDay()
        {
            // Arrange: Start Mar 15 with DayOfMonth = 15
            var startDate = new DateTime(2025, 3, 15);

            // Act
            var dates = NextDateCalculator.ComputeNextDates(
                RecurringCadence.Monthly,
                interval: 1,
                startDate: startDate,
                dayOfMonth: 15,
                count: 3);

            // Assert: First occurrence is Mar 15
            Assert.Equal(3, dates.Count);
            Assert.Equal(new DateTime(2025, 3, 15), dates[0]);
            Assert.Equal(new DateTime(2025, 4, 15), dates[1]);
            Assert.Equal(new DateTime(2025, 5, 15), dates[2]);
        }

        [Fact]
        public void FirstDate_Monthly_DayOfMonthAfterStartDay_ReturnsSameMonth()
        {
            // Arrange: Start Mar 5 with DayOfMonth = 15
            // First occurrence should be Mar 15 (after start, same month)
            var startDate = new DateTime(2025, 3, 5);

            // Act
            var dates = NextDateCalculator.ComputeNextDates(
                RecurringCadence.Monthly,
                interval: 1,
                startDate: startDate,
                dayOfMonth: 15,
                count: 3);

            // Assert: First occurrence is Mar 15
            Assert.Equal(3, dates.Count);
            Assert.Equal(new DateTime(2025, 3, 15), dates[0]);
            Assert.Equal(new DateTime(2025, 4, 15), dates[1]);
            Assert.Equal(new DateTime(2025, 5, 15), dates[2]);
        }

        [Fact]
        public void FirstDate_Quarterly_DayOfMonthBeforeStartDay_AdvancesOneQuarter()
        {
            // Arrange: Start Mar 15 with DayOfMonth = 1
            var startDate = new DateTime(2025, 3, 15);

            // Act
            var dates = NextDateCalculator.ComputeNextDates(
                RecurringCadence.Quarterly,
                interval: 1,
                startDate: startDate,
                dayOfMonth: 1,
                count: 3);

            // Assert: First occurrence is Jun 1 (3 months later)
            Assert.Equal(3, dates.Count);
            Assert.Equal(new DateTime(2025, 6, 1), dates[0]);
            Assert.Equal(new DateTime(2025, 9, 1), dates[1]);
            Assert.Equal(new DateTime(2025, 12, 1), dates[2]);
        }

        [Fact]
        public void FirstDate_Yearly_DayOfMonthBeforeStartDay_AdvancesOneYear()
        {
            // Arrange: Start Jun 15 with DayOfMonth = 1, running yearly
            var startDate = new DateTime(2025, 6, 15);

            // Act
            var dates = NextDateCalculator.ComputeNextDates(
                RecurringCadence.Yearly,
                interval: 1,
                startDate: startDate,
                dayOfMonth: 1,
                count: 3);

            // Assert: First occurrence is Jun 1, 2026 (1 year later)
            Assert.Equal(3, dates.Count);
            Assert.Equal(new DateTime(2026, 6, 1), dates[0]);
            Assert.Equal(new DateTime(2027, 6, 1), dates[1]);
            Assert.Equal(new DateTime(2028, 6, 1), dates[2]);
        }

        #endregion

        #region Validation Tests

        [Fact]
        public void ComputeNextDates_ZeroInterval_ThrowsArgumentOutOfRangeException()
        {
            // Arrange
            var startDate = new DateTime(2025, 1, 15);

            // Act & Assert
            Assert.Throws<ArgumentOutOfRangeException>(() => NextDateCalculator.ComputeNextDates(
                RecurringCadence.Monthly,
                interval: 0,
                startDate: startDate,
                dayOfMonth: 15,
                count: 3));
        }

        [Fact]
        public void ComputeNextDates_NegativeInterval_ThrowsArgumentOutOfRangeException()
        {
            // Arrange
            var startDate = new DateTime(2025, 1, 15);

            // Act & Assert
            Assert.Throws<ArgumentOutOfRangeException>(() => NextDateCalculator.ComputeNextDates(
                RecurringCadence.Monthly,
                interval: -1,
                startDate: startDate,
                dayOfMonth: 15,
                count: 3));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(99)]
        public void ComputeNextDates_UnknownCadence_ThrowsArgumentOutOfRangeException(int cadenceValue)
        {
            var startDate = new DateTime(2025, 1, 15);

            Assert.Throws<ArgumentOutOfRangeException>(() => NextDateCalculator.ComputeNextDates(
                (RecurringCadence)cadenceValue,
                interval: 1,
                startDate: startDate,
                dayOfMonth: 15,
                count: 3));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(99)]
        public void CountOverduePeriods_UnknownCadence_ThrowsArgumentOutOfRangeException(int cadenceValue)
        {
            var startDate = new DateTime(2025, 1, 15);

            Assert.Throws<ArgumentOutOfRangeException>(() => NextDateCalculator.CountOverduePeriods(
                nextRunDate: startDate,
                today: startDate.AddMonths(1),
                cadence: (RecurringCadence)cadenceValue,
                interval: 1,
                dayOfMonth: 15,
                startDate: startDate));
        }

        [Fact]
        public void ComputeNextDates_MonthlyWithoutDayOfMonth_ThrowsInvalidOperationException()
        {
            // Arrange
            var startDate = new DateTime(2025, 1, 15);

            // Act & Assert
            Assert.Throws<InvalidOperationException>(() => NextDateCalculator.ComputeNextDates(
                RecurringCadence.Monthly,
                interval: 1,
                startDate: startDate,
                dayOfMonth: null,
                count: 3));
        }

        [Fact]
        public void ComputeNextDates_QuarterlyWithoutDayOfMonth_ThrowsInvalidOperationException()
        {
            // Arrange
            var startDate = new DateTime(2025, 1, 15);

            // Act & Assert
            Assert.Throws<InvalidOperationException>(() => NextDateCalculator.ComputeNextDates(
                RecurringCadence.Quarterly,
                interval: 1,
                startDate: startDate,
                dayOfMonth: null,
                count: 3));
        }

        [Fact]
        public void ComputeNextDates_YearlyWithoutDayOfMonth_ThrowsInvalidOperationException()
        {
            // Arrange
            var startDate = new DateTime(2025, 1, 15);

            // Act & Assert
            Assert.Throws<InvalidOperationException>(() => NextDateCalculator.ComputeNextDates(
                RecurringCadence.Yearly,
                interval: 1,
                startDate: startDate,
                dayOfMonth: null,
                count: 3));
        }

        #endregion

        #region Service Period Tests

        [Fact]
        public void ServicePeriod_CurrentMonth()
        {
            // Arrange: Document date Jan 15
            var documentDate = new DateTime(2025, 1, 15);

            // Act
            var (periodStart, periodEnd) = NextDateCalculator.ComputeServicePeriod(
                documentDate,
                RecurringServicePeriodMode.CurrentMonth);

            // Assert: Jan 1 to Jan 31
            Assert.Equal(new DateTime(2025, 1, 1), periodStart);
            Assert.Equal(new DateTime(2025, 1, 31), periodEnd);
        }

        [Fact]
        public void ServicePeriod_PreviousMonth()
        {
            // Arrange: Document date Mar 10 (billing for February service)
            var documentDate = new DateTime(2025, 3, 10);

            // Act
            var (periodStart, periodEnd) = NextDateCalculator.ComputeServicePeriod(
                documentDate,
                RecurringServicePeriodMode.PreviousMonth);

            // Assert: Feb 1 to Feb 28
            Assert.Equal(new DateTime(2025, 2, 1), periodStart);
            Assert.Equal(new DateTime(2025, 2, 28), periodEnd);
        }

        [Fact]
        public void ServicePeriodCrossesMonthBoundary_DifferentMonths_ReturnsTrue()
        {
            // Arrange: Period Jan 15 to Feb 15
            var periodStart = new DateTime(2025, 1, 15);
            var periodEnd = new DateTime(2025, 2, 15);

            // Act
            var crosses = NextDateCalculator.ServicePeriodCrossesMonthBoundary(periodStart, periodEnd);

            // Assert: True - crosses month boundary
            Assert.True(crosses);
        }

        [Fact]
        public void ServicePeriodCrossesMonthBoundary_SameMonth_ReturnsFalse()
        {
            // Arrange: Period Jan 1 to Jan 31
            var periodStart = new DateTime(2025, 1, 1);
            var periodEnd = new DateTime(2025, 1, 31);

            // Act
            var crosses = NextDateCalculator.ServicePeriodCrossesMonthBoundary(periodStart, periodEnd);

            // Assert: False - within same month
            Assert.False(crosses);
        }

        #endregion

        #region Overdue Periods Tests

        [Fact]
        public void CountOverduePeriods_OnSchedule_ReturnsZero()
        {
            // Arrange: Next run date is in the future
            var startDate = new DateTime(2025, 1, 1);
            var nextRunDate = new DateTime(2025, 2, 15);
            var today = new DateTime(2025, 1, 20);

            // Act
            var overdue = NextDateCalculator.CountOverduePeriods(
                nextRunDate,
                today,
                RecurringCadence.Monthly,
                interval: 1,
                dayOfMonth: 15,
                startDate: startDate);

            // Assert: No overdue periods
            Assert.Equal(0, overdue);
        }

        [Fact]
        public void CountOverduePeriods_TwoPeriodsBehind_ReturnsTwo()
        {
            // Arrange: Next run date was Dec 15, today is Feb 20
            var startDate = new DateTime(2025, 1, 1);
            var nextRunDate = new DateTime(2025, 1, 15);
            var today = new DateTime(2025, 2, 20);

            // Act
            var overdue = NextDateCalculator.CountOverduePeriods(
                nextRunDate,
                today,
                RecurringCadence.Monthly,
                interval: 1,
                dayOfMonth: 15,
                startDate: startDate);

            // Assert: 2 overdue periods (Jan 15, Feb 15)
            Assert.Equal(2, overdue);
        }

        [Fact]
        public void CountOverduePeriods_SixPeriodsBehind_ReturnsSix()
        {
            // Arrange: Next run date was Sep 15, today is Feb 20
            var startDate = new DateTime(2024, 9, 1);
            var nextRunDate = new DateTime(2024, 9, 15);
            var today = new DateTime(2025, 2, 20);

            // Act
            var overdue = NextDateCalculator.CountOverduePeriods(
                nextRunDate,
                today,
                RecurringCadence.Monthly,
                interval: 1,
                dayOfMonth: 15,
                startDate: startDate);

            // Assert: 6 overdue periods (Sep, Oct, Nov, Dec, Jan, Feb)
            Assert.Equal(6, overdue);
        }

        [Fact]
        public void CountOverduePeriods_ZeroInterval_ThrowsArgumentOutOfRangeException()
        {
            // Arrange
            var startDate = new DateTime(2025, 1, 1);
            var nextRunDate = new DateTime(2025, 1, 15);
            var today = new DateTime(2025, 2, 20);

            // Act & Assert
            Assert.Throws<ArgumentOutOfRangeException>(() => NextDateCalculator.CountOverduePeriods(
                nextRunDate,
                today,
                RecurringCadence.Monthly,
                interval: 0,
                dayOfMonth: 15,
                startDate: startDate));
        }

        #endregion

        #region AdjustToDayOfMonth Edge Cases

        [Fact]
        public void AdjustToDayOfMonth_Day32_ClampedToLastDay()
        {
            // Arrange: Requesting day 32 in January
            var date = new DateTime(2025, 1, 1);

            // Act: Day 32 should clamp to 31
            var result = NextDateCalculator.AdjustToDayOfMonth(date, 32);

            // Assert: Jan 31
            Assert.Equal(new DateTime(2025, 1, 31), result);
        }

        [Fact]
        public void AdjustToDayOfMonth_Day0_ThrowsArgumentOutOfRangeException()
        {
            // Arrange
            var date = new DateTime(2025, 1, 15);

            // Act & Assert - Day 0 should throw
            Assert.Throws<ArgumentOutOfRangeException>(() => NextDateCalculator.AdjustToDayOfMonth(date, 0));
        }

        [Fact]
        public void AdjustToDayOfMonth_NegativeDay_ThrowsArgumentOutOfRangeException()
        {
            // Arrange
            var date = new DateTime(2025, 1, 15);

            // Act & Assert - Negative day should throw
            Assert.Throws<ArgumentOutOfRangeException>(() => NextDateCalculator.AdjustToDayOfMonth(date, -1));
        }

        #endregion

        #region IsLastDayOfMonth Tests

        [Fact]
        public void IsLastDayOfMonth_Jan31_ReturnsTrue()
        {
            var result = NextDateCalculator.IsLastDayOfMonth(new DateTime(2025, 1, 31));
            Assert.True(result);
        }

        [Fact]
        public void IsLastDayOfMonth_Jan30_ReturnsFalse()
        {
            var result = NextDateCalculator.IsLastDayOfMonth(new DateTime(2025, 1, 30));
            Assert.False(result);
        }

        [Fact]
        public void IsLastDayOfMonth_Feb28_NonLeapYear_ReturnsTrue()
        {
            var result = NextDateCalculator.IsLastDayOfMonth(new DateTime(2025, 2, 28));
            Assert.True(result);
        }

        [Fact]
        public void IsLastDayOfMonth_Feb29_LeapYear_ReturnsTrue()
        {
            var result = NextDateCalculator.IsLastDayOfMonth(new DateTime(2024, 2, 29));
            Assert.True(result);
        }

        #endregion

        #region Cadence-aware service period (#482 follow-up)

        [Fact]
        public void ServicePeriod_Weekly_IsTheWeek_NotTheMonth()
        {
            // A weekly run bills its own week, so two runs in one month get distinct periods.
            var (start, end) = NextDateCalculator.ComputeServicePeriod(
                new DateTime(2026, 6, 1), RecurringServicePeriodMode.CurrentMonth,
                RecurringCadence.Weekly, interval: 1);

            Assert.Equal(new DateTime(2026, 6, 1), start);
            Assert.Equal(new DateTime(2026, 6, 7), end);
        }

        [Fact]
        public void ServicePeriod_Weekly_DistinctRunsHaveDistinctPeriods()
        {
            var first = NextDateCalculator.ComputeServicePeriod(
                new DateTime(2026, 6, 1), RecurringServicePeriodMode.CurrentMonth, RecurringCadence.Weekly, 1);
            var second = NextDateCalculator.ComputeServicePeriod(
                new DateTime(2026, 6, 8), RecurringServicePeriodMode.CurrentMonth, RecurringCadence.Weekly, 1);

            Assert.NotEqual(first, second);
        }

        [Fact]
        public void ServicePeriod_Weekly_StraddlingMonthEnd_CrossesBoundary()
        {
            // A week that spans month-end should be detectable as crossing a month boundary (so it downgrades).
            var (start, end) = NextDateCalculator.ComputeServicePeriod(
                new DateTime(2026, 6, 29), RecurringServicePeriodMode.CurrentMonth, RecurringCadence.Weekly, 1);

            Assert.Equal(new DateTime(2026, 6, 29), start);
            Assert.Equal(new DateTime(2026, 7, 5), end);
            Assert.True(NextDateCalculator.ServicePeriodCrossesMonthBoundary(start, end));
        }

        [Fact]
        public void ServicePeriod_Monthly_IsTheCalendarMonth()
        {
            var (start, end) = NextDateCalculator.ComputeServicePeriod(
                new DateTime(2026, 6, 15), RecurringServicePeriodMode.CurrentMonth, RecurringCadence.Monthly, 1);

            Assert.Equal(new DateTime(2026, 6, 1), start);
            Assert.Equal(new DateTime(2026, 6, 30), end);
        }

        [Fact]
        public void ServicePeriod_Quarterly_SpansThreeMonths()
        {
            var (start, end) = NextDateCalculator.ComputeServicePeriod(
                new DateTime(2026, 1, 15), RecurringServicePeriodMode.CurrentMonth, RecurringCadence.Quarterly, 1);

            Assert.Equal(new DateTime(2026, 1, 1), start);
            Assert.Equal(new DateTime(2026, 3, 31), end);
            Assert.True(NextDateCalculator.ServicePeriodCrossesMonthBoundary(start, end));
        }

        [Fact]
        public void ServicePeriod_Yearly_SpansTwelveMonths()
        {
            var (start, end) = NextDateCalculator.ComputeServicePeriod(
                new DateTime(2026, 1, 15), RecurringServicePeriodMode.CurrentMonth, RecurringCadence.Yearly, 1);

            Assert.Equal(new DateTime(2026, 1, 1), start);
            Assert.Equal(new DateTime(2026, 12, 31), end);
        }

        [Fact]
        public void ServicePeriod_Monthly_Interval2_SpansTwoMonths()
        {
            // Every two months: the recorded period must cover both months, not just the document month.
            var (start, end) = NextDateCalculator.ComputeServicePeriod(
                new DateTime(2026, 6, 15), RecurringServicePeriodMode.CurrentMonth, RecurringCadence.Monthly, 2);

            Assert.Equal(new DateTime(2026, 6, 1), start);
            Assert.Equal(new DateTime(2026, 7, 31), end);
        }

        [Fact]
        public void ServicePeriod_Monthly_PreviousMonth_ShiftsBackOneSpan()
        {
            // interval 2, PreviousMonth: the span is the two months immediately before the document month.
            var (start, end) = NextDateCalculator.ComputeServicePeriod(
                new DateTime(2026, 6, 15), RecurringServicePeriodMode.PreviousMonth, RecurringCadence.Monthly, 2);

            Assert.Equal(new DateTime(2026, 4, 1), start);
            Assert.Equal(new DateTime(2026, 5, 31), end);
        }

        [Fact]
        public void ServicePeriod_Quarterly_PreviousMonth_IsThePreviousQuarter()
        {
            // An April quarterly run billing the previous period must record Jan-Mar, not a March-anchored quarter.
            var (start, end) = NextDateCalculator.ComputeServicePeriod(
                new DateTime(2026, 4, 1), RecurringServicePeriodMode.PreviousMonth, RecurringCadence.Quarterly, 1);

            Assert.Equal(new DateTime(2026, 1, 1), start);
            Assert.Equal(new DateTime(2026, 3, 31), end);
        }

        [Fact]
        public void ServicePeriod_Yearly_PreviousMonth_IsThePriorYearSpan()
        {
            var (start, end) = NextDateCalculator.ComputeServicePeriod(
                new DateTime(2026, 1, 15), RecurringServicePeriodMode.PreviousMonth, RecurringCadence.Yearly, 1);

            Assert.Equal(new DateTime(2025, 1, 1), start);
            Assert.Equal(new DateTime(2025, 12, 31), end);
        }

        [Fact]
        public void ServicePeriod_Weekly_PreviousMonth_IsThePriorWeekSpan()
        {
            // PreviousMonth is generalized to "the previous period"; for weekly that is the prior interval weeks.
            var (start, end) = NextDateCalculator.ComputeServicePeriod(
                new DateTime(2026, 6, 15), RecurringServicePeriodMode.PreviousMonth, RecurringCadence.Weekly, 1);

            Assert.Equal(new DateTime(2026, 6, 8), start);
            Assert.Equal(new DateTime(2026, 6, 14), end);
        }

        [Fact]
        public void TryGetSeriesPosition_FindsValidDate_AndRejectsOffCalendar()
        {
            var start = new DateTime(2026, 1, 1);

            // Day-1 monthly: Jan is position 0, Apr is position 3.
            Assert.Equal(0, NextDateCalculator.TryGetSeriesPosition(RecurringCadence.Monthly, 1, start, 1, new DateTime(2026, 1, 1)));
            Assert.Equal(3, NextDateCalculator.TryGetSeriesPosition(RecurringCadence.Monthly, 1, start, 1, new DateTime(2026, 4, 1)));

            // The 15th is never produced by a day-1 schedule.
            Assert.Equal(-1, NextDateCalculator.TryGetSeriesPosition(RecurringCadence.Monthly, 1, start, 1, new DateTime(2026, 4, 15)));
        }

        [Fact]
        public void TryGetSeriesPosition_FarFutureWeeklyDate_IsFound()
        {
            // ~15 years of weekly runs is well past the old fixed 600 horizon; it must still resolve.
            var start = new DateTime(2026, 1, 1);
            var target = start.AddDays(7 * 800); // position 800

            Assert.Equal(800, NextDateCalculator.TryGetSeriesPosition(RecurringCadence.Weekly, 1, start, null, target));
        }

        #endregion
    }
}
