using System.Text.Json;
using Api.DocumentCaptures;
using Xunit;

namespace AccountGo.BackendTests.DocumentCapture;

public sealed class DocumentCaptureMaskingTests
{
    [Theory]
    [InlineData("routing 021000021", "routing ••••")]
    [InlineData("account 1234-5678-9012", "account ••••")]
    [InlineData("12345678", "••••")]
    [InlineData("12345678901234567", "••••")]
    [InlineData("1234567", "1234567")]
    [InlineData("123456789012345678", "123456789012345678")]
    [InlineData("A021000021Z", "A••••Z")]
    [InlineData("2026-11-21", "2026-11-21")]
    [InlineData("Due 2026-11-21; account 12345678", "Due 2026-11-21; account ••••")]
    [InlineData("20261121", "20261121")]
    [InlineData("20261321", "••••")]
    [InlineData("account 123456789012", "account ••••")]
    [InlineData("account 1234 5678 9012", "account ••••")]
    [InlineData("IBAN GB82 WEST 1234 5698 7654 32", "IBAN ••••")]
    public void MaskSensitiveValue_MasksFinancialIdentifiers(string input, string expected)
    {
        Assert.Equal(expected, DocumentCaptureMasking.MaskSensitiveValue(input));
    }

    [Fact]
    public void AssistantFields_AreMaskedBeforeCanonicalStorage()
    {
        using var input = JsonDocument.Parse("""{"fields":[{"label":"Routing number","value":"021000021","confidence":0.9}]}""");

        var result = DocumentCaptureMasking.MaskAssistantFields(input.RootElement);

        using var output = JsonDocument.Parse(result);
        Assert.Equal("••••", output.RootElement.GetProperty("fields")[0].GetProperty("value").GetString());
        Assert.DoesNotContain("021000021", result, StringComparison.Ordinal);
    }

    [Fact]
    public void AssistantLabels_AreMaskedAlongsideValues()
    {
        using var input = JsonDocument.Parse("""{"fields":[{"label":"021000021","value":"021000021"}]}""");

        var result = DocumentCaptureMasking.MaskAssistantFields(input.RootElement);

        using var output = JsonDocument.Parse(result);
        Assert.Equal("••••", output.RootElement.GetProperty("fields")[0].GetProperty("label").GetString());
        Assert.Equal("••••", output.RootElement.GetProperty("fields")[0].GetProperty("value").GetString());
    }

    [Fact]
    public void RawText_IsBoundedAndMasked()
    {
        var result = DocumentCaptureMasking.MaskRawText("Routing: 021000021");

        Assert.Equal("Routing: ••••", result);
        Assert.Equal(DocumentCaptureFieldSchemas.MaxRawTextLength,
            DocumentCaptureMasking.MaskRawText(new string('x', DocumentCaptureFieldSchemas.MaxRawTextLength + 1))!.Length);
    }

    [Fact]
    public void FinancialFreeText_IsMaskedWithoutChangingStructuredNumbers()
    {
        const string fields = """{"notes":"Pay 021000021","sourceInvoiceNo":"INV-021000021","grandTotal":123456789,"lines":[{"description":"Account 1234-5678-9012","quantity":1}]}""";
        var result = DocumentCaptureMasking.MaskFinancialFreeText("purchase-invoice", fields);
        using var output = JsonDocument.Parse(result);
        Assert.Equal("Pay ••••", output.RootElement.GetProperty("notes").GetString());
        Assert.Equal("Account ••••", output.RootElement.GetProperty("lines")[0].GetProperty("description").GetString());
        Assert.Equal("INV-021000021", output.RootElement.GetProperty("sourceInvoiceNo").GetString());
        Assert.Equal(123456789, output.RootElement.GetProperty("grandTotal").GetInt32());
    }

    [Fact]
    public void ProviderWarnings_AreReducedToStableCodes()
    {
        Assert.Equal("LOW_CONFIDENCE", DocumentCaptureWarningCodes.Sanitize("LOW_CONFIDENCE"));
        Assert.Equal("REVIEW_REQUIRED", DocumentCaptureWarningCodes.Sanitize("Routing number 021000021"));
    }
}
