// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System.Buffers;
using System.Collections.Concurrent;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Exceptions;
using UglyToad.PdfPig.Filters;
using UglyToad.PdfPig.Tokens;

namespace Api.DocumentCaptures;

public static class DocumentCapturePdfLimits
{
    public const int MaxInputBytes = 8 * 1024 * 1024;
    public const int MaxAggregateDecodedBytes = 32 * 1024 * 1024;
    public const int MaxDecodedBytesPerStream = 8 * 1024 * 1024;
    public const int MaxStackDepth = 64;
    public const int MaxPages = 10;
    public const int MaxAnnotationsPerPage = 500;
    public const int ParseGateSlots = 1;
    public const int MaxParseGateSlots = 4;
    public static readonly TimeSpan ParseAdmissionWait = TimeSpan.FromMilliseconds(500);
    public static readonly TimeSpan ParseDeadline = TimeSpan.FromSeconds(3);
}

public sealed record DocumentCapturePdfValidationResult(bool IsValid, int? PageCount, string? ErrorCode);

public static class DocumentCapturePdfValidator
{
    internal static readonly ConcurrentDictionary<int, SemaphoreSlim> ParseGates = new();

    public static async Task<DocumentCapturePdfValidationResult> ValidateAsync(
        ReadOnlyMemory<byte> bytes,
        CancellationToken cancellationToken = default) =>
        await ValidateAsync(bytes, DocumentCapturePdfLimits.ParseGateSlots, cancellationToken).ConfigureAwait(false);

    public static async Task<DocumentCapturePdfValidationResult> ValidateAsync(
        ReadOnlyMemory<byte> bytes,
        int parseSlots,
        CancellationToken cancellationToken = default)
        => await ValidateAsync(bytes, parseSlots, TimeProvider.System, cancellationToken).ConfigureAwait(false);

