using System;
using System.Globalization;

namespace Core.Domain
{
    /// <summary>A posting is dated before the company's books start date.</summary>
    public class BooksStartViolationException : InvalidOperationException
    {
        public BooksStartViolationException(DateTime entryDate, DateTime booksStartDate)
            : base($"Entry date {entryDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)} is before the books start date {booksStartDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.")
        {
        }
    }
}
