// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using System.Text.RegularExpressions;

namespace Api.DocumentCaptures.Parsing;

/// <summary>
/// Builds two views of one document from the capture worker's text pieces (one <see cref="TextLine"/> per piece of a row,
/// see <c>CaptureWorkerHeader.Segments</c>):
/// <list type="bullet">
/// <item><see cref="Rows"/>: pieces joined row by row, left to right. Field rules read these, so a table row keeps its
/// description, quantity and amount on one line.</item>
/// <item><see cref="ReadingLines"/>: the same text in reading order. Side-by-side columns (a navigation sidebar next to
/// page content, an address block next to invoice details) are read one column at a time.</item>
/// </list>
/// Rotated pieces (stamps, watermarks) are never part of a row or column; they follow their page's body text.
/// </summary>
public static class TextLayout
{
    // A column region needs at least this many rows that share one empty vertical band.
    internal const int MinColumnRows = 3;

    public static IReadOnlyList<TextLine> Rows(IReadOnlyList<TextLine> pieces)
    {
        var result = new List<TextLine>(pieces.Count);
        foreach (var page in Pages(pieces))
        {
            foreach (var row in GroupRows(page.Body))
                result.Add(Join(row));
            result.AddRange(page.Rotated);
        }
        return result;
    }

    public static IReadOnlyList<TextLine> ReadingLines(IReadOnlyList<TextLine> pieces)
    {
        var result = new List<TextLine>(pieces.Count);
        foreach (var page in Pages(pieces))
        {
            result.AddRange(OrderPage(GroupRows(page.Body)));
            result.AddRange(page.Rotated);
        }
        return result;
    }

    private static IEnumerable<(List<TextLine> Body, List<TextLine> Rotated)> Pages(IReadOnlyList<TextLine> pieces) =>
        pieces.GroupBy(piece => piece.Page)
            .OrderBy(group => group.Key)
            .Select(group => (group.Where(piece => !piece.Rotated).ToList(), group.Where(piece => piece.Rotated).ToList()));

    /// <summary>Same row test as the OCR worker: vertical centres within half a line height.</summary>
    internal static List<List<TextLine>> GroupRows(List<TextLine> pieces)
    {
        var rows = new List<List<TextLine>>();
        var row = new List<TextLine>();
        var rowY = 0f;
        var rowHeight = 0f;
        foreach (var piece in pieces.OrderBy(CenterY).ThenBy(piece => piece.X0))
        {
            var center = CenterY(piece);
            var height = Math.Max(1, piece.Y1 - piece.Y0);
            if (row.Count > 0 && Math.Abs(center - rowY) > 0.5f * Math.Max(height, rowHeight))
            {
                rows.Add(row);
                row = [];
            }
            if (row.Count == 0)
                (rowY, rowHeight) = (center, height);
            row.Add(piece);
        }
        if (row.Count > 0)
            rows.Add(row);
        foreach (var r in rows)
            r.Sort((a, b) => a.X0.CompareTo(b.X0));
        return rows;
    }

    private static IEnumerable<TextLine> OrderPage(List<List<TextLine>> rows)
    {
        if (rows.Count == 0)
            yield break;
        var all = rows.SelectMany(row => row).ToList();
        var left = all.Min(piece => piece.X0);
        var right = all.Max(piece => piece.X1);
        var minGutter = 1.5f * Median(all.Select(piece => piece.Y1 - piece.Y0));

        var index = 0;
        while (index < rows.Count)
        {
            var region = FindColumnRegion(rows, index, left, right, minGutter);
            if (region is null)
            {
                yield return Join(rows[index]);
                index++;
                continue;
            }

            foreach (var column in region.Value.Columns)
                foreach (var row in column)
                    yield return Join(row);
            index = region.Value.End;
        }
    }

