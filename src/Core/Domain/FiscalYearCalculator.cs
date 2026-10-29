using System;
using System.Globalization;

namespace Core.Domain
{
    /// <summary>Derives the fiscal year that contains a books start date.</summary>
    public static class FiscalYearCalculator
    {
        public static int StartMonth(FiscalYearPattern pattern, int? customStartMonth)
        {
            switch (pattern)
            {
                case FiscalYearPattern.Calendar: return 1;
                case FiscalYearPattern.AprMar: return 4;
                case FiscalYearPattern.JulJun: return 7;
                case FiscalYearPattern.Custom:
                    if (customStartMonth is int m && m >= 1 && m <= 12)
                        return m;
                    throw new ArgumentOutOfRangeException(nameof(customStartMonth), "Custom fiscal year requires a start month from 1 to 12.");
                default:
                    throw new ArgumentOutOfRangeException(nameof(pattern));
            }
        }

        public static (DateTime Start, DateTime End, string Code) Containing(DateTime date, FiscalYearPattern pattern, int? customStartMonth)
        {
            var month = StartMonth(pattern, customStartMonth);
            var startYear = date.Month >= month ? date.Year : date.Year - 1;
            var start = new DateTime(startYear, month, 1, 0, 0, 0, DateTimeKind.Utc);
            var end = start.AddYears(1).AddTicks(-1);
            var code = month == 1
                ? "FY" + startYear.ToString(CultureInfo.InvariantCulture)
                : "FY" + startYear.ToString(CultureInfo.InvariantCulture) + "-" + ((startYear + 1) % 100).ToString("00", CultureInfo.InvariantCulture);
            return (start, end, code);
        }
    }
}
