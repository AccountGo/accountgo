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

/// <summary>
/// Byte, page, decode and time bounds for one PDF validation. <see cref="Scan"/> is the extraction profile (unchanged limits);
/// stored-only library uploads use <see cref="ForStoredDocument"/>, which scales the decode budget and deadline with the byte limit.
/// </summary>
public sealed record DocumentCapturePdfProfile(
    int MaxInputBytes,
    int MaxPages,
    int MaxAggregateDecodedBytes,
    int MaxDecodedBytesPerStream,
    TimeSpan ParseDeadline,
    bool ReportsPageLimit = false)
{
    public const int MaxStoredInputBytes = 15 * 1024 * 1024;
    public const int MaxStoredPages = 300;

    public static DocumentCapturePdfProfile Scan { get; } = new(
        DocumentCapturePdfLimits.MaxInputBytes,
        DocumentCapturePdfLimits.MaxPages,
        DocumentCapturePdfLimits.MaxAggregateDecodedBytes,
        DocumentCapturePdfLimits.MaxDecodedBytesPerStream,
        DocumentCapturePdfLimits.ParseDeadline);

    /// <summary>Stored-only documents: never above 15 MiB / 300 pages; at or below the scan limits it is the scan profile.</summary>
    public static DocumentCapturePdfProfile ForStoredDocument(int maxInputBytes, int maxPages)
    {
        if (maxInputBytes is < 1 or > MaxStoredInputBytes)
            throw new ArgumentOutOfRangeException(nameof(maxInputBytes));
        if (maxPages is < 1 or > MaxStoredPages)
            throw new ArgumentOutOfRangeException(nameof(maxPages));
        if (maxInputBytes <= DocumentCapturePdfLimits.MaxInputBytes && maxPages <= DocumentCapturePdfLimits.MaxPages)
            return Scan with { MaxInputBytes = maxInputBytes, MaxPages = maxPages, ReportsPageLimit = true };
        return new DocumentCapturePdfProfile(
            maxInputBytes,
            maxPages,
            MaxAggregateDecodedBytes: 64 * 1024 * 1024,
            MaxDecodedBytesPerStream: Math.Max(DocumentCapturePdfLimits.MaxDecodedBytesPerStream, maxInputBytes),
            ParseDeadline: TimeSpan.FromSeconds(8),
            ReportsPageLimit: true);
    }
}

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

    internal static Task<DocumentCapturePdfValidationResult> ValidateAsync(
        ReadOnlyMemory<byte> bytes,
        int parseSlots,
        TimeProvider clock,
        CancellationToken cancellationToken = default) =>
        ValidateWithDeadlineAsync(bytes, parseSlots, clock, DocumentCapturePdfLimits.ParseDeadline, cancellationToken);

    /// <summary>Validates with an explicit profile (stored-only library documents).</summary>
    public static Task<DocumentCapturePdfValidationResult> ValidateAsync(
        ReadOnlyMemory<byte> bytes,
        int parseSlots,
        DocumentCapturePdfProfile profile,
        TimeProvider clock,
        CancellationToken cancellationToken = default) =>
        ValidateWithProfileAsync(bytes, parseSlots, clock, profile ?? throw new ArgumentNullException(nameof(profile)), cancellationToken);

    internal static Task<DocumentCapturePdfValidationResult> ValidateWithDeadlineAsync(
        ReadOnlyMemory<byte> bytes,
        int parseSlots,
        TimeProvider clock,
        TimeSpan deadline,
        CancellationToken cancellationToken) =>
        ValidateWithProfileAsync(bytes, parseSlots, clock, DocumentCapturePdfProfile.Scan with { ParseDeadline = deadline }, cancellationToken);

    private static async Task<DocumentCapturePdfValidationResult> ValidateWithProfileAsync(
        ReadOnlyMemory<byte> bytes,
        int parseSlots,
        TimeProvider clock,
        DocumentCapturePdfProfile profile,
        CancellationToken cancellationToken)
    {
        var deadline = profile.ParseDeadline;
        if (bytes.Length == 0)
            return Invalid("DOCUMENT_EMPTY");
        if (bytes.Length > profile.MaxInputBytes)
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
            var decodeBudget = new PdfDecodeBudget(profile.MaxAggregateDecodedBytes,
                profile.MaxDecodedBytesPerStream, deadline, clock);
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
            if (document.NumberOfPages < 1)
                return Invalid("DOCUMENT_UNSUPPORTED_TYPE");
            if (document.NumberOfPages > profile.MaxPages)
                return Invalid(profile.ReportsPageLimit ? "DOCUMENT_TOO_MANY_PAGES" : "DOCUMENT_UNSUPPORTED_TYPE");
            if (HasUnsupportedActiveContent(document, profile.MaxPages))
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
        catch (Exception) when (budget?.LimitExceeded == true)
        {
            return Invalid("DOCUMENT_UNSUPPORTED_TYPE");
        }
        catch (Exception) when (budget?.DeadlineExceeded == true)
        {
            return Invalid("DOCUMENT_BUSY");
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

    private static bool HasUnsupportedActiveContent(PdfDocument document, int maxPages)
    {
        var catalog = document.Structure.Catalog.CatalogDictionary;
        var actionBudget = new PdfActionInspectionBudget();
        if (catalog.TryGet(NameToken.Create("OpenAction"), out var openAction)
            && IsUnsupportedOpenAction(document, openAction, actionBudget))
            return true;
        if (catalog.ContainsKey(NameToken.Create("AA"))
            || catalog.ContainsKey(NameToken.Create("Collection"))
            || catalog.ContainsKey(NameToken.Create("Perms"))
            || catalog.ContainsKey(NameToken.Create("AF")))
            return true;
        if (catalog.TryGet(NameToken.Create("NeedsRendering"), out var needsRendering)
            && ResolveToken(document, needsRendering) is BooleanToken { Data: true })
            return true;
        if (catalog.TryGet(NameToken.Create("AcroForm"), out var acroForm)
            && IsUnsupportedAcroForm(document, acroForm, actionBudget))
            return true;

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
        var hasUnsupportedContent = InspectPageTree(document, pagesToken, depth: 0, ref pageCount, ref nodesVisited, actionBudget, maxPages);
        if (pageCount != document.NumberOfPages)
            throw new InvalidDataException("PdfPig page count does not match the active page tree.");
        return hasUnsupportedContent;
    }

    private static bool InspectPageTree(PdfDocument document, IToken nodeToken, int depth, ref int pageCount,
        ref int nodesVisited, PdfActionInspectionBudget actionBudget, int maxPages)
    {
        if (depth >= DocumentCapturePdfLimits.MaxStackDepth || ++nodesVisited > 5000)
            throw new PdfDecodeLimitException("PDF active page tree exceeded its inspection bounds.");

        var node = ResolveDictionary(document, nodeToken);
        if (!node.TryGet(NameToken.Create("Type"), out var typeToken) || typeToken is not NameToken type)
            throw new InvalidDataException("PDF page-tree node has no valid type.");

        if (type.Data == "Page")
        {
            if (++pageCount > maxPages)
                return false;
            return InspectPageAnnotations(document, node, actionBudget);
        }

        if (type.Data != "Pages" || !node.TryGet(NameToken.Create("Kids"), out var kidsToken))
            throw new InvalidDataException("PDF page-tree node is malformed.");
        var kids = ResolveArray(document, kidsToken);
        if (kids.Length > 500)
            throw new PdfDecodeLimitException("PDF page-tree child array exceeded its inspection bound.");
        var hasUnsupportedContent = false;
        foreach (var child in kids.Data)
        {
            if (InspectPageTree(document, child, depth + 1, ref pageCount, ref nodesVisited, actionBudget, maxPages))
                hasUnsupportedContent = true;
        }
        return hasUnsupportedContent;
    }

    private static bool InspectPageAnnotations(PdfDocument document, DictionaryToken page, PdfActionInspectionBudget actionBudget)
    {
        if (page.TryGet(NameToken.Create("AA"), out var additionalActions))
        {
            var actions = ResolveDictionary(document, additionalActions);
            if (actions.Data.Count > 500)
                throw new PdfDecodeLimitException("PDF page action dictionary exceeded its inspection bound.");
            foreach (var action in actions.Data.Values)
            {
                if (IsUnsupportedAction(document, action, actionBudget))
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
                && subtype.Data is "Screen" or "RichMedia" or "FileAttachment" or "Movie" or "Sound" or "3D")
                return true;

            if (annotation.TryGet(NameToken.Create("A"), out var action)
                && IsUnsupportedAction(document, action, actionBudget))
                return true;

            if (annotation.TryGet(NameToken.Create("AA"), out var annotationActions))
            {
                var actions = ResolveDictionary(document, annotationActions);
                if (actions.Data.Count > 500)
                    throw new PdfDecodeLimitException("PDF annotation action dictionary exceeded its inspection bound.");
                foreach (var additionalAction in actions.Data.Values)
                {
                    if (IsUnsupportedAction(document, additionalAction, actionBudget))
                        return true;
                }
            }
        }

        return false;
    }

    private static bool IsUnsupportedOpenAction(PdfDocument document, IToken token, PdfActionInspectionBudget budget)
    {
        var resolved = ResolveToken(document, token);
        if (resolved is ArrayToken or NameToken or StringToken)
            return false;
        return IsUnsupportedAction(document, token, budget, goToOnly: true);
    }

    private static bool IsUnsupportedAction(PdfDocument document, IToken token, PdfActionInspectionBudget budget,
        bool goToOnly = false)
    {
        var pending = new Stack<(IToken Token, int Depth)>();
        var visitedReferences = new HashSet<IndirectReference>();
        pending.Push((token, 0));
        while (pending.TryPop(out var entry))
        {
            if (entry.Depth >= DocumentCapturePdfLimits.MaxStackDepth)
                throw new PdfDecodeLimitException("PDF action chain exceeded its depth bound.");
            if (entry.Token is IndirectReferenceToken reference && !visitedReferences.Add(reference.Data))
                throw new PdfDecodeLimitException("PDF action chain contains a cycle.");
            var resolved = ResolveToken(document, entry.Token);
            var action = resolved as DictionaryToken ?? throw new InvalidDataException("PDF action is not a dictionary.");
            if (!action.TryGet(NameToken.Create("S"), out var typeToken) || typeToken is not NameToken type)
                throw new InvalidDataException("PDF action has no valid type.");
            if (budget.Increment() > 256)
                throw new PdfDecodeLimitException("PDF document exceeded its action inspection bound.");
            if (goToOnly ? type.Data != "GoTo" : type.Data is not ("GoTo" or "URI"))
                return true;

            if (!action.TryGet(NameToken.Create("Next"), out var next))
                continue;
            var nextResolved = ResolveToken(document, next);
            if (nextResolved is ArrayToken nextArray)
            {
                if (nextArray.Length > 256)
                    throw new PdfDecodeLimitException("PDF action next-array exceeded its inspection bound.");
                for (var index = nextArray.Length - 1; index >= 0; index--)
                    pending.Push((nextArray.Data[index], entry.Depth + 1));
            }
            else
            {
                pending.Push((next, entry.Depth + 1));
            }
        }
        return false;
    }

    private static bool IsUnsupportedAcroForm(PdfDocument document, IToken token, PdfActionInspectionBudget actionBudget)
    {
        var form = ResolveDictionary(document, token);
        if (form.ContainsKey(NameToken.Create("XFA")))
            return true;
        if (!form.TryGet(NameToken.Create("Fields"), out var fieldsToken))
            return false;

        var fields = ResolveArray(document, fieldsToken);
        if (fields.Length > 500)
            throw new PdfDecodeLimitException("PDF form field array exceeded its inspection bound.");
        var pending = new Stack<(IToken Token, int Depth)>();
        var visitedReferences = new HashSet<IndirectReference>();
        foreach (var field in fields.Data)
            pending.Push((field, 0));
        var visitedNodes = 0;
        while (pending.TryPop(out var entry))
        {
            if (entry.Depth >= DocumentCapturePdfLimits.MaxStackDepth || ++visitedNodes > 5000)
                throw new PdfDecodeLimitException("PDF form field tree exceeded its inspection bounds.");
            if (entry.Token is IndirectReferenceToken reference && !visitedReferences.Add(reference.Data))
                throw new PdfDecodeLimitException("PDF form field tree contains a cycle.");
            var field = ResolveDictionary(document, entry.Token);
            if (field.TryGet(NameToken.Create("A"), out var action)
                && IsUnsupportedAction(document, action, actionBudget))
                return true;
            if (field.TryGet(NameToken.Create("AA"), out var additionalActions))
            {
                var actions = ResolveDictionary(document, additionalActions);
                if (actions.Data.Keys.Any(key => key is "K" or "F" or "V" or "C"))
                    return true;
                if (actions.Data.Count > 500)
                    throw new PdfDecodeLimitException("PDF field action dictionary exceeded its inspection bound.");
                foreach (var fieldAction in actions.Data.Values)
                    if (IsUnsupportedAction(document, fieldAction, actionBudget))
                        return true;
            }
            if (!field.TryGet(NameToken.Create("Kids"), out var kidsToken))
                continue;
            var kids = ResolveArray(document, kidsToken);
            if (kids.Length > 500)
                throw new PdfDecodeLimitException("PDF field child array exceeded its inspection bound.");
            foreach (var child in kids.Data)
                pending.Push((child, entry.Depth + 1));
        }
        return false;
    }

    private sealed class PdfActionInspectionBudget
    {
        private int _actions;
        public int Increment() => ++_actions;
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

internal sealed class PdfParseDeadlineException : Exception;

internal sealed class PdfDecodeBudget
{
    private readonly int _maxAggregate;
    private readonly int _maxPerStream;
    private readonly long _deadlineTimestamp;
    private readonly TimeProvider _clock;
    private readonly ConditionalWeakTable<DictionaryToken, StreamCounter> _streams = new();
    private long _aggregateBytes;
    private int _limitExceeded;
    private bool _deadlineExceeded;

    public PdfDecodeBudget(int maxAggregate, int maxPerStream, TimeSpan deadline, TimeProvider clock)
    {
        _maxAggregate = maxAggregate;
        _maxPerStream = maxPerStream;
        _clock = clock;
        _deadlineTimestamp = clock.GetTimestamp() + (long)(deadline.TotalSeconds * clock.TimestampFrequency);
    }

    public int MaxPerStream => _maxPerStream;

    public void ThrowIfExpired()
    {
        if (_clock.GetTimestamp() >= _deadlineTimestamp)
        {
            _deadlineExceeded = true;
            throw new PdfParseDeadlineException();
        }
    }

    // PdfPig can swallow exceptions thrown from a filter while searching for an xref.
    public bool DeadlineExceeded => _deadlineExceeded;
    public bool LimitExceeded => Volatile.Read(ref _limitExceeded) != 0;
    public void MarkLimitExceeded() => Volatile.Write(ref _limitExceeded, 1);

    public void Reserve(DictionaryToken stream, int count)
    {
        ThrowIfExpired();
        if (count < 0)
        {
            MarkLimitExceeded();
            throw new PdfDecodeLimitException("PDF decoder produced an invalid output size.");
        }

        var streamCounter = _streams.GetValue(stream, static _ => new StreamCounter());
        var streamTotal = Interlocked.Add(ref streamCounter.Bytes, count);
        var total = Interlocked.Add(ref _aggregateBytes, count);
        if (streamTotal > _maxPerStream || total > _maxAggregate)
        {
            MarkLimitExceeded();
            throw new PdfDecodeLimitException("PDF decoded byte limit exceeded.");
        }
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
        try
        {
            budget.ThrowIfExpired();
            var predictor = ReadPredictorParameters(streamDictionary, filterIndex);
            if (predictor.Predictor != 1 && !SupportsPredictors)
                throw new PdfDecodeLimitException("Predictors are supported only for Flate and LZW streams.");
            var output = new ArrayBufferWriter<byte>(Math.Min(input.Length, 64 * 1024));
            DecodeInto(input.Span, streamDictionary, output, filterIndex);
            return predictor.Predictor == 1
                ? output.WrittenMemory.ToArray()
                : DecodePredictor(output.WrittenSpan, streamDictionary, predictor);
        }
        catch (PdfDecodeLimitException)
        {
            budget.MarkLimitExceeded();
            throw;
        }
    }

    protected virtual bool SupportsPredictors => false;

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

    protected int MaxDecodedBytesPerStream => budget.MaxPerStream;

    private PdfPredictorParameters ReadPredictorParameters(DictionaryToken stream, int filterIndex)
    {
        var decodeParams = GetDecodeParameters(stream, filterIndex);
        if (decodeParams is null or NullToken)
            return PdfPredictorParameters.Default;
        if (decodeParams is not DictionaryToken dictionary)
            throw new PdfDecodeLimitException("Unsupported PDF decode parameters.");

        var predictor = ReadInteger(dictionary, "Predictor", 1);
        var colors = ReadInteger(dictionary, "Colors", 1);
        var bitsPerComponent = ReadInteger(dictionary, "BitsPerComponent", 8);
        var columns = ReadInteger(dictionary, "Columns", 1);
        if (predictor is not (1 or 2) && predictor is not (>= 10 and <= 15))
            throw new PdfDecodeLimitException("Unsupported PDF predictor.");
        if (colors is < 1 or > 4 || bitsPerComponent is not (1 or 2 or 4 or 8 or 16) || columns < 1)
            throw new PdfDecodeLimitException("Invalid PDF predictor parameters.");
        var rowBitCount = (long)colors * bitsPerComponent * columns;
        var rowBytesLong = (rowBitCount + 7) / 8;
        if (rowBytesLong is < 1 or > 65_536)
            throw new PdfDecodeLimitException("PDF predictor row exceeds its size bound.");
        var rowBytes = (int)rowBytesLong;
        var bytesPerPixel = Math.Max(1, (colors * bitsPerComponent + 7) / 8);
        return new PdfPredictorParameters(predictor, colors, bitsPerComponent, columns, rowBytes, bytesPerPixel);
    }

    private static int ReadInteger(DictionaryToken dictionary, string key, int defaultValue)
    {
        if (!dictionary.TryGet(NameToken.Create(key), out var token) || token is null or NullToken)
            return defaultValue;
        if (token is not NumericToken numeric || numeric.Data < int.MinValue || numeric.Data > int.MaxValue
            || numeric.Data != Math.Truncate(numeric.Data))
            throw new PdfDecodeLimitException($"PDF predictor parameter {key} is not an integer.");
        return (int)numeric.Data;
    }

    private Memory<byte> DecodePredictor(ReadOnlySpan<byte> encoded, DictionaryToken stream,
        PdfPredictorParameters parameters)
    {
        if (parameters.Predictor == 2)
        {
            if (encoded.Length % parameters.RowBytes != 0)
                throw new PdfDecodeLimitException("TIFF predictor data ends in a partial row.");
            budget.Reserve(stream, encoded.Length);
            var decoded = encoded.ToArray();
            var samplesPerRow = parameters.Columns * parameters.Colors;
            var sampleMask = (1 << parameters.BitsPerComponent) - 1;
            for (var rowOffset = 0; rowOffset < decoded.Length; rowOffset += parameters.RowBytes)
            {
                budget.ThrowIfExpired();
                var row = decoded.AsSpan(rowOffset, parameters.RowBytes);
                for (var sample = parameters.Colors; sample < samplesPerRow; sample++)
                {
                    var value = ReadPackedSample(row, sample, parameters.BitsPerComponent);
                    var left = ReadPackedSample(row, sample - parameters.Colors, parameters.BitsPerComponent);
                    WritePackedSample(row, sample, parameters.BitsPerComponent, (value + left) & sampleMask);
                }
            }
            return decoded;
        }

        var encodedRowBytes = parameters.RowBytes + 1;
        if (encoded.Length % encodedRowBytes != 0)
            throw new PdfDecodeLimitException("PNG predictor data ends in a partial row.");
        var rowCount = encoded.Length / encodedRowBytes;
        var outputLength = rowCount * parameters.RowBytes;
        if (outputLength > encoded.Length)
            throw new PdfDecodeLimitException("PNG predictor output exceeds its decoded input.");
        budget.Reserve(stream, outputLength);
        var result = new byte[outputLength];
        for (var rowIndex = 0; rowIndex < rowCount; rowIndex++)
        {
            budget.ThrowIfExpired();
            var inputOffset = rowIndex * encodedRowBytes;
            var filter = encoded[inputOffset];
            if (filter > 4)
                throw new PdfDecodeLimitException("PNG predictor row has an unknown filter type.");
            var source = encoded.Slice(inputOffset + 1, parameters.RowBytes);
            var row = result.AsSpan(rowIndex * parameters.RowBytes, parameters.RowBytes);
            var previousRow = rowIndex == 0 ? ReadOnlySpan<byte>.Empty
                : result.AsSpan((rowIndex - 1) * parameters.RowBytes, parameters.RowBytes);
            for (var index = 0; index < row.Length; index++)
            {
                var left = index >= parameters.BytesPerPixel ? row[index - parameters.BytesPerPixel] : 0;
                var above = previousRow.IsEmpty ? 0 : previousRow[index];
                var upperLeft = previousRow.IsEmpty || index < parameters.BytesPerPixel
                    ? 0 : previousRow[index - parameters.BytesPerPixel];
                var prediction = filter switch
                {
                    0 => 0,
                    1 => left,
                    2 => above,
                    3 => (left + above) / 2,
                    4 => Paeth(left, above, upperLeft),
                    _ => 0
                };
                row[index] = unchecked((byte)(source[index] + prediction));
            }
        }
        return result;
    }

    private static int Paeth(int left, int above, int upperLeft)
    {
        var estimate = left + above - upperLeft;
        var leftDistance = Math.Abs(estimate - left);
        var aboveDistance = Math.Abs(estimate - above);
        var upperLeftDistance = Math.Abs(estimate - upperLeft);
        return leftDistance <= aboveDistance && leftDistance <= upperLeftDistance ? left
            : aboveDistance <= upperLeftDistance ? above : upperLeft;
    }

    private static int ReadPackedSample(ReadOnlySpan<byte> row, int sample, int bitsPerComponent)
    {
        var bitOffset = sample * bitsPerComponent;
        if (bitsPerComponent == 16)
            return (row[bitOffset / 8] << 8) | row[bitOffset / 8 + 1];
        var shift = 8 - bitsPerComponent - (bitOffset & 7);
        return (row[bitOffset >> 3] >> shift) & ((1 << bitsPerComponent) - 1);
    }

    private static void WritePackedSample(Span<byte> row, int sample, int bitsPerComponent, int value)
    {
        var bitOffset = sample * bitsPerComponent;
        if (bitsPerComponent == 16)
        {
            row[bitOffset / 8] = (byte)(value >> 8);
            row[bitOffset / 8 + 1] = (byte)value;
            return;
        }
        var shift = 8 - bitsPerComponent - (bitOffset & 7);
        var mask = ((1 << bitsPerComponent) - 1) << shift;
        row[bitOffset >> 3] = (byte)((row[bitOffset >> 3] & ~mask) | (value << shift));
    }

    private readonly record struct PdfPredictorParameters(int Predictor, int Colors, int BitsPerComponent,
        int Columns, int RowBytes, int BytesPerPixel)
    {
        public static readonly PdfPredictorParameters Default = new(1, 1, 8, 1, 1, 1);
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
    protected override bool SupportsPredictors => true;

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

internal sealed class BoundedLzwFilter(PdfDecodeBudget decodeBudget) : BoundedPdfFilter(decodeBudget)
{
    protected override bool SupportsPredictors => true;

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
                if (tableBytes > MaxDecodedBytesPerStream)
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