    /// <summary>
    /// Looks for side-by-side columns starting at <paramref name="start"/>: consecutive rows that leave one vertical band
    /// of at least <paramref name="minGutter"/> empty. Rejected when fewer than <see cref="MinColumnRows"/> rows share the
    /// band, when the band has no text on one side, when every row fills every column (an aligned grid such as a table
    /// or a label/value list keeps its rows), or when a column is mostly numbers. A row with two or more numeric
    /// pieces (a table row), the header row directly above one, or a row that crosses the band (a full-width heading) ends
    /// the region.
    /// </summary>
    private static (int End, List<List<List<TextLine>>> Columns)? FindColumnRegion(
        List<List<TextLine>> rows, int start, float left, float right, float minGutter)
    {
        if (IsTablePart(rows, start))
            return null;

        var bands = Wide(FreeBands(rows[start], left, right), minGutter);
        var end = start + 1;
        while (end < rows.Count && !IsTablePart(rows, end))
        {
            var narrowed = Wide(Intersect(bands, FreeBands(rows[end], left, right)), minGutter);
            if (narrowed.Count == 0)
                break;
            bands = narrowed;
            end++;
        }

        var regionRows = rows.GetRange(start, end - start);
        // Keep only bands with text on both sides inside this region.
        var gutters = bands
            .Where(band => regionRows.Any(row => row.Any(piece => piece.X1 <= band.From))
                && regionRows.Any(row => row.Any(piece => piece.X0 >= band.To)))
            .OrderBy(band => band.From)
            .ToList();
        if (gutters.Count == 0 || regionRows.Count < MinColumnRows)
            return null;

        var columns = new List<List<List<TextLine>>>();
        for (var column = 0; column <= gutters.Count; column++)
            columns.Add([]);
        var filledEverywhere = 0;
        foreach (var row in regionRows)
        {
            var cells = new List<TextLine>[gutters.Count + 1];
            for (var i = 0; i < cells.Length; i++)
                cells[i] = [];
            foreach (var piece in row)
                cells[ColumnOf(piece, gutters)].Add(piece);
            for (var i = 0; i < cells.Length; i++)
                if (cells[i].Count > 0)
                    columns[i].Add(cells[i]);
            if (cells.All(cell => cell.Count > 0))
                filledEverywhere++;
        }

        if (filledEverywhere == regionRows.Count)
            return null;
        foreach (var column in columns)
        {
            var pieces = column.SelectMany(row => row).ToList();
            if (pieces.Count(IsNumeric) * 2 > pieces.Count)
                return null;
        }

        return (end, columns);
    }

    private static int ColumnOf(TextLine piece, List<Band> gutters)
    {
        var column = 0;
        while (column < gutters.Count && piece.X0 >= gutters[column].To)
            column++;
        return column;
    }

    private static List<Band> FreeBands(List<TextLine> row, float left, float right)
    {
        var bands = new List<Band>();
        var cursor = left;
        foreach (var piece in row)
        {
            if (piece.X0 > cursor)
                bands.Add(new Band(cursor, piece.X0));
            cursor = Math.Max(cursor, piece.X1);
        }
        if (right > cursor)
            bands.Add(new Band(cursor, right));
        return bands;
    }

    private static List<Band> Intersect(List<Band> a, List<Band> b)
    {
        var result = new List<Band>();
        foreach (var x in a)
            foreach (var y in b)
            {
                var from = Math.Max(x.From, y.From);
                var to = Math.Min(x.To, y.To);
                if (to > from)
                    result.Add(new Band(from, to));
            }
        return result;
    }

    private static List<Band> Wide(List<Band> bands, float minWidth) =>
        bands.Where(band => band.To - band.From >= minWidth).ToList();

    private static bool IsTableRow(List<TextLine> row) => row.Sum(NumericCells) >= 2;

    // A table row, or the header row directly above one.
    private static bool IsTablePart(List<List<TextLine>> rows, int index) =>
        IsTableRow(rows[index]) || (index + 1 < rows.Count && IsTableRow(rows[index + 1]));

    private static bool IsNumeric(TextLine piece) => NumericCells(piece) > 0;

    // Number of amounts in a piece made only of amounts and currency codes ("3 $4.99 $14.97" has three, "EUR 40" one);
    // 0 when any word is not one.
    private static int NumericCells(TextLine piece)
    {
        var words = piece.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return words.All(word => NumericWord.IsMatch(word)) ? words.Count(word => word.Any(char.IsAsciiDigit)) : 0;
    }

    private static TextLine Join(List<TextLine> row) =>
        row.Count == 1
            ? row[0]
            : new TextLine(row[0].Page, string.Join(" ", row.Select(piece => piece.Text)),
                row.Min(piece => piece.X0), row.Min(piece => piece.Y0), row.Max(piece => piece.X1),
                row.Max(piece => piece.Y1), row[0].Source);

    private static float CenterY(TextLine line) => (line.Y0 + line.Y1) / 2;

    private static float Median(IEnumerable<float> values)
    {
        var sorted = values.Where(float.IsFinite).Order().ToList();
        return sorted.Count == 0 ? 10f : Math.Max(1f, sorted[sorted.Count / 2]);
    }

    private readonly record struct Band(float From, float To);

    // Amounts, quantities and percentages, optionally with a currency symbol or code: "$1,250.00", "3", "12.5%", "S$4.20",
    // "RM12.00", "EUR", "(40.00)". A letter-and-digit code such as "A1" or "Q3" is not a number.
    private static readonly Regex NumericWord = new(@"^(?:[A-Z]{3}|[-+(]?(?:(?:[A-Z]{1,2})?[$€£¥฿]|RM|Rp)?\d[\d.,]*%?\)?)$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
}
