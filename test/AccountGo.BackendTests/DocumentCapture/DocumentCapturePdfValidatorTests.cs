using Api.DocumentCaptures;
using System.Diagnostics;
using System.IO.Compression;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using UglyToad.PdfPig.Writer;
using Xunit;

namespace AccountGo.BackendTests.DocumentCapture;

[Collection(DocumentCaptureResourceCollection.Name)]
public sealed class DocumentCapturePdfValidatorTests
{
    private const long MaxParserAllocatedBytesPerOperation = 220_000_000;
    private const long MaxParserWorkingSetGrowthBytesPerOperation = 160_000_000;

    [Fact]
    public async Task ValidateAsync_AcceptsOnePageAndReturnsItsStructuralCount()
    {
        var result = await ValidateStructureAsync(CreatePdf(1));

        Assert.True(result.IsValid);
        Assert.Equal(1, result.PageCount);
        Assert.Null(result.ErrorCode);
    }

    [Fact]
    public async Task ValidateAsync_RejectsElevenPageDocument()
    {
        var result = await ValidateStructureAsync(CreatePdf(11));

        Assert.False(result.IsValid);
        Assert.Equal("DOCUMENT_UNSUPPORTED_TYPE", result.ErrorCode);
    }

    [Fact]
    public async Task ValidateAsync_RejectsOversizedInputBeforeParsing()
    {
        var result = await ValidateStructureAsync(new byte[DocumentCapturePdfLimits.MaxInputBytes + 1]);

        Assert.False(result.IsValid);
        Assert.Equal("DOCUMENT_TOO_LARGE", result.ErrorCode);
    }

    [Fact]
    public async Task ValidateAsync_ReturnsDecodeBusyWhenParseSlotIsHeld()
    {
        var gate = DocumentCapturePdfValidator.ParseGates.GetOrAdd(
            DocumentCapturePdfLimits.ParseGateSlots,
            slots => new SemaphoreSlim(slots, slots));
        await gate.WaitAsync();
        try
        {
            var result = await ValidateStructureAsync(CreatePdf(1));

            Assert.False(result.IsValid);
            Assert.Equal("DOCUMENT_DECODE_BUSY", result.ErrorCode);
        }
        finally
        {
            gate.Release();
        }
    }

    [Fact]
    public async Task ValidateAsync_RejectsMalformedStructureFailClosed()
    {
        var result = await ValidateStructureAsync(Encoding.ASCII.GetBytes("%PDF-1.7\nnot a document"));

        Assert.False(result.IsValid);
        Assert.Equal("DOCUMENT_MALFORMED", result.ErrorCode);
    }

    [Fact]
    public async Task ValidateAsync_RejectsTruncatedDocument()
    {
        var complete = CreatePdf(1);
        var result = await ValidateStructureAsync(complete.AsMemory(0, complete.Length - 32));

        Assert.False(result.IsValid);
        Assert.Equal("DOCUMENT_MALFORMED", result.ErrorCode);
    }

    [Fact(Timeout = 60000)]
    public async Task ValidateAsync_RejectsCyclicPageTreeWithoutHanging()
    {
        var result = await ValidateStructureAsync(CreateStructuralPdf(cycle: true));

        Assert.False(result.IsValid);
    }

    [Fact(Timeout = 60000)]
    public async Task ValidateAsync_RejectsPageTreeWhoseDeclaredCountHidesElevenPages()
    {
        var result = await ValidateStructureAsync(CreateStructuralPdf(pageCount: 11, declaredCount: 1));

        Assert.False(result.IsValid);
    }

    [Fact(Timeout = 60000)]
    public async Task ValidateAsync_RejectsHugeKidsArrayWithinResourceBounds()
    {
        var pdf = CreateStructuralPdfWithHugeKids(100_000);
        var metrics = await RunPdfProbeAsync(pdf);

        Assert.False(metrics.IsValid);
        Assert.Equal("DOCUMENT_UNSUPPORTED_TYPE", metrics.ErrorCode);
        AssertResourceBounds(metrics);
    }

    [Fact]
    public async Task ValidateAsync_UsesLatestPageTreeFromIncrementalRevision()
    {
        var result = await ValidateStructureAsync(CreateIncrementalPdf(oldPageCount: 11, activePageCount: 1));

        Assert.True(result.IsValid);
        Assert.Equal(1, result.PageCount);
    }

    [Fact]
    public async Task ValidateAsync_IgnoresPageTypeTextInAnUnrelatedStream()
    {
        var result = await ValidateStructureAsync(
            CreateIncrementalPdf(oldPageCount: 1, activePageCount: 1, unrelatedPageToken: true));

        Assert.True(result.IsValid);
        Assert.Equal(1, result.PageCount);
    }

    [Fact]
    public async Task ValidateAsync_RejectsEncryptedDocument()
    {
        var result = await ValidateStructureAsync(
            CreateIncrementalPdf(oldPageCount: 1, activePageCount: 1, encrypted: true));

        Assert.False(result.IsValid);
        Assert.Contains(result.ErrorCode, new[] { "DOCUMENT_UNSUPPORTED_TYPE", "DOCUMENT_MALFORMED" });
    }

    [Theory]
    [InlineData("AA")]
    [InlineData("Collection")]
    [InlineData("Perms")]
    [InlineData("AF")]
    public async Task ValidateAsync_RejectsUnsupportedCatalogFeatures(string key)
    {
        var result = await ValidateStructureAsync(
            CreateStructuralPdf(catalogEntries: $" /{key} << >>"));

        Assert.False(result.IsValid);
        Assert.Equal("DOCUMENT_UNSUPPORTED_TYPE", result.ErrorCode);
    }

    [Theory]
    [InlineData("JavaScript")]
    [InlineData("EmbeddedFiles")]
    public async Task ValidateAsync_RejectsActiveOrEmbeddedNameTrees(string key)
    {
        var result = await ValidateStructureAsync(
            CreateStructuralPdf(catalogEntries: $" /Names << /{key} << >> >>"));

        Assert.False(result.IsValid);
        Assert.Equal("DOCUMENT_UNSUPPORTED_TYPE", result.ErrorCode);
    }

    [Fact]
    public async Task ValidateAsync_AllowsNamedDestinationTree()
    {
        var result = await ValidateStructureAsync(
            CreateStructuralPdf(catalogEntries: " /Names << /Dests << /invoice [3 0 R /Fit] >> >>"));

        Assert.True(result.IsValid);
        Assert.Equal(1, result.PageCount);
    }

    [Theory]
    [InlineData("Screen")]
    [InlineData("RichMedia")]
    [InlineData("FileAttachment")]
    [InlineData("Movie")]
    [InlineData("Sound")]
    [InlineData("3D")]
    public async Task ValidateAsync_RejectsUnsupportedAnnotationSubtypes(string subtype)
    {
        var result = await ValidateStructureAsync(
            CreateIncrementalAnnotationPdf($"<< /Type /Annot /Subtype /{subtype} >>"));

        Assert.False(result.IsValid);
        Assert.Equal("DOCUMENT_UNSUPPORTED_TYPE", result.ErrorCode);
    }

    [Theory]
    [InlineData("JavaScript")]
    [InlineData("Launch")]
    [InlineData("SubmitForm")]
    [InlineData("ImportData")]
    [InlineData("Rendition")]
    [InlineData("GoToR")]
    [InlineData("GoToE")]
    [InlineData("Sound")]
    [InlineData("Movie")]
    public async Task ValidateAsync_RejectsUnsupportedAnnotationActions(string actionType)
    {
        var result = await ValidateStructureAsync(
            CreateIncrementalAnnotationPdf($"<< /Type /Annot /Subtype /Link /A << /S /{actionType} >> >>"));

        Assert.False(result.IsValid);
        Assert.Equal("DOCUMENT_UNSUPPORTED_TYPE", result.ErrorCode);
    }

