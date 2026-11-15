#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Core.Domain.Sales
{
    /// <summary>
    /// Parses, validates, renders, and parses back optional document number templates such as
    /// <c>INV-{FY}-{####}</c> or <c>INV-{#####}</c>, keeping plain continuous integers when unset.
    /// </summary>
    public sealed class DocumentNumberFormat
    {
        public const int MaxFormatLength = 40;
        public const int MaxSequenceDigits = 9;

        private const string WorstCaseFiscalYearToken = "2026-2027T";
        private const string WorstCaseSequenceValue = "999999999";

        private static readonly Regex AllowedLiteralChars = new(@"^[A-Za-z0-9\-_/.]+$", RegexOptions.CultureInvariant);

        public static DocumentNumberFormat Default { get; } = new(null, Array.Empty<Segment>(), 0, false);

        private enum SegmentKind { Literal, FiscalYear, Sequence }

        private sealed record Segment(SegmentKind Kind, string Value, int Width);

        private readonly IReadOnlyList<Segment> _segments;

        private DocumentNumberFormat(string? rawFormat, IReadOnlyList<Segment> segments, int sequenceWidth, bool usesFiscalYear)
        {
            RawFormat = rawFormat;
            _segments = segments;
            SequenceWidth = sequenceWidth;
            UsesFiscalYear = usesFiscalYear;
        }

        public string? RawFormat { get; }
        public bool IsDefault => RawFormat == null;
        public bool UsesFiscalYear { get; }
        public int SequenceWidth { get; }

        public static string? Normalize(string? format)
        {
            var trimmed = format?.Trim();
            return string.IsNullOrEmpty(trimmed) ? null : trimmed;
        }

        /// <summary>
        /// Renders a stored <c>FinancialYear.FiscalYearCode</c> for the <c>{FY}</c> token by stripping
        /// a leading <c>FY</c> prefix (e.g. <c>FY2026</c> -> <c>2026</c>, <c>FY2026-27</c> -> <c>2026-27</c>).
        /// </summary>
        public static string FormatFiscalYearToken(string? fiscalYearCode)
        {
            if (string.IsNullOrWhiteSpace(fiscalYearCode))
                return string.Empty;
            var trimmed = fiscalYearCode.Trim();
            return trimmed.StartsWith("FY", StringComparison.OrdinalIgnoreCase) && trimmed.Length > 2
                ? trimmed[2..]
                : trimmed;
        }

        public static DocumentNumberFormat Parse(string? format)
        {
            var normalized = Normalize(format);
            if (normalized == null)
                return Default;

            if (normalized.Length > MaxFormatLength)
                throw new InvoiceNumberException(InvoiceNumberError.Invalid,
                    $"Number format can be at most {MaxFormatLength} characters.");

            var segments = new List<Segment>();
            var fyCount = 0;
            var seqCount = 0;
            var seqWidth = 0;
            var i = 0;

            while (i < normalized.Length)
            {
                if (normalized[i] == '}')
                    throw new InvoiceNumberException(InvoiceNumberError.Invalid,
                        "Number format contains an unmatched '}'. Use {FY} and {#...#} tokens only.");

                if (normalized[i] == '{')
                {
                    var close = normalized.IndexOf('}', i + 1);
                    if (close < 0)
                        throw new InvoiceNumberException(InvoiceNumberError.Invalid,
                            "Number format contains an unclosed '{'. Use {FY} and {#...#} tokens only.");

                    var token = normalized[(i + 1)..close];
                    if (token.Contains('{'))
                        throw new InvoiceNumberException(InvoiceNumberError.Invalid,
                            "Number format contains an unclosed '{'. Use {FY} and {#...#} tokens only.");
                    if (token == "FY")
                    {
                        fyCount++;
                        if (fyCount > 1)
                            throw new InvoiceNumberException(InvoiceNumberError.Invalid,
                                "Number format can contain at most one {FY} token.");
                        segments.Add(new Segment(SegmentKind.FiscalYear, token, 0));
                    }
                    else if (token.Length > 0 && token.All(c => c == '#'))
                    {
                        if (token.Length > MaxSequenceDigits)
                            throw new InvoiceNumberException(InvoiceNumberError.Invalid,
                                $"Sequence token can have at most {MaxSequenceDigits} '#' characters.");
                        seqCount++;
                        if (seqCount > 1)
                            throw new InvoiceNumberException(InvoiceNumberError.Invalid,
                                "Number format must contain exactly one sequence token such as {####}.");
                        seqWidth = token.Length;
                        segments.Add(new Segment(SegmentKind.Sequence, token, token.Length));
                    }
                    else
                    {
                        throw new InvoiceNumberException(InvoiceNumberError.Invalid,
                            $"Unknown token '{{{token}}}'. Only {{FY}} and {{#...#}} are supported.");
                    }

                    i = close + 1;
                    continue;
                }

                var nextBrace = normalized.IndexOfAny(new[] { '{', '}' }, i);
                var literal = nextBrace < 0 ? normalized[i..] : normalized[i..nextBrace];
                if (!AllowedLiteralChars.IsMatch(literal))
                    throw new InvoiceNumberException(InvoiceNumberError.Invalid,
                        "Number format literal text can only use letters, digits, and - _ / .");
                segments.Add(new Segment(SegmentKind.Literal, literal, 0));
                i += literal.Length;
            }

            if (seqCount != 1)
                throw new InvoiceNumberException(InvoiceNumberError.Invalid,
                    "Number format must contain exactly one sequence token such as {####}.");

            var parsed = new DocumentNumberFormat(normalized, segments, seqWidth, fyCount == 1);

            // Validate worst-case rendered output (longest FY token + 9-digit sequence) against InvoiceNumber rules.
            var worstCase = parsed.RenderInternal(WorstCaseSequenceValue, WorstCaseFiscalYearToken);
            if (worstCase.Length > InvoiceNumber.MaxLength)
                throw new InvoiceNumberException(InvoiceNumberError.Invalid,
                    $"Number format can exceed {InvoiceNumber.MaxLength} characters when numbers grow. Shorten the literal text or padding.");
            InvoiceNumber.Validate(worstCase);

            return parsed;
        }

        public static bool TryParse(string? format, out DocumentNumberFormat? parsed, out string? error)
        {
            try
            {
                parsed = Parse(format);
                error = null;
                return true;
            }
            catch (InvoiceNumberException ex)
            {
                parsed = null;
                error = ex.Message;
                return false;
            }
        }

        public string Render(int sequenceValue, string? fiscalYearCode = null)
        {
            if (sequenceValue <= 0)
                throw new ArgumentOutOfRangeException(nameof(sequenceValue), "Sequence value must be positive.");

            if (IsDefault)
                return sequenceValue.ToString(CultureInfo.InvariantCulture);

            if (UsesFiscalYear && string.IsNullOrWhiteSpace(fiscalYearCode))
                throw new InvalidOperationException("A fiscal year code is required to render a format containing {FY}.");

            var digits = sequenceValue.ToString(CultureInfo.InvariantCulture);
            if (digits.Length < SequenceWidth)
                digits = digits.PadLeft(SequenceWidth, '0');

            return RenderInternal(digits, FormatFiscalYearToken(fiscalYearCode));
        }

        private string RenderInternal(string sequenceDigits, string fyToken)
        {
            var sb = new StringBuilder();
            foreach (var segment in _segments)
            {
                sb.Append(segment.Kind switch
                {
                    SegmentKind.Literal => segment.Value,
                    SegmentKind.FiscalYear => fyToken,
                    SegmentKind.Sequence => sequenceDigits,
                    _ => string.Empty
                });
            }
            return sb.ToString();
        }

        public bool TryParseSequence(string? number, string? fiscalYearCode, out int sequenceValue)
        {
            sequenceValue = 0;
            var normalized = InvoiceNumber.Normalize(number);
            if (normalized == null)
                return false;

            if (IsDefault)
            {
                var plain = InvoiceNumber.AsSequence(normalized);
                if (!plain.HasValue)
                    return false;
                sequenceValue = plain.Value;
                return true;
            }

            if (UsesFiscalYear && string.IsNullOrWhiteSpace(fiscalYearCode))
                return false;

            var fyToken = FormatFiscalYearToken(fiscalYearCode);
            var pattern = new StringBuilder("^");
            foreach (var segment in _segments)
            {
                switch (segment.Kind)
                {
                    case SegmentKind.Literal:
                        pattern.Append(Regex.Escape(segment.Value));
                        break;
                    case SegmentKind.FiscalYear:
                        pattern.Append(Regex.Escape(fyToken));
                        break;
                    case SegmentKind.Sequence:
                        pattern.Append("([0-9]+)");
                        break;
                }
            }
            pattern.Append('$');

            var match = Regex.Match(normalized, pattern.ToString(), RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
            if (!match.Success)
                return false;

            var digits = match.Groups[1].Value;
            if (digits.Length > MaxSequenceDigits)
                return false;

            return int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out sequenceValue) && sequenceValue > 0;
        }
    }
}
