// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using System.Text.RegularExpressions;

namespace Api.DocumentCaptures.Parsing;

/// <summary>
/// Rule A-02. Decides whether an assistant attachment shows positive financial evidence before any financial label/value
/// fields are read from it. Chat attachments are general purpose (a web page, a guide, code), so words such as
/// <c>total: integer</c> or <c>invoice_id: string</c> must not turn a document into a bill.
/// <para>Financial: at least two different signals among a document title line (not "Statement", which bank statements share), an amount on a total/subtotal/tax/
/// amount-due label, a document-number label with a value containing a digit, and a date label with a date.</para>
/// <para>Bank: a bank identifier label (IBAN, SWIFT/BIC, routing, account number, BSB, sort code) with a value of at
/// least six digits, or an IBAN-shaped value.</para>
/// </summary>
public static class AssistantFinancialEvidence
{
    private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking;

    // The whole line is a document title.
    private static readonly Regex TitleLineRegex = new(
        @"^\s*(?:tax\s+invoice|commercial\s+invoice|invoice|official\s+receipt|sales\s+receipt|receipt|bill|credit\s+note|debit\s+note|quotation|purchase\s+order|ใบกำกับภาษี|ใบเสร็จรับเงิน|ใบแจ้งหนี้)\s*$",
        Options);

    // A totals label followed (on the same line) by an amount with two decimals or a currency symbol.
    private static readonly Regex TotalAmountRegex = new(
        @"(?:^|[^\p{L}\p{N}_])(?:grand\s*total|sub\s*-?\s*total|total(?:\s+due)?|amount\s+due|balance\s+due|tax|vat|gst|ยอดรวมทั้งสิ้น|ยอดสุทธิ|รวมทั้งสิ้น|ยอดรวม|ภาษีมูลค่าเพิ่ม|ภาษี)(?:[^\p{L}\p{N}_]|$)[^\n]*?(?:[$€£¥฿]\s*\d|\d[\d,]*[.,]\d{2}(?:\D|$))",
        Options);

    // A document-number label followed by a value that contains a digit.
    private static readonly Regex DocumentNumberRegex = new(
        @"(?:^|[^\p{L}\p{N}_])(?:invoice|inv\.?|receipt|bill|order|po|credit\s+note|เลขที่)\s*(?:no\.?|number|#)?\s*[:#]\s*[A-Za-z\-/]*\d|(?:^|[^\p{L}\p{N}_])(?:invoice|receipt|bill|order|po)\s+(?:no\.?|number)\s+[A-Za-z\-/]*\d|เลขที่\s+[A-Za-z\-/]*\d",
        Options);

    // A date label followed by a numeric or month-name date.
    private static readonly Regex DateRegex = new(
        @"(?:^|[^\p{L}\p{N}_])(?:invoice\s+date|issue\s+date|bill\s+date|receipt\s+date|due\s+date|date|วันที่)(?:[^\p{L}\p{N}_]|$)\s*[:#]?\s*(?:\d{1,4}[/.\-]\d{1,2}[/.\-]\d{1,4}|\d{1,2}\s+\p{L}{3,}\.?\s+\d{2,4}|\p{L}{3,}\.?\s+\d{1,2},?\s+\d{2,4})",
        Options);

    private static readonly Regex BankLabelValueRegex = new(
        @"(?:^|[^\p{L}\p{N}_])(?<label>iban|swift|bic|routing(?:\s+(?:no\.?|number))?|aba|bsb|sort\s+code|acct\.?\s*(?:no\.?|#)?|account\s*(?:number|no\.?|#))(?:[^\p{L}\p{N}_]|$)\s*[:#]?\s*(?<value>[A-Z0-9\- ]*?\d(?:[\s\-]?\d){5,})",
        Options);

    // A field label that names a bank identifier (same words as BankLabelValueRegex).
    private static readonly Regex BankLabelRegex = new(
        @"(?:^|[^\p{L}\p{N}_])(?:iban|swift|bic|routing|aba|bsb|sort\s+code|acct|account\s*(?:number|no\.?|#))(?:[^\p{L}\p{N}_]|$)",
        Options);

    private static readonly Regex IbanValueRegex = new(
        @"(?:^|[^A-Z0-9])[A-Z]{2}\d{2}(?:\s?[A-Z0-9]{4}){3,7}(?:\s?[A-Z0-9]{1,4})?(?:[^A-Z0-9]|$)",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    public static Evidence Assess(IReadOnlyList<string> lines)
    {
        bool title = false, amount = false, number = false, date = false;
        (string Label, string Value)? bank = null;
        foreach (var line in lines)
        {
            title |= TitleLineRegex.IsMatch(line);
            amount |= TotalAmountRegex.IsMatch(line);
            number |= DocumentNumberRegex.IsMatch(line);
            date |= DateRegex.IsMatch(line);
            if (bank is null)
            {
                var labelled = BankLabelValueRegex.Match(line);
                if (labelled.Success)
                    bank = (labelled.Groups["label"].Value.Trim(), labelled.Groups["value"].Value.Trim());
                else if (IbanValueRegex.Match(line) is { Success: true } iban)
                    bank = ("IBAN", iban.Value.Trim());
            }
        }
        var signals = (title ? 1 : 0) + (amount ? 1 : 0) + (number ? 1 : 0) + (date ? 1 : 0);
        return new Evidence(signals >= 2, bank is not null, signals, title || number, bank);
    }

    public static bool IsBankLabel(string label) => BankLabelRegex.IsMatch(label);

    /// <param name="Financial">At least two different financial signals.</param>
    /// <param name="Bank">A bank identifier with a number.</param>
    /// <param name="DocumentSignal">A document title line or a document-number label: what an invoice with remittance
    /// details has and a bank statement (dates, totals and balances) does not.</param>
    /// <param name="BankDetail">The first bank identifier found, as label and value (unlabelled IBANs get "IBAN").</param>
    public readonly record struct Evidence(
        bool Financial, bool Bank, int FinancialSignals, bool DocumentSignal = false, (string Label, string Value)? BankDetail = null)
    {
        public bool Any => Financial || Bank;

        /// <summary>Bank details win unless the document is also clearly an invoice or receipt.</summary>
        public bool IsBankDocument => Bank && !(Financial && DocumentSignal);
    }
}
