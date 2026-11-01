using System;
using System.Collections.Generic;

namespace Core.Domain.Recurring.Calendar;

/// <summary>
/// Pure functions for computing next run dates from a schedule's calendar rules.
/// All dates are computed from the schedule start, never from "now" or the last success.
/// </summary>
public static class NextDateCalculator
{
    /// <summary>
    /// Computes the next N run dates from a schedule's start date.
    /// Used for preview and for advancing NextRunDate.
    /// </summary>
    /// <param name="cadence">How often to run.</param>
    /// <param name="interval">Interval multiplier (e.g., 2 for every 2 months).</param>
    /// <param name="startDate">The anchor date; weekday for weekly, day-of-month for others.</param>
    /// <param name="dayOfMonth">For monthly/quarterly/yearly: the day of month (1-31). Day 31 = last day of month.</param>
    /// <param name="count">How many dates to compute.</param>
    /// <param name="endDate">Optional end date; stops returning dates after this.</param>
    /// <param name="maxOccurrences">Optional max occurrences; stops after this many.</param>
    /// <returns>List of computed dates.</returns>
    public static IReadOnlyList<DateTime> ComputeNextDates(
        RecurringCadence cadence,
        int interval,
        DateTime startDate,
        int? dayOfMonth,
        int count,
        DateTime? endDate = null,
        int? maxOccurrences = null)
    {
        var dates = new List<DateTime>();
        var current = ComputeFirstDate(cadence, interval, startDate, dayOfMonth);
        var occurrenceCount = 0;

        while (dates.Count < count)
        {
            if (endDate.HasValue && current > endDate.Value)
                break;
            if (maxOccurrences.HasValue && occurrenceCount >= maxOccurrences.Value)
                break;

            dates.Add(current);
            occurrenceCount++;

            current = ComputeSubsequentDate(cadence, interval, current, dayOfMonth);
        }

        return dates;
    }

    /// <summary>
    /// Computes the first occurrence date from the schedule start.
    /// </summary>
    public static DateTime ComputeFirstDate(
        RecurringCadence cadence,
        int interval,
        DateTime startDate,
        int? dayOfMonth)
    {
        // The start date itself is the first occurrence (time component ignored)
        var date = startDate.Date;

        return cadence switch
        {
            RecurringCadence.Weekly => date,
            RecurringCadence.Monthly => AdjustToDayOfMonth(date, dayOfMonth),
            RecurringCadence.Quarterly => AdjustToDayOfMonth(date, dayOfMonth),
            RecurringCadence.Yearly => AdjustToDayOfMonth(date, dayOfMonth),
            _ => date
        };
    }

    /// <summary>
    /// Computes the next occurrence date after the given date.
    /// </summary>
    public static DateTime ComputeNextDate(
        RecurringCadence cadence,
        int interval,
        DateTime afterDate,
        int? dayOfMonth)
    {
        var next = ComputeSubsequentDate(cadence, interval, afterDate, dayOfMonth);
        return next;
    }

    /// <summary>
    /// Adjusts a date to the specified day of month, handling month-end edge cases.
    /// Day 31 means: last day of the month (31 Jan, 28/29 Feb, 31 Mar, 30 Apr, etc.)
    /// </summary>
    public static DateTime AdjustToDayOfMonth(DateTime date, int? dayOfMonth)
    {
        if (!dayOfMonth.HasValue || dayOfMonth.Value <= 0)
            return date;

        var requestedDay = dayOfMonth.Value;
        var daysInMonth = DateTime.DaysInMonth(date.Year, date.Month);

        // If requesting day 31 (or any day beyond the month's last day), use the last day
        var actualDay = Math.Min(requestedDay, daysInMonth);

        return new DateTime(date.Year, date.Month, actualDay);
    }

    /// <summary>
    /// Gets the last day of the given month.
    /// </summary>
    public static DateTime GetLastDayOfMonth(DateTime date)
    {
        var daysInMonth = DateTime.DaysInMonth(date.Year, date.Month);
        return new DateTime(date.Year, date.Month, daysInMonth);
    }

    /// <summary>
    /// Determines if a date is the last day of its month.
    /// </summary>
    public static bool IsLastDayOfMonth(DateTime date)
    {
        return date.Day == DateTime.DaysInMonth(date.Year, date.Month);
    }

    /// <summary>
    /// Computes the service period start and end dates for a given document date.
    /// </summary>
    /// <param name="documentDate">The invoice or bill date.</param>
    /// <param name="servicePeriodMode">Whether service is in current month or previous month.</param>
    /// <returns>Tuple of (periodStart, periodEnd).</returns>
    public static (DateTime PeriodStart, DateTime PeriodEnd) ComputeServicePeriod(
        DateTime documentDate,
        RecurringServicePeriodMode servicePeriodMode)
    {
        return servicePeriodMode switch
        {
            RecurringServicePeriodMode.CurrentMonth => (
                new DateTime(documentDate.Year, documentDate.Month, 1),
                GetLastDayOfMonth(documentDate)
            ),
            RecurringServicePeriodMode.PreviousMonth => (
                new DateTime(documentDate.Year, documentDate.Month, 1).AddMonths(-1),
                GetLastDayOfMonth(documentDate.AddMonths(-1))
            ),
            _ => (
                new DateTime(documentDate.Year, documentDate.Month, 1),
                GetLastDayOfMonth(documentDate)
            )
        };
    }

    /// <summary>
    /// Checks if a service period crosses a calendar month boundary.
    /// Used to determine if auto-issue/approve is allowed.
    /// </summary>
    public static bool ServicePeriodCrossesMonthBoundary(DateTime periodStart, DateTime periodEnd)
    {
        return periodStart.Month != periodEnd.Month || periodStart.Year != periodEnd.Year;
    }

    /// <summary>
    /// Computes how many periods are overdue for a schedule.
    /// </summary>
    /// <param name="nextRunDate">The scheduled next run date.</param>
    /// <param name="today">Today's date (in workspace timezone).</param>
    /// <param name="cadence">The cadence to determine period boundaries.</param>
    /// <param name="interval">The interval multiplier.</param>
    /// <param name="dayOfMonth">The day of month for monthly cadences.</param>
    /// <returns>Number of missed periods (0 if on schedule).</returns>
    public static int CountOverduePeriods(
        DateTime nextRunDate,
        DateTime today,
        RecurringCadence cadence,
        int interval,
        int? dayOfMonth)
    {
        if (nextRunDate > today)
            return 0;

        var count = 0;
        var current = nextRunDate;

        while (current <= today)
        {
            count++;
            current = ComputeSubsequentDate(cadence, interval, current, dayOfMonth);
        }

        return count;
    }

    // Private helper to compute subsequent date
    private static DateTime ComputeSubsequentDate(
        RecurringCadence cadence,
        int interval,
        DateTime currentDate,
        int? dayOfMonth)
    {
        return cadence switch
        {
            RecurringCadence.Weekly => currentDate.AddDays(7 * interval),
            RecurringCadence.Monthly => AdjustToDayOfMonth(currentDate.AddMonths(interval), dayOfMonth),
            RecurringCadence.Quarterly => AdjustToDayOfMonth(currentDate.AddMonths(3 * interval), dayOfMonth),
            RecurringCadence.Yearly => AdjustToDayOfMonth(currentDate.AddYears(interval), dayOfMonth),
            _ => currentDate.AddDays(1)
        };
    }
}
