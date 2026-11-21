using System.Text.Json;
using Api.DocumentCaptures;
using Xunit;

namespace AccountGo.BackendTests.DocumentCapture;

public sealed class DocumentCaptureFieldSchemaTests
{
    [Fact]
    public void VendorSchema_AcceptsInvariantReviewedFields()
    {
        using var input = JsonDocument.Parse("""
            {
              "vendorName": "Northwind Supplies",
              "vendorInvoiceNo": "INV-1042",
              "date": "2026-11-20",
              "currency": "USD",
              "lines": [{ "description": "Paper", "quantity": "2", "unitAmount": "12.50", "sourceLineTotal": "25.00", "priceBasis": "exclusive" }],
              "sourceTotals": { "subtotal": "25.00", "taxTotal": "2.50", "grandTotal": "27.50" },
              "paymentHints": { "paymentStatus": "unknown" }
            }
            """);

        Assert.True(DocumentCaptureFieldSchemas.TryValidate("expense-receipt", 1, input.RootElement, out var canonical, out var error));
        Assert.Null(error);
        Assert.Contains("Northwind Supplies", canonical, StringComparison.Ordinal);
    }

    [Fact]
    public void Schema_RejectsUnknownVersionAndUnknownFields()
    {
        using var input = JsonDocument.Parse("{\"vendorName\":\"Vendor\",\"date\":\"2026-11-20\",\"lines\":[],\"openingBalance\":\"500\"}");

        Assert.False(DocumentCaptureFieldSchemas.TryValidate("purchase-invoice", 2, input.RootElement, out _, out var versionError));
        Assert.Equal("DOCUMENT_KIND_UNKNOWN", versionError);

        Assert.False(DocumentCaptureFieldSchemas.TryValidate("purchase-invoice", 1, input.RootElement, out _, out var fieldError));
        Assert.Equal("DOCUMENT_ACCEPT_INVALID", fieldError);
    }

    [Fact]
    public void VendorSchema_RejectsMoreThanOneHundredLines()
    {
        var lines = string.Join(',', Enumerable.Repeat("{\"description\":\"x\"}", 101));
        using var input = JsonDocument.Parse($"{{\"vendorName\":\"Vendor\",\"date\":\"2026-11-20\",\"lines\":[{lines}]}}");

        Assert.False(DocumentCaptureFieldSchemas.TryValidate("purchase-invoice", 1, input.RootElement, out _, out var error));
        Assert.Equal("DOCUMENT_ACCEPT_INVALID", error);
    }

    [Theory]
    [InlineData("1234567890123456789")]
    [InlineData("1.12345")]
    [InlineData("1,25")]
    public void VendorSchema_RejectsUnboundedOrLocaleDependentAmounts(string amount)
    {
        using var input = JsonDocument.Parse($"{{\"vendorName\":\"Vendor\",\"date\":\"2026-11-20\",\"lines\":[{{\"description\":\"x\",\"unitAmount\":\"{amount}\"}}]}}");

        Assert.False(DocumentCaptureFieldSchemas.TryValidate("purchase-invoice", 1, input.RootElement, out _, out var error));
        Assert.Equal("DOCUMENT_ACCEPT_INVALID", error);
    }

    [Theory]
    [InlineData("{\"fields\":[{\"label\":\"total\",\"value\":\"12\",\"box\":{\"page\":1,\"x\":0.1,\"y\":0.2,\"width\":0.3,\"height\":0.4}}]}", true)]
    [InlineData("{\"fields\":[{\"label\":\"total\",\"value\":\"12\",\"box\":{\"page\":1,\"x\":0.1,\"y\":0.2,\"width\":0.3,\"height\":0.4,\"secret\":\"x\"}}]}", false)]
    [InlineData("{\"fields\":[{\"label\":\"total\",\"value\":\"12\",\"box\":{\"page\":{\"nested\":{\"value\":1}}}}]}", false)]
    public void AssistantSchema_ValidatesPageAwareBoxesAndNesting(string json, bool expected)
    {
        using var input = JsonDocument.Parse(json);

        Assert.Equal(expected, DocumentCaptureFieldSchemas.TryValidate("assistant-attachment", 1, input.RootElement, out _, out _));
    }

    [Fact]
    public void CanonicalFields_AreIndependentOfObjectPropertyOrder()
    {
        using var first = JsonDocument.Parse("{\"date\":\"2026-11-20\",\"vendorName\":\"Vendor\"}");
        using var second = JsonDocument.Parse("{\"vendorName\":\"Vendor\",\"date\":\"2026-11-20\"}");

        Assert.True(DocumentCaptureFieldSchemas.TryValidate("purchase-invoice", 1, first.RootElement, out var canonicalFirst, out _));
        Assert.True(DocumentCaptureFieldSchemas.TryValidate("purchase-invoice", 1, second.RootElement, out var canonicalSecond, out _));
        Assert.Equal(canonicalFirst, canonicalSecond);
    }

    [Theory]
    [InlineData("{\"vendorName\":\"Vendor\"}")]
    [InlineData("{\"date\":\"2026-11-20\",\"lines\":[{\"description\":\"Item\",\"quantity\":\"0\",\"unitAmount\":\"10\"}]}")]
    [InlineData("{\"date\":\"2026-11-20\",\"lines\":[{\"description\":\"Item\"}]}")]
    public void AcceptedFinancialFields_RequireDateAndUsablePopulatedLines(string json)
    {
        using var input = JsonDocument.Parse(json);

        Assert.False(DocumentCaptureFieldSchemas.TryValidateAcceptedFields(
            "purchase-invoice", 1, input.RootElement, out _, out var error));
        Assert.Equal("DOCUMENT_ACCEPT_INVALID", error);
    }

    [Fact]
    public void ProviderSanitization_DiscardsUnknownFinancialAndAssistantFields()
    {
        var financial = DocumentCaptureFieldSchemas.SanitizeProviderFields("expense-receipt",
            "{\"vendorName\":\"Vendor\",\"vendorId\":42,\"lines\":[{\"description\":\"Paper\",\"accountId\":11}]}" );
        var assistant = DocumentCaptureFieldSchemas.SanitizeProviderFields("assistant-attachment",
            "{\"fields\":[{\"label\":\"Total\",\"value\":\"10\",\"taxId\":9}]}" );

        Assert.Equal("{\"lines\":[{\"description\":\"Paper\"}],\"vendorName\":\"Vendor\"}", financial);
        Assert.Equal("{\"fields\":[{\"label\":\"Total\",\"value\":\"10\"}]}", assistant);
    }
}
