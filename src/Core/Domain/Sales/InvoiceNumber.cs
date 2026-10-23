#nullable enable
using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace Core.Domain.Sales
{
    /// <summary>
    /// Sales invoice numbering. A draft has no number unless one is typed in; the invoice takes the next number from
    /// the workspace sequence when it is issued, so the issued series has no gaps. A typed number is kept as typed,
    /// and a number never changes once the invoice is issued.
    /// </summary>
    public static class InvoiceNumber
    {
        public const int MaxLength = 30;

        private static readonly Regex Allowed = new(@"^[A-Za-z0-9][A-Za-z0-9\-_/.]*$", RegexOptions.CultureInvariant);

        /// <summary>Trims and drops a leading '#'. Blank means "number it automatically".</summary>
        public static string? Normalize(string? value)
        {
            var text = value?.Trim().TrimStart('#').Trim();
            return string.IsNullOrEmpty(text) ? null : text;
        }

        /// <summary>Issued invoices carry a number. Drafts, and drafts that were voided, need not.</summary>
        public static bool IsRequired(SalesInvoiceStatus? status) =>
            status != SalesInvoiceStatus.Draft && status != SalesInvoiceStatus.Void;

        /// <summary>
        /// The number to store when an invoice is saved with <paramref name="requested"/> typed in. A draft takes what
        /// was typed (blank: numbered at issue); an issued invoice keeps its number and refuses a different one.
        /// </summary>
        public static string? ForSave(string? currentNo, SalesInvoiceStatus? currentStatus, bool exists, string? requested)
        {
            var number = Normalize(requested);
            var current = Normalize(currentNo);

            if (exists && current != null && IsRequired(currentStatus))
            {
                if (number != null && !string.Equals(number, current, StringComparison.OrdinalIgnoreCase))
                    throw new InvoiceNumberException(InvoiceNumberError.Locked,
                        $"Invoice #{current} is issued, so its number can't change.");
                return current;
            }

            // A number the invoice already had stays valid even if it predates these rules.
            if (number != null && !string.Equals(number, current, StringComparison.Ordinal))
                Validate(number);
            return number;
        }

        public static void Validate(string number)
        {
            if (number.Length > MaxLength)
                throw new InvoiceNumberException(InvoiceNumberError.Invalid,
                    $"An invoice number can be at most {MaxLength} characters.");
            if (!Allowed.IsMatch(number))
                throw new InvoiceNumberException(InvoiceNumberError.Invalid,
                    "An invoice number can use letters, digits and - _ / . and must start with a letter or digit.");
        }

        /// <summary>The sequence value a plain number such as "1043" stands for; null for anything else.</summary>
        public static int? AsSequence(string number) =>
            number.Length <= 9 && number.All(char.IsAsciiDigit)
            && int.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value > 0
                ? value
                : null;
    }

    public enum InvoiceNumberError
    {
        Invalid,
        Taken,
        Locked,
        /// <summary>The next number is no longer the one the user confirmed.</summary>
        Changed,
    }

    public class InvoiceNumberException : Exception
    {
        public InvoiceNumberException(InvoiceNumberError error, string message) : base(message)
        {
            Error = error;
        }

        public InvoiceNumberError Error { get; }
    }
}
