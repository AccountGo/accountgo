using AccountGo.BackendTests.DocumentCapture;
using Api.DocumentCaptures;
using Core.Entitlements;
using SkiaSharp;
using UglyToad.PdfPig.Writer;
using Xunit;

namespace AccountGo.BackendTests.Documents;

[Collection(DocumentCaptureResourceCollection.Name)]
public sealed class DocumentsLimitsTests
{
    private const int MiB = 1024 * 1024;

    [Fact]
    public void Policy_DefaultEngineGrantsNoStorage_CommunityGrantsLibraryMaximum()
    {
        Assert.Equal(0, DocumentStoragePolicy.NotGranted.StorageLimitBytes);
        Assert.Equal(8 * MiB, DocumentStoragePolicy.NotGranted.LibraryMaxFileBytes);
        Assert.Equal(10, DocumentStoragePolicy.NotGranted.LibraryMaxPdfPages);
        Assert.Null(DocumentStoragePolicy.Community.StorageLimitBytes);
        Assert.Equal(15 * MiB, DocumentStoragePolicy.Community.LibraryMaxFileBytes);
        Assert.Equal(300, DocumentStoragePolicy.Community.LibraryMaxPdfPages);
        Assert.Equal(8 * MiB, DocumentStoragePolicy.Community.RecordMaxFileBytes);
        Assert.Equal(10, DocumentStoragePolicy.Community.RecordMaxPdfPages);
    }

    [Fact]
    public void Policy_ClampNeverExceedsPlatformMaximum()
    {
        var clamped = new DocumentStoragePolicy(-5, 100 * MiB, 5_000, 100 * MiB, 500).Clamp();
        Assert.Equal(0, clamped.StorageLimitBytes);
        Assert.Equal(15 * MiB, clamped.LibraryMaxFileBytes);
        Assert.Equal(300, clamped.LibraryMaxPdfPages);
        Assert.Equal(8 * MiB, clamped.RecordMaxFileBytes);
        Assert.Equal(10, clamped.RecordMaxPdfPages);
    }

    [Theory]
    [InlineData(null, DocumentUploadProfile.Library)]
    [InlineData("customer", DocumentUploadProfile.Library)]
    [InlineData("vendor", DocumentUploadProfile.Library)]
    [InlineData("bill", DocumentUploadProfile.Record)]
    [InlineData("invoice", DocumentUploadProfile.Record)]
    public void Profile_IsDecidedByTheLinkTarget(string? targetType, DocumentUploadProfile expected) =>
        Assert.Equal(expected, DocumentUploadLimits.ProfileFor(targetType));

    [Fact]
    public void Resolve_UsesThePolicyForEachProfile()
    {
        Assert.Equal(new DocumentUploadLimit(15 * MiB, 300),
            DocumentUploadLimits.Resolve(DocumentUploadProfile.Library, DocumentStoragePolicy.Community));
        Assert.Equal(new DocumentUploadLimit(8 * MiB, 10),
            DocumentUploadLimits.Resolve(DocumentUploadProfile.Record, DocumentStoragePolicy.Community));
        Assert.Equal(new DocumentUploadLimit(8 * MiB, 10),
            DocumentUploadLimits.Resolve(DocumentUploadProfile.Library, DocumentStoragePolicy.NotGranted));
    }

    [Fact]
    public async Task Pdf_LibraryProfileAcceptsThreeHundredPagesAndReportsPageLimitAboveIt()
    {
        var library = DocumentCapturePdfProfile.ForStoredDocument(15 * MiB, 300);
        var ok = await ValidateAsync(CreatePdf(300), library);
        Assert.True(ok.IsValid, ok.ErrorCode);
        Assert.Equal(300, ok.PageCount);

        var over = await ValidateAsync(CreatePdf(301), library);
        Assert.False(over.IsValid);
        Assert.Equal("DOCUMENT_TOO_MANY_PAGES", over.ErrorCode);
    }

    [Fact]
    public async Task Pdf_RecordProfileReportsPageLimitButScanProfileKeepsItsCode()
    {
        var record = DocumentCapturePdfProfile.ForStoredDocument(8 * MiB, 10);
        var recordResult = await ValidateAsync(CreatePdf(11), record);
        Assert.Equal("DOCUMENT_TOO_MANY_PAGES", recordResult.ErrorCode);

        var scanResult = await ValidateAsync(CreatePdf(11), DocumentCapturePdfProfile.Scan);
        Assert.Equal("DOCUMENT_UNSUPPORTED_TYPE", scanResult.ErrorCode);
    }

