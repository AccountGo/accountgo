using System;

namespace Core.Domain
{
    /// <summary>
    /// Payment terms count business days: Monday to Friday, with no holiday calendar. Must match addBusinessDays in
    /// src/Web/src/lib/businessDays.ts, which shows the same due date while the invoice is edited.
    /// </summary>
    public static class BusinessDays
    {
        /// <summary>The date <paramref name="days"/> business days after <paramref name="start"/>; 0 is the start date itself.</summary>
        public static DateTime Add(DateTime start, int days)
        {
            var date = start.Date;
            var remaining = Math.Max(0, days);
            while (remaining > 0)
            {
                date = date.AddDays(1);
                if (date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
                    remaining--;
            }
            return date;
        }
    }
}