    [Fact]
    public async Task ValidateAsync_RejectsUnsupportedAdditionalAnnotationActions()
    {
        var result = await ValidateStructureAsync(
            CreateIncrementalAnnotationPdf("<< /Type /Annot /Subtype /Text /AA << /O << /S /JavaScript >> >> >>"));

        Assert.False(result.IsValid);
        Assert.Equal("DOCUMENT_UNSUPPORTED_TYPE", result.ErrorCode);
    }

    [Fact]
    public async Task ValidateAsync_RejectsUnsupportedPageAdditionalActions()
    {
        var result = await ValidateStructureAsync(
            CreateStructuralPdf(pageEntries: " /AA << /O << /S /JavaScript >> >>"));

        Assert.False(result.IsValid);
        Assert.Equal("DOCUMENT_UNSUPPORTED_TYPE", result.ErrorCode);
    }

    [Fact]
    public async Task ValidateAsync_RejectsMoreThanFiveHundredAnnotationsOnOnePage()
    {
        var annotations = string.Join(" ", Enumerable.Repeat("<< /Type /Annot /Subtype /Text >>", 501));
        var result = await ValidateStructureAsync(
            CreateStructuralPdf(pageEntries: $" /Annots [{annotations}]"));

        Assert.False(result.IsValid);
        Assert.Equal("DOCUMENT_UNSUPPORTED_TYPE", result.ErrorCode);
    }

    [Fact]
    public async Task ValidateAsync_AllowsUriAndInternalLinkAnnotations()
    {
        var uriResult = await ValidateStructureAsync(
            CreateIncrementalAnnotationPdf("<< /Type /Annot /Subtype /Link /A << /S /URI /URI (https://example.test/invoice) >> >>"));
        var internalResult = await ValidateStructureAsync(
            CreateIncrementalAnnotationPdf("<< /Type /Annot /Subtype /Link /A << /S /GoTo /D [3 0 R /Fit] >> >>"));
        var textResult = await ValidateStructureAsync(
            CreateIncrementalAnnotationPdf("<< /Type /Annot /Subtype /Text >>"));

        Assert.True(uriResult.IsValid);
        Assert.True(internalResult.IsValid);
        Assert.True(textResult.IsValid);
    }

    [Theory]
    [InlineData("[3 0 R /Fit]")]
    [InlineData("/invoice")]
    [InlineData("(invoice)")]
    [InlineData("<< /S /GoTo /D [3 0 R /Fit] >>")]
    public async Task ValidateAsync_AllowsPassiveOpenActionDestinationsAndGoTo(string openAction)
    {
        var result = await ValidateStructureAsync(CreateStructuralPdf(catalogEntries: $" /OpenAction {openAction}"));

        Assert.True(result.IsValid);
        Assert.Equal(1, result.PageCount);
    }

    [Theory]
    [InlineData("<< /S /JavaScript /JS (app.alert(1)) >>")]
    [InlineData("<< /S /GoTo /D [3 0 R /Fit] /Next << /S /JavaScript /JS (app.alert(1)) >> >>")]
    [InlineData("99 0 R")]
    public async Task ValidateAsync_RejectsActiveOrUnresolvableOpenAction(string openAction)
    {
        var result = await ValidateStructureAsync(CreateStructuralPdf(catalogEntries: $" /OpenAction {openAction}"));

        Assert.False(result.IsValid);
        Assert.Contains(result.ErrorCode, new[] { "DOCUMENT_UNSUPPORTED_TYPE", "DOCUMENT_MALFORMED" });
    }

    [Theory]
    [InlineData("<< /S /URI /URI (https://example.test) /Next << /S /JavaScript /JS (app.alert(1)) >> >>")]
    [InlineData("<< /S /GoTo /D [3 0 R /Fit] /Next << /S /Launch /F (invoice.pdf) >> >>")]
    [InlineData("<< /S /URI /URI (https://example.test) /Next [<< /S /GoTo /D [3 0 R /Fit] >> << /S /JavaScript /JS (app.alert(1)) >>] >>")]
    public async Task ValidateAsync_RejectsUnsupportedActionAnywhereInNextChain(string action)
    {
        var result = await ValidateStructureAsync(
            CreateIncrementalAnnotationPdf($"<< /Type /Annot /Subtype /Link /A {action} >>"));

        Assert.False(result.IsValid);
        Assert.Equal("DOCUMENT_UNSUPPORTED_TYPE", result.ErrorCode);
    }

    [Fact]
    public async Task ValidateAsync_RejectsActionChainInPageAdditionalActions()
    {
        var result = await ValidateStructureAsync(CreateStructuralPdf(pageEntries:
            " /AA << /O << /S /URI /URI (https://example.test) /Next << /S /JavaScript /JS (x) >> >> >>"));

        Assert.False(result.IsValid);
        Assert.Equal("DOCUMENT_UNSUPPORTED_TYPE", result.ErrorCode);
    }

    [Fact(Timeout = 60000)]
    public async Task ValidateAsync_RejectsCyclicActionNextChainWithinBounds()
    {
        var pdf = CreateIncrementalAnnotationPdf(
            "<< /Type /Annot /Subtype /Link /A << /S /URI /URI (https://example.test) /Next 5 0 R >> >>",
            "<< /S /GoTo /D [3 0 R /Fit] /Next 5 0 R >>");

        var result = await ValidateStructureAsync(pdf);

        Assert.False(result.IsValid);
        Assert.Equal("DOCUMENT_UNSUPPORTED_TYPE", result.ErrorCode);
    }

