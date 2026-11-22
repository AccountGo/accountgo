// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System.Security.Cryptography;
using System.Collections.Concurrent;
using SkiaSharp;

namespace Api.DocumentCaptures;

public static class DocumentCaptureImageLimits
{
    public const int MaxInputBytes = 8 * 1024 * 1024;
    public const int MaxStoredBytes = 1_500_000;
    public const int MaxDimensionPixels = 8_000;
    public const long MaxTotalPixels = 24_000_000;
    public const int MaxNormalizedDimension = 2_048;
    public const int DecodeGateSlots = 4;
    public static readonly TimeSpan DecodeAdmissionWait = TimeSpan.FromMilliseconds(500);
}

public sealed class DocumentCaptureMediaException(string errorCode, string message) : Exception(message)
{
    public string ErrorCode { get; } = errorCode;
}

public sealed record NormalizedDocumentImage(
    byte[] Bytes,
    string ContentType,
    string SourceHash,
    string ContentHash,
    int Width,
    int Height,
    int SourceByteCount);

public static class DocumentCaptureImageNormalizer
{
    internal static readonly ConcurrentDictionary<int, SemaphoreSlim> DecodeGates = new();

    public static async Task<NormalizedDocumentImage> NormalizeAsync(
        ReadOnlyMemory<byte> original,
        string declaredContentType,
        CancellationToken cancellationToken = default) =>
        await NormalizeAsync(original, declaredContentType, DocumentCaptureImageLimits.DecodeGateSlots, cancellationToken).ConfigureAwait(false);