    internal static async Task<DocumentCapturePdfValidationResult> ValidateAsync(
        ReadOnlyMemory<byte> bytes,
        int parseSlots,
        TimeProvider clock,
        CancellationToken cancellationToken = default)
    {
        if (bytes.Length == 0)
            return Invalid("DOCUMENT_EMPTY");
        if (bytes.Length > DocumentCapturePdfLimits.MaxInputBytes)
            return Invalid("DOCUMENT_TOO_LARGE");
        if (bytes.Length < 5 || !bytes.Span[..5].SequenceEqual("%PDF-"u8))
            return Invalid("DOCUMENT_MALFORMED");
        if (!HasTerminalEndOfFileMarker(bytes.Span))
            return Invalid("DOCUMENT_MALFORMED");

        if (parseSlots is < 1 or > DocumentCapturePdfLimits.MaxParseGateSlots)
            throw new ArgumentOutOfRangeException(nameof(parseSlots));
        var parseGate = ParseGates.GetOrAdd(parseSlots, static slots => new SemaphoreSlim(slots, slots));
        var entered = await parseGate.WaitAsync(DocumentCapturePdfLimits.ParseAdmissionWait, cancellationToken).ConfigureAwait(false);
        if (!entered)
            return Invalid("DOCUMENT_DECODE_BUSY");

        PdfDecodeBudget? budget = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var decodeBudget = new PdfDecodeBudget(DocumentCapturePdfLimits.MaxAggregateDecodedBytes,
                DocumentCapturePdfLimits.MaxDecodedBytesPerStream, DocumentCapturePdfLimits.ParseDeadline, clock);
            budget = decodeBudget;
            var options = new ParsingOptions
            {
                UseLenientParsing = false,
                MaxStackDepth = DocumentCapturePdfLimits.MaxStackDepth,
                FilterProvider = new BoundedPdfFilterProvider(decodeBudget)
            };

            using var document = PdfDocument.Open(bytes, options);
            decodeBudget.ThrowIfExpired();
            if (document.IsEncrypted)
                return Invalid("DOCUMENT_UNSUPPORTED_TYPE");
            if (document.NumberOfPages is < 1 or > DocumentCapturePdfLimits.MaxPages)
                return Invalid("DOCUMENT_UNSUPPORTED_TYPE");
            if (HasUnsupportedActiveContent(document))
                return Invalid("DOCUMENT_UNSUPPORTED_TYPE");
            decodeBudget.ThrowIfExpired();

            return new DocumentCapturePdfValidationResult(true, document.NumberOfPages, null);
        }
        catch (PdfDecodeLimitException)
        {
            return Invalid("DOCUMENT_UNSUPPORTED_TYPE");
        }
        catch (Exception ex) when (ContainsDecodeLimitException(ex))
        {
            return Invalid("DOCUMENT_UNSUPPORTED_TYPE");
        }
        catch (Exception) when (budget?.IsExpired == true)
        {
            return Invalid("DOCUMENT_UNSUPPORTED_TYPE");
        }
        catch (PdfDocumentEncryptedException)
        {
            return Invalid("DOCUMENT_UNSUPPORTED_TYPE");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return Invalid("DOCUMENT_MALFORMED");
        }
        finally
        {
            parseGate.Release();
        }
    }

    private static DocumentCapturePdfValidationResult Invalid(string errorCode) => new(false, null, errorCode);

    private static bool HasTerminalEndOfFileMarker(ReadOnlySpan<byte> bytes)
    {
        var end = bytes.Length;
        while (end > 0 && bytes[end - 1] is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n' or (byte)'\f')
            end--;
        return end >= 5 && bytes.Slice(end - 5, 5).SequenceEqual("%%EOF"u8);
    }

    private static bool HasUnsupportedActiveContent(PdfDocument document)
    {
        var catalog = document.Structure.Catalog.CatalogDictionary;
        foreach (var key in new[] { "OpenAction", "AA", "AcroForm", "Collection", "Perms", "AF" })
        {
            if (catalog.ContainsKey(NameToken.Create(key)))
                return true;
        }

        if (catalog.TryGet(NameToken.Create("Names"), out var namesToken))
        {
            var names = ResolveDictionary(document, namesToken);
            if (names.ContainsKey(NameToken.Create("JavaScript"))
                || names.ContainsKey(NameToken.Create("EmbeddedFiles")))
                return true;
        }

        if (!catalog.TryGet(NameToken.Create("Pages"), out var pagesToken))
            throw new InvalidDataException("PDF catalog is missing its active page tree.");

        var pageCount = 0;
        var nodesVisited = 0;
        var hasUnsupportedContent = InspectPageTree(document, pagesToken, depth: 0, ref pageCount, ref nodesVisited);
        if (pageCount != document.NumberOfPages)
            throw new InvalidDataException("PdfPig page count does not match the active page tree.");
        return hasUnsupportedContent;
    }

    private static bool InspectPageTree(PdfDocument document, IToken nodeToken, int depth, ref int pageCount, ref int nodesVisited)
    {
        if (depth >= DocumentCapturePdfLimits.MaxStackDepth || ++nodesVisited > 5000)
            throw new PdfDecodeLimitException("PDF active page tree exceeded its inspection bounds.");

        var node = ResolveDictionary(document, nodeToken);
        if (!node.TryGet(NameToken.Create("Type"), out var typeToken) || typeToken is not NameToken type)
            throw new InvalidDataException("PDF page-tree node has no valid type.");

        if (type.Data == "Page")
        {
            if (++pageCount > DocumentCapturePdfLimits.MaxPages)
                return false;
            return InspectPageAnnotations(document, node);
        }

        if (type.Data != "Pages" || !node.TryGet(NameToken.Create("Kids"), out var kidsToken))
            throw new InvalidDataException("PDF page-tree node is malformed.");
        var kids = ResolveArray(document, kidsToken);
        if (kids.Length > 500)
            throw new PdfDecodeLimitException("PDF page-tree child array exceeded its inspection bound.");
        var hasUnsupportedContent = false;
        foreach (var child in kids.Data)
        {
            if (InspectPageTree(document, child, depth + 1, ref pageCount, ref nodesVisited))
                hasUnsupportedContent = true;
        }
        return hasUnsupportedContent;
    }

    private static bool InspectPageAnnotations(PdfDocument document, DictionaryToken page)
    {
        if (page.TryGet(NameToken.Create("AA"), out var additionalActions))
        {
            var actions = ResolveDictionary(document, additionalActions);
            if (actions.Data.Count > 500)
                throw new PdfDecodeLimitException("PDF page action dictionary exceeded its inspection bound.");
            foreach (var action in actions.Data.Values)
            {
                if (IsUnsupportedAction(document, action))
                    return true;
            }
        }

        if (!page.TryGet(NameToken.Create("Annots"), out var annotationsToken))
            return false;
        var annotations = ResolveArray(document, annotationsToken);
        if (annotations.Length > DocumentCapturePdfLimits.MaxAnnotationsPerPage)
            throw new PdfDecodeLimitException("PDF page annotation count exceeded its inspection bound.");

        foreach (var annotationToken in annotations.Data)
        {
            var annotation = ResolveDictionary(document, annotationToken);
            if (annotation.TryGet(NameToken.Create("Subtype"), out var subtypeToken)
                && subtypeToken is NameToken subtype
                && subtype.Data is "Screen" or "RichMedia" or "FileAttachment" or "Movie" or "Sound" or "3D" or "Widget")
                return true;

            if (annotation.TryGet(NameToken.Create("A"), out var action)
                && IsUnsupportedAction(document, action))
                return true;

            if (annotation.TryGet(NameToken.Create("AA"), out var annotationActions))
            {
                var actions = ResolveDictionary(document, annotationActions);
                if (actions.Data.Count > 500)
                    throw new PdfDecodeLimitException("PDF annotation action dictionary exceeded its inspection bound.");
                foreach (var additionalAction in actions.Data.Values)
                {
                    if (IsUnsupportedAction(document, additionalAction))
                        return true;
                }
            }
        }

        return false;
    }

    private static bool IsUnsupportedAction(PdfDocument document, IToken token)
    {
        var action = ResolveDictionary(document, token);
        if (!action.TryGet(NameToken.Create("S"), out var typeToken) || typeToken is not NameToken type)
            throw new InvalidDataException("PDF action has no valid type.");
        return type.Data is "JavaScript" or "Launch" or "SubmitForm" or "ImportData" or "Rendition"
            or "GoToR" or "GoToE" or "Sound" or "Movie";
    }

    private static DictionaryToken ResolveDictionary(PdfDocument document, IToken token)
    {
        var resolved = ResolveToken(document, token);
        return resolved as DictionaryToken ?? throw new InvalidDataException("PDF structure entry is not a dictionary.");
    }

    private static ArrayToken ResolveArray(PdfDocument document, IToken token)
    {
        var resolved = ResolveToken(document, token);
        return resolved as ArrayToken ?? throw new InvalidDataException("PDF structure entry is not an array.");
    }

    private static IToken ResolveToken(PdfDocument document, IToken token)
    {
        var references = new HashSet<IndirectReference>();
        while (token is IndirectReferenceToken reference)
        {
            if (!references.Add(reference.Data) || references.Count > DocumentCapturePdfLimits.MaxStackDepth)
                throw new PdfDecodeLimitException("PDF structure contains a cyclic or overly deep indirect reference.");
            token = document.Structure.GetObject(reference.Data)
                ?? throw new InvalidDataException("PDF structure contains an unresolved indirect reference.");
            if (token is ObjectToken objectToken)
                token = objectToken.Data;
        }
        return token;
    }

    private static bool ContainsDecodeLimitException(Exception exception) => exception switch
    {
        PdfDecodeLimitException => true,
        AggregateException aggregate => aggregate.InnerExceptions.Any(ContainsDecodeLimitException),
        _ => exception.InnerException is not null && ContainsDecodeLimitException(exception.InnerException)
    };
}

