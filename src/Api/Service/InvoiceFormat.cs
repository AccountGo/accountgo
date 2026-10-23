using System;
using System.Collections.Generic;
using System.Globalization;

namespace Api.Service
{
    /// <summary>How invoice numbers, amounts and dates read on everything the customer receives (the PDF and the email).</summary>
    public static class InvoiceFormat
    {
        // Matches the web app's display symbols (src/Web/src/lib/currency.ts).
        private static readonly Dictionary<string, string> CurrencySymbols = new(StringComparer.OrdinalIgnoreCase)
        {
            ["USD"] = "$",
            ["PHP"] = "₱",
            ["CAD"] = "CA$",
            ["EUR"] = "€",
            ["GBP"] = "£",
            ["AUD"] = "AU$",
            ["SGD"] = "S$",
        };

        public static string CurrencyCode(string? code) =>
            string.IsNullOrWhiteSpace(code) ? "USD" : code.Trim().ToUpperInvariant();

        public static string Number(Dto.Sales.SalesInvoice invoice) =>
            string.IsNullOrWhiteSpace(invoice.No) ? invoice.Id.ToString(CultureInfo.InvariantCulture) : invoice.No!;

        public static string Money(decimal amount, string currencyCode)
        {
            var symbol = CurrencySymbols.TryGetValue(currencyCode, out var known) ? known : "$";
            var sign = amount < 0 ? "−" : string.Empty;
            return $"{sign}{symbol}{Math.Abs(amount).ToString("#,##0.00", CultureInfo.InvariantCulture)}";
        }

        public static string Date(DateTime date) => date.ToString("MMM d, yyyy", CultureInfo.InvariantCulture);
    }
}
