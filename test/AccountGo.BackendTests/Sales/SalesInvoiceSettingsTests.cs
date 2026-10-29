using Core.Domain;
using Xunit;

namespace AccountGo.BackendTests.Sales;

public class SalesInvoiceSettingsTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{not json")]
    [InlineData("[1,2,3]")]
    // Shape written by an earlier draft of the settings endpoint: booleans stored as strings.
    [InlineData("{\"showNotes\":\"False\",\"sellerName\":\"LegalName\"}")]
    public void FromJson_UnreadableInput_ReturnsDefaults(string? json)
    {
        AssertDefaults(SalesInvoiceSettings.FromJson(json));
    }

    [Fact]
    public void FromJson_PartialJson_KeepsDefaultsForMissingKeys()
    {
        var settings = SalesInvoiceSettings.FromJson("{\"showCustomerEmail\":false,\"futureOption\":42}");

        Assert.False(settings.ShowCustomerEmail);
        Assert.True(settings.ShowSellerAddress);
        Assert.True(settings.ShowNotes);
        Assert.True(settings.ShowPaymentInstructions);
        Assert.Equal(SalesInvoiceSettings.TradingName, settings.SellerName);
    }

    [Fact]
    public void FromJson_UnknownSellerName_FallsBackToTradingName()
    {
        Assert.Equal(SalesInvoiceSettings.TradingName, SalesInvoiceSettings.FromJson("{\"sellerName\":\"Nickname\"}").SellerName);
    }

    [Fact]
    public void ToJson_RoundTripsEverySetting()
    {
        var original = new SalesInvoiceSettings
        {
            SellerName = SalesInvoiceSettings.LegalName,
            ShowSellerAddress = false,
            ShowCustomerEmail = false,
            ShowNotes = false,
            ShowPaymentInstructions = false,
            DefaultNotes = "Thanks!",
            DefaultPaymentInstructions = "Bank transfer",
        };

        var json = original.ToJson();
        var copy = SalesInvoiceSettings.FromJson(json);

        Assert.Contains("\"showNotes\":false", json);
        Assert.Equal(original.SellerName, copy.SellerName);
        Assert.False(copy.ShowSellerAddress);
        Assert.False(copy.ShowCustomerEmail);
        Assert.False(copy.ShowNotes);
        Assert.False(copy.ShowPaymentInstructions);
        Assert.Equal("Thanks!", copy.DefaultNotes);
        Assert.Equal("Bank transfer", copy.DefaultPaymentInstructions);
    }

    [Fact]
    public void PostalAddress_ToLines_SkipsBlankParts()
    {
        var full = new PostalAddress { Line1 = "12 Main St", Line2 = " Unit 4 ", City = "Makati", Region = "Metro Manila", PostalCode = "1200", CountryCode = "Philippines" };
        Assert.Equal(new[] { "12 Main St", "Unit 4", "Makati, Metro Manila 1200", "Philippines" }, full.ToLines());

        Assert.Equal(new[] { "Makati" }, new PostalAddress { City = "Makati" }.ToLines());
        Assert.Equal(new[] { "Makati 1200" }, new PostalAddress { City = "Makati", PostalCode = "1200" }.ToLines());
        Assert.Equal(new[] { "Metro Manila 1200" }, new PostalAddress { Region = "Metro Manila", PostalCode = "1200" }.ToLines());
        Assert.True(new PostalAddress { Line1 = "  " }.IsEmpty);
    }

    [Fact]
    public void InvoiceSeller_RoundTripsAndIgnoresUnreadableJson()
    {
        var seller = new Core.Domain.Sales.InvoiceSeller { Name = "Juan", TaxId = "123", AddressLines = { "12 Main St", "Makati" }, Email = "j@example.com" };
        var copy = Core.Domain.Sales.InvoiceSeller.FromJson(seller.ToJson())!;

        Assert.Equal("Juan", copy.Name);
        Assert.Equal("123", copy.TaxId);
        Assert.Equal(new[] { "12 Main St", "Makati" }, copy.AddressLines);
        Assert.Equal("j@example.com", copy.Email);
        Assert.Null(copy.Phone);
        Assert.Null(Core.Domain.Sales.InvoiceSeller.FromJson(null));
        Assert.Null(Core.Domain.Sales.InvoiceSeller.FromJson("{not json"));
    }

    [Theory]
    [InlineData(SalesInvoiceStatus.Draft, false)]
    [InlineData(SalesInvoiceStatus.Void, false)]
    [InlineData(SalesInvoiceStatus.Open, true)]
    [InlineData(SalesInvoiceStatus.Closed, true)]
    public void CaptureSellerIfIssued_OnlyRecordsIssuedInvoices_AndOnlyOnce(SalesInvoiceStatus status, bool captured)
    {
        var company = new Company { Name = "Acme" };
        var invoice = new Core.Domain.Sales.SalesInvoiceHeader { Status = status };

        invoice.CaptureSellerIfIssued(company, null);
        Assert.Equal(captured, invoice.SellerJson != null);

        if (captured)
        {
            company.Name = "Renamed";
            invoice.CaptureSellerIfIssued(company, null);
            Assert.Equal("Acme", Core.Domain.Sales.InvoiceSeller.FromJson(invoice.SellerJson)!.Name);
        }
    }

    private static void AssertDefaults(SalesInvoiceSettings settings)
    {
        Assert.Equal(SalesInvoiceSettings.TradingName, settings.SellerName);
        Assert.True(settings.ShowSellerAddress);
        Assert.True(settings.ShowCustomerEmail);
        Assert.True(settings.ShowNotes);
        Assert.True(settings.ShowPaymentInstructions);
        Assert.Null(settings.DefaultNotes);
        Assert.Null(settings.DefaultPaymentInstructions);
    }
}
