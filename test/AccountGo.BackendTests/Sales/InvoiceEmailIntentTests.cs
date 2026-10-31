using Api.Service;
using Dto.Sales;
using Xunit;

namespace AccountGo.BackendTests.Sales;

public class InvoiceEmailIntentTests
{
    [Fact]
    public void Fingerprint_DeliveryHistoryAndRegeneratedLink_DoNotChangeRetryIntent()
    {
        var invoice = new SalesInvoice { Id = 42, No = "INV-0042", BalanceDue = 100 };
        var message = new EmailMessage { To = "customer@example.org", Html = "first link", ReplyTo = "accounts@example.com" };
        var original = InvoiceEmailIntent.Fingerprint(invoice, false, false, message);
        invoice.Emails.Add(new InvoiceEmailItemDto { Id = 12 });
        message.Html = "regenerated link";
        Assert.Equal(original, InvoiceEmailIntent.Fingerprint(invoice, false, false, message));
        message.Cc.Add("changed@example.org");
        Assert.NotEqual(original, InvoiceEmailIntent.Fingerprint(invoice, false, false, message));
    }
}
