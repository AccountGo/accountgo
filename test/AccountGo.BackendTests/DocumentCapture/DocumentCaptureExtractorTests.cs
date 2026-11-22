using Api.DocumentCaptures;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace AccountGo.BackendTests.DocumentCapture;

public sealed class DocumentCaptureExtractorTests
{
    [Fact]
    public void Options_DefaultToDisabledAndNoExtractor()
    {
        var options = new DocumentCaptureOptions();

        Assert.False(options.Enabled);
        Assert.Equal("None", options.Extractor);
        Assert.Equal(20, options.MaxAttemptsPerHour);
        Assert.Equal(60, options.MaxAttemptsPerDay);
        Assert.Equal(4, options.ImageDecodeSlots);
        Assert.Equal(1, options.PdfParseSlots);
    }

    [Fact]
    public void Options_AcceptTheDocumentedRateCapsAndRejectHigherCaps()
    {
        var environment = new TestHostEnvironment { EnvironmentName = Environments.Development };

        DocumentCaptureOptions.Validate(new DocumentCaptureOptions
        {
            MaxAttemptsPerHour = 20,
            MaxAttemptsPerDay = 60
        }, environment);
        Assert.Throws<InvalidOperationException>(() => DocumentCaptureOptions.Validate(
            new DocumentCaptureOptions { MaxAttemptsPerHour = 21 }, environment));
        Assert.Throws<InvalidOperationException>(() => DocumentCaptureOptions.Validate(
            new DocumentCaptureOptions { MaxAttemptsPerDay = 61 }, environment));
    }

    [Fact]
    public void Options_RejectDecodeSlotCountsOutsideSupportedRanges()
    {
        var environment = new TestHostEnvironment { EnvironmentName = Environments.Development };

        Assert.Throws<InvalidOperationException>(() => DocumentCaptureOptions.Validate(
            new DocumentCaptureOptions { ImageDecodeSlots = 5 }, environment));
        Assert.Throws<InvalidOperationException>(() => DocumentCaptureOptions.Validate(
            new DocumentCaptureOptions { PdfParseSlots = 0 }, environment));
    }

    [Fact]
    public void Options_RejectFakeExtractorInProduction()
    {
        var options = new DocumentCaptureOptions { Extractor = "Fake" };
        var environment = new TestHostEnvironment { EnvironmentName = "Production" };

        var exception = Assert.Throws<InvalidOperationException>(() => DocumentCaptureOptions.Validate(options, environment));

        Assert.Contains("only in development and tests", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Options_RequireLeaseMarginAndRestrictFakeToDevelopmentAndTests()
    {
        var development = new TestHostEnvironment { EnvironmentName = Environments.Development };
        DocumentCaptureOptions.Validate(new DocumentCaptureOptions { ExtractionLeaseSeconds = 30, Extractor = "Fake" }, development);
        Assert.Throws<InvalidOperationException>(() => DocumentCaptureOptions.Validate(
            new DocumentCaptureOptions { ExtractionLeaseSeconds = 29 }, development));
        Assert.Throws<InvalidOperationException>(() => DocumentCaptureOptions.Validate(
            new DocumentCaptureOptions { Extractor = "Fake" }, new TestHostEnvironment { EnvironmentName = "Staging" }));
        DocumentCaptureOptions.Validate(new DocumentCaptureOptions { Extractor = "Fake" },
            new TestHostEnvironment { EnvironmentName = "Testing" });
    }

    [Fact]
    public async Task FakeExtractor_UsesConfiguredFixtureForKind()
    {
        var options = Options.Create(new DocumentCaptureOptions
        {
            Extractor = "Fake",
            FakeFixtures = new Dictionary<string, string>
            {
                ["expense-receipt"] = "{\"vendorName\":\"Fixture Vendor\",\"date\":\"2026-11-20\",\"lines\":[]}"
            }
        });
        var extractor = new FakeDocumentFieldExtractor(options);

        var result = await extractor.ExtractAsync(
            new DocumentExtractionRequest("expense-receipt", 1, "image/png", new byte[] { 1, 2, 3 }, 1),
            CancellationToken.None);

        Assert.Equal("fake", result.ProviderName);
        Assert.Contains("Fixture Vendor", result.FieldsJson, StringComparison.Ordinal);
        Assert.Equal(1, result.DetectedDocumentCount);
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