    public static async Task<NormalizedDocumentImage> NormalizeAsync(
        ReadOnlyMemory<byte> original,
        string declaredContentType,
        int decodeSlots,
        CancellationToken cancellationToken = default)
    {
        if (original.IsEmpty)
            throw new DocumentCaptureMediaException("DOCUMENT_EMPTY", "An image file is required.");
        if (original.Length > DocumentCaptureImageLimits.MaxInputBytes)
            throw new DocumentCaptureMediaException("DOCUMENT_TOO_LARGE", "The uploaded image exceeds the size limit.");
        if (!string.Equals(declaredContentType, "image/png", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(declaredContentType, "image/jpeg", StringComparison.OrdinalIgnoreCase))
            throw new DocumentCaptureMediaException("DOCUMENT_UNSUPPORTED_TYPE", "Only PNG and JPEG images are supported.");

        if (decodeSlots is < 1 or > DocumentCaptureImageLimits.DecodeGateSlots)
            throw new ArgumentOutOfRangeException(nameof(decodeSlots));
        var decodeGate = DecodeGates.GetOrAdd(decodeSlots, static slots => new SemaphoreSlim(slots, slots));
        if (!await decodeGate.WaitAsync(DocumentCaptureImageLimits.DecodeAdmissionWait, cancellationToken).ConfigureAwait(false))
            throw new DocumentCaptureMediaException("DOCUMENT_DECODE_BUSY", "Image processing is busy. Try again shortly.");

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            return NormalizeCore(original.Span, declaredContentType);
        }
        catch (DocumentCaptureMediaException)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            throw new DocumentCaptureMediaException("DOCUMENT_MALFORMED", "The image is corrupt or incomplete.");
        }
        finally
        {
            decodeGate.Release();
        }
    }

    private static NormalizedDocumentImage NormalizeCore(ReadOnlySpan<byte> source, string declaredContentType)
    {
        using var data = SKData.CreateCopy(source.ToArray());
        using var codec = SKCodec.Create(data);
        if (codec is null)
            throw new DocumentCaptureMediaException("DOCUMENT_MALFORMED", "The image is corrupt or incomplete.");

        var expectedContentType = codec.EncodedFormat switch
        {
            SKEncodedImageFormat.Png => "image/png",
            SKEncodedImageFormat.Jpeg => "image/jpeg",
            _ => throw new DocumentCaptureMediaException("DOCUMENT_UNSUPPORTED_TYPE", "Only PNG and JPEG images are supported.")
        };
        if (!string.Equals(declaredContentType, expectedContentType, StringComparison.OrdinalIgnoreCase))
            throw new DocumentCaptureMediaException("DOCUMENT_MEDIA_MISMATCH", "The declared media type does not match the image.");
        if (codec.FrameCount > 1 || IsAnimatedPng(source))
            throw new DocumentCaptureMediaException("DOCUMENT_UNSUPPORTED_TYPE", "Animated images are not supported.");

        var width = codec.Info.Width;
        var height = codec.Info.Height;
        if (width <= 0 || height <= 0 || Math.Max(width, height) > DocumentCaptureImageLimits.MaxDimensionPixels
            || (long)width * height > DocumentCaptureImageLimits.MaxTotalPixels)
            throw new DocumentCaptureMediaException("DOCUMENT_DIMENSIONS", "Image dimensions exceed the supported limit.");

        var decodeSize = codec.EncodedFormat == SKEncodedImageFormat.Jpeg
            ? GetJpegDecodeSize(codec, width, height)
            : new SKSizeI(width, height);
        var imageInfo = new SKImageInfo(decodeSize.Width, decodeSize.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var resized = DecodeAndResize(codec, imageInfo);
        using var oriented = codec.EncodedOrigin == SKEncodedOrigin.TopLeft ? null : ApplyOrientation(resized, codec.EncodedOrigin);
        var normalized = oriented ?? resized;
        using var image = SKImage.FromBitmap(normalized);
        var (stored, storedContentType) = EncodeWithinLimit(image)
            ?? throw new DocumentCaptureMediaException("DOCUMENT_TOO_LARGE", "The normalized image exceeds the storage limit.");

        return new NormalizedDocumentImage(
            stored,
            storedContentType,
            Hash(source),
            Hash(stored),
            normalized.Width,
            normalized.Height,
            source.Length);
    }

    private static (byte[] Bytes, string ContentType)? EncodeWithinLimit(SKImage image)
    {
        using (var png = image.Encode(SKEncodedImageFormat.Png, 100))
        {
            if (png is not null && png.Size <= DocumentCaptureImageLimits.MaxStoredBytes)
                return (png.ToArray(), "image/png");
        }

        using var opaque = new SKBitmap(new SKImageInfo(image.Width, image.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(opaque))
        {
            canvas.Clear(SKColors.White);
            canvas.DrawImage(image, 0, 0);
            canvas.Flush();
        }
        using var jpegImage = SKImage.FromBitmap(opaque);
        foreach (var quality in new[] { 85, 70, 55, 40 })
        {
            using var jpeg = jpegImage.Encode(SKEncodedImageFormat.Jpeg, quality);
            if (jpeg is not null && jpeg.Size <= DocumentCaptureImageLimits.MaxStoredBytes)
                return (jpeg.ToArray(), "image/jpeg");
        }
        return null;
    }

    private static SKBitmap Resize(SKBitmap source)
    {
        if (source.Width <= DocumentCaptureImageLimits.MaxNormalizedDimension
            && source.Height <= DocumentCaptureImageLimits.MaxNormalizedDimension)
            return source.Copy();

        var scale = (double)DocumentCaptureImageLimits.MaxNormalizedDimension / Math.Max(source.Width, source.Height);
        var width = Math.Max(1, (int)Math.Round(source.Width * scale));
        var height = Math.Max(1, (int)Math.Round(source.Height * scale));
        return source.Resize(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul), SKFilterQuality.High)
            ?? throw new DocumentCaptureMediaException("DOCUMENT_NORMALIZATION_FAILED", "The image could not be resized.");
    }

    private static SKSizeI GetJpegDecodeSize(SKCodec codec, int width, int height)
    {
        foreach (var scale in new[] { 0.5f, 0.25f, 0.125f })
        {
            var scaled = codec.GetScaledDimensions(scale);
            if (scaled.Width > 0 && scaled.Height > 0
                && Math.Max(scaled.Width, scaled.Height) >= DocumentCaptureImageLimits.MaxNormalizedDimension)
                return scaled;
        }
        return new SKSizeI(width, height);
    }

    private static SKBitmap DecodeAndResize(SKCodec codec, SKImageInfo imageInfo)
    {
        using var decoded = new SKBitmap(imageInfo);
        if (codec.GetPixels(imageInfo, decoded.GetPixels()) != SKCodecResult.Success)
            throw new DocumentCaptureMediaException("DOCUMENT_MALFORMED", "The image is corrupt or incomplete.");
        return Resize(decoded);
    }

    private static SKBitmap ApplyOrientation(SKBitmap source, SKEncodedOrigin origin)
    {
        var swapsAxes = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop
            or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var width = swapsAxes ? source.Height : source.Width;
        var height = swapsAxes ? source.Width : source.Height;
        var result = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(result);
        canvas.Clear(SKColors.Transparent);

        // Move the source origin into the positive quadrant, then apply the EXIF rotation/reflection.
        var (degrees, flipX, flipY) = origin switch
        {
            SKEncodedOrigin.TopRight => (0f, true, false),
            SKEncodedOrigin.BottomRight => (180f, false, false),
            SKEncodedOrigin.BottomLeft => (0f, false, true),
            SKEncodedOrigin.LeftTop => (270f, true, false),
            SKEncodedOrigin.RightTop => (90f, false, false),
            SKEncodedOrigin.RightBottom => (90f, true, false),
            SKEncodedOrigin.LeftBottom => (270f, false, false),
            _ => (0f, false, false)
        };
        canvas.Translate(width / 2f, height / 2f);
        canvas.RotateDegrees(degrees);
        canvas.Scale(flipX ? -1 : 1, flipY ? -1 : 1);
        canvas.DrawBitmap(source, -source.Width / 2f, -source.Height / 2f);
        canvas.Flush();
        return result;
    }

    private static bool IsAnimatedPng(ReadOnlySpan<byte> source)
    {
        ReadOnlySpan<byte> signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        if (source.Length < signature.Length || !source[..signature.Length].SequenceEqual(signature))
            return false;
        var offset = signature.Length;
        while (offset + 12 <= source.Length)
        {
            var length = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(source.Slice(offset, 4));
            var type = source.Slice(offset + 4, 4);
            if (type.SequenceEqual("acTL"u8) || type.SequenceEqual("fcTL"u8))
                return true;
            var next = (long)offset + 12 + length;
            if (next <= offset || next > source.Length)
                return false;
            offset = (int)next;
        }
        return false;
    }

    private static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