    [Fact]
    public async Task ValidateAsync_AllowsUriToGoToActionChain()
    {
        var result = await ValidateStructureAsync(
            CreateIncrementalAnnotationPdf("<< /Type /Annot /Subtype /Link /A << /S /URI /URI (https://example.test) /Next << /S /GoTo /D [3 0 R /Fit] >> >> >>"));

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task ValidateAsync_AllowsPassiveAcroFormAndWidgetAnnotations()
    {
        var form = await ValidateStructureAsync(CreateStructuralPdf(
            catalogEntries: " /AcroForm << /Fields [<< /FT /Tx /T (VendorName) /V (Fixture) >>] >>"));
        var widget = await ValidateStructureAsync(
            CreateIncrementalAnnotationPdf("<< /Type /Annot /Subtype /Widget /Parent 4 0 R >>"));

        Assert.True(form.IsValid);
        Assert.True(widget.IsValid);
    }

    [Fact]
    public async Task ValidateAsync_RejectsXfaFieldActionsAndWidgetActions()
    {
        var xfa = await ValidateStructureAsync(CreateStructuralPdf(
            catalogEntries: " /AcroForm << /Fields [] /XFA (form) >>"));
        var fieldAction = await ValidateStructureAsync(CreateStructuralPdf(
            catalogEntries: " /AcroForm << /Fields [<< /FT /Tx /AA << /K << /S /JavaScript /JS (x) >> >> >>] >>"));
        var widgetAction = await ValidateStructureAsync(
            CreateIncrementalAnnotationPdf("<< /Type /Annot /Subtype /Widget /A << /S /JavaScript /JS (x) >> >>"));

        Assert.All(new[] { xfa, fieldAction, widgetAction }, result =>
        {
            Assert.False(result.IsValid);
            Assert.Equal("DOCUMENT_UNSUPPORTED_TYPE", result.ErrorCode);
        });
    }

    [Fact]
    public async Task ValidateAsync_RejectsFieldActionChainAndNeedsRenderingForm()
    {
        var fieldAction = await ValidateStructureAsync(CreateStructuralPdf(
            catalogEntries: " /AcroForm << /Fields [<< /FT /Tx /A << /S /URI /Next << /S /JavaScript /JS (x) >> >> >>] >>"));
        var needsRendering = await ValidateStructureAsync(CreateStructuralPdf(
            catalogEntries: " /NeedsRendering true /AcroForm << /Fields [] >>"));

        Assert.All(new[] { fieldAction, needsRendering }, result =>
        {
            Assert.False(result.IsValid);
            Assert.Equal("DOCUMENT_UNSUPPORTED_TYPE", result.ErrorCode);
        });
    }

    [Fact(Timeout = 60000)]
    public async Task ValidateAsync_RejectsCyclicFormFieldTreeWithinBounds()
    {
        var result = await ValidateStructureAsync(CreateStructuralPdf(
            catalogEntries: " /AcroForm << /Fields [4 0 R] >>",
            extraObject: "<< /FT /Tx /T (cycle) /Kids [4 0 R] >>"));

        Assert.False(result.IsValid);
        Assert.Contains(result.ErrorCode, new[] { "DOCUMENT_UNSUPPORTED_TYPE", "DOCUMENT_MALFORMED" });
    }

    [Fact(Timeout = 60000)]
    public async Task ValidateAsync_RejectsOverlyDeepFormFieldTreeWithinBounds()
    {
        var field = "<< /FT /Tx >>";
        for (var depth = 0; depth < 70; depth++)
            field = $"<< /Kids [{field}] >>";
        var result = await ValidateStructureAsync(CreateStructuralPdf(
            catalogEntries: $" /AcroForm << /Fields [{field}] >>"));

        Assert.False(result.IsValid);
        Assert.Contains(result.ErrorCode, new[] { "DOCUMENT_UNSUPPORTED_TYPE", "DOCUMENT_MALFORMED" });
    }

    [Fact(Timeout = 60000)]
    public async Task ValidateAsync_RejectsOverlyDeepPageTreeWithinTimeAndAllocationBounds()
    {
        var pdf = CreateStructuralPdf(nestingDepth: 2000);
        var metrics = await RunPdfProbeAsync(pdf);

        Assert.False(metrics.IsValid);
        Assert.Equal("DOCUMENT_UNSUPPORTED_TYPE", metrics.ErrorCode);
        AssertResourceBounds(metrics);
    }

    [Fact(Timeout = 60000)]
    public async Task ValidateAsync_RejectsOversizedDecodedXrefStreamWithinResourceBounds()
    {
        var pdf = CreateXrefStreamBomb();
        Assert.True(pdf.Length < DocumentCapturePdfLimits.MaxInputBytes);
        var metrics = await RunPdfProbeAsync(pdf);

        Assert.False(metrics.IsValid);
        Assert.Contains(metrics.ErrorCode, new[] { "DOCUMENT_MALFORMED", "DOCUMENT_UNSUPPORTED_TYPE" });
        AssertResourceBounds(metrics);
    }

    [Fact(Timeout = 60000)]
    public async Task ValidateAsync_RejectsOversizedDecodedObjectStreamWithinResourceBounds()
    {
        var pdf = CreateObjectStreamBomb();
        Assert.True(pdf.Length < DocumentCapturePdfLimits.MaxInputBytes);
        var metrics = await RunPdfProbeAsync(pdf);

        Assert.False(metrics.IsValid);
        Assert.Equal("DOCUMENT_UNSUPPORTED_TYPE", metrics.ErrorCode);
        AssertResourceBounds(metrics);
    }

    [Fact]
    public async Task ValidateAsync_AcceptsXrefStreamWithPngUpPredictor()
    {
        var result = await ValidateStructureAsync(CreatePredictorXrefPdf());

        Assert.True(result.IsValid, $"Parser returned {result.ErrorCode}.");
        Assert.Equal(1, result.PageCount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task ValidateAsync_AcceptsEachPngPredictorRowFilter(byte filterType)
    {
        var result = await ValidateStructureAsync(CreatePredictorXrefPdf(filterType: filterType));

        Assert.True(result.IsValid, $"PNG row filter {filterType} returned {result.ErrorCode}.");
        Assert.Equal(1, result.PageCount);
    }

    [Fact]
    public async Task ValidateAsync_AcceptsXrefStreamWithTiffPredictor()
    {
        var result = await ValidateStructureAsync(CreatePredictorXrefPdf(predictor: 2));

        Assert.True(result.IsValid, $"Parser returned {result.ErrorCode}.");
        Assert.Equal(1, result.PageCount);
    }

    [Fact]
    public async Task ValidateAsync_AcceptsLzwXrefStreamWithPngPredictor()
    {
        var result = await ValidateStructureAsync(CreatePredictorXrefPdf(useLzw: true));

        Assert.True(result.IsValid, $"Parser returned {result.ErrorCode}.");
        Assert.Equal(1, result.PageCount);
    }

    [Fact]
    public async Task ValidateAsync_AcceptsObjectStreamWithPngPredictor()
    {
        var result = await ValidateStructureAsync(CreatePredictorObjectStreamPdf());

        Assert.True(result.IsValid, $"Parser returned {result.ErrorCode}.");
        Assert.Equal(1, result.PageCount);
    }

    [Theory]
    [InlineData(0, 8, 6)]
    [InlineData(5, 8, 6)]
    [InlineData(1, 3, 6)]
    [InlineData(1, 8, 0)]
    [InlineData(1, 8, 70000)]
    public async Task ValidateAsync_RejectsInvalidPredictorParameters(int colors, int bitsPerComponent, int columns)
    {
        var result = await ValidateStructureAsync(
            CreatePredictorXrefPdf(colors: colors, bitsPerComponent: bitsPerComponent, columns: columns));

        Assert.False(result.IsValid);
        Assert.Equal("DOCUMENT_UNSUPPORTED_TYPE", result.ErrorCode);
    }

    [Fact]
    public async Task ValidateAsync_EncryptedXrefWithPredictorIsUnsupportedType()
    {
        var result = await ValidateStructureAsync(CreatePredictorXrefPdf(encrypted: true));

        Assert.False(result.IsValid);
        Assert.Equal("DOCUMENT_UNSUPPORTED_TYPE", result.ErrorCode);
    }

    [Fact(Timeout = 60000)]
    public async Task ValidateAsync_FlateBombBehindPredictorStaysWithinDecodeBudget()
    {
        var pdf = CreatePredictorXrefBomb(1_500_000);
        Assert.True(pdf.Length < DocumentCapturePdfLimits.MaxInputBytes);
        var metrics = await RunPdfProbeAsync(pdf);

        Assert.False(metrics.IsValid);
        Assert.Equal("DOCUMENT_UNSUPPORTED_TYPE", metrics.ErrorCode);
        AssertResourceBounds(metrics);
    }

    [Fact(Timeout = 60000)]
    public async Task ValidateAsync_ManyTinyUnreferencedObjectsStayWithinResourceBounds()
    {
        var pdf = CreateManyTinyObjectsPdf(175_000);
        Assert.InRange(pdf.Length, DocumentCapturePdfLimits.MaxInputBytes * 9 / 10, DocumentCapturePdfLimits.MaxInputBytes);
        var metrics = await RunPdfProbeAsync(pdf);

        Assert.True(metrics.IsValid, $"Parser returned {metrics.ErrorCode}.");
        Assert.Equal(1, metrics.PageCount);
        AssertResourceBounds(metrics);
    }

    [Fact(Timeout = 60000)]
    public async Task ValidateAsync_FullSizeObjectAndXrefStreamsStayWithinResourceBounds()
    {
        var pdf = CreateFullSizeObjectAndXrefStreamPdf();
        Assert.Equal(DocumentCapturePdfLimits.MaxInputBytes, pdf.Length);
        var metrics = await RunPdfProbeAsync(pdf);

        Assert.True(metrics.IsValid, $"Parser returned {metrics.ErrorCode}.");
        Assert.Equal(1, metrics.PageCount);
        AssertResourceBounds(metrics);
    }

    [Fact]
    public async Task ValidateAsync_CooperativeDecodeDeadlineReturnsRetryableBusy()
    {
        var result = await DocumentCapturePdfValidator.ValidateAsync(
            CreateFullSizeObjectAndXrefStreamPdf(), 1, new AdvancingTimeProvider());

        Assert.False(result.IsValid);
        Assert.Equal("DOCUMENT_BUSY", result.ErrorCode);
    }

    // Structural verdicts are measured independently of the runner's scheduling.
    private static Task<DocumentCapturePdfValidationResult> ValidateStructureAsync(ReadOnlyMemory<byte> bytes) =>
        DocumentCapturePdfValidator.ValidateAsync(bytes, DocumentCapturePdfLimits.ParseGateSlots, new FrozenTimeProvider());

    private sealed class FrozenTimeProvider : TimeProvider
    {
        public override long GetTimestamp() => 0;
    }

    private static byte[] CreatePdf(int pageCount)
    {
        var builder = new PdfDocumentBuilder();
        for (var page = 0; page < pageCount; page++)
            builder.AddPage(100, 100);
        return builder.Build();
    }

    private static async Task<PdfProbeMetrics> RunPdfProbeAsync(byte[] pdf)
    {
        var filePath = Path.Combine(Path.GetTempPath(), $"doc-capture-pdf-{Guid.NewGuid():N}.pdf");
        await File.WriteAllBytesAsync(filePath, pdf);
        try
        {
            var probePath = Path.Combine(AppContext.BaseDirectory, "AccountGo.PdfCaptureProbe.dll");
            Assert.True(File.Exists(probePath), $"PDF measurement probe not found at {probePath}.");
            var hostPath = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? Environment.ProcessPath ?? "dotnet";
            var start = new ProcessStartInfo(hostPath)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            start.ArgumentList.Add("exec");
            start.ArgumentList.Add(probePath);
            start.ArgumentList.Add(filePath);
            using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start the PDF measurement probe.");
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            var readyLine = await process.StandardOutput.ReadLineAsync(deadline.Token)
                ?? throw new InvalidOperationException("PDF measurement probe did not report its baseline.");
            Assert.StartsWith("READY|", readyLine, StringComparison.Ordinal);
            var baselineWorkingSet = long.Parse(readyLine["READY|".Length..], System.Globalization.CultureInfo.InvariantCulture);
            var outputTask = process.StandardOutput.ReadToEndAsync(deadline.Token);
            var errorTask = process.StandardError.ReadToEndAsync(deadline.Token);
            var peakWorkingSet = baselineWorkingSet;
            try
            {
                while (!process.HasExited)
                {
                    process.Refresh();
                    peakWorkingSet = Math.Max(peakWorkingSet, process.WorkingSet64);
                    await Task.Delay(TimeSpan.FromMilliseconds(1), deadline.Token);
                }
                await process.WaitForExitAsync(deadline.Token);
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                throw new TimeoutException("The PDF measurement subprocess exceeded 45 seconds.");
            }
            var output = await outputTask;
            var error = await errorTask;
            Assert.True(process.ExitCode == 0, $"PDF measurement probe exited {process.ExitCode}: {error}");
            using var document = System.Text.Json.JsonDocument.Parse(output);
            var root = document.RootElement;
            return new PdfProbeMetrics(
                root.GetProperty("IsValid").GetBoolean(),
                root.GetProperty("PageCount").ValueKind == System.Text.Json.JsonValueKind.Number
                    ? root.GetProperty("PageCount").GetInt32() : null,
                root.GetProperty("InputBytes").GetInt32(),
                root.GetProperty("AllocatedBytes").GetInt64(),
                root.GetProperty("WorkingSetGrowthBytes").GetInt64(),
                Math.Max(0, peakWorkingSet - baselineWorkingSet),
                root.GetProperty("ElapsedMilliseconds").GetDouble(),
                root.GetProperty("ErrorCode").ValueKind == System.Text.Json.JsonValueKind.String
                    ? root.GetProperty("ErrorCode").GetString() : null);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    private static void AssertResourceBounds(PdfProbeMetrics metrics)
    {
        Assert.True(metrics.ElapsedMilliseconds < DocumentCapturePdfLimits.ParseDeadline.TotalMilliseconds,
            $"Parsing {metrics.InputBytes:N0} bytes took {metrics.ElapsedMilliseconds:N0} ms.");
        Assert.True(metrics.AllocatedBytes < MaxParserAllocatedBytesPerOperation,
            $"Parsing {metrics.InputBytes:N0} bytes allocated {metrics.AllocatedBytes:N0} bytes (limit {MaxParserAllocatedBytesPerOperation:N0}).");
        Assert.True(metrics.PeakWorkingSetGrowthBytes < MaxParserWorkingSetGrowthBytesPerOperation,
            $"Parsing {metrics.InputBytes:N0} bytes grew peak working set by {metrics.PeakWorkingSetGrowthBytes:N0} bytes (limit {MaxParserWorkingSetGrowthBytesPerOperation:N0}).");
    }

    private sealed record PdfProbeMetrics(
        bool IsValid,
        int? PageCount,
        int InputBytes,
        long AllocatedBytes,
        long WorkingSetGrowthBytes,
        long PeakWorkingSetGrowthBytes,
        double ElapsedMilliseconds,
        string? ErrorCode);

    private sealed class AdvancingTimeProvider : TimeProvider
    {
        private long _timestamp;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Interlocked.Add(ref _timestamp, TimeSpan.FromSeconds(4).Ticks);
    }

    private static byte[] CreateStructuralPdf(int pageCount = 1, int declaredCount = 1, int nestingDepth = 0,
        bool cycle = false, string catalogEntries = "", string pageEntries = "", string? extraObject = null)
    {
        var objects = new List<string> { $"<< /Type /Catalog /Pages 2 0 R{catalogEntries} >>" };
        var nextObject = 3;
        if (cycle)
        {
            objects.Add("<< /Type /Pages /Kids [2 0 R] /Count 1 >>");
        }

        if (extraObject is not null)
        {
            objects.Add(extraObject);
            nextObject = objects.Count + 1;
        }
        else if (nestingDepth > 0)
        {
            var firstNode = 2;
            for (var i = 0; i < nestingDepth; i++)
            {
                var node = firstNode + i;
                var child = node + 1;
                objects.Add($"<< /Type /Pages /Parent {(node == 2 ? "1 0 R" : $"{node - 1} 0 R")} /Kids [{child} 0 R] /Count 1 >>");
            }
            var leaf = firstNode + nestingDepth;
            objects.Add($"<< /Type /Pages /Parent {leaf - 1} 0 R /Kids [{leaf + 1} 0 R] /Count 1 >>");
            objects.Add($"<< /Type /Page /Parent {leaf} 0 R /MediaBox [0 0 100 100] /Resources << >>{pageEntries} >>");
            nextObject = objects.Count + 1;
        }
        else
        {
            var pageRefs = Enumerable.Range(0, pageCount).Select(i => $"{3 + i} 0 R");
            objects.Add($"<< /Type /Pages /Kids [{string.Join(" ", pageRefs)}] /Count {declaredCount} >>");
            for (var i = 0; i < pageCount; i++)
                objects.Add($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] /Resources << >>{pageEntries} >>");
            nextObject = objects.Count + 1;
        }

        var builder = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int> { 0 };
        for (var i = 0; i < objects.Count; i++)
        {
            offsets.Add(Encoding.ASCII.GetByteCount(builder.ToString()));
            builder.Append($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }
        var xrefOffset = Encoding.ASCII.GetByteCount(builder.ToString());
        builder.Append($"xref\n0 {nextObject}\n0000000000 65535 f \n");
        for (var i = 1; i < nextObject; i++)
            builder.Append($"{offsets[i]:D10} 00000 n \n");
        builder.Append($"trailer\n<< /Size {nextObject} /Root 1 0 R >>\nstartxref\n{xrefOffset}\n%%EOF\n");
        return Encoding.ASCII.GetBytes(builder.ToString());
    }

    private static byte[] CreateIncrementalAnnotationPdf(string annotationDictionary, string? actionObject = null)
    {
        var original = CreateStructuralPdf();
        var text = Encoding.ASCII.GetString(original);
        var marker = "startxref\n";
        var markerIndex = text.LastIndexOf(marker, StringComparison.Ordinal);
        var previousXref = int.Parse(text[(markerIndex + marker.Length)..].Trim().Split('\n')[0],
            System.Globalization.CultureInfo.InvariantCulture);
        using var pdf = new MemoryStream();
        pdf.Write(original);
        WriteAscii(pdf, "\n");
        var pageOffset = pdf.Position;
        WriteAscii(pdf, "3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] /Resources << >> /Annots [4 0 R] >>\nendobj\n");
        var annotationOffset = pdf.Position;
        WriteAscii(pdf, $"4 0 obj\n{annotationDictionary}\nendobj\n");
        var actionOffset = pdf.Position;
        if (actionObject is not null)
            WriteAscii(pdf, $"5 0 obj\n{actionObject}\nendobj\n");
        var xrefOffset = pdf.Position;
        WriteAscii(pdf, $"xref\n3 {(actionObject is null ? 2 : 3)}\n{pageOffset:D10} 00000 n \n{annotationOffset:D10} 00000 n \n");
        if (actionObject is not null)
            WriteAscii(pdf, $"{actionOffset:D10} 00000 n \n");
        WriteAscii(pdf, $"trailer\n<< /Size {(actionObject is null ? 5 : 6)} /Root 1 0 R /Prev {previousXref} >>\nstartxref\n{xrefOffset}\n%%EOF\n");
        return pdf.ToArray();
    }

    private static byte[] CreateStructuralPdfWithHugeKids(int childCount)
    {
        var kids = string.Join(" ", Enumerable.Repeat("3 0 R", childCount));
        var builder = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int> { 0 };
        void AddObject(int id, string body)
        {
            offsets.Add(Encoding.ASCII.GetByteCount(builder.ToString()));
            builder.Append($"{id} 0 obj\n{body}\nendobj\n");
        }

        AddObject(1, "<< /Type /Catalog /Pages 2 0 R >>");
        AddObject(2, $"<< /Type /Pages /Kids [{kids}] /Count 1 >>");
        AddObject(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] /Resources << >> >>");
        var xrefOffset = Encoding.ASCII.GetByteCount(builder.ToString());
        builder.Append("xref\n0 4\n0000000000 65535 f \n");
        for (var id = 1; id <= 3; id++)
            builder.Append($"{offsets[id]:D10} 00000 n \n");
        builder.Append($"trailer\n<< /Size 4 /Root 1 0 R >>\nstartxref\n{xrefOffset}\n%%EOF\n");
        return Encoding.ASCII.GetBytes(builder.ToString());
    }

    private static byte[] CreateIncrementalPdf(int oldPageCount, int activePageCount, bool unrelatedPageToken = false, bool encrypted = false)
    {
        var original = CreateStructuralPdf(oldPageCount);
        var text = Encoding.ASCII.GetString(original);
        var marker = "startxref\n";
        var markerIndex = text.LastIndexOf(marker, StringComparison.Ordinal);
        if (markerIndex < 0)
            throw new InvalidOperationException("Base PDF has no startxref section.");
        var previousXref = int.Parse(text[(markerIndex + marker.Length)..].Trim().Split('\n')[0],
            System.Globalization.CultureInfo.InvariantCulture);

        using var pdf = new MemoryStream();
        pdf.Write(original);
        WriteAscii(pdf, "\n");
        var offsets = new SortedDictionary<int, long>();
        offsets[1] = pdf.Position;
        WriteAscii(pdf, "1 0 obj\n<< /Type /Catalog /Pages 4 0 R >>\nendobj\n");
        var lastPageId = 4 + activePageCount;
        offsets[4] = pdf.Position;
        var children = string.Join(" ", Enumerable.Range(0, activePageCount).Select(index => $"{5 + index} 0 R"));
        WriteAscii(pdf, $"4 0 obj\n<< /Type /Pages /Kids [{children}] /Count {activePageCount} >>\nendobj\n");
        for (var index = 0; index < activePageCount; index++)
        {
            var objectId = 5 + index;
            offsets[objectId] = pdf.Position;
            WriteAscii(pdf, $"{objectId} 0 obj\n<< /Type /Page /Parent 4 0 R /MediaBox [0 0 100 100] /Resources << >> >>\nendobj\n");
        }

        if (unrelatedPageToken)
        {
            lastPageId++;
            offsets[lastPageId] = pdf.Position;
            const string streamContent = "/Type /Page";
            WriteAscii(pdf, $"{lastPageId} 0 obj\n<< /Length {streamContent.Length} >>\nstream\n{streamContent}\nendstream\nendobj\n");
        }
        var encryptObjectId = 0;
        if (encrypted)
        {
            encryptObjectId = ++lastPageId;
            offsets[encryptObjectId] = pdf.Position;
            WriteAscii(pdf, $"{encryptObjectId} 0 obj\n<< /Filter /Standard /V 2 /R 3 /Length 128 /O <{new string('0', 64)}> /U <{new string('0', 64)}> /P -4 >>\nendobj\n");
        }

        var xrefOffset = pdf.Position;
        WriteAscii(pdf, "xref\n1 1\n");
        WriteAscii(pdf, $"{offsets[1]:D10} 00000 n \n");
        WriteAscii(pdf, $"4 {lastPageId - 3}\n");
        for (var objectId = 4; objectId <= lastPageId; objectId++)
            WriteAscii(pdf, offsets.TryGetValue(objectId, out var offset) ? $"{offset:D10} 00000 n \n" : "0000000000 00000 f \n");
        var encryptEntry = encrypted ? $" /Encrypt {encryptObjectId} 0 R" : string.Empty;
        WriteAscii(pdf, $"trailer\n<< /Size {lastPageId + 1} /Root 1 0 R /Prev {previousXref}{encryptEntry} >>\nstartxref\n{xrefOffset}\n%%EOF\n");
        return pdf.ToArray();
    }

    private static byte[] CreateXrefStreamBomb()
    {
        const int xrefEntries = 5_000_000;
        var prefix = new StringBuilder("%PDF-1.5\n");
        var offsets = new int[5];
        var baseObjects = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] /Resources << >> >>"
        };
        for (var i = 0; i < baseObjects.Length; i++)
        {
            offsets[i + 1] = Encoding.ASCII.GetByteCount(prefix.ToString());
            prefix.Append($"{i + 1} 0 obj\n{baseObjects[i]}\nendobj\n");
        }
        var compressed = Array.Empty<byte>();
        var xrefOffset = 0;
        for (var attempt = 0; attempt < 4; attempt++)
        {
            offsets[4] = Encoding.ASCII.GetByteCount(prefix.ToString());
            var header = $"4 0 obj\n<< /Type /XRef /Size {xrefEntries} /Root 1 0 R /W [1 4 2] /Length {compressed.Length} /Filter /FlateDecode >>\nstream\n";
            xrefOffset = offsets[4];
            compressed = CompressXrefRows(offsets, xrefEntries);
            if (compressed.Length == ParseDeclaredLength(header))
                break;
        }
        offsets[4] = Encoding.ASCII.GetByteCount(prefix.ToString());
        compressed = CompressXrefRows(offsets, xrefEntries);
        var finalPrefix = prefix.ToString();
        var dictionaryAndStream = $"4 0 obj\n<< /Type /XRef /Size {xrefEntries} /Root 1 0 R /W [1 4 2] /Length {compressed.Length} /Filter /FlateDecode >>\nstream\n";
        xrefOffset = offsets[4];
        using var pdf = new MemoryStream();
        var prefixBytes = Encoding.ASCII.GetBytes(finalPrefix + dictionaryAndStream);
        pdf.Write(prefixBytes);
        pdf.Write(compressed);
        var suffix = Encoding.ASCII.GetBytes($"\nendstream\nendobj\nstartxref\n{xrefOffset}\n%%EOF\n");
        pdf.Write(suffix);
        return pdf.ToArray();
    }

    private static byte[] CreatePredictorXrefPdf(int predictor = 12, int colors = 1, int bitsPerComponent = 8,
        int columns = 6, byte? filterType = null, bool encrypted = false, bool useLzw = false)
    {
        var prefix = new StringBuilder("%PDF-1.5\n");
        var offsets = new List<int> { 0 };
        void AddObject(string body)
        {
            var objectId = offsets.Count;
            offsets.Add(Encoding.ASCII.GetByteCount(prefix.ToString()));
            prefix.Append($"{objectId} 0 obj\n{body}\nendobj\n");
        }
        AddObject("<< /Type /Catalog /Pages 2 0 R >>");
        AddObject("<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        AddObject("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] /Resources << >> >>");
        if (encrypted)
            AddObject($"<< /Filter /Standard /V 2 /R 3 /Length 128 /O <{new string('0', 64)}> /U <{new string('0', 64)}> /P -4 >>");

        var xrefObjectId = encrypted ? 5 : 4;
        var xrefOffset = Encoding.ASCII.GetByteCount(prefix.ToString());
        var rows = new byte[(xrefObjectId + 1) * 6];
        SetPredictorXrefEntry(rows, 0, 0, 0, 255);
        for (var id = 1; id <= 3; id++)
            SetPredictorXrefEntry(rows, id, 1, (uint)offsets[id], 0);
        if (encrypted)
            SetPredictorXrefEntry(rows, 4, 1, (uint)offsets[4], 0);
        SetPredictorXrefEntry(rows, xrefObjectId, 1, (uint)xrefOffset, 0);

        var predicted = predictor == 2
            ? EncodeTiffPredictorRows(rows, rowBytes: 6)
            : EncodePngPredictorRows(rows, rowBytes: 6, filterType);
        var compressed = useLzw ? EncodeLzw(predicted) : Compress(predicted);
        var encryptionEntry = encrypted ? " /Encrypt 4 0 R" : string.Empty;
        var filter = useLzw ? "LZWDecode" : "FlateDecode";
        var earlyChange = useLzw ? " /EarlyChange 1" : string.Empty;
        var dictionary = $"<< /Type /XRef /Size {xrefObjectId + 1} /Root 1 0 R{encryptionEntry} /W [1 4 1] /Index [0 {xrefObjectId + 1}] /Length {compressed.Length} /Filter /{filter} /DecodeParms << /Predictor {predictor} /Colors {colors} /BitsPerComponent {bitsPerComponent} /Columns {columns}{earlyChange} >> >>";
        using var pdf = new MemoryStream();
        WriteAscii(pdf, prefix.ToString());
        WriteAscii(pdf, $"{xrefObjectId} 0 obj\n{dictionary}\nstream\n");
        pdf.Write(compressed);
        WriteAscii(pdf, $"\nendstream\nendobj\nstartxref\n{xrefOffset}\n%%EOF\n");
        return pdf.ToArray();
    }

    private static byte[] CreatePredictorObjectStreamPdf()
    {
        var catalog = "<< /Type /Catalog /Pages 2 0 R >>";
        var pages = "<< /Type /Pages /Kids [3 0 R] /Count 1 >>";
        var page = "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] /Resources << >> >>";
        var offset2 = Encoding.ASCII.GetByteCount(catalog) + 1;
        var offset3 = offset2 + Encoding.ASCII.GetByteCount(pages) + 1;
        var objectHeader = $"1 0 2 {offset2} 3 {offset3}\n";
        var decodedObjects = Encoding.ASCII.GetBytes(objectHeader + catalog + "\n" + pages + "\n" + page);
        var compressedObjects = Compress(EncodePngPredictorRows(decodedObjects, rowBytes: 1, filterType: 0));
        using var pdf = new MemoryStream();
        WriteAscii(pdf, "%PDF-1.5\n");
        var objectStreamOffset = (int)pdf.Position;
        WriteAscii(pdf, $"4 0 obj\n<< /Type /ObjStm /N 3 /First {objectHeader.Length} /Length {compressedObjects.Length} /Filter /FlateDecode /DecodeParms << /Predictor 12 /Colors 1 /BitsPerComponent 8 /Columns 1 >> >>\nstream\n");
        pdf.Write(compressedObjects);
        WriteAscii(pdf, "\nendstream\nendobj\n");
        var xrefOffset = (int)pdf.Position;
        var rows = new byte[6 * 7];
        WriteXrefEntry(rows.AsSpan(0, 7), 0, 0, ushort.MaxValue);
        WriteXrefEntry(rows.AsSpan(7, 7), 2, 4, 0);
        WriteXrefEntry(rows.AsSpan(14, 7), 2, 4, 1);
        WriteXrefEntry(rows.AsSpan(21, 7), 2, 4, 2);
        WriteXrefEntry(rows.AsSpan(28, 7), 1, (uint)objectStreamOffset, 0);
        WriteXrefEntry(rows.AsSpan(35, 7), 1, (uint)xrefOffset, 0);
        var compressedXref = Compress(rows);
        WriteAscii(pdf, $"5 0 obj\n<< /Type /XRef /Size 6 /Root 1 0 R /W [1 4 2] /Index [0 6] /Length {compressedXref.Length} /Filter /FlateDecode >>\nstream\n");
        pdf.Write(compressedXref);
        WriteAscii(pdf, $"\nendstream\nendobj\nstartxref\n{xrefOffset}\n%%EOF\n");
        return pdf.ToArray();
    }

    private static byte[] CreatePredictorXrefBomb(int entryCount)
    {
        var prefix = new StringBuilder("%PDF-1.5\n");
        var offsets = new List<int> { 0 };
        void AddObject(string body)
        {
            var objectId = offsets.Count;
            offsets.Add(Encoding.ASCII.GetByteCount(prefix.ToString()));
            prefix.Append($"{objectId} 0 obj\n{body}\nendobj\n");
        }
        AddObject("<< /Type /Catalog /Pages 2 0 R >>");
        AddObject("<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        AddObject("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] /Resources << >> >>");
        var xrefOffset = Encoding.ASCII.GetByteCount(prefix.ToString());
        var rows = new byte[entryCount * 6];
        SetPredictorXrefEntry(rows, 0, 0, 0, 255);
        for (var id = 1; id <= 3; id++)
            SetPredictorXrefEntry(rows, id, 1, (uint)offsets[id], 0);
        SetPredictorXrefEntry(rows, 4, 1, (uint)xrefOffset, 0);
        var predicted = EncodePngPredictorRows(rows, rowBytes: 6, filterType: 2);
        var compressed = Compress(predicted);
        using var pdf = new MemoryStream();
        WriteAscii(pdf, prefix.ToString());
        WriteAscii(pdf, $"4 0 obj\n<< /Type /XRef /Size {entryCount} /Root 1 0 R /W [1 4 1] /Index [0 {entryCount}] /Length {compressed.Length} /Filter /FlateDecode /DecodeParms << /Predictor 12 /Colors 1 /BitsPerComponent 8 /Columns 6 >> >>\nstream\n");
        pdf.Write(compressed);
        WriteAscii(pdf, $"\nendstream\nendobj\nstartxref\n{xrefOffset}\n%%EOF\n");
        return pdf.ToArray();
    }

    private static void SetPredictorXrefEntry(byte[] rows, int id, byte type, uint offset, byte generation)
    {
        var row = rows.AsSpan(id * 6, 6);
        row[0] = type;
        BinaryPrimitives.WriteUInt32BigEndian(row.Slice(1, 4), offset);
        row[5] = generation;
    }

    private static byte[] EncodePngPredictorRows(byte[] raw, int rowBytes, byte? filterType)
    {
        Assert.Equal(0, raw.Length % rowBytes);
        var rowCount = raw.Length / rowBytes;
        var encoded = new byte[rowCount * (rowBytes + 1)];
        for (var rowIndex = 0; rowIndex < rowCount; rowIndex++)
        {
            var filter = filterType ?? (byte)(rowIndex % 5);
            var sourceOffset = rowIndex * rowBytes;
            var outputOffset = rowIndex * (rowBytes + 1);
            encoded[outputOffset] = filter;
            for (var index = 0; index < rowBytes; index++)
            {
                var value = raw[sourceOffset + index];
                var left = index == 0 ? 0 : raw[sourceOffset + index - 1];
                var above = rowIndex == 0 ? 0 : raw[sourceOffset - rowBytes + index];
                var upperLeft = rowIndex == 0 || index == 0 ? 0 : raw[sourceOffset - rowBytes + index - 1];
                var prediction = filter switch
                {
                    0 => 0,
                    1 => left,
                    2 => above,
                    3 => (left + above) / 2,
                    4 => ReferencePaeth(left, above, upperLeft),
                    _ => throw new ArgumentOutOfRangeException(nameof(filterType))
                };
                encoded[outputOffset + index + 1] = unchecked((byte)(value - prediction));
            }
        }
        return encoded;
    }

    private static byte[] EncodeTiffPredictorRows(byte[] raw, int rowBytes)
    {
        var encoded = raw.ToArray();
        for (var rowOffset = 0; rowOffset < encoded.Length; rowOffset += rowBytes)
        for (var index = rowBytes - 1; index > 0; index--)
            encoded[rowOffset + index] = unchecked((byte)(raw[rowOffset + index] - raw[rowOffset + index - 1]));
        return encoded;
    }

    private static int ReferencePaeth(int left, int above, int upperLeft)
    {
        var p = left + above - upperLeft;
        var pa = Math.Abs(p - left);
        var pb = Math.Abs(p - above);
        var pc = Math.Abs(p - upperLeft);
        return pa <= pb && pa <= pc ? left : pb <= pc ? above : upperLeft;
    }

    private static byte[] CreateObjectStreamBomb()
    {
        const int fillerLength = 36 * 1024 * 1024;
        var catalog = "<< /Type /Catalog /Pages 2 0 R >>";
        var pages = "<< /Type /Pages /Kids [3 0 R] /Count 1 >>";
        var page = "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] /Resources << >> >>";
        var offset2 = Encoding.ASCII.GetByteCount(catalog) + 1;
        var offset3 = offset2 + Encoding.ASCII.GetByteCount(pages) + 1;
        var objectHeader = $"1 0 2 {offset2} 3 {offset3}\n";
        byte[] compressedObjectBody;
        using (var objectBody = new MemoryStream())
        {
            var filler = new byte[64 * 1024];
            Array.Fill(filler, (byte)'A');
            using (var compressor = new ZLibStream(objectBody, CompressionLevel.Fastest, leaveOpen: true))
            {
                // Compress into a separate stream because the object stream header is part of the decoded payload.
                using var decoded = new MemoryStream();
                WriteAscii(decoded, objectHeader + catalog + "\n" + pages + "\n" + page + "\n(");
                for (var remaining = fillerLength; remaining > 0; remaining -= Math.Min(remaining, filler.Length))
                    decoded.Write(filler, 0, Math.Min(remaining, filler.Length));
                WriteAscii(decoded, ")");
                decoded.Position = 0;
                decoded.CopyTo(compressor);
            }
            compressedObjectBody = objectBody.ToArray();
        }

        using var pdf = new MemoryStream();
        WriteAscii(pdf, "%PDF-1.5\n");
        var objectStreamOffset = (int)pdf.Position;
        WriteAscii(pdf, $"4 0 obj\n<< /Type /ObjStm /N 3 /First {Encoding.ASCII.GetByteCount(objectHeader)} /Length {compressedObjectBody.Length} /Filter /FlateDecode >>\nstream\n");
        pdf.Write(compressedObjectBody);
        WriteAscii(pdf, "\nendstream\nendobj\n");
        var xrefOffset = (int)pdf.Position;
        var rows = new byte[6 * 7];
        WriteXrefEntry(rows.AsSpan(0, 7), 0, 0, ushort.MaxValue);
        WriteXrefEntry(rows.AsSpan(7, 7), 2, 4, 0);
        WriteXrefEntry(rows.AsSpan(14, 7), 2, 4, 1);
        WriteXrefEntry(rows.AsSpan(21, 7), 2, 4, 2);
        WriteXrefEntry(rows.AsSpan(28, 7), 1, (uint)objectStreamOffset, 0);
        WriteXrefEntry(rows.AsSpan(35, 7), 1, (uint)xrefOffset, 0);
        var compressedRows = Compress(rows);
        WriteAscii(pdf, $"5 0 obj\n<< /Type /XRef /Size 6 /Root 1 0 R /W [1 4 2] /Index [0 6] /Length {compressedRows.Length} /Filter /FlateDecode >>\nstream\n");
        pdf.Write(compressedRows);
        WriteAscii(pdf, $"\nendstream\nendobj\nstartxref\n{xrefOffset}\n%%EOF\n");
        return pdf.ToArray();
    }

    private static byte[] CreateManyTinyObjectsPdf(int unusedObjectCount)
    {
        var objectCount = unusedObjectCount + 3;
        var builder = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>(objectCount + 1) { 0 };
        var currentOffset = Encoding.ASCII.GetByteCount(builder.ToString());
        void AddObject(int id, string body)
        {
            offsets.Add(currentOffset);
            var text = $"{id} 0 obj\n{body}\nendobj\n";
            builder.Append(text);
            currentOffset += Encoding.ASCII.GetByteCount(text);
        }

        AddObject(1, "<< /Type /Catalog /Pages 2 0 R >>");
        AddObject(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        AddObject(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] /Resources << >> >>");
        for (var id = 4; id <= objectCount; id++)
            AddObject(id, "null");

        var xrefOffset = currentOffset;
        builder.Append($"xref\n0 {objectCount + 1}\n0000000000 65535 f \n");
        for (var id = 1; id <= objectCount; id++)
            builder.Append($"{offsets[id]:D10} 00000 n \n");
        builder.Append($"trailer\n<< /Size {objectCount + 1} /Root 1 0 R >>\nstartxref\n{xrefOffset}\n%%EOF\n");
        return Encoding.ASCII.GetBytes(builder.ToString());
    }

    private static byte[] CreateFullSizeObjectAndXrefStreamPdf()
    {
        const int fillerCharacters = 6_500_000;
        const int targetLength = DocumentCapturePdfLimits.MaxInputBytes;
        var fillerBytes = new byte[fillerCharacters];
        RandomNumberGenerator.Fill(fillerBytes);
        const string safeCharacters = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789!#$%&'*+,-./:;=?@^_`{|}~";
        for (var i = 0; i < fillerBytes.Length; i++)
            fillerBytes[i] = (byte)safeCharacters[fillerBytes[i] % safeCharacters.Length];
        var filler = Encoding.ASCII.GetString(fillerBytes);

        var catalog = "<< /Type /Catalog /Pages 2 0 R >>";
        var pages = "<< /Type /Pages /Kids [3 0 R] /Count 1 >>";
        var page = "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] /Resources << >> >>";
        var fillerObject = $"({filler})";
        var offset2 = Encoding.ASCII.GetByteCount(catalog) + 1;
        var offset3 = offset2 + Encoding.ASCII.GetByteCount(pages) + 1;
        var offset6 = offset3 + Encoding.ASCII.GetByteCount(page) + 1;
        var objectHeader = $"1 0 2 {offset2} 3 {offset3} 6 {offset6}\n";
        var objectBody = Encoding.ASCII.GetBytes(objectHeader + catalog + "\n" + pages + "\n" + page + "\n" + fillerObject);
        var compressedObjectBody = Compress(objectBody);

        using var pdf = new MemoryStream(targetLength);
        WriteAscii(pdf, "%PDF-1.5\n");
        var objectStreamOffset = (int)pdf.Position;
        WriteAscii(pdf, $"4 0 obj\n<< /Type /ObjStm /N 4 /First {Encoding.ASCII.GetByteCount(objectHeader)} /Length {compressedObjectBody.Length} /Filter /FlateDecode >>\nstream\n");
        pdf.Write(compressedObjectBody);
        WriteAscii(pdf, "\nendstream\nendobj\n");

        var xrefOffset = (int)pdf.Position;
        var xrefRows = new byte[7 * 7];
        WriteXrefEntry(xrefRows.AsSpan(0, 7), 0, 0, ushort.MaxValue);
        WriteXrefEntry(xrefRows.AsSpan(7, 7), 2, 4, 0);
        WriteXrefEntry(xrefRows.AsSpan(14, 7), 2, 4, 1);
        WriteXrefEntry(xrefRows.AsSpan(21, 7), 2, 4, 2);
        WriteXrefEntry(xrefRows.AsSpan(28, 7), 1, (uint)objectStreamOffset, 0);
        WriteXrefEntry(xrefRows.AsSpan(35, 7), 1, (uint)xrefOffset, 0);
        WriteXrefEntry(xrefRows.AsSpan(42, 7), 2, 4, 3);
        var compressedXrefRows = Compress(xrefRows);
        WriteAscii(pdf, $"5 0 obj\n<< /Type /XRef /Size 7 /Root 1 0 R /W [1 4 2] /Index [0 7] /Length {compressedXrefRows.Length} /Filter /FlateDecode >>\nstream\n");
        pdf.Write(compressedXrefRows);
        WriteAscii(pdf, "\nendstream\nendobj\n");

        var suffix = Encoding.ASCII.GetBytes($"\nstartxref\n{xrefOffset}\n%%EOF\n");
        var paddingHeader = Encoding.ASCII.GetBytes("\n%");
        var padCount = targetLength - (int)pdf.Position - paddingHeader.Length - suffix.Length;
        if (padCount < 0)
            throw new InvalidOperationException("The full-size PDF fixture exceeded its target size.");
        pdf.Write(paddingHeader);
        pdf.Write(Enumerable.Repeat((byte)'A', padCount).ToArray());
        pdf.Write(suffix);
        return pdf.ToArray();
    }

    private static void WriteXrefEntry(Span<byte> row, byte type, uint field2, ushort field3)
    {
        row[0] = type;
        BinaryPrimitives.WriteUInt32BigEndian(row.Slice(1, 4), field2);
        BinaryPrimitives.WriteUInt16BigEndian(row.Slice(5, 2), field3);
    }

    private static byte[] Compress(ReadOnlySpan<byte> bytes)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.Fastest, leaveOpen: true))
            zlib.Write(bytes);
        return output.ToArray();
    }

    private static byte[] EncodeLzw(ReadOnlySpan<byte> bytes)
    {
        using var output = new MemoryStream();
        var bitCount = 0;
        byte current = 0;
        void WriteCode(int code)
        {
            for (var bit = 8; bit >= 0; bit--)
            {
                current = (byte)((current << 1) | ((code >> bit) & 1));
                if (++bitCount != 8)
                    continue;
                output.WriteByte(current);
                current = 0;
                bitCount = 0;
            }
        }

        WriteCode(256);
        foreach (var value in bytes)
            WriteCode(value);
        WriteCode(257);
        if (bitCount > 0)
            output.WriteByte((byte)(current << (8 - bitCount)));
        return output.ToArray();
    }

    private static void WriteAscii(Stream stream, string text) => stream.Write(Encoding.ASCII.GetBytes(text));

    private static int ParseDeclaredLength(string header)
    {
        var marker = "/Length ";
        var start = header.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
        var end = header.IndexOf(' ', start);
        return int.Parse(header.AsSpan(start, end - start));
    }

    private static byte[] CompressXrefRows(int[] offsets, int entryCount)
    {
        using var compressed = new MemoryStream();
        using (var compressor = new ZLibStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
        {
            Span<byte> row = stackalloc byte[7];
            row[0] = 0;
            row[5] = 0xFF;
            row[6] = 0xFF;
            compressor.Write(row);
            for (var id = 1; id <= 4; id++)
            {
                row.Clear();
                row[0] = 1;
                row[1] = (byte)(offsets[id] >> 24);
                row[2] = (byte)(offsets[id] >> 16);
                row[3] = (byte)(offsets[id] >> 8);
                row[4] = (byte)offsets[id];
                compressor.Write(row);
            }
            var zeros = new byte[64 * 1024];
            var remaining = ((long)entryCount - 5) * 7;
            while (remaining > 0)
            {
                var count = (int)Math.Min(remaining, zeros.Length);
                compressor.Write(zeros.AsSpan(0, count));
                remaining -= count;
            }
        }
        return compressed.ToArray();
    }
}
