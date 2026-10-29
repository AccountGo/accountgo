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
        /// What to store from a save request. Absent (null) stays null, meaning "use the workspace's payment bank or
        /// default text". Blank text is a field the user cleared on purpose and is stored empty, so it keeps printing nothing.
        /// </summary>
        public static string? ForSave(string? requested) => requested?.Trim();

        /// <summary>
        /// What a draft invoice prints, by what is stored on it:
        /// <list type="bullet">
        /// <item>null: never set, so the workspace's payment bank (when set and still active) formatted as instructions,
        /// else the free-text default;</item>
        /// <item>empty or blank: the user cleared the field on purpose, so nothing prints;</item>
        /// <item>text: the invoice's own text wins. A copy of the free-text default is not an override, so it still
        /// yields to the payment bank.</item>
        /// </list>
        /// </summary>
        public static string? Resolve(
            string? invoiceInstructions,
            SalesInvoiceSettings settings,
            Bank? paymentBank,
            string? payeeName,
            string? invoiceNo)
        {
            if (invoiceInstructions == null)
                return FromWorkspaceDefaults(settings, paymentBank, payeeName, invoiceNo);

            var own = Clean(invoiceInstructions);
            if (own == null)
                return null;

            // An invoice prefilled from the default text is not an override; only a differing edit is.
            if (own != Clean(settings.DefaultPaymentInstructions))
                return own;

            return FromWorkspaceDefaults(settings, paymentBank, payeeName, invoiceNo) ?? own;
        }

        /// <summary>
        /// What an invoice prints. Issued invoices print only what was stored on them when they were issued (see
        /// <c>SalesInvoiceHeader.CapturePaymentInstructionsIfIssued</c>), so later changes to the payment bank or the
        /// default text never rewrite them; drafts resolve live through <see cref="Resolve"/>.
        /// </summary>
        public static string? ForPresentation(
            bool isIssued,
            string? invoiceInstructions,
            SalesInvoiceSettings settings,
            Bank? paymentBank,
            string? payeeName,
            string? invoiceNo) =>
            isIssued
                ? Clean(invoiceInstructions)
                : Resolve(invoiceInstructions, settings, paymentBank, payeeName, invoiceNo);

        private static string? FromWorkspaceDefaults(
            SalesInvoiceSettings settings,
            Bank? paymentBank,
            string? payeeName,
            string? invoiceNo)
        {
            if (paymentBank != null && paymentBank.ArchivedAt == null)
            {
                var bankText = Format(paymentBank, payeeName, invoiceNo);
                if (bankText != null)
                    return bankText;
            }

            return Clean(settings.DefaultPaymentInstructions);
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
