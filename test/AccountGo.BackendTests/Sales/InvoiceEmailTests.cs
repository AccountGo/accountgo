using System;
using System.Collections.Generic;
using System.Text.Json;
using Api.Service;
using Xunit;

namespace AccountGo.BackendTests.Sales;

public class InvoiceEmailTests
{
    private static Dto.Sales.SalesInvoice Invoice(decimal balanceDue = 1250m, decimal paid = 0m) => new()
    {
        Id = 7,
        No = "INV-0042",
        CustomerName = "Juan <script>alert(1)</script>",
        InvoiceDate = new DateTime(2026, 10, 23),
        DueDate = new DateTime(2026, 11, 22),
        ReferenceNo = "PO-9",
        TotalAmountAfterTax = 1250m,
        PaidAmount = paid,
        BalanceDue = balanceDue,
        Notes = "Thank you!\nSee you again",
        PaymentInstructions = "BDO 1234 & GCash",
        Seller = new Dto.Sales.SellerInfo { Name = "Acme \"Studio\"", Email = "hello@acme.example", Phone = "0917" },
        SalesInvoiceLines = new List<Dto.Sales.SalesInvoiceLine> { new() { Amount = 1250m, Quantity = 1 } },
    };

    [Fact]
    public void Build_ShowsAmountDueAndDetails_AndEncodesWhatTheWorkspaceTyped()
    {
        var email = InvoiceEmailTemplate.Build(Invoice(), "PHP", canReply: true);

        Assert.Equal("Invoice INV-0042 from Acme \"Studio\": ₱1,250.00 due Nov 22, 2026", email.Subject);
        Assert.Contains("₱1,250.00", email.Html);
        Assert.Contains("Nov 22, 2026", email.Html);
        Assert.Contains("PO-9", email.Html);
        Assert.Contains("Invoice-INV-0042.pdf", email.Html);
        Assert.Contains("reply to this email", email.Html);
        Assert.Contains("Thank you!<br>See you again", email.Html);
        Assert.Contains("BDO 1234 &amp; GCash", email.Html);
        Assert.DoesNotContain("<script>", email.Html);
        Assert.Contains("Juan &lt;script&gt;", email.Html);
        Assert.Contains("Due date: Nov 22, 2026", email.Text);
        Assert.Contains("HOW TO PAY", email.Text);
    }

    [Fact]
    public void Build_RespectsDisplaySettings_AndReplyAvailability()
    {
        var invoice = Invoice();
        invoice.Display = new Dto.Sales.DisplayFlags { ShowNotes = false, ShowPaymentInstructions = false };

        var email = InvoiceEmailTemplate.Build(invoice, "USD", canReply: false);

        Assert.DoesNotContain("Thank you!", email.Html);
        Assert.DoesNotContain("BDO", email.Html);
        Assert.DoesNotContain("reply to this email", email.Html);
        Assert.Contains("$1,250.00", email.Html);
    }

    [Fact]
    public void Build_PaidInvoice_SaysNothingIsDue()
    {
        var email = InvoiceEmailTemplate.Build(Invoice(balanceDue: 0, paid: 1250m), "USD", canReply: true);

        Assert.Equal("Invoice INV-0042 from Acme \"Studio\"", email.Subject);
        Assert.Contains("Paid in full", email.Html);
        Assert.DoesNotContain("BDO", email.Html);
    }

    [Fact]
    public void Build_PartlyPaid_ShowsBalanceAndWhatWasPaid()
    {
        var email = InvoiceEmailTemplate.Build(Invoice(balanceDue: 250m, paid: 1000m), "USD", canReply: true);

        Assert.Contains("$250.00 due", email.Subject);
        Assert.Contains("Already paid: $1,000.00", email.Text);
    }

    [Fact]
    public void Build_SubjectIsAlwaysOneLine()
    {
        var invoice = Invoice();
        invoice.Seller.Name = "Acme\r\nBcc: victim@example.com";

        Assert.DoesNotContain("\n", InvoiceEmailTemplate.Build(invoice, "USD", canReply: true).Subject);
    }

    [Theory]
    [InlineData("INV-0042", "Invoice-INV-0042.pdf")]
    [InlineData("../../etc/passwd", "Invoice-------etc-passwd.pdf")]
    public void PdfFileName_KeepsOnlySafeCharacters(string number, string expected)
    {
        Assert.Equal(expected, InvoiceEmailTemplate.PdfFileName(number));
    }

    [Theory]
    [InlineData("Acme Studio via AccountGo", "\"Acme Studio via AccountGo\"")]
    [InlineData("Evil\" <attacker@x.com>, \"x", "\"Evil attacker@x.com x\"")]
    [InlineData("Line\r\nBreak", "\"LineBreak\"")]
    [InlineData("   ", null)]
    public void SafeDisplayName_CannotBreakOutOfTheFromHeader(string input, string? expected)
    {
        Assert.Equal(expected, ResendEmailSender.SafeDisplayName(input));
    }

    [Fact]
    public void BuildPayload_SendsAttachmentsBlindCopyAndReplyToInResendShape()
    {
        var payload = ResendEmailSender.BuildPayload("\"Acme\" <noreply@example.com>", new EmailMessage
        {
            To = "customer@example.com",
            Subject = "Invoice",
            Html = "<p>Hi</p>",
            Text = "Hi",
            ReplyTo = "hello@acme.example",
            Bcc = { "me@acme.example" },
            Attachments = { new EmailAttachment { FileName = "Invoice-1.pdf", Content = new byte[] { 1, 2, 3 } } },
        });

        using var json = JsonDocument.Parse(payload);
        var root = json.RootElement;
        Assert.Equal("customer@example.com", root.GetProperty("to")[0].GetString());
        Assert.Equal("me@acme.example", root.GetProperty("bcc")[0].GetString());
        Assert.Equal("hello@acme.example", root.GetProperty("reply_to").GetString());
        Assert.Equal("Hi", root.GetProperty("text").GetString());
        Assert.Equal("Invoice-1.pdf", root.GetProperty("attachments")[0].GetProperty("filename").GetString());
        Assert.Equal("AQID", root.GetProperty("attachments")[0].GetProperty("content").GetString());
        Assert.False(root.TryGetProperty("cc", out _));
    }

    [Fact]
    public void BuildPayload_PlainMessage_OmitsOptionalFields()
    {
        using var json = JsonDocument.Parse(ResendEmailSender.BuildPayload("AccountGo <noreply@example.com>",
            new EmailMessage { To = "a@example.com", Subject = "Code", Html = "<p>1</p>" }));

        Assert.False(json.RootElement.TryGetProperty("bcc", out _));
        Assert.False(json.RootElement.TryGetProperty("reply_to", out _));
        Assert.False(json.RootElement.TryGetProperty("attachments", out _));
        Assert.False(json.RootElement.TryGetProperty("text", out _));
    }
}
