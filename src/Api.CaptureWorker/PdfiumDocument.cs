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

    public IReadOnlyList<TextLine> ReadText(int pageNumber)
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
                return GroupText(textPage, pageNumber, size);
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

    private static List<TextLine> GroupText(IntPtr textPage, int pageNumber, PageSize size)
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

            glyphs.Add(new Glyph(index, rune, rect));
        }

        // Fallback fonts and combining clusters can shift the loose baseline within one row.
        // Keep the original PDFium character order inside each overlapping row.
        var rows = new List<List<Glyph>>();
        foreach (var glyph in glyphs.OrderByDescending(glyph => glyph.Rect.Bottom))
        {
            if (rows.Count == 0 || !SameRow(rows[^1][0].Rect, glyph.Rect))
                rows.Add([]);
            rows[^1].Add(glyph);
        }
        var lines = new List<TextLine>(rows.Count);
        foreach (var row in rows)
        {
            var text = new StringBuilder();
            float previousRight = float.NegativeInfinity;
            var previousWasWhitespace = true;
            foreach (var glyph in row.OrderBy(glyph => glyph.Index))
            {
                var whitespace = Rune.IsWhiteSpace(glyph.Rune);
                if (!previousWasWhitespace && !whitespace && glyph.Rect.Left - previousRight > 6)
                    text.Append(' ');
                text.Append(glyph.Rune.ToString());
                previousRight = Math.Max(previousRight, glyph.Rect.Right);
                previousWasWhitespace = whitespace;
            }
            var value = text.ToString().Trim();
            if (value.Length == 0)
                continue;
            var minX = Math.Max(0, row.Min(glyph => glyph.Rect.Left));
            var maxX = Math.Max(minX, row.Max(glyph => glyph.Rect.Right));
            var minY = Math.Max(0, size.PointHeight - row.Max(glyph => glyph.Rect.Top));
            var maxY = Math.Max(minY, size.PointHeight - row.Min(glyph => glyph.Rect.Bottom));
            lines.Add(new TextLine(pageNumber, value, minX * size.Scale, minY * size.Scale,
                maxX * size.Scale, maxY * size.Scale, TextLineSource.PdfTextLayer));
        }
        return lines;
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

    private readonly record struct Glyph(int Index, Rune Rune, PdfRect Rect);
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
        [DllImport(Library)] public static extern IntPtr FPDFBitmap_CreateEx(int width, int height, int format, IntPtr firstScan, int stride);
        [DllImport(Library)] public static extern void FPDFBitmap_Destroy(IntPtr bitmap);
        [DllImport(Library)]
        public static extern void FPDF_RenderPageBitmap(IntPtr bitmap, IntPtr page, int left, int top, int width, int height, int rotation, int flags);
    }
}