    [Fact]
    public async Task Pdf_LibraryProfileAcceptsLargeFileThatScanProfileRejects()
    {
        var pdf = CreatePdfWithLargeImage(targetBytes: 10 * MiB);
        Assert.InRange(pdf.Length, 9 * MiB, 15 * MiB);

        var library = await ValidateAsync(pdf, DocumentCapturePdfProfile.ForStoredDocument(15 * MiB, 300));
        Assert.True(library.IsValid, library.ErrorCode);

        var scan = await ValidateAsync(pdf, DocumentCapturePdfProfile.Scan);
        Assert.Equal("DOCUMENT_TOO_LARGE", scan.ErrorCode);
    }

    [Fact]
    public async Task Pdf_InputOneByteOverTheLibraryMaximumIsRefusedBeforeParsing()
    {
        var result = await ValidateAsync(new byte[15 * MiB + 1], DocumentCapturePdfProfile.ForStoredDocument(15 * MiB, 300));
        Assert.Equal("DOCUMENT_TOO_LARGE", result.ErrorCode);
        Assert.Throws<ArgumentOutOfRangeException>(() => DocumentCapturePdfProfile.ForStoredDocument(15 * MiB + 1, 300));
        Assert.Throws<ArgumentOutOfRangeException>(() => DocumentCapturePdfProfile.ForStoredDocument(15 * MiB, 301));
    }

    [Fact]
    public async Task Image_LibraryLimitAcceptsNineToFifteenMiBAndScanLimitRejects()
    {
        var image = CreateNoisePng(2_400, 1_500);
        Assert.InRange(image.Length, 9 * MiB, 15 * MiB);

        var stored = await DocumentCaptureImageNormalizer.NormalizeAsync(image, "image/png",
            DocumentCaptureImageLimits.DecodeGateSlots, 15 * MiB);
        Assert.True(stored.Bytes.Length <= DocumentCaptureImageLimits.MaxStoredBytes);

        var scan = await Assert.ThrowsAsync<DocumentCaptureMediaException>(() =>
            DocumentCaptureImageNormalizer.NormalizeAsync(image, "image/png"));
        Assert.Equal("DOCUMENT_TOO_LARGE", scan.ErrorCode);

        var over = await Assert.ThrowsAsync<DocumentCaptureMediaException>(() =>
            DocumentCaptureImageNormalizer.NormalizeAsync(new byte[15 * MiB + 1], "image/png",
                DocumentCaptureImageLimits.DecodeGateSlots, 15 * MiB));
        Assert.Equal("DOCUMENT_TOO_LARGE", over.ErrorCode);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            DocumentCaptureImageNormalizer.NormalizeAsync(image, "image/png", DocumentCaptureImageLimits.DecodeGateSlots, 15 * MiB + 1));
    }

    private static Task<DocumentCapturePdfValidationResult> ValidateAsync(byte[] pdf, DocumentCapturePdfProfile profile) =>
        DocumentCapturePdfValidator.ValidateAsync(pdf, DocumentCapturePdfLimits.ParseGateSlots, profile with { ParseDeadline = TimeSpan.FromMinutes(1) },
            TimeProvider.System);

    private static byte[] CreatePdf(int pageCount)
    {
        var builder = new PdfDocumentBuilder();
        for (var page = 0; page < pageCount; page++)
            builder.AddPage(100, 100);
        return builder.Build();
    }

    private static byte[] CreatePdfWithLargeImage(int targetBytes)
    {
        var width = 1_200;
        byte[] jpeg;
        do
        {
            jpeg = EncodeNoise(width, width, SKEncodedImageFormat.Jpeg, 100);
            width += 300;
        }
        while (jpeg.Length < targetBytes - 64 * 1024);
        var builder = new PdfDocumentBuilder();
        var page = builder.AddPage(600, 600);
        page.AddJpeg(jpeg, new UglyToad.PdfPig.Core.PdfRectangle(0, 0, 600, 600));
        return builder.Build();
    }

    private static byte[] CreateNoisePng(int width, int height) => EncodeNoise(width, height, SKEncodedImageFormat.Png, 100);

    private static byte[] EncodeNoise(int width, int height, SKEncodedImageFormat format, int quality)
    {
        var random = new Random(42);
        using var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Opaque);
        var pixels = new byte[width * height * 4];
        random.NextBytes(pixels);
        for (var i = 3; i < pixels.Length; i += 4)
            pixels[i] = 255;
        System.Runtime.InteropServices.Marshal.Copy(pixels, 0, bitmap.GetPixels(), pixels.Length);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, quality);
        return data.ToArray();
    }
}
