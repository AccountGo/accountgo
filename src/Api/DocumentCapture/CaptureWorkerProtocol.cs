// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using System.Buffers.Binary;
using System.Text.Json;
using Api.DocumentCaptures.Parsing;

namespace Api.DocumentCaptures;

internal sealed record CaptureWorkerHeader(string ContentType, int PageCount, int MaxOcrPages);
internal sealed record CaptureWorkerResult(TextLine[] Lines, int[] OcrPages, int[] SkippedPages, string[] Warnings);

internal static class CaptureWorkerProtocol
{
    internal const int MaxInputBytes = 8 * 1024 * 1024;
    internal const int MaxOutputBytes = 256 * 1024;
    private const int MaxHeaderBytes = 1024;
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { MaxDepth = 12 };

    internal static async Task WriteAsync(Stream stream, CaptureWorkerHeader header, ReadOnlyMemory<byte> bytes,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(header, Json);
        var prefix = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(prefix, json.Length);
        await stream.WriteAsync(prefix, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(json, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
    }

    internal static async Task<(CaptureWorkerHeader Header, byte[] Bytes)> ReadAsync(Stream stream)
    {
        var prefix = new byte[4];
        await stream.ReadExactlyAsync(prefix).ConfigureAwait(false);
        var headerLength = BinaryPrimitives.ReadInt32LittleEndian(prefix);
        if (headerLength is < 1 or > MaxHeaderBytes)
            throw new InvalidDataException();

        var json = new byte[headerLength];
        await stream.ReadExactlyAsync(json).ConfigureAwait(false);
        var header = JsonSerializer.Deserialize<CaptureWorkerHeader>(json, Json);
        if (header is null || header.PageCount is < 1 or > 10 || header.MaxOcrPages is < 1 or > 3
            || header.ContentType is not ("application/pdf" or "image/jpeg" or "image/png")
            || (header.ContentType != "application/pdf" && header.PageCount != 1))
            throw new InvalidDataException();

        var bytes = await ReadBoundedAsync(stream, MaxInputBytes, CancellationToken.None).ConfigureAwait(false);
        return bytes.Length > 0 ? (header, bytes) : throw new InvalidDataException();
    }

    internal static async Task<byte[]> ReadBoundedAsync(Stream stream, int limit, CancellationToken token)
    {
        using var result = new MemoryStream();
        var buffer = new byte[8192];
        while (true)
        {
            var remaining = limit - (int)result.Length;
            var count = await stream.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, remaining + 1)), token).ConfigureAwait(false);
            if (count == 0)
                return result.ToArray();
            if (count > remaining)
                throw new InvalidDataException();
            result.Write(buffer, 0, count);
        }
    }
}

internal static class CaptureWorkerModels
{
    internal const string Detection = "PP-OCRv5_mobile_det.onnx";
    internal const string Recognition = "en_PP-OCRv5_mobile_rec.onnx";
    internal const string Dictionary = "en_dict.json";
    internal const string Version = "ocr-1+d5de5df3+20b69451";
    internal static readonly (string File, string Sha256)[] Files =
    [
        (Detection, "d5de5df358366210d16419b9636a2fc1efa5d7a20688f38a7869ec7b1a4f4f7d"),
        (Recognition, "20b6945179e9aa1dabe8770a0e73dba9a05350431549e58d7e88837f033c2890"),
        (Dictionary, "a981c85a58da77df3ac590a516a489a9e77bdb0d10428f8c008295b8b6f40f28")
    ];
}
