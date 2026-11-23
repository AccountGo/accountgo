// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Api.DocumentCaptures;
using Api.DocumentCaptures.Parsing;
using SkiaSharp;

namespace AccountGo.CaptureWorker;

internal static class Program
{
    public static async Task<int> Main()
    {
        try
        {
            var models = Path.Combine(AppContext.BaseDirectory, "Models");
            VerifyModels(models);
            await using var stdin = Console.OpenStandardInput();
            var (header, bytes) = await CaptureWorkerProtocol.ReadAsync(stdin);
            var threads = int.TryParse(Environment.GetEnvironmentVariable("ACCOUNTGO_CAPTURE_OCR_THREADS"), out var selected)
                ? Math.Clamp(selected, 1, 4) : 2;
            var arena = Environment.GetEnvironmentVariable("ACCOUNTGO_CAPTURE_OCR_ARENA") != "0";
            using var engine = new OcrEngine(models, threads, arena);
            var result = header.ContentType == "application/pdf"
                ? ExtractPdf(header, bytes, engine)
                : ExtractImage(header.ContentType, bytes, engine);
            await using var stdout = Console.OpenStandardOutput();
            await JsonSerializer.SerializeAsync(stdout, result, CaptureWorkerProtocol.Json);
            return 0;
        }
        catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException or JsonException)
        {
            Console.Error.WriteLine("INVALID_INPUT");
            return 2;
        }
        catch (Exception)
        {
            Console.Error.WriteLine("WORKER_ERROR");
            return 3;
        }
    }

    private static void VerifyModels(string directory)
    {
        var manifest = File.ReadAllLines(Path.Combine(directory, "SHA256SUMS"));
        if (manifest.Length != CaptureWorkerModels.Files.Length)
            throw new InvalidOperationException();

        foreach (var (file, hash) in CaptureWorkerModels.Files)
        {
            using var stream = File.OpenRead(Path.Combine(directory, file));
            if (!manifest.Contains($"{hash}  {file}", StringComparer.Ordinal)
                || !Convert.ToHexStringLower(SHA256.HashData(stream)).Equals(hash, StringComparison.Ordinal))
                throw new InvalidOperationException();
        }
    }

    private static CaptureWorkerResult ExtractImage(string contentType, byte[] bytes, OcrEngine engine)
    {
        using var data = SKData.CreateCopy(bytes);
        using var codec = SKCodec.Create(data) ?? throw new InvalidDataException();
        var expected = contentType == "image/png" ? SKEncodedImageFormat.Png : SKEncodedImageFormat.Jpeg;
        if (codec.EncodedFormat != expected || codec.Info.Width is < 1 or > 2048
            || codec.Info.Height is < 1 or > 2048 || codec.FrameCount > 1)
            throw new InvalidDataException();

        using var bitmap = new SKBitmap(new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        if (codec.GetPixels(bitmap.Info, bitmap.GetPixels()) != SKCodecResult.Success)
            throw new InvalidDataException();

        return new CaptureWorkerResult(engine.Read(bitmap, 1).ToArray(), [1], [], []);
    }

    private static CaptureWorkerResult ExtractPdf(CaptureWorkerHeader header, byte[] bytes, OcrEngine engine)
    {
        using var pdf = new PdfiumDocument(bytes);
        if (pdf.PageCount != header.PageCount)
            throw new InvalidDataException();

        var pages = new IReadOnlyList<TextLine>[header.PageCount];
        var scanned = new List<int>();
        for (var page = 1; page <= pages.Length; page++)
        {
            var text = pdf.ReadText(page);
            if (HasTextLayer(text))
                pages[page - 1] = text;
            else
            {
                pages[page - 1] = [];
                scanned.Add(page);
            }
        }

        var selected = scanned.Count <= header.MaxOcrPages ? scanned
            : scanned.Take(header.MaxOcrPages - 1).Append(scanned[^1]).ToList();
        foreach (var page in selected)
        {
            using var image = pdf.Render(page);
            pages[page - 1] = engine.Read(image, page);
        }

        var skipped = scanned.Except(selected).ToArray();
        return new CaptureWorkerResult(pages.SelectMany(page => page).ToArray(), selected.ToArray(), skipped,
            skipped.Length > 0 ? ["OCR_PARTIAL_PAGES"] : []);
    }

    private static bool HasTextLayer(IReadOnlyList<TextLine> lines)
    {
        var count = 0;
        foreach (var line in lines)
            foreach (var rune in line.Text.EnumerateRunes())
                if (!Rune.IsWhiteSpace(rune) && ++count >= 20)
                    return true;
        return false;
    }
}
