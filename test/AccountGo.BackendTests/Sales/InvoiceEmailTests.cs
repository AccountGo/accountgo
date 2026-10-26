using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Api.Service;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
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

    private const string SamplePdfUrl = "https://api.example.com/api/public/invoices/pdf?c=1&i=7&e=a%40b.com&exp=1&sig=x";

    [Fact]
    public void Build_ShowsAmountDueAndDetails_AndEncodesWhatTheWorkspaceTyped()
    {
        var email = InvoiceEmailTemplate.Build(Invoice(), "PHP", canReply: true, pdfUrl: SamplePdfUrl);

        Assert.Equal("Invoice INV-0042 from Acme \"Studio\"", email.Subject);
        Assert.Contains("₱1,250.00", email.Html);
        Assert.Contains("Nov 22, 2026", email.Html);
        Assert.Contains("PO-9", email.Html);
        Assert.Contains("View invoice", email.Html);
        Assert.Contains(WebUtility.HtmlEncode(SamplePdfUrl), email.Html);
        Assert.DoesNotContain("Attached:", email.Html);
        Assert.Contains("reply to this email", email.Html);
        Assert.Contains("Thank you!<br>See you again", email.Html);
        Assert.Contains("BDO 1234 &amp; GCash", email.Html);
        Assert.DoesNotContain("<script>", email.Html);
        Assert.Contains("Juan &lt;script&gt;", email.Html);
        Assert.Contains("Due: Nov 22, 2026", email.Text);
        Assert.Contains("PAYMENT", email.Text);
        Assert.Contains(SamplePdfUrl, email.Text);
        Assert.Contains("has issued invoice", email.Html);
    }

    [Fact]
    public void Build_AttachPdf_ShowsAttachmentNote_AlongsideLink()
    {
        var email = InvoiceEmailTemplate.Build(Invoice(), "USD", canReply: true, pdfUrl: SamplePdfUrl, attachPdf: true);

        Assert.Contains("View invoice", email.Html);
        Assert.Contains("Invoice-INV-0042.pdf", email.Html);
        Assert.Contains("Attached:", email.Html);
        Assert.Contains("Attached: Invoice-INV-0042.pdf", email.Text);
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
        Assert.Contains("paid in full", email.Html);
        Assert.DoesNotContain("BDO", email.Html);
    }

    [Fact]
    public void Build_PartlyPaid_ShowsBalanceAndWhatWasPaid()
    {
        var email = InvoiceEmailTemplate.Build(Invoice(balanceDue: 250m, paid: 1000m), "USD", canReply: true);

        Assert.Equal("Invoice INV-0042 from Acme \"Studio\"", email.Subject);
        Assert.Contains("Already paid: $1,000.00", email.Text);
        Assert.Contains("Amount due: $250.00", email.Text);
    }

    [Fact]
    public void Build_SubjectIsAlwaysOneLine()
    {
        var invoice = Invoice();
        invoice.Seller.Name = "Acme\r\nBcc: victim@example.com";

        Assert.DoesNotContain("\n", InvoiceEmailTemplate.Build(invoice, "USD", canReply: true).Subject);
    }

    [Fact]
    public void BuildFollowUp_SubjectAndIntro_RemindAboutOutstandingBalance()
    {
        var email = InvoiceEmailTemplate.BuildFollowUp(Invoice(), "PHP", canReply: true, pdfUrl: SamplePdfUrl);

        Assert.Equal("Reminder: Invoice INV-0042 from Acme \"Studio\"", email.Subject);
        Assert.Contains("remains unpaid", email.Html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("₱1,250.00", email.Html);
        Assert.Contains("Nov 22, 2026", email.Html);
        Assert.Contains("View invoice", email.Html);
        Assert.Contains(WebUtility.HtmlEncode(SamplePdfUrl), email.Html);
        Assert.DoesNotContain("Attached:", email.Html);
        Assert.Contains("reply to this email", email.Html);
        Assert.DoesNotContain("<script>", email.Html);
        Assert.Contains("Due: Nov 22, 2026", email.Text);
        Assert.Contains("PAYMENT", email.Text);
    }

    [Fact]
    public void BuildFollowUp_SubjectIsAlwaysOneLine()
    {
        var invoice = Invoice();
        invoice.Seller.Name = "Acme\r\nBcc: victim@example.com";

        Assert.DoesNotContain("\n", InvoiceEmailTemplate.BuildFollowUp(invoice, "USD", canReply: true).Subject);
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

    [Fact]
    public async Task ResendSender_UsesMessageFromEmailAndDefaultReplyTo()
    {
        string? capturedBody = null;
        var sender = Sender(req =>
        {
            capturedBody = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"id\":\"msg_123\"}")
            };
        });

        await sender.SendAsync(new EmailMessage
        {
            To = "user@example.com",
            Subject = "Test OTP",
            Html = "<p>Code</p>",
            FromName = "AccountGo Security",
            FromEmail = "auth@accountgo.dev",
            ReplyTo = "support@accountgo.dev"
        });

        Assert.NotNull(capturedBody);
        using var doc = JsonDocument.Parse(capturedBody!);
        Assert.Equal("\"AccountGo Security\" <auth@accountgo.dev>", doc.RootElement.GetProperty("from").GetString());
        Assert.Equal("support@accountgo.dev", doc.RootElement.GetProperty("reply_to").GetString());
    }

    [Fact]
    public async Task ResendSender_Rejection_ReportsResendsReason()
    {
        var sender = Sender(_ => new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent("{\"statusCode\":403,\"name\":\"validation_error\",\"message\":\"The example.com domain is not verified.\"}")
        });

        var error = await Assert.ThrowsAsync<EmailDeliveryException>(() => sender.SendAsync(new EmailMessage { To = "a@example.com", Subject = "s", Html = "h" }));

        Assert.Equal("The email service refused it: The example.com domain is not verified.", error.Message);
    }

    [Fact]
    public async Task ResendSender_Unreachable_SaysSo()
    {
        var sender = Sender(_ => throw new TaskCanceledException("timed out"));

        var error = await Assert.ThrowsAsync<EmailDeliveryException>(() => sender.SendAsync(new EmailMessage { To = "a@example.com", Subject = "s", Html = "h" }));

        Assert.Equal("The email service could not be reached (TaskCanceledException).", error.Message);
    }

    [Fact]
    public async Task ResendSender_Accepted_ReturnsMessageId()
    {
        var sender = Sender(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"id\":\"abc-123\"}") });

        Assert.Equal("abc-123", await sender.SendAsync(new EmailMessage { To = "a@example.com", Subject = "s", Html = "h" }));
    }

    [Theory]
    [InlineData("not json", 500, "HTTP 500")]
    [InlineData("{\"message\":\"  \"}", 422, "HTTP 422")]
    [InlineData("[]", 400, "HTTP 400")]
    public void ProviderReason_FallsBackToStatus(string body, int status, string expected)
    {
        Assert.Equal(expected, ResendEmailSender.ProviderReason(body, status));
    }

    private static ResendEmailSender Sender(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["RESEND_API_KEY"] = "test-key", ["RESEND_EMAIL_FROM"] = "noreply@example.com" })
            .Build();
        return new ResendEmailSender(new HttpClient(new StubHandler(respond)), configuration, NullLogger<ResendEmailSender>.Instance);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;
        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(_respond(request));
    }
}
