// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;

namespace Core.Domain.TaxSystem
{
    public sealed record TaxPeriod(DateTime Start, DateTime End, string Label);

    /// <summary>
    /// Pure tax-period grid and label calculator. Tax periods are calendar-month based and independent
    /// of fiscal-year boundaries; quarter labels always use month ranges rather than "Q1"–"Q4".
    /// </summary>
    public static class TaxPeriodCalculator
    {
        private static readonly string[] EnglishMonths =
        {
            "Jan", "Feb", "Mar", "Apr", "May", "Jun",
            "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"
        };

        public static int DefaultQuarterCycleStartMonth(FiscalYearPattern pattern, int? fiscalStartMonth)
        {
            var fiscalStart = pattern switch
            {
                FiscalYearPattern.Calendar => 1,
                FiscalYearPattern.AprMar => 4,
                FiscalYearPattern.JulJun => 7,
                FiscalYearPattern.Custom => fiscalStartMonth is >= 1 and <= 12 ? fiscalStartMonth.Value : 1,
                _ => 1
            };
            return ((fiscalStart - 1) % 3) + 1;
        }

        public static int EffectiveCycleStart(CompanySetting setting)
        {
            if (setting == null) throw new ArgumentNullException(nameof(setting));
            if (setting.TaxQuarterCycleStartMonth is int cycle && cycle >= 1 && cycle <= 3)
                return cycle;
            return DefaultQuarterCycleStartMonth(setting.FiscalYearPattern, setting.FiscalYearStartMonth);
        }

        public static TaxPeriod Containing(DateTime date, TaxPeriodFrequency frequency, int cycleStart)
        {
            var d = date.Date;
            if (frequency == TaxPeriodFrequency.Monthly)
            {
                var start = new DateTime(d.Year, d.Month, 1);
                var end = new DateTime(d.Year, d.Month, DateTime.DaysInMonth(d.Year, d.Month));
                var label = $"{EnglishMonths[d.Month - 1]} {d.Year.ToString(CultureInfo.InvariantCulture)}";
                return new TaxPeriod(start, end, label);
            }

            if (frequency == TaxPeriodFrequency.Quarterly)
            {
                if (cycleStart < 1 || cycleStart > 3)
                    throw new ArgumentOutOfRangeException(nameof(cycleStart), "Quarter cycle start month must be 1, 2, or 3.");

                var offset = ((d.Month - cycleStart) % 3 + 3) % 3;
                var start = new DateTime(d.Year, d.Month, 1).AddMonths(-offset);
                var end = start.AddMonths(3).AddDays(-1);
                var startMonth = EnglishMonths[start.Month - 1];
                var endMonth = EnglishMonths[end.Month - 1];
                var label = start.Year == end.Year
                    ? $"{startMonth}\u2013{endMonth} {start.Year.ToString(CultureInfo.InvariantCulture)}"
                    : $"{startMonth} {start.Year.ToString(CultureInfo.InvariantCulture)}\u2013{endMonth} {end.Year.ToString(CultureInfo.InvariantCulture)}";
                return new TaxPeriod(start, end, label);
            }

            throw new ArgumentOutOfRangeException(nameof(frequency));
        }

        public static IReadOnlyList<TaxPeriod> Recent(DateTime today, TaxPeriodFrequency frequency, int cycleStart, int count)
        {
            if (count < 0)
                throw new ArgumentOutOfRangeException(nameof(count));
            if (count == 0)
                return Array.Empty<TaxPeriod>();

            var result = new List<TaxPeriod>(count);
            var current = Containing(today, frequency, cycleStart);
            result.Add(current);

            for (var i = 1; i < count; i++)
            {
                current = Containing(current.Start.AddDays(-1), frequency, cycleStart);
                result.Add(current);
            }

            return result;
        }

        public static bool IsWholePeriod(DateTime from, DateTime to, TaxPeriodFrequency frequency, int cycleStart)
        {
            var f = from.Date;
            var t = to.Date;
            if (f > t)
                return false;
            var period = Containing(f, frequency, cycleStart);
            return f == period.Start && t == period.End;
        }
    }
}
