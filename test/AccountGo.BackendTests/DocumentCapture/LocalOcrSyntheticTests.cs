using System.Globalization;
using System.Text;
using System.Text.Json;
using Api.DocumentCaptures;
using Microsoft.Extensions.Options;
using SkiaSharp;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Writer;
using Xunit;
using Xunit.Abstractions;

namespace AccountGo.BackendTests.DocumentCapture;

[Collection(DocumentCaptureResourceCollection.Name)]
[Trait("Category", "LocalOcr")]
public sealed class LocalOcrSyntheticTests(ITestOutputHelper output)
{
    private static readonly string[] Variants = ["clean", "skew", "blur", "dark", "lowres"];

    [LocalOcrFact]
    public async Task ScannedPdfRecognizesThreeSyntheticEnglishDocuments()
    {
        using var extractor = CreateExtractor();
        var (directory, fixtures) = await LoadFixturesAsync();
        var problems = new List<string>();
        foreach (var fixture in fixtures.Where(fixture => fixture.Lang == "en").Take(3))
        {
            var image = await File.ReadAllBytesAsync(Path.Combine(directory, fixture.Id + "_clean.png"));
            using var bitmap = SKBitmap.Decode(image);
            var width = bitmap.Width * 72d / 200;
            var height = bitmap.Height * 72d / 200;
            var pdf = new PdfDocumentBuilder();
            pdf.AddPage(width, height).AddPng(image, new PdfRectangle(0, 0, width, height));
            var bytes = pdf.Build();
            using (var parsed = UglyToad.PdfPig.PdfDocument.Open(bytes))
                Assert.Empty(parsed.GetPage(1).Text);
            await CheckDocumentAsync(extractor, fixture, fixture.Id + " scanned", "application/pdf", bytes, problems);
        }
        Assert.Empty(problems);
    }

    [LocalOcrFact]
    public async Task ScannedPdfSkipsAdditionalPagesWithAReviewWarning()
    {
        using var extractor = CreateExtractor();
        var pdf = new PdfDocumentBuilder();
        for (var page = 0; page < 4; page++)
            pdf.AddPage(200, 200);

        var result = await extractor.ExtractAsync(
            new DocumentExtractionRequest("purchase-invoice", 1, "application/pdf", pdf.Build(), 4), CancellationToken.None);

        Assert.Contains("OCR_PARTIAL_PAGES", result.Warnings);
    }

    [LocalOcrFact]
    [Trait("Rule", "V-01")]
    [Trait("Rule", "D-01")]
    [Trait("Rule", "T-03")]
    public async Task PdfTextLayerMatchesEverySyntheticInvoice()
    {
        using var extractor = CreateExtractor();
        var (directory, fixtures) = await LoadFixturesAsync();
        var problems = new List<string>();
        foreach (var fixture in fixtures)
            await CheckAsync(extractor, directory, fixture, fixture.Id + ".pdf", "application/pdf", problems);

        output.WriteLine($"PDF text layer: {fixtures.Length - problems.Select(problem => problem.Split(' ')[0]).Distinct().Count()}/{fixtures.Length} documents match all fields.");
        Assert.Empty(problems);
    }

    [LocalOcrFact]
    [Trait("Rule", "V-01")]
    [Trait("Rule", "D-01")]
    [Trait("Rule", "T-03")]
    public async Task EnglishImageOcrMeetsSyntheticFieldAccuracyFloor()
    {
        using var extractor = CreateExtractor();
        var (directory, allFixtures) = await LoadFixturesAsync();
        var fixtures = allFixtures.Where(fixture => fixture.Lang == "en").ToArray();
        Assert.Equal(65, fixtures.Length * Variants.Length);
        var problems = new List<string>();
        foreach (var fixture in fixtures)
            foreach (var variant in Variants)
                await CheckAsync(extractor, directory, fixture, $"{fixture.Id}_{variant}.png", "image/png", problems);

        var allowedErrors = fixtures.Length * Variants.Length * 2 / 100;
        foreach (var field in new[] { "vendor", "date", "total" })
        {
            var failures = problems.Count(problem => problem.EndsWith(" " + field, StringComparison.Ordinal));
            output.WriteLine($"English OCR {field}: {fixtures.Length * Variants.Length - failures}/{fixtures.Length * Variants.Length} match.");
            Assert.True(failures <= allowedErrors,
                "Synthetic mismatches: " + string.Join(", ", problems));
        }
    }

