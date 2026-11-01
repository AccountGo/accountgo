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
    /// <param name="interval">Interval multiplier (e.g., 2 for every 2 months). Must be >= 1.</param>
    /// <param name="startDate">The anchor date; weekday for weekly, day-of-month for others.</param>
    /// <param name="dayOfMonth">For monthly/quarterly/yearly: the day of month (1-31). Day 31 = last day of month. Required for non-weekly cadences.</param>
    /// <param name="count">How many dates to compute.</param>
    /// <param name="endDate">Optional end date; stops returning dates after this.</param>
    /// <param name="maxOccurrences">Optional max occurrences; stops after this many.</param>
    /// <returns>List of computed dates.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when interval is less than 1.</exception>
    /// <exception cref="InvalidOperationException">Thrown when dayOfMonth is not set for non-weekly cadence.</exception>
    public static IReadOnlyList<DateTime> ComputeNextDates(
        RecurringCadence cadence,
        int interval,
        DateTime startDate,
        int? dayOfMonth,
        int count,
        DateTime? endDate = null,
        int? maxOccurrences = null)
    {
        if (interval < 1)
            throw new ArgumentOutOfRangeException(nameof(interval), "Interval must be at least 1.");
        
        ValidateDayOfMonthForCadence(cadence, dayOfMonth, startDate);

        var dates = new List<DateTime>();
        var position = 0;

        while (dates.Count < count)
        {
            var current = ComputeDateAtPosition(cadence, interval, startDate, dayOfMonth, position);
            
            if (endDate.HasValue && current > endDate.Value)
                break;
            if (maxOccurrences.HasValue && position >= maxOccurrences.Value)
                break;

            dates.Add(current);
            position++;
        }

        return dates;
    }

    /// <summary>
    /// Computes the first occurrence date from the schedule start.
    /// The first occurrence is always on or after the start date.
    /// </summary>
    public static DateTime ComputeFirstDate(
        RecurringCadence cadence,
        int interval,
        DateTime startDate,
        int? dayOfMonth)
    {
        if (interval < 1)
            throw new ArgumentOutOfRangeException(nameof(interval), "Interval must be at least 1.");
        
        ValidateDayOfMonthForCadence(cadence, dayOfMonth, startDate);

        // The start date itself is the first occurrence (time component ignored)
        var date = startDate.Date;

        var adjusted = cadence switch
        {
            RecurringCadence.Weekly => date,
            RecurringCadence.Monthly => AdjustToDayOfMonth(date, dayOfMonth!.Value),
            RecurringCadence.Quarterly => AdjustToDayOfMonth(date, dayOfMonth!.Value),
            RecurringCadence.Yearly => AdjustToDayOfMonth(date, dayOfMonth!.Value),
            _ => date
        };

        // If the adjusted day is before the start date, advance one interval.
        // Example: Start 15 March with DayOfMonth 1 -> adjusted to 1 March -> advance to 1 April.
        if (adjusted < date)
        {
            adjusted = cadence switch
            {
                RecurringCadence.Weekly => adjusted.AddDays(7 * interval),
                RecurringCadence.Monthly => AdjustToDayOfMonth(adjusted.AddMonths(interval), dayOfMonth!.Value),
                RecurringCadence.Quarterly => AdjustToDayOfMonth(adjusted.AddMonths(3 * interval), dayOfMonth!.Value),
                RecurringCadence.Yearly => AdjustToDayOfMonth(adjusted.AddYears(interval), dayOfMonth!.Value),
                _ => adjusted.AddDays(interval)
            };
        }

        return adjusted;
    }

    /// <summary>
    /// Computes the next occurrence date after the given date.
    /// The next date is computed from the schedule's anchor (start date), not from afterDate,
    /// to ensure the series does not shift when runs are skipped or retried.
    /// </summary>
    public static DateTime ComputeNextDate(
        RecurringCadence cadence,
        int interval,
        DateTime afterDate,
        int? dayOfMonth,
        DateTime startDate)
    {
        if (interval < 1)
            throw new ArgumentOutOfRangeException(nameof(interval), "Interval must be at least 1.");
        
        ValidateDayOfMonthForCadence(cadence, dayOfMonth, startDate);

        // Find the position of the first date AFTER afterDate
        var position = 0;
        var current = ComputeDateAtPosition(cadence, interval, startDate, dayOfMonth, position);
        
        while (current <= afterDate)
        {
            position++;
            current = ComputeDateAtPosition(cadence, interval, startDate, dayOfMonth, position);
        }
        
        return current;
    }

    /// <summary>
    /// Finds the zero-based position of <paramref name="target"/> in the schedule's series, or -1 if the target is
    /// not a produced date. Walks only as far as the target (the series is ascending), so there is no fixed horizon
    /// that would reject otherwise-valid far-future dates; a large safety cap guards against pathological input.
    /// </summary>
    public static int TryGetSeriesPosition(
        RecurringCadence cadence,
        int interval,
        DateTime startDate,
        int? dayOfMonth,
        DateTime target)
    {
        if (interval < 1)
            throw new ArgumentOutOfRangeException(nameof(interval), "Interval must be at least 1.");

        ValidateDayOfMonthForCadence(cadence, dayOfMonth, startDate);

        var targetDate = target.Date;
        const int safetyCap = 1_000_000; // far beyond any real schedule; prevents a runaway loop.

        for (var position = 0; position < safetyCap; position++)
        {
            var current = ComputeDateAtPosition(cadence, interval, startDate, dayOfMonth, position).Date;
            if (current == targetDate)
                return position;
            if (current > targetDate)
                return -1; // ascending series passed the target without hitting it.
        }

        return -1;
    }

    /// <summary>
    /// Adjusts a date to the specified day of month, handling month-end edge cases.
    /// Day 31 means: last day of the month (31 Jan, 28/29 Feb, 31 Mar, 30 Apr, etc.)
    /// Values above 31 are clamped to the month's last day.
    /// </summary>
    public static DateTime AdjustToDayOfMonth(DateTime date, int dayOfMonth)
    {
        if (dayOfMonth <= 0)
            throw new ArgumentOutOfRangeException(nameof(dayOfMonth), "Day of month must be at least 1.");

        var daysInMonth = DateTime.DaysInMonth(date.Year, date.Month);

        // If requesting day 31 (or any day beyond the month's last day), use the last day
        var actualDay = Math.Min(dayOfMonth, daysInMonth);

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
    /// Computes the service period start and end dates for a given document date (monthly semantics).
    /// Overload kept for callers that bill a calendar month; see the cadence-aware overload for weekly,
    /// quarterly, and yearly spans.
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
    /// Computes the service period for a document date using the schedule's cadence and interval, so the period
    /// reflects what the run actually bills: a week-span for weekly, the interval's months for monthly, the
    /// quarter(s) for quarterly, the year(s) for yearly. A truthful period is what lets the auto-issue same-month
    /// guard distinguish a run that sits inside one month from a multi-month run, and stops two runs from recording
    /// the same period.
    ///
    /// <paramref name="servicePeriodMode"/> is generalized from its monthly origin: <c>CurrentMonth</c> is the
    /// current period for the cadence, and <c>PreviousMonth</c> is the period immediately before it (one whole
    /// cadence span earlier), so an April quarterly run in PreviousMonth mode bills the previous quarter, not a
    /// March-anchored quarter.
    /// </summary>
    public static (DateTime PeriodStart, DateTime PeriodEnd) ComputeServicePeriod(
        DateTime documentDate,
        RecurringServicePeriodMode servicePeriodMode,
        RecurringCadence cadence,
        int interval)
    {
        if (interval < 1)
            interval = 1;

        var previous = servicePeriodMode == RecurringServicePeriodMode.PreviousMonth;

        switch (cadence)
        {
            case RecurringCadence.Weekly:
            {
                // The run bills interval weeks. PreviousMonth shifts back one whole span (the prior interval weeks).
                var spanDays = 7 * interval;
                var start = documentDate.Date.AddDays(previous ? -spanDays : 0);
                var end = start.AddDays(spanDays - 1);
                return (start, end);
            }

            case RecurringCadence.Monthly:
            {
                // interval months, anchored at the document month; PreviousMonth shifts back one whole span.
                var firstOfMonth = new DateTime(documentDate.Year, documentDate.Month, 1);
                var start = firstOfMonth.AddMonths(previous ? -interval : 0);
                var end = GetLastDayOfMonth(start.AddMonths(interval - 1));
                return (start, end);
            }

            case RecurringCadence.Quarterly:
            {
                var spanMonths = 3 * interval;
                var firstOfMonth = new DateTime(documentDate.Year, documentDate.Month, 1);
                var start = firstOfMonth.AddMonths(previous ? -spanMonths : 0);
                var end = GetLastDayOfMonth(start.AddMonths(spanMonths - 1));
                return (start, end);
            }

            case RecurringCadence.Yearly:
            {
                var spanMonths = 12 * interval;
                var firstOfMonth = new DateTime(documentDate.Year, documentDate.Month, 1);
                var start = firstOfMonth.AddMonths(previous ? -spanMonths : 0);
                var end = GetLastDayOfMonth(start.AddMonths(spanMonths - 1));
                return (start, end);
            }

            default:
                return ComputeServicePeriod(documentDate, servicePeriodMode);
        }
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
    /// <param name="interval">The interval multiplier. Must be >= 1.</param>
    /// <param name="dayOfMonth">The day of month for monthly cadences.</param>
    /// <param name="startDate">The schedule's start date (anchor for the series).</param>
    /// <returns>Number of missed periods (0 if on schedule).</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when interval is less than 1.</exception>
    public static int CountOverduePeriods(
        DateTime nextRunDate,
        DateTime today,
        RecurringCadence cadence,
        int interval,
        int? dayOfMonth,
        DateTime startDate)
    {
        if (interval < 1)
            throw new ArgumentOutOfRangeException(nameof(interval), "Interval must be at least 1.");

        ValidateDayOfMonthForCadence(cadence, dayOfMonth, startDate);

        if (nextRunDate > today)
            return 0;

        // Count how many scheduled dates are in the range [nextRunDate, today]
        var count = 0;
        var position = 0;
        var current = ComputeDateAtPosition(cadence, interval, startDate, dayOfMonth, position);
        
        // Safety limit to prevent infinite loops
        var maxIterations = 10000;

        while (current <= today && count < maxIterations)
        {
            if (current >= nextRunDate)
                count++;
            position++;
            current = ComputeDateAtPosition(cadence, interval, startDate, dayOfMonth, position);
        }
        
        return count;
    }

    /// <summary>
    /// Validates the cadence and whether its calendar rules have the required day-of-month.
    /// </summary>
    private static void ValidateDayOfMonthForCadence(RecurringCadence cadence, int? dayOfMonth, DateTime startDate)
    {
        if (cadence != RecurringCadence.Weekly
            && cadence != RecurringCadence.Monthly
            && cadence != RecurringCadence.Quarterly
            && cadence != RecurringCadence.Yearly)
        {
            throw new ArgumentOutOfRangeException(nameof(cadence), cadence, "Cadence is not supported.");
        }

        if (cadence != RecurringCadence.Weekly && !dayOfMonth.HasValue)
            throw new InvalidOperationException(
                $"DayOfMonth is required for {cadence} cadence. " +
                $"Schedule starting {startDate:yyyy-MM-dd} must specify which day of the month to run.");
    }

    /// <summary>
    /// Computes the date at a specific position in the series (0-indexed from start).
    /// For non-weekly cadences, if the adjusted day falls before the start date, the series
    /// is anchored to the first valid date, not the original start date.
    /// </summary>
    private static DateTime ComputeDateAtPosition(
        RecurringCadence cadence,
        int interval,
        DateTime startDate,
        int? dayOfMonth,
        int position)
    {
        // For weekly cadence, no day-of-month adjustment needed
        if (cadence == RecurringCadence.Weekly)
        {
            return startDate.AddDays(7 * interval * position);
        }

        // For monthly/quarterly/yearly, first compute what the first valid date is
        var firstBaseDate = startDate;
        var firstAdjusted = AdjustToDayOfMonth(firstBaseDate, dayOfMonth!.Value);
        
        // If the first adjusted date is before start, shift the anchor by one interval
        if (firstAdjusted < startDate)
        {
            firstBaseDate = cadence switch
            {
                RecurringCadence.Monthly => startDate.AddMonths(interval),
                RecurringCadence.Quarterly => startDate.AddMonths(3 * interval),
                RecurringCadence.Yearly => startDate.AddYears(interval),
                _ => startDate.AddDays(interval)
            };
            firstAdjusted = AdjustToDayOfMonth(firstBaseDate, dayOfMonth!.Value);
        }

        // Now compute the date at position, anchored to the first valid date
        var baseDate = cadence switch
        {
            RecurringCadence.Monthly => firstBaseDate.AddMonths(interval * position),
            RecurringCadence.Quarterly => firstBaseDate.AddMonths(3 * interval * position),
            RecurringCadence.Yearly => firstBaseDate.AddYears(interval * position),
            _ => firstBaseDate.AddDays(position)
        };

        return AdjustToDayOfMonth(baseDate, dayOfMonth!.Value);
    }
}
