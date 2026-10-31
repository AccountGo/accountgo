using System.Net;
using System.Text;
using Api.Service;
using Api.Data;
using Core.Domain.Sales;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AccountGo.BackendTests.Sales;

public class EmailDispatchContractTests
{
    [Theory]
    [InlineData(408, true)] [InlineData(409, true)] [InlineData(422, false)] [InlineData(429, false)] [InlineData(503, true)]
    public async Task ResendFailure_OnlyKnownNonAcceptance_IsDefinitive(int status, bool unknown)
    {
        var sender = new ResendEmailSender(new HttpClient(new CaptureHandler((HttpStatusCode)status)),
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["RESEND_API_KEY"] = "test" }).Build(),
            NullLogger<ResendEmailSender>.Instance);
        var ex = await Assert.ThrowsAsync<EmailDeliveryException>(() => sender.SendAsync(new EmailMessage { To = "customer@example.org" }));
        Assert.Equal(unknown, ex.AcceptanceUnknown);
    }
    [Fact]
    public async Task CommunityDispatch_CustomMode_IsDeniedBeforeTransport()
    {
        var handler = new CaptureHandler();
        var sender = new ResendEmailSender(new HttpClient(handler), new ConfigurationBuilder().Build(), NullLogger<ResendEmailSender>.Instance);
        var dispatch = new PlatformEmailDispatchService(new PlatformEmailSenderResolver(), sender);
        var ex = await Assert.ThrowsAsync<EmailPolicyException>(() => dispatch.SendAsync(
            new EmailDispatchContext(1, "actor", 42, "Invoice", "custom", "foreign-sender", Guid.NewGuid()), new EmailMessage()));
        Assert.Equal("CUSTOM_EMAIL_UNAVAILABLE", ex.Code);
        Assert.Null(handler.Body);
    }

    [Fact]
    public async Task InvoiceHistory_SameDispatchTwice_DatabaseRejectsDuplicate()
    {
        await using var db = new ApiDbContext(new DbContextOptionsBuilder<ApiDbContext>().UseSqlite("Data Source=:memory:").Options);
        await db.Database.OpenConnectionAsync();
        await db.Database.EnsureCreatedAsync();
        await db.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys=OFF");
        db.SalesInvoiceEmails.Add(new SalesInvoiceEmail { CompanyId = 1, SalesInvoiceHeaderId = 42, DispatchId = "dispatch-1" });
        await db.SaveChangesAsync();
        db.SalesInvoiceEmails.Add(new SalesInvoiceEmail { CompanyId = 1, SalesInvoiceHeaderId = 42, DispatchId = "dispatch-1" });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task ResendSend_StableDispatchKey_ForwardsKeyWithoutChangingMessage()
    {
        var handler = new CaptureHandler();
        var sender = new ResendEmailSender(new HttpClient(handler),
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["RESEND_API_KEY"] = "test" }).Build(),
            NullLogger<ResendEmailSender>.Instance);
        var message = new EmailMessage { To = "customer@example.org", Subject = "Invoice", Html = "<p>Due</p>" };
        // A missing idempotency contract must fail as an assertion before the transport is changed.
        var key = typeof(EmailMessage).GetProperty("IdempotencyKey");
        Assert.NotNull(key);
        key.SetValue(message, "invoice/0192c530-1111-7000-8000-000000000001");
        await sender.SendAsync(message);
        Assert.Equal("invoice/0192c530-1111-7000-8000-000000000001", handler.Key);
        Assert.Contains("<p>Due</p>", handler.Body!.Replace("\\u003C", "<").Replace("\\u003E", ">"));
    }

    private sealed class CaptureHandler(HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public string? Key;
        public string? Body;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Key = request.Headers.TryGetValues("Idempotency-Key", out var keys) ? keys.Single() : null;
            Body = await request.Content!.ReadAsStringAsync(ct);
            return new HttpResponseMessage(status) { Content = new StringContent("{\"id\":\"message-1\"}", Encoding.UTF8, "application/json") };
        }
    }
}
