// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System.Collections.Generic;
using Core.Domain;
using Core.Domain.Financials;

namespace Services.Banking
{
    /// <summary>Turns a bank account's details into the payment instructions printed on invoices (3G).</summary>
    public static class BankPaymentInstructions
    {
        public const string Heading = "Pay by bank transfer to:";

        /// <summary>
        /// Null when the bank has no account details worth printing, so the caller falls back to the free-text instructions.
        /// </summary>
        public static string? Format(Bank bank, string? payeeName, string? invoiceNo)
        {
            var details = new List<string>();
            Add(details, "Bank", Clean(bank.BankName) ?? Clean(bank.Name));
            Add(details, "Account name", Clean(payeeName));
            Add(details, "Account number", Clean(bank.Number));
            Add(details, "Routing number", Clean(bank.RoutingNumber));
            Add(details, "IBAN", Clean(bank.Iban));
            Add(details, "SWIFT/BIC", Clean(bank.Swift));

            var hasAccountIdentifier = Clean(bank.Number) != null || Clean(bank.Iban) != null;
            if (!hasAccountIdentifier)
                return null;

            Add(details, "Reference", Clean(invoiceNo));
            return Heading + "\n" + string.Join("\n", details);
        }

        /// <summary>
        /// What an invoice prints: its own edited text wins; otherwise the workspace's payment bank (when set and still
        /// active) formatted as instructions; otherwise the stored text or the free-text default.
        /// </summary>
        public static string? Resolve(
            string? invoiceInstructions,
            SalesInvoiceSettings settings,
            Bank? paymentBank,
            string? payeeName,
            string? invoiceNo)
        {
            var own = Clean(invoiceInstructions);
            var freeTextDefault = Clean(settings.DefaultPaymentInstructions);

            // An invoice prefilled from the default text is not an override; only a differing edit is.
            if (own != null && own != freeTextDefault)
                return own;

            if (paymentBank != null && paymentBank.ArchivedAt == null)
            {
                var bankText = Format(paymentBank, payeeName, invoiceNo);
                if (bankText != null)
                    return bankText;
            }

            return own ?? freeTextDefault;
        }

        private static void Add(List<string> lines, string label, string? value)
        {
            if (value != null)
                lines.Add($"{label}: {value}");
        }

        private static string? Clean(string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