internal sealed class PdfDecodeLimitException(string message) : Exception(message);

internal sealed class PdfDecodeBudget
{
    private readonly int _maxAggregate;
    private readonly int _maxPerStream;
    private readonly long _deadlineTimestamp;
    private readonly TimeProvider _clock;
    private readonly ConditionalWeakTable<DictionaryToken, StreamCounter> _streams = new();
    private long _aggregateBytes;

    public PdfDecodeBudget(int maxAggregate, int maxPerStream, TimeSpan deadline, TimeProvider clock)
    {
        _maxAggregate = maxAggregate;
        _maxPerStream = maxPerStream;
        _clock = clock;
        _deadlineTimestamp = clock.GetTimestamp() + (long)(deadline.TotalSeconds * clock.TimestampFrequency);
    }

    public void ThrowIfExpired()
    {
        if (IsExpired)
            throw new PdfDecodeLimitException("PDF parsing exceeded its bounded deadline.");
    }

    public bool IsExpired => _clock.GetTimestamp() > _deadlineTimestamp;

    public void Reserve(DictionaryToken stream, int count)
    {
        ThrowIfExpired();
        if (count < 0)
            throw new PdfDecodeLimitException("PDF decoder produced an invalid output size.");

        var streamCounter = _streams.GetValue(stream, static _ => new StreamCounter());
        var streamTotal = Interlocked.Add(ref streamCounter.Bytes, count);
        var total = Interlocked.Add(ref _aggregateBytes, count);
        if (streamTotal > _maxPerStream || total > _maxAggregate)
            throw new PdfDecodeLimitException("PDF decoded byte limit exceeded.");
    }

