using Api.DocumentCaptures;
using System.Diagnostics;
using System.Buffers.Binary;
using System.Text;
using SkiaSharp;
using Xunit;
using Xunit.Abstractions;

namespace AccountGo.BackendTests.DocumentCapture;

[Collection(DocumentCaptureResourceCollection.Name)]
public sealed class DocumentCaptureImageNormalizerTests
{
    private readonly ITestOutputHelper _output;

    public DocumentCaptureImageNormalizerTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task NormalizeAsync_ProducesBoundedPngAndHashesOriginalAndStoredBytes()
    {
        var original = CreateImage(2400, 1200, SKEncodedImageFormat.Jpeg);

        var result = await DocumentCaptureImageNormalizer.NormalizeAsync(original, "image/jpeg");

        Assert.Equal("image/png", result.ContentType);
        Assert.Equal(2048, result.Width);
        Assert.Equal(1024, result.Height);
        Assert.Equal(original.Length, result.SourceByteCount);
        Assert.Equal(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(original)).ToLowerInvariant(), result.SourceHash);
        Assert.Equal(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(result.Bytes)).ToLowerInvariant(), result.ContentHash);
        Assert.True(result.Bytes.Length <= DocumentCaptureImageLimits.MaxStoredBytes);
    }

    [Fact]
    public async Task NormalizeAsync_RejectsOversizedDimensionsBeforePixelDecode()
    {
        var original = CreateImage(8001, 1, SKEncodedImageFormat.Png);

        var exception = await Assert.ThrowsAsync<DocumentCaptureMediaException>(
            () => DocumentCaptureImageNormalizer.NormalizeAsync(original, "image/png"));

        Assert.Equal("DOCUMENT_DIMENSIONS", exception.ErrorCode);
    }

    [Fact]
    public async Task NormalizeAsync_RejectsUnsupportedAndMislabeledMedia()
    {
        var original = CreateImage(10, 10, SKEncodedImageFormat.Png);

        var exception = await Assert.ThrowsAsync<DocumentCaptureMediaException>(
            () => DocumentCaptureImageNormalizer.NormalizeAsync(original, "image/jpeg"));

        Assert.Equal("DOCUMENT_MEDIA_MISMATCH", exception.ErrorCode);
    }

    [Fact]
    public async Task NormalizeAsync_HonorsExifOrientationAndDropsExifGpsMetadata()
    {
        var original = CreateExifJpegWithOrientationAndGps();

        var result = await DocumentCaptureImageNormalizer.NormalizeAsync(original, "image/jpeg");

        Assert.Equal(40, result.Width);
        Assert.Equal(80, result.Height);
        Assert.Equal("image/png", result.ContentType);
        Assert.False(HasPngChunk(result.Bytes, "eXIf"), "Normalized output retained an EXIF chunk.");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public async Task NormalizeAsync_MapsAllExifOrientationsAtPixelLevel(byte orientation)
    {
        var encoded = CreateAsymmetricExifJpeg(orientation);
        using var sourceData = SKData.CreateCopy(encoded);
        using var sourceCodec = SKCodec.Create(sourceData)!;
        using var source = new SKBitmap(sourceCodec.Info.Width, sourceCodec.Info.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        Assert.Equal(SKCodecResult.Success, sourceCodec.GetPixels(source.Info, source.GetPixels()));

        var result = await DocumentCaptureImageNormalizer.NormalizeAsync(encoded, "image/jpeg");
        using var outputData = SKData.CreateCopy(result.Bytes);
        using var outputCodec = SKCodec.Create(outputData)!;
        using var output = new SKBitmap(outputCodec.Info.Width, outputCodec.Info.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        Assert.Equal(SKCodecResult.Success, outputCodec.GetPixels(output.Info, output.GetPixels()));

        var samplePoints = new[]
        {
            (X: 3, Y: 3), (X: source.Width - 4, Y: 3),
            (X: 3, Y: source.Height - 4), (X: source.Width - 4, Y: source.Height - 4)
        };
        foreach (var point in samplePoints)
        {
            var (x, y) = MapSourceToDestination(point.X, point.Y, source.Width, source.Height, orientation);
            AssertColorNear(source.GetPixel(point.X, point.Y), output.GetPixel(x, y), orientation, point);
        }
    }

    [Fact(Timeout = 120000)]
    public async Task NormalizeAsync_FallsBackToBoundedJpegQualitiesWhenPngIsTooLarge()
    {
        using var bitmap = new SKBitmap(1024, 1024, SKColorType.Rgba8888, SKAlphaType.Premul);
        var random = new Random(451);
        for (var y = 0; y < bitmap.Height; y++)
        for (var x = 0; x < bitmap.Width; x++)
            bitmap.SetPixel(x, y, new SKColor((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256)));
        using var image = SKImage.FromBitmap(bitmap);
        using var sourceData = image.Encode(SKEncodedImageFormat.Jpeg, 95);
        var original = sourceData!.ToArray();
        Assert.True(original.Length < DocumentCaptureImageLimits.MaxInputBytes);

        var result = await DocumentCaptureImageNormalizer.NormalizeAsync(original, "image/jpeg");

        Assert.Equal("image/jpeg", result.ContentType);
        Assert.InRange(result.Bytes.Length, 1, DocumentCaptureImageLimits.MaxStoredBytes);
    }

    [Fact]
    public async Task NormalizeAsync_FifthRequestAfterFourHeldSlotsReturnsDecodeBusy()
    {
        var gate = DocumentCaptureImageNormalizer.DecodeGates.GetOrAdd(
            DocumentCaptureImageLimits.DecodeGateSlots,
            slots => new SemaphoreSlim(slots, slots));
        for (var slot = 0; slot < DocumentCaptureImageLimits.DecodeGateSlots; slot++)
            await gate.WaitAsync();
        try
        {
            var exception = await Assert.ThrowsAsync<DocumentCaptureMediaException>(() =>
                DocumentCaptureImageNormalizer.NormalizeAsync(CreateImage(20, 20, SKEncodedImageFormat.Png), "image/png",
                    DocumentCaptureImageLimits.DecodeGateSlots));

            Assert.Equal("DOCUMENT_DECODE_BUSY", exception.ErrorCode);
        }
        finally
        {
            for (var slot = 0; slot < DocumentCaptureImageLimits.DecodeGateSlots; slot++)
                gate.Release();
        }
    }

    [Fact(Timeout = 120000)]
    [Trait("Category", "Resource")]
    public async Task NormalizeAsync_FourConcurrentTwentyFourMegapixelImagesStayWithinMeasuredMemoryCeiling()
    {
        var original = AddExifOrientation(CreateImage(6000, 4000, SKEncodedImageFormat.Jpeg), 6);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        using var process = Process.GetCurrentProcess();
        process.Refresh();
        var baseline = process.WorkingSet64;
        var peak = baseline;
        var jobs = Enumerable.Range(0, DocumentCaptureImageLimits.DecodeGateSlots)
            .Select(_ => Task.Run(() => DocumentCaptureImageNormalizer.NormalizeAsync(original, "image/jpeg")))
            .ToArray();
        var sampler = Task.Run(async () =>
        {
            while (jobs.Any(job => !job.IsCompleted))
            {
                process.Refresh();
                peak = Math.Max(peak, process.WorkingSet64);
                await Task.Delay(5);
            }
            process.Refresh();
            peak = Math.Max(peak, process.WorkingSet64);
        });

        var normalized = await Task.WhenAll(jobs);
        await sampler;
        var peakGrowth = peak - baseline;
        _output.WriteLine($"Four 24 MP orientation-6 JPEG normalizations: baseline={baseline:N0} bytes, peak={peak:N0} bytes, growth={peakGrowth:N0} bytes.");

        Assert.All(normalized, image => Assert.Equal(2048, Math.Max(image.Width, image.Height)));
        Assert.True(peakGrowth < 600L * 1024 * 1024, $"Peak working-set growth was {peakGrowth:N0} bytes.");
    }

    [Fact(Timeout = 120000)]
    [Trait("Category", "Resource")]
    public async Task NormalizeAsync_FourConcurrentTwentyFourMegapixelPngsStayWithinMeasuredMemoryCeiling()
    {
        var original = CreateImage(6000, 4000, SKEncodedImageFormat.Png);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        using var process = Process.GetCurrentProcess();
        process.Refresh();
        var baseline = process.WorkingSet64;
        var peak = baseline;
        var jobs = Enumerable.Range(0, DocumentCaptureImageLimits.DecodeGateSlots)
            .Select(_ => Task.Run(() => DocumentCaptureImageNormalizer.NormalizeAsync(original, "image/png")))
            .ToArray();
        var sampler = Task.Run(async () =>
        {
            while (jobs.Any(job => !job.IsCompleted))
            {
                process.Refresh();
                peak = Math.Max(peak, process.WorkingSet64);
                await Task.Delay(5);
            }
            process.Refresh();
            peak = Math.Max(peak, process.WorkingSet64);
        });

        var normalized = await Task.WhenAll(jobs);
        await sampler;
        var peakGrowth = peak - baseline;
        _output.WriteLine($"Four 24 MP PNG normalizations: baseline={baseline:N0} bytes, peak={peak:N0} bytes, growth={peakGrowth:N0} bytes.");

        Assert.All(normalized, image => Assert.Equal(2048, image.Width));
        Assert.True(peakGrowth < 800L * 1024 * 1024, $"Peak working-set growth was {peakGrowth:N0} bytes.");
    }

    private static byte[] CreateImage(int width, int height, SKEncodedImageFormat format)
    {
        using var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        bitmap.Erase(SKColors.White);
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(format, 90);
        return encoded.ToArray();
    }

    private static byte[] CreateExifJpegWithOrientationAndGps()
    {
        var jpeg = CreateImage(80, 40, SKEncodedImageFormat.Jpeg);
        var tiff = new byte[56];
        tiff[0] = (byte)'I'; tiff[1] = (byte)'I';
        BinaryPrimitives.WriteUInt16LittleEndian(tiff.AsSpan(2, 2), 42);
        BinaryPrimitives.WriteUInt32LittleEndian(tiff.AsSpan(4, 4), 8);
        BinaryPrimitives.WriteUInt16LittleEndian(tiff.AsSpan(8, 2), 2);
        BinaryPrimitives.WriteUInt16LittleEndian(tiff.AsSpan(10, 2), 0x0112);
        BinaryPrimitives.WriteUInt16LittleEndian(tiff.AsSpan(12, 2), 3);
        BinaryPrimitives.WriteUInt32LittleEndian(tiff.AsSpan(14, 4), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(tiff.AsSpan(18, 2), 6);
        BinaryPrimitives.WriteUInt16LittleEndian(tiff.AsSpan(22, 2), 0x8825);
        BinaryPrimitives.WriteUInt16LittleEndian(tiff.AsSpan(24, 2), 4);
        BinaryPrimitives.WriteUInt32LittleEndian(tiff.AsSpan(26, 4), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(tiff.AsSpan(30, 4), 38);
        BinaryPrimitives.WriteUInt32LittleEndian(tiff.AsSpan(34, 4), 0);
        BinaryPrimitives.WriteUInt16LittleEndian(tiff.AsSpan(38, 2), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(tiff.AsSpan(40, 2), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(tiff.AsSpan(42, 2), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(tiff.AsSpan(44, 4), 2);
        tiff[48] = (byte)'N';
        BinaryPrimitives.WriteUInt32LittleEndian(tiff.AsSpan(52, 4), 0);

        var payload = new byte[6 + tiff.Length];
        Encoding.ASCII.GetBytes("Exif\0\0").CopyTo(payload, 0);
        tiff.CopyTo(payload, 6);
        var marker = new byte[4 + payload.Length];
        marker[0] = 0xff;
        marker[1] = 0xe1;
        BinaryPrimitives.WriteUInt16BigEndian(marker.AsSpan(2, 2), checked((ushort)(payload.Length + 2)));
        payload.CopyTo(marker, 4);
        return [.. jpeg.AsSpan(0, 2).ToArray(), .. marker, .. jpeg.AsSpan(2).ToArray()];
    }

    private static byte[] CreateAsymmetricExifJpeg(byte orientation)
    {
        using var bitmap = new SKBitmap(30, 20, SKColorType.Rgba8888, SKAlphaType.Premul);
        for (var y = 0; y < bitmap.Height; y++)
        for (var x = 0; x < bitmap.Width; x++)
            bitmap.SetPixel(x, y, (x < 15, y < 10) switch
            {
                (true, true) => new SKColor(230, 20, 30),
                (false, true) => new SKColor(20, 210, 40),
                (true, false) => new SKColor(30, 50, 220),
                _ => new SKColor(220, 190, 20)
            });
        using var image = SKImage.FromBitmap(bitmap);
        using var jpegData = image.Encode(SKEncodedImageFormat.Jpeg, 100);
        var jpeg = jpegData!.ToArray();
        return AddExifOrientation(jpeg, orientation);
    }

    private static byte[] AddExifOrientation(byte[] jpeg, byte orientation)
    {
        var tiff = new byte[26];
        tiff[0] = (byte)'I'; tiff[1] = (byte)'I';
        BinaryPrimitives.WriteUInt16LittleEndian(tiff.AsSpan(2, 2), 42);
        BinaryPrimitives.WriteUInt32LittleEndian(tiff.AsSpan(4, 4), 8);
        BinaryPrimitives.WriteUInt16LittleEndian(tiff.AsSpan(8, 2), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(tiff.AsSpan(10, 2), 0x0112);
        BinaryPrimitives.WriteUInt16LittleEndian(tiff.AsSpan(12, 2), 3);
        BinaryPrimitives.WriteUInt32LittleEndian(tiff.AsSpan(14, 4), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(tiff.AsSpan(18, 2), orientation);
        var payload = new byte[6 + tiff.Length];
        Encoding.ASCII.GetBytes("Exif\0\0").CopyTo(payload, 0);
        tiff.CopyTo(payload, 6);
        var marker = new byte[4 + payload.Length];
        marker[0] = 0xff; marker[1] = 0xe1;
        BinaryPrimitives.WriteUInt16BigEndian(marker.AsSpan(2, 2), checked((ushort)(payload.Length + 2)));
        payload.CopyTo(marker, 4);
        return [.. jpeg.AsSpan(0, 2).ToArray(), .. marker, .. jpeg.AsSpan(2).ToArray()];
    }

    private static (int X, int Y) MapSourceToDestination(int x, int y, int width, int height, byte orientation) => orientation switch
    {
        1 => (x, y),
        2 => (width - 1 - x, y),
        3 => (width - 1 - x, height - 1 - y),
        4 => (x, height - 1 - y),
        5 => (y, x),
        6 => (height - 1 - y, x),
        7 => (height - 1 - y, width - 1 - x),
        8 => (y, width - 1 - x),
        _ => throw new ArgumentOutOfRangeException(nameof(orientation))
    };

    private static void AssertColorNear(SKColor expected, SKColor actual, byte orientation, (int X, int Y) point)
    {
        Assert.True(Math.Abs(expected.Red - actual.Red) <= 35
            && Math.Abs(expected.Green - actual.Green) <= 35
            && Math.Abs(expected.Blue - actual.Blue) <= 35,
            $"Orientation {orientation} moved source pixel {point} incorrectly: expected {expected}, got {actual}.");
    }

    private static bool HasPngChunk(byte[] png, string chunkName)
    {
        var offset = 8;
        while (offset + 12 <= png.Length)
        {
            var length = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(offset, 4));
            if (png.AsSpan(offset + 4, 4).SequenceEqual(Encoding.ASCII.GetBytes(chunkName)))
                return true;
            var next = (long)offset + 12 + length;
            if (next > png.Length)
                break;
            offset = (int)next;
        }
        return false;
    }
}
