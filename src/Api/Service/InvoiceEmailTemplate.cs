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
    /// Customer invoice email: a short professional message plus a signed PDF link
    /// (attachment is an advanced opt-in). Table layout with inline styles for Gmail/Outlook/Apple Mail.
    /// Everything the workspace typed is HTML-encoded.
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

        public static InvoiceEmail Build(
            Dto.Sales.SalesInvoice invoice,
            string? currencyCode,
            bool canReply,
            string? pdfUrl = null,
            bool attachPdf = false)
        {
            var ctx = Context(invoice, currencyCode);
            // Keep the subject short; amount and due date live in the body.
            var subject = $"Invoice {ctx.Number} from {ctx.Seller}";
            var intro = FirstIntro(ctx, hasLink: !string.IsNullOrWhiteSpace(pdfUrl), attachPdf);
            return Finish(ctx, subject, intro, canReply, pdfUrl, attachPdf, followUp: false);
        }

        /// <summary>
        /// A polite reminder that the invoice is still unpaid. Same layout; clearer reminder copy.
        /// </summary>
        public static InvoiceEmail BuildFollowUp(
            Dto.Sales.SalesInvoice invoice,
            string? currencyCode,
            bool canReply,
            string? pdfUrl = null,
            bool attachPdf = false)
        {
            var ctx = Context(invoice, currencyCode);
            var subject = $"Reminder: Invoice {ctx.Number} from {ctx.Seller}";
            var intro = FollowUpIntro(ctx, hasLink: !string.IsNullOrWhiteSpace(pdfUrl), attachPdf);
            return Finish(ctx, subject, intro, canReply, pdfUrl, attachPdf, followUp: true);
        }

        private static string FirstIntro(EmailContext ctx, bool hasLink, bool attachPdf)
        {
            string body;
            if (ctx.PaidInFull)
                body = $"{ctx.Seller} has issued invoice {ctx.Number}. It is paid in full; nothing further is due.";
            else if (ctx.Due != null)
                body = $"{ctx.Seller} has issued invoice {ctx.Number} for {ctx.Headline}, due {ctx.Due}.";
            else
                body = $"{ctx.Seller} has issued invoice {ctx.Number} for {ctx.Headline}.";

            return body + AccessSentence(hasLink, attachPdf, anotherCopy: false);
        }

        private static string FollowUpIntro(EmailContext ctx, bool hasLink, bool attachPdf)
        {
            var body = ctx.Due != null
                ? $"This is a reminder that invoice {ctx.Number} for {ctx.Headline} remains unpaid (due {ctx.Due})."
                : $"This is a reminder that invoice {ctx.Number} for {ctx.Headline} remains unpaid.";
            return body + AccessSentence(hasLink, attachPdf, anotherCopy: true);
        }

        private static string AccessSentence(bool hasLink, bool attachPdf, bool anotherCopy)
        {
            if (hasLink && attachPdf)
                return anotherCopy
                    ? " You can open the invoice below; a PDF is also attached."
                    : " You can open the invoice below; a PDF is also attached.";
            if (hasLink)
                return " You can open the invoice using the button below.";
            return anotherCopy
                ? " The invoice PDF is attached if you need another copy."
                : " The invoice PDF is attached.";
        }

        private sealed class EmailContext
        {
            public string Number { get; init; } = "";
            public string Seller { get; init; } = "";
            public string? Customer { get; init; }
            public string Headline { get; init; } = "";
            public string? Due { get; init; }
            public bool PaidInFull { get; init; }
            public List<(string Label, string Value)> Facts { get; init; } = new();
            public List<(string Heading, string Body)> Sections { get; init; } = new();
            public string Contact { get; init; } = "";
        }

        private static EmailContext Context(Dto.Sales.SalesInvoice invoice, string? currencyCode)
        {
            var currency = InvoiceFormat.CurrencyCode(currencyCode);
            var number = InvoiceFormat.Number(invoice);
            var seller = string.IsNullOrWhiteSpace(invoice.Seller.Name) ? "AccountGo" : invoice.Seller.Name.Trim();
            var customer = string.IsNullOrWhiteSpace(invoice.CustomerName) ? null : invoice.CustomerName.Trim();
            var total = invoice.TotalAmountAfterTax ?? invoice.Amount;
            var paidInFull = invoice.BalanceDue <= 0.005m && total > 0;
            var headline = InvoiceFormat.Money(paidInFull ? total : invoice.BalanceDue, currency);
            var due = invoice.DueDate.HasValue ? InvoiceFormat.Date(invoice.DueDate.Value) : null;

            // Keep the email lean: identity + amount. Line items and tax live in the PDF.
            var facts = new List<(string Label, string Value)>
            {
                ("Invoice", number),
                ("Date", InvoiceFormat.Date(invoice.InvoiceDate)),
            };
            if (due != null)
                facts.Add(("Due", due));
            if (!string.IsNullOrWhiteSpace(invoice.ReferenceNo))
                facts.Add(("Reference", invoice.ReferenceNo.Trim()));

            var partlyPaid = invoice.PaidAmount > 0 && !paidInFull;
            if (partlyPaid)
            {
                facts.Add(("Invoice total", InvoiceFormat.Money(total, currency)));
                facts.Add(("Already paid", InvoiceFormat.Money(invoice.PaidAmount, currency)));
            }
            facts.Add((paidInFull ? "Amount paid" : "Amount due", headline));

            var sections = new List<(string Heading, string Body)>();
            if (invoice.Display.ShowPaymentInstructions && !string.IsNullOrWhiteSpace(invoice.PaymentInstructionsToPrint()) && !paidInFull)
                sections.Add(("Payment", invoice.PaymentInstructionsToPrint()!.Trim()));
            if (invoice.Display.ShowNotes && !string.IsNullOrWhiteSpace(invoice.Notes))
                sections.Add(("Notes", invoice.Notes.Trim()));

            var contact = string.Join(" · ", new[] { invoice.Seller.Email, invoice.Seller.Phone }.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!.Trim()));

            return new EmailContext
            {
                Number = number,
                Seller = seller,
                Customer = customer,
                Headline = headline,
                Due = due,
                PaidInFull = paidInFull,
                Facts = facts,
                Sections = sections,
                Contact = contact,
            };
        }

        private static InvoiceEmail Finish(
            EmailContext ctx,
            string subject,
            string intro,
            bool canReply,
            string? pdfUrl,
            bool attachPdf,
            bool followUp)
        {
            var replyLine = canReply ? "If you have questions, reply to this email." : null;
            return new InvoiceEmail
            {
                Subject = OneLine(subject),
                Html = BuildHtml(ctx, intro, replyLine, pdfUrl, attachPdf, followUp),
                Text = BuildText(ctx, intro, replyLine, pdfUrl, attachPdf),
            };
        }

        private static string BuildHtml(
            EmailContext ctx,
            string intro,
            string? replyLine,
            string? pdfUrl,
            bool attachPdf,
            bool followUp)
        {
            var h = new StringBuilder();
            h.Append("<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"utf-8\">")
             .Append("<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">")
             .Append("<meta name=\"color-scheme\" content=\"light\"><meta name=\"supported-color-schemes\" content=\"light\">")
             .Append($"<title>{E($"Invoice {ctx.Number}")}</title></head>")
             .Append($"<body style=\"margin:0;padding:0;background:#F8FAFC;font-family:{Font};color:{Ink};\">");

            h.Append($"<div style=\"display:none;max-height:0;overflow:hidden;opacity:0;\">{E(intro)}</div>");

            h.Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"background:#F8FAFC;\"><tr><td align=\"center\" style=\"padding:28px 16px;\">")
             .Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"max-width:520px;\">");

            // Card — quiet, OTP-adjacent professionalism
            h.Append($"<tr><td style=\"background:#FFFFFF;border:1px solid {Rule};border-radius:8px;padding:28px;\">");

            h.Append($"<p style=\"margin:0;font-size:13px;font-weight:600;letter-spacing:.04em;text-transform:uppercase;color:{Muted};\">")
             .Append(followUp ? "Payment reminder" : "Invoice")
             .Append("</p>")
             .Append($"<p style=\"margin:8px 0 0;font-size:20px;font-weight:700;color:{Ink};\">{E(ctx.Seller)}</p>");

            h.Append($"<p style=\"margin:20px 0 0;font-size:15px;line-height:1.55;color:{Ink};\">")
             .Append(ctx.Customer != null ? $"Hello {E(ctx.Customer)}," : "Hello,")
             .Append("</p>")
             .Append($"<p style=\"margin:8px 0 0;font-size:15px;line-height:1.55;color:{Secondary};\">{E(intro)}</p>");

            h.Append($"<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"margin-top:22px;border-top:1px solid {Rule};\">");
            foreach (var (label, value) in ctx.Facts)
            {
                h.Append("<tr>")
                 .Append($"<td style=\"padding:10px 0;font-size:14px;color:{Muted};border-bottom:1px solid {Shade};\">{E(label)}</td>")
                 .Append($"<td align=\"right\" style=\"padding:10px 0;font-size:14px;font-weight:600;color:{Ink};border-bottom:1px solid {Shade};\">{E(value)}</td>")
                 .Append("</tr>");
            }
            h.Append("</table>");

            if (!string.IsNullOrWhiteSpace(pdfUrl))
            {
                h.Append("<table role=\"presentation\" cellpadding=\"0\" cellspacing=\"0\" style=\"margin-top:24px;\"><tr>")
                 .Append($"<td align=\"center\" bgcolor=\"{Brand}\" style=\"border-radius:6px;\">")
                 .Append($"<a href=\"{E(pdfUrl)}\" target=\"_blank\" rel=\"noopener noreferrer\" ")
                 .Append($"style=\"display:inline-block;padding:11px 20px;font-size:14px;font-weight:600;color:#FFFFFF;text-decoration:none;\">")
                 .Append("View invoice</a></td></tr></table>")
                 .Append($"<p style=\"margin:10px 0 0;font-size:12px;color:{Muted};\">This link expires in 7 days.</p>");
            }

            if (attachPdf)
            {
                h.Append($"<p style=\"margin:14px 0 0;font-size:13px;color:{Secondary};\">Attached: {E(PdfFileName(ctx.Number))}</p>");
            }

            foreach (var (heading, body) in ctx.Sections)
            {
                h.Append($"<p style=\"margin:24px 0 4px;font-size:12px;font-weight:600;letter-spacing:.04em;text-transform:uppercase;color:{Muted};\">{E(heading)}</p>")
                 .Append($"<p style=\"margin:0;font-size:14px;line-height:1.55;color:{Ink};\">{Multiline(body)}</p>");
            }

            if (replyLine != null)
                h.Append($"<p style=\"margin:24px 0 0;font-size:14px;line-height:1.55;color:{Secondary};\">{E(replyLine)}</p>");

            h.Append("</td></tr>");

            h.Append($"<tr><td style=\"padding:18px 4px 0;font-size:12px;line-height:1.5;color:{Muted};\">");
            h.Append(E(ctx.Seller));
            if (ctx.Contact.Length > 0)
                h.Append($" · {E(ctx.Contact)}");
            h.Append("<br>Sent with AccountGo</td></tr>");

            h.Append("</table></td></tr></table></body></html>");
            return h.ToString();
        }

        private static string BuildText(
            EmailContext ctx,
            string intro,
            string? replyLine,
            string? pdfUrl,
            bool attachPdf)
        {
            var t = new StringBuilder();
            t.AppendLine(ctx.Customer != null ? $"Hello {ctx.Customer}," : "Hello,").AppendLine().AppendLine(intro).AppendLine();
            foreach (var (label, value) in ctx.Facts)
                t.AppendLine($"{label}: {value}");
            if (!string.IsNullOrWhiteSpace(pdfUrl))
                t.AppendLine().AppendLine($"View invoice: {pdfUrl}").AppendLine("(Link expires in 7 days.)");
            if (attachPdf)
                t.AppendLine().AppendLine($"Attached: {PdfFileName(ctx.Number)}");
            foreach (var (heading, body) in ctx.Sections)
                t.AppendLine().AppendLine(heading.ToUpperInvariant()).AppendLine(body);
            if (replyLine != null)
                t.AppendLine().AppendLine(replyLine);
            t.AppendLine().AppendLine("--").AppendLine(ctx.Seller);
            if (ctx.Contact.Length > 0)
                t.AppendLine(ctx.Contact);
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