    private sealed class StreamCounter
    {
        public long Bytes;
    }
}

internal sealed class BoundedPdfFilterProvider : BaseFilterProvider
{
    public BoundedPdfFilterProvider(PdfDecodeBudget budget)
        : base(CreateFilters(budget))
    {
    }

    private static IReadOnlyDictionary<string, IFilter> CreateFilters(PdfDecodeBudget budget)
    {
        var ascii85 = new BoundedAscii85Filter(budget);
        var asciiHex = new BoundedAsciiHexFilter(budget);
        var flate = new BoundedFlateFilter(budget);
        var runLength = new BoundedRunLengthFilter(budget);
        var lzw = new BoundedLzwFilter(budget);

        return new Dictionary<string, IFilter>(StringComparer.Ordinal)
        {
            [NameToken.Ascii85Decode.Data] = ascii85,
            [NameToken.Ascii85DecodeAbbreviation.Data] = ascii85,
            [NameToken.AsciiHexDecode.Data] = asciiHex,
            [NameToken.AsciiHexDecodeAbbreviation.Data] = asciiHex,
            [NameToken.FlateDecode.Data] = flate,
            [NameToken.FlateDecodeAbbreviation.Data] = flate,
            [NameToken.RunLengthDecode.Data] = runLength,
            [NameToken.RunLengthDecodeAbbreviation.Data] = runLength,
            [NameToken.LzwDecode.Data] = lzw,
            [NameToken.LzwDecodeAbbreviation.Data] = lzw
        };
    }
}

internal abstract class BoundedPdfFilter(PdfDecodeBudget budget) : IFilter
{
    public bool IsSupported => true;

    public Memory<byte> Decode(Memory<byte> input, DictionaryToken streamDictionary, IFilterProvider filterProvider, int filterIndex)
    {
        budget.ThrowIfExpired();
        RejectUnsupportedPredictor(streamDictionary, filterIndex);
        var output = new ArrayBufferWriter<byte>(Math.Min(input.Length, 64 * 1024));
        DecodeInto(input.Span, streamDictionary, output, filterIndex);
        return output.WrittenMemory.ToArray();
    }

    protected abstract void DecodeInto(ReadOnlySpan<byte> input, DictionaryToken stream, ArrayBufferWriter<byte> output, int filterIndex);

    protected void Write(ArrayBufferWriter<byte> output, DictionaryToken stream, ReadOnlySpan<byte> value)
    {
        if (value.IsEmpty)
            return;
        budget.Reserve(stream, value.Length);
        value.CopyTo(output.GetSpan(value.Length));
        output.Advance(value.Length);
    }

    protected void CheckDeadline() => budget.ThrowIfExpired();

    private static void RejectUnsupportedPredictor(DictionaryToken stream, int filterIndex)
    {
        var decodeParams = GetDecodeParameters(stream, filterIndex);
        if (decodeParams is null or NullToken)
            return;
        if (decodeParams is not DictionaryToken dictionary)
            throw new PdfDecodeLimitException("Unsupported PDF decode parameters.");

        dictionary.TryGet(NameToken.Predictor, out var predictor);
        if (predictor is null or NullToken)
            return;
        if (predictor is not NumericToken number || number.Data != 1)
            throw new PdfDecodeLimitException("Unsupported PDF predictor.");
    }

    protected static IToken? GetDecodeParameters(DictionaryToken stream, int filterIndex)
    {
        foreach (var pair in stream.Data)
        {
            if (pair.Key is "DecodeParms" or "DP")
            {
                if (pair.Value is ArrayToken parameters)
                    return filterIndex >= 0 && filterIndex < parameters.Length ? parameters[filterIndex] : null;
                return pair.Value;
            }
        }
        return null;
    }
}

internal sealed class BoundedFlateFilter(PdfDecodeBudget budget) : BoundedPdfFilter(budget)
{
    protected override void DecodeInto(ReadOnlySpan<byte> input, DictionaryToken stream, ArrayBufferWriter<byte> output, int filterIndex)
    {
        using var inputStream = new MemoryStream(input.ToArray(), writable: false);
        using var decoder = new ZLibStream(inputStream, CompressionMode.Decompress, leaveOpen: false);
        Span<byte> buffer = stackalloc byte[4096];
        while (true)
        {
            CheckDeadline();
            var read = decoder.Read(buffer);
            if (read == 0)
                break;
            Write(output, stream, buffer[..read]);
        }
    }
}

