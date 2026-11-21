using System.Text.Json;
using Api.DocumentCaptures;
using Xunit;

namespace AccountGo.BackendTests.DocumentCapture;

public sealed class DocumentCaptureMaskingTests
{
    [Theory]
    [InlineData("routing 021000021", "routing *****0021")]
    [InlineData("account 1234-5678-9012", "account ****-****-9012")]
    [InlineData("IBAN GB82 WEST 1234 5698 7654 32", "IBAN **** **** **** **** **54 32")]
    public void MaskSensitiveValue_MasksFinancialIdentifiers(string input, string expected)
    {
        Assert.Equal(expected, DocumentCaptureMasking.MaskSensitiveValue(input));
    }

    [Fact]
    public void AssistantFields_AreMaskedBeforeCanonicalStorage()
    {
        using var input = JsonDocument.Parse("""{"fields":[{"label":"Routing number","value":"021000021","confidence":0.9}]}""");

        var result = DocumentCaptureMasking.MaskAssistantFields(input.RootElement);

        Assert.Contains("*****0021", result, StringComparison.Ordinal);
        Assert.DoesNotContain("021000021", result, StringComparison.Ordinal);
    }

    [Fact]
    public void AssistantLabels_AreMaskedAlongsideValues()
    {
        using var input = JsonDocument.Parse("""{"fields":[{"label":"021000021","value":"021000021"}]}""");

        var result = DocumentCaptureMasking.MaskAssistantFields(input.RootElement);

        Assert.Equal(2, result.Split("*****0021", StringSplitOptions.None).Length - 1);
        Assert.DoesNotContain("021000021", result, StringComparison.Ordinal);
    }

    [Fact]
    public void RawText_IsBoundedAndMasked()
    {
        var result = DocumentCaptureMasking.MaskRawText("Routing: 021000021");

        Assert.Equal("Routing: *****0021", result);
        Assert.Equal(DocumentCaptureFieldSchemas.MaxRawTextLength,
            DocumentCaptureMasking.MaskRawText(new string('x', DocumentCaptureFieldSchemas.MaxRawTextLength + 1))!.Length);
    }

    [Fact]
    public void ProviderWarnings_AreReducedToStableCodes()
    {
        Assert.Equal("LOW_CONFIDENCE", DocumentCaptureWarningCodes.Sanitize("LOW_CONFIDENCE"));
        Assert.Equal("REVIEW_REQUIRED", DocumentCaptureWarningCodes.Sanitize("Routing number 021000021"));
    }
}
