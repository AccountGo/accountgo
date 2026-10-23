using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;

namespace Api.Service
{
    public class InvoiceEmail
    {
        public string Subject { get; init; } = string.Empty;
        public string Html { get; init; } = string.Empty;
        public string Text { get; init; } = string.Empty;
    }

    /// <summary>
    /// The email a customer receives with an invoice PDF attached. Table layout with inline styles, since that is what
    /// Gmail, Outlook and Apple Mail all render the same way. Everything the workspace typed is HTML-encoded.
    /// </summary>
    public static class InvoiceEmailTemplate
    {
        private const string Ink = "#0F172A";
        private const string Secondary = "#475569";
        private const string Muted = "#64748B";
        private const string Rule = "#E2E8F0";
        private const string Shade = "#F8FAFC";
        private const string Brand = "#059669";
        private const string Font = "-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif";

        public static InvoiceEmail Build(Dto.Sales.SalesInvoice invoice, string? currencyCode, bool canReply)
        {
            var currency = InvoiceFormat.CurrencyCode(currencyCode);
            var number = InvoiceFormat.Number(invoice);
            var seller = string.IsNullOrWhiteSpace(invoice.Seller.Name) ? "AccountGo" : invoice.Seller.Name.Trim();
            var customer = string.IsNullOrWhiteSpace(invoice.CustomerName) ? null : invoice.CustomerName.Trim();
            var total = invoice.TotalAmountAfterTax ?? invoice.Amount;
            var paidInFull = invoice.BalanceDue <= 0.005m && total > 0;
            var headline = InvoiceFormat.Money(paidInFull ? total : invoice.BalanceDue, currency);
            var due = invoice.DueDate.HasValue ? InvoiceFormat.Date(invoice.DueDate.Value) : null;

            var subject = paidInFull || due == null
                ? $"Invoice {number} from {seller}"
                : $"Invoice {number} from {seller}: {headline} due {due}";

            var intro = paidInFull
                ? $"{seller} has sent you invoice {number}. It is paid in full, so there is nothing to pay; the PDF is attached for your records."
                : $"{seller} has sent you invoice {number} for {headline}{(due != null ? $", due {due}" : "")}. The invoice is attached as a PDF.";

            var facts = new List<(string Label, string Value)>
            {
                ("Invoice number", number),
                ("Invoice date", InvoiceFormat.Date(invoice.InvoiceDate)),
            };
            if (due != null)
                facts.Add(("Due date", due));
            if (!string.IsNullOrWhiteSpace(invoice.ReferenceNo))
                facts.Add(("Reference / PO", invoice.ReferenceNo.Trim()));
            if (invoice.PaidAmount > 0 && !paidInFull)
            {
                facts.Add(("Invoice total", InvoiceFormat.Money(total, currency)));
                facts.Add(("Already paid", InvoiceFormat.Money(invoice.PaidAmount, currency)));
            }

            var sections = new List<(string Heading, string Body)>();
            if (invoice.Display.ShowPaymentInstructions && !string.IsNullOrWhiteSpace(invoice.PaymentInstructions) && !paidInFull)
                sections.Add(("How to pay", invoice.PaymentInstructions.Trim()));
            if (invoice.Display.ShowNotes && !string.IsNullOrWhiteSpace(invoice.Notes))
                sections.Add(("Notes", invoice.Notes.Trim()));

            var contact = string.Join(" · ", new[] { invoice.Seller.Email, invoice.Seller.Phone }.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!.Trim()));
            var replyLine = canReply ? "Questions about this invoice? Just reply to this email." : null;

            return new InvoiceEmail
            {
                Subject = OneLine(subject),
                Html = BuildHtml(seller, customer, number, intro, paidInFull ? "Amount paid" : "Amount due", headline, paidInFull, due, facts, sections, replyLine, contact),
                Text = BuildText(seller, customer, intro, facts, sections, replyLine, contact),
            };
        }

        private static string BuildHtml(string seller, string? customer, string number, string intro, string amountLabel, string amount,
            bool paidInFull, string? due, List<(string Label, string Value)> facts, List<(string Heading, string Body)> sections,
            string? replyLine, string contact)
        {
            var h = new StringBuilder();
            h.Append("<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"utf-8\">")
             .Append("<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">")
             .Append("<meta name=\"color-scheme\" content=\"light\"><meta name=\"supported-color-schemes\" content=\"light\">")
             .Append($"<title>{E($"Invoice {number}")}</title></head>")
             .Append($"<body style=\"margin:0;padding:0;background:#F1F5F9;font-family:{Font};color:{Ink};\">");

            // Preheader: the line inbox lists show after the subject.
            h.Append($"<div style=\"display:none;max-height:0;overflow:hidden;opacity:0;\">{E(intro)}</div>");

            h.Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"background:#F1F5F9;\"><tr><td align=\"center\" style=\"padding:32px 16px;\">")
             .Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"max-width:560px;\">");

            // Seller
            h.Append($"<tr><td style=\"padding:0 4px 16px;font-size:16px;font-weight:700;color:{Ink};\">{E(seller)}</td></tr>");