internal sealed class BoundedAsciiHexFilter(PdfDecodeBudget budget) : BoundedPdfFilter(budget)
{
    protected override void DecodeInto(ReadOnlySpan<byte> input, DictionaryToken stream, ArrayBufferWriter<byte> output, int filterIndex)
    {
        var highNibble = -1;
        Span<byte> value = stackalloc byte[1];
        foreach (var character in input)
        {
            CheckDeadline();
            if (character == '>')
                break;
            if (IsWhitespace(character))
                continue;
            var digit = HexDigit(character);
            if (digit < 0)
                throw new PdfDecodeLimitException("Invalid ASCIIHex stream.");
            if (highNibble < 0)
            {
                highNibble = digit;
                continue;
            }
            value[0] = (byte)((highNibble << 4) | digit);
            Write(output, stream, value);
            highNibble = -1;
        }

        if (highNibble >= 0)
        {
            value[0] = (byte)(highNibble << 4);
            Write(output, stream, value);
        }
    }

    internal static bool IsWhitespace(byte value) => value is 0 or 9 or 10 or 12 or 13 or 32;

    private static int HexDigit(byte value) => value switch
    {
        >= (byte)'0' and <= (byte)'9' => value - '0',
        >= (byte)'A' and <= (byte)'F' => value - 'A' + 10,
        >= (byte)'a' and <= (byte)'f' => value - 'a' + 10,
        _ => -1
    };
}

internal sealed class BoundedAscii85Filter(PdfDecodeBudget budget) : BoundedPdfFilter(budget)
{
    protected override void DecodeInto(ReadOnlySpan<byte> input, DictionaryToken stream, ArrayBufferWriter<byte> output, int filterIndex)
    {
        Span<byte> decoded = stackalloc byte[4];
        ulong accumulator = 0;
        var digits = 0;
        var index = 0;
        while (index < input.Length && BoundedAsciiHexFilter.IsWhitespace(input[index])) index++;
        if (index + 1 < input.Length && input[index] == '<' && input[index + 1] == '~') index += 2;

        for (; index < input.Length; index++)
        {
            CheckDeadline();
            var character = input[index];
            if (BoundedAsciiHexFilter.IsWhitespace(character))
                continue;
            if (character == '~')
                break;
            if (character == 'z')
            {
                if (digits != 0)
                    throw new PdfDecodeLimitException("Invalid ASCII85 zero shorthand.");
                decoded.Clear();
                Write(output, stream, decoded);
                continue;
            }
            if (character is < (byte)'!' or > (byte)'u')
                throw new PdfDecodeLimitException("Invalid ASCII85 stream.");

            accumulator = (accumulator * 85) + (uint)(character - '!');
            if (++digits != 5)
                continue;
            if (accumulator > uint.MaxValue)
                throw new PdfDecodeLimitException("ASCII85 tuple overflow.");
            decoded[0] = (byte)(accumulator >> 24);
            decoded[1] = (byte)(accumulator >> 16);
            decoded[2] = (byte)(accumulator >> 8);
            decoded[3] = (byte)accumulator;
            Write(output, stream, decoded);
            accumulator = 0;
            digits = 0;
        }

        if (digits == 1)
            throw new PdfDecodeLimitException("Truncated ASCII85 tuple.");
        if (digits > 1)
        {
            var outputBytes = digits - 1;
            while (digits < 5)
            {
                accumulator = (accumulator * 85) + 84;
                digits++;
            }
            if (accumulator > uint.MaxValue)
                throw new PdfDecodeLimitException("ASCII85 tuple overflow.");
            decoded[0] = (byte)(accumulator >> 24);
            decoded[1] = (byte)(accumulator >> 16);
            decoded[2] = (byte)(accumulator >> 8);
            decoded[3] = (byte)accumulator;
            Write(output, stream, decoded[..outputBytes]);
        }
    }
}

