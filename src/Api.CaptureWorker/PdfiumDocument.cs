// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using System.Runtime.InteropServices;
using System.Text;
using Api.DocumentCaptures.Parsing;
using SkiaSharp;

namespace AccountGo.CaptureWorker;

internal sealed class PdfiumDocument : IDisposable
{
    private const float PixelsPerPoint = 200f / 72f;
    private const int MaxPageEdge = 2048;
    private const int MaxCharactersPerPage = 100_000;
    private GCHandle _input;
    private IntPtr _document;

    public PdfiumDocument(byte[] input)
    {
        Native.FPDF_InitLibrary();
        try
        {
            _input = GCHandle.Alloc(input, GCHandleType.Pinned);
            _document = Native.FPDF_LoadMemDocument64(_input.AddrOfPinnedObject(), (nuint)input.Length, IntPtr.Zero);
            if (_document == IntPtr.Zero)
                throw new InvalidDataException();
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public int PageCount => Native.FPDF_GetPageCount(_document);

    public IReadOnlyList<TextLine> ReadText(int pageNumber, bool segments = false)
    {
        var page = LoadPage(pageNumber);
        try
        {
            var size = GetPageSize(page);
            var textPage = Native.FPDFText_LoadPage(page);
            if (textPage == IntPtr.Zero)
                throw new InvalidDataException();
            try
            {
                return GroupText(textPage, pageNumber, size, segments);
            }
            finally
            {
                Native.FPDFText_ClosePage(textPage);
            }
        }
        finally
        {
            Native.FPDF_ClosePage(page);
        }
    }

    private static List<TextLine> GroupText(IntPtr textPage, int pageNumber, PageSize size, bool segments)
    {
        var count = Native.FPDFText_CountChars(textPage);
        if (count is < 0 or > MaxCharactersPerPage)
            throw new InvalidDataException();

        var glyphs = new List<Glyph>(count);
        for (var index = 0; index < count; index++)
        {
            var codePoint = Native.FPDFText_GetUnicode(textPage, index);
            if (!Rune.TryCreate((int)codePoint, out var rune) || Rune.IsControl(rune)
                || Native.FPDFText_GetLooseCharBox(textPage, index, out var rect) == 0
                || !rect.IsFinite)
                continue;

            glyphs.Add(new Glyph(index, rune, rect, AngleBucket(Native.FPDFText_GetCharAngle(textPage, index))));
        }

        // Text drawn at another angle than the page's main text (a diagonal "PAID" stamp, a watermark) has loose boxes that
        // overlap several rows, so row grouping would splice its letters into amounts. Keep it as separate lines instead.
        var mainAngle = MainAngle(glyphs);
        var body = glyphs.Where(glyph => !IsRotated(glyph, mainAngle)).ToList();
        var rotated = glyphs.Where(glyph => IsRotated(glyph, mainAngle)).ToList();

        // Fallback fonts and combining clusters can shift the loose baseline within one row.
        // Keep the original PDFium character order inside each overlapping row.
        var rows = new List<List<Glyph>>();
        foreach (var glyph in body.OrderByDescending(glyph => glyph.Rect.Bottom))
        {
            if (rows.Count == 0 || !SameRow(rows[^1][0].Rect, glyph.Rect))
                rows.Add([]);
            rows[^1].Add(glyph);
        }
        var lines = new List<TextLine>(rows.Count);
        foreach (var row in rows)
        {
            var ordered = row.OrderBy(glyph => glyph.Index).ToList();
            if (segments)
            {
                // A piece's box comes from its visible characters: an indent's spaces can sit across a column gutter.
                foreach (var piece in SplitAtGaps(ordered).OrderBy(piece => Visible(piece).Min(glyph => glyph.Rect.Left)))
                    AddLine(lines, piece, pageNumber, size, rotated: false, Visible(piece));
            }
            else
                AddLine(lines, ordered, pageNumber, size, rotated: false);
        }

        // Each run of consecutive characters at one angle becomes one line, after the page's body text.
        var run = new List<Glyph>();
        foreach (var glyph in rotated.OrderBy(glyph => glyph.Index))
        {
            if (run.Count > 0 && (glyph.Angle != run[^1].Angle || glyph.Index - run[^1].Index > 2))
            {
                AddLine(lines, run, pageNumber, size, rotated: true);
                run = [];
            }
            run.Add(glyph);
        }
        if (run.Count > 0)
            AddLine(lines, run, pageNumber, size, rotated: true);
        return lines;
    }

    private static List<Glyph> Visible(List<Glyph> glyphs) => glyphs.Where(glyph => !Rune.IsWhiteSpace(glyph.Rune)).ToList();

    private static void AddLine(List<TextLine> lines, List<Glyph> glyphs, int pageNumber, PageSize size, bool rotated,
        List<Glyph>? boundsFrom = null)
    {
        var text = new StringBuilder();
        float previousRight = float.NegativeInfinity;
        var previousWasWhitespace = true;
        foreach (var glyph in glyphs)
        {
            var whitespace = Rune.IsWhiteSpace(glyph.Rune);
            if (!rotated && !previousWasWhitespace && !whitespace && glyph.Rect.Left - previousRight > 6)
                text.Append(' ');
            text.Append(glyph.Rune.ToString());
            previousRight = Math.Max(previousRight, glyph.Rect.Right);
            previousWasWhitespace = whitespace;
        }
        var value = text.ToString().Trim();
        if (value.Length == 0)
            return;
        var box = boundsFrom is { Count: > 0 } ? boundsFrom : glyphs;
        var minX = Math.Max(0, box.Min(glyph => glyph.Rect.Left));
        var maxX = Math.Max(minX, box.Max(glyph => glyph.Rect.Right));
        var minY = Math.Max(0, size.PointHeight - box.Max(glyph => glyph.Rect.Top));
        var maxY = Math.Max(minY, size.PointHeight - box.Min(glyph => glyph.Rect.Bottom));
        lines.Add(new TextLine(pageNumber, value, minX * size.Scale, minY * size.Scale,
            maxX * size.Scale, maxY * size.Scale, TextLineSource.PdfTextLayer, rotated));
    }

    /// <summary>
    /// Splits one row (in PDFium index order) where the next visible glyph jumps more than about 1.5 line heights
    /// forward, or back, from the previous one: a column gutter or a separately drawn text block.
    /// Word spaces in justified text stay well under that.
    /// </summary>
    private static List<List<Glyph>> SplitAtGaps(List<Glyph> row)
    {
        var heights = row.Select(glyph => glyph.Rect.Top - glyph.Rect.Bottom).Where(height => height > 0).Order().ToList();
        var lineHeight = heights.Count == 0 ? 10f : heights[heights.Count / 2];
        var threshold = Math.Max(12f, 1.5f * lineHeight);

        var pieces = new List<List<Glyph>> { new() };
        Glyph? previous = null;
        foreach (var glyph in row)
        {
            if (Rune.IsWhiteSpace(glyph.Rune))
            {
                pieces[^1].Add(glyph);
                continue;
            }
            if (previous is { } last
                && (glyph.Rect.Left - last.Rect.Right > threshold || last.Rect.Left - glyph.Rect.Right > threshold))
                pieces.Add([]);
            pieces[^1].Add(glyph);
            previous = glyph;
        }
        return pieces.Where(piece => piece.Any(glyph => !Rune.IsWhiteSpace(glyph.Rune))).ToList();
    }

    // Whole degrees; -1 when PDFium can't report an angle, which then counts as the page's main angle.
    private static int AngleBucket(float radians) =>
        radians >= 0 && float.IsFinite(radians) ? (int)MathF.Round(radians * 180f / MathF.PI) % 360 : -1;

    private static int MainAngle(List<Glyph> glyphs) =>
        glyphs.Where(glyph => glyph.Angle >= 0 && !Rune.IsWhiteSpace(glyph.Rune))
            .GroupBy(glyph => glyph.Angle)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key)
            .Select(group => group.Key)
            .DefaultIfEmpty(0)
            .First();

    private static bool IsRotated(Glyph glyph, int mainAngle)
    {
        if (glyph.Angle < 0)
            return false;
        var difference = Math.Abs(glyph.Angle - mainAngle) % 360;
        return Math.Min(difference, 360 - difference) > 2;
    }

    private static bool SameRow(PdfRect anchor, PdfRect candidate)
    {
        var shorterHeight = Math.Min(anchor.Top - anchor.Bottom, candidate.Top - candidate.Bottom);
        var overlap = Math.Min(anchor.Top, candidate.Top) - Math.Max(anchor.Bottom, candidate.Bottom);
        return Math.Abs(anchor.Bottom - candidate.Bottom) <= 1
            || (shorterHeight > 0 && overlap >= shorterHeight * 0.5f);
    }

    public SKBitmap Render(int pageNumber)
    {
        var page = LoadPage(pageNumber);
        try
        {
            var size = GetPageSize(page);
            var result = new SKBitmap(new SKImageInfo(size.Width, size.Height, SKColorType.Bgra8888, SKAlphaType.Opaque));
            try
            {
                result.Erase(SKColors.White);
                var bitmap = Native.FPDFBitmap_CreateEx(size.Width, size.Height, 4, result.GetPixels(), result.RowBytes);
                if (bitmap == IntPtr.Zero)
                    throw new InvalidOperationException();
                try
                {
                    Native.FPDF_RenderPageBitmap(bitmap, page, 0, 0, size.Width, size.Height, 0, 0);
                }
                finally
                {
                    Native.FPDFBitmap_Destroy(bitmap);
                }
                return result;
            }
            catch
            {
                result.Dispose();
                throw;
            }
        }
        finally
        {
            Native.FPDF_ClosePage(page);
        }
    }

    private IntPtr LoadPage(int pageNumber)
    {
        var page = Native.FPDF_LoadPage(_document, pageNumber - 1);
        return page != IntPtr.Zero ? page : throw new InvalidDataException();
    }

    private static PageSize GetPageSize(IntPtr page)
    {
        var width = Native.FPDF_GetPageWidthF(page);
        var height = Native.FPDF_GetPageHeightF(page);
        if (!(width > 0 && height > 0 && float.IsFinite(width) && float.IsFinite(height)))
            throw new InvalidDataException();
        var scale = Math.Min(PixelsPerPoint, MaxPageEdge / Math.Max(width, height));
        return new PageSize(Math.Clamp((int)MathF.Round(width * scale), 1, MaxPageEdge),
            Math.Clamp((int)MathF.Round(height * scale), 1, MaxPageEdge), height, scale);
    }

    public void Dispose()
    {
        if (!_input.IsAllocated)
            return;
        if (_document != IntPtr.Zero)
            Native.FPDF_CloseDocument(_document);
        _input.Free();
        Native.FPDF_DestroyLibrary();
    }

    private readonly record struct Glyph(int Index, Rune Rune, PdfRect Rect, int Angle);
    private readonly record struct PageSize(int Width, int Height, float PointHeight, float Scale);

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct PdfRect
    {
        public readonly float Left;
        public readonly float Top;
        public readonly float Right;
        public readonly float Bottom;
        public bool IsFinite => float.IsFinite(Left) && float.IsFinite(Top) && float.IsFinite(Right) && float.IsFinite(Bottom);
    }

    private static partial class Native
    {
        private const string Library = "pdfium";

        [DllImport(Library)] public static extern void FPDF_InitLibrary();
        [DllImport(Library)] public static extern void FPDF_DestroyLibrary();
        [DllImport(Library)] public static extern IntPtr FPDF_LoadMemDocument64(IntPtr data, nuint size, IntPtr password);
        [DllImport(Library)] public static extern void FPDF_CloseDocument(IntPtr document);
        [DllImport(Library)] public static extern int FPDF_GetPageCount(IntPtr document);
        [DllImport(Library)] public static extern IntPtr FPDF_LoadPage(IntPtr document, int index);
        [DllImport(Library)] public static extern void FPDF_ClosePage(IntPtr page);
        [DllImport(Library)] public static extern float FPDF_GetPageWidthF(IntPtr page);
        [DllImport(Library)] public static extern float FPDF_GetPageHeightF(IntPtr page);
        [DllImport(Library)] public static extern IntPtr FPDFText_LoadPage(IntPtr page);
        [DllImport(Library)] public static extern void FPDFText_ClosePage(IntPtr textPage);
        [DllImport(Library)] public static extern int FPDFText_CountChars(IntPtr textPage);
        [DllImport(Library)] public static extern uint FPDFText_GetUnicode(IntPtr textPage, int index);
        [DllImport(Library)] public static extern int FPDFText_GetLooseCharBox(IntPtr textPage, int index, out PdfRect rect);
        [DllImport(Library)] public static extern float FPDFText_GetCharAngle(IntPtr textPage, int index);
        [DllImport(Library)] public static extern IntPtr FPDFBitmap_CreateEx(int width, int height, int format, IntPtr firstScan, int stride);
        [DllImport(Library)] public static extern void FPDFBitmap_Destroy(IntPtr bitmap);
        [DllImport(Library)]
        public static extern void FPDF_RenderPageBitmap(IntPtr bitmap, IntPtr page, int left, int top, int width, int height, int rotation, int flags);
    }
}