            // Card
            h.Append($"<tr><td style=\"background:#FFFFFF;border:1px solid {Rule};border-radius:12px;padding:32px;\">");

            h.Append($"<p style=\"margin:0 0 4px;font-size:12px;font-weight:600;letter-spacing:.08em;text-transform:uppercase;color:{Muted};\">Invoice {E(number)}</p>")
             .Append($"<p style=\"margin:0;font-size:13px;color:{Secondary};\">{E(amountLabel)}</p>")
             .Append($"<p style=\"margin:2px 0 0;font-size:34px;line-height:1.15;font-weight:700;color:{Ink};\">{E(amount)}</p>");
            if (paidInFull)
                h.Append($"<p style=\"margin:10px 0 0;\"><span style=\"display:inline-block;padding:3px 10px;border-radius:999px;background:#ECFDF5;color:{Brand};font-size:12px;font-weight:700;\">Paid in full</span></p>");
            else if (due != null)
                h.Append($"<p style=\"margin:6px 0 0;font-size:14px;color:{Secondary};\">Due <strong style=\"color:{Ink};\">{E(due)}</strong></p>");

            h.Append($"<hr style=\"border:0;border-top:1px solid {Rule};margin:24px 0;\">");

            h.Append($"<p style=\"margin:0 0 12px;font-size:15px;line-height:1.6;\">{(customer != null ? $"Hi {E(customer)}," : "Hello,")}</p>")
             .Append($"<p style=\"margin:0;font-size:15px;line-height:1.6;color:{Secondary};\">{E(intro)}</p>");

            // Facts
            h.Append($"<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"margin-top:24px;background:{Shade};border-radius:8px;\">");
            foreach (var (label, value) in facts)
            {
                h.Append("<tr>")
                 .Append($"<td style=\"padding:8px 16px;font-size:13px;color:{Muted};\">{E(label)}</td>")
                 .Append($"<td align=\"right\" style=\"padding:8px 16px;font-size:13px;font-weight:600;color:{Ink};\">{E(value)}</td>")
                 .Append("</tr>");
            }
            h.Append("</table>");

            // Attachment chip
            h.Append($"<table role=\"presentation\" cellpadding=\"0\" cellspacing=\"0\" style=\"margin-top:16px;\"><tr><td style=\"padding:10px 14px;border:1px solid {Rule};border-radius:8px;font-size:13px;color:{Secondary};\">")
             .Append($"&#128206;&nbsp; <strong style=\"color:{Ink};\">{E(PdfFileName(number))}</strong> is attached")
             .Append("</td></tr></table>");

            foreach (var (heading, body) in sections)
            {
                h.Append($"<p style=\"margin:28px 0 6px;font-size:12px;font-weight:600;letter-spacing:.08em;text-transform:uppercase;color:{Muted};\">{E(heading)}</p>")
                 .Append($"<p style=\"margin:0;font-size:14px;line-height:1.6;color:{Ink};\">{Multiline(body)}</p>");
            }

            if (replyLine != null)
                h.Append($"<p style=\"margin:28px 0 0;font-size:14px;line-height:1.6;color:{Secondary};\">{E(replyLine)}</p>");

            h.Append("</td></tr>");

            // Footer
            h.Append($"<tr><td style=\"padding:20px 4px 0;font-size:12px;line-height:1.6;color:{Muted};\">");
            h.Append($"<strong style=\"color:{Secondary};\">{E(seller)}</strong>");
            if (contact.Length > 0)
                h.Append($"<br>{E(contact)}");
            h.Append("<br>Sent with AccountGo</td></tr>");

            h.Append("</table></td></tr></table></body></html>");
            return h.ToString();
        }

        private static string BuildText(string seller, string? customer, string intro, List<(string Label, string Value)> facts,
            List<(string Heading, string Body)> sections, string? replyLine, string contact)
        {
            var t = new StringBuilder();
            t.AppendLine(customer != null ? $"Hi {customer}," : "Hello,").AppendLine().AppendLine(intro).AppendLine();
            foreach (var (label, value) in facts)
                t.AppendLine($"{label}: {value}");
            foreach (var (heading, body) in sections)
                t.AppendLine().AppendLine(heading.ToUpperInvariant()).AppendLine(body);
            if (replyLine != null)
                t.AppendLine().AppendLine(replyLine);
            t.AppendLine().AppendLine("--").AppendLine(seller);
            if (contact.Length > 0)
                t.AppendLine(contact);
            t.Append("Sent with AccountGo");
            return t.ToString();
        }

        public static string PdfFileName(string invoiceNumber)
        {
            var safe = new string(invoiceNumber.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '-').ToArray());
            return $"Invoice-{safe}.pdf";
        }

        private static string E(string value) => WebUtility.HtmlEncode(value);

        private static string Multiline(string value) =>
            string.Join("<br>", value.Replace("\r\n", "\n").Split('\n').Select(E));

        private static string OneLine(string value) =>
            string.Join(" ", value.Split(new[] { '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries)).Trim();
    }
}