    private static async Task CheckAsync(LocalOcrDocumentFieldExtractor extractor, string directory, Truth fixture,
        string file, string contentType, List<string> problems)
    {
        var bytes = await File.ReadAllBytesAsync(Path.Combine(directory, file));
        if (contentType == "image/png")
        {
            var normalized = await DocumentCaptureImageNormalizer.NormalizeAsync(bytes, contentType);
            bytes = normalized.Bytes;
            contentType = normalized.ContentType;
        }
        await CheckDocumentAsync(extractor, fixture, file, contentType, bytes, problems);
    }

    private static async Task CheckDocumentAsync(LocalOcrDocumentFieldExtractor extractor, Truth fixture, string file,
        string contentType, byte[] bytes, List<string> problems)
    {
        var result = await extractor.ExtractAsync(new DocumentExtractionRequest("purchase-invoice", 1, contentType, bytes, 1),
            CancellationToken.None);
        Assert.Equal("local-ocr", result.ProviderName);
        Assert.StartsWith("ocr-2+", result.ProviderVersion, StringComparison.Ordinal);
        using var json = JsonDocument.Parse(result.FieldsJson);
        var root = json.RootElement;
        if (NormalizeName(ReadString(root, "vendorName")) != NormalizeName(fixture.Vendor))
            problems.Add(file + " vendor");
        if (ReadString(root, "date") != fixture.Date)
            problems.Add(file + " date");
        if (!root.TryGetProperty("sourceTotals", out var totals)
            || !decimal.TryParse(ReadString(totals, "grandTotal"), NumberStyles.Number, CultureInfo.InvariantCulture, out var observed)
            || observed != decimal.Parse(fixture.Total, NumberStyles.Number, CultureInfo.InvariantCulture))
            problems.Add(file + " total");
    }

    private static string ReadString(JsonElement node, string key) =>
        node.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";

    private static string NormalizeName(string name)
    {
        var canonical = new StringBuilder();
        foreach (var rune in name.Normalize().EnumerateRunes())
        {
            var category = Rune.GetUnicodeCategory(rune);
            if (Rune.IsLetterOrDigit(rune) || category is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark)
                canonical.Append(Rune.ToUpperInvariant(rune).ToString());
        }
        return canonical.ToString();
    }

    private static LocalOcrDocumentFieldExtractor CreateExtractor()
    {
        var options = new DocumentCaptureOptions { Extractor = "local-ocr" };
        options.OcrWorker.WorkerPath = Environment.GetEnvironmentVariable("ACCOUNTGO_TEST_OCR_WORKER");
        options.OcrWorker.ValidateFiles();
        return new LocalOcrDocumentFieldExtractor(Options.Create(options));
    }

    private static async Task<(string Directory, Truth[] Fixtures)> LoadFixturesAsync()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var directory = Path.Combine(root, "test", "AccountGo.BackendTests", "Fixtures", "DocumentCapture", "Synthetic");
        var fixtures = JsonSerializer.Deserialize<Truth[]>(await File.ReadAllBytesAsync(Path.Combine(directory, "truth.json")),
            CaptureWorkerProtocol.Json);
        Assert.NotNull(fixtures);
        Assert.Equal(18, fixtures.Length);
        return (directory, fixtures);
    }

    private sealed record Truth(string Id, string Vendor, string Date, string Total, string Lang);

    private sealed class LocalOcrFactAttribute : FactAttribute
    {
        public LocalOcrFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ACCOUNTGO_TEST_OCR_WORKER")))
                Skip = "Set ACCOUNTGO_TEST_OCR_WORKER to the built local OCR worker.";
        }
    }
}