internal sealed class BoundedRunLengthFilter(PdfDecodeBudget budget) : BoundedPdfFilter(budget)
{
    protected override void DecodeInto(ReadOnlySpan<byte> input, DictionaryToken stream, ArrayBufferWriter<byte> output, int filterIndex)
    {
        var index = 0;
        Span<byte> repeated = stackalloc byte[128];
        while (index < input.Length)
        {
            CheckDeadline();
            var length = input[index++];
            if (length == 128)
                return;
            if (length <= 127)
            {
                var count = length + 1;
                if (index + count > input.Length)
                    throw new PdfDecodeLimitException("Truncated RunLength stream.");
                Write(output, stream, input.Slice(index, count));
                index += count;
                continue;
            }

            if (index >= input.Length)
                throw new PdfDecodeLimitException("Truncated RunLength repeat.");
            var countToRepeat = 257 - length;
            repeated[..countToRepeat].Fill(input[index++]);
            Write(output, stream, repeated[..countToRepeat]);
        }
        throw new PdfDecodeLimitException("RunLength stream has no end marker.");
    }
}

internal sealed class BoundedLzwFilter(PdfDecodeBudget budget) : BoundedPdfFilter(budget)
{
    protected override void DecodeInto(ReadOnlySpan<byte> input, DictionaryToken stream, ArrayBufferWriter<byte> output, int filterIndex)
    {
        var earlyChange = ReadEarlyChange(stream, filterIndex);
        var reader = new MsbBitReader(input);
        var table = new byte[4096][];
        ResetTable(table);
        var nextCode = 258;
        var codeSize = 9;
        byte[]? previous = null;
        long tableBytes = 258;

        while (reader.TryRead(codeSize, out var code))
        {
            CheckDeadline();
            if (code == 256)
            {
                ResetTable(table);
                nextCode = 258;
                codeSize = 9;
                previous = null;
                tableBytes = 258;
                continue;
            }
            if (code == 257)
                return;

            byte[] current;
            if (code < nextCode && table[code] is { } entry)
            {
                current = entry;
            }
            else if (code == nextCode && previous is not null)
            {
                current = Append(previous, previous[0]);
            }
            else
            {
                throw new PdfDecodeLimitException("Invalid LZW code.");
            }

            Write(output, stream, current);
            if (previous is not null && nextCode < 4096)
            {
                var added = Append(previous, current[0]);
                tableBytes += added.Length;
                if (tableBytes > DocumentCapturePdfLimits.MaxDecodedBytesPerStream)
                    throw new PdfDecodeLimitException("LZW dictionary memory limit exceeded.");
                table[nextCode++] = added;
                if (codeSize < 12 && nextCode + earlyChange == (1 << codeSize))
                    codeSize++;
            }
            previous = current;
        }

        throw new PdfDecodeLimitException("LZW stream has no end code.");
    }

    private static int ReadEarlyChange(DictionaryToken stream, int filterIndex)
    {
        var decodeParams = GetDecodeParameters(stream, filterIndex);
        if (decodeParams is null or NullToken)
            return 1;
        if (decodeParams is not DictionaryToken dictionary)
            throw new PdfDecodeLimitException("Unsupported LZW parameters.");
        dictionary.TryGet(NameToken.EarlyChange, out var earlyChange);
        if (earlyChange is null or NullToken)
            return 1;
        if (earlyChange is not NumericToken numeric || numeric.Data is not (0 or 1))
            throw new PdfDecodeLimitException("Invalid LZW EarlyChange.");
        return (int)numeric.Data;
    }

    private static byte[] Append(byte[] prefix, byte value)
    {
        var result = new byte[prefix.Length + 1];
        prefix.CopyTo(result, 0);
        result[^1] = value;
        return result;
    }

    private static void ResetTable(byte[][] table)
    {
        Array.Clear(table);
        for (var value = 0; value < 256; value++)
            table[value] = new[] { (byte)value };
    }

    private ref struct MsbBitReader
    {
        private readonly ReadOnlySpan<byte> _bytes;
        private int _bitOffset;

        public MsbBitReader(ReadOnlySpan<byte> bytes)
        {
            _bytes = bytes;
            _bitOffset = 0;
        }

        public bool TryRead(int count, out int value)
        {
            value = 0;
            if (_bitOffset + count > _bytes.Length * 8)
                return false;
            for (var i = 0; i < count; i++)
            {
                var byteIndex = _bitOffset >> 3;
                var bitIndex = 7 - (_bitOffset & 7);
                value = (value << 1) | ((_bytes[byteIndex] >> bitIndex) & 1);
                _bitOffset++;
            }
            return true;
        }
    }
}
