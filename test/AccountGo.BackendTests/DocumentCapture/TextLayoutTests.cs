// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using Api.DocumentCaptures.Parsing;
using Xunit;

namespace AccountGo.BackendTests.DocumentCapture;

/// <summary>Rule G-04: rows for the field rules, reading order for assistant text. Pieces are invented page boxes.</summary>
public sealed class TextLayoutTests
{
    private static TextLine P(string text, float x0, float y0, float x1, int page = 1, bool rotated = false) =>
        new(page, text, x0, y0, x1, y0 + 20, TextLineSource.PdfTextLayer, rotated);

    private static string[] Texts(IEnumerable<TextLine> lines) => lines.Select(line => line.Text).ToArray();

    // A sidebar (x 30-200) next to page content (x 340-1100), the same shape as the issue's screenshot.
    private static readonly TextLine[] SidebarPage =
    [
        P("Navigation", 30, 100, 150), P("API Guide", 340, 98, 560),
        P("Overview", 30, 150, 120), P("Send a request to create a note.", 340, 160, 700),
        P("Settings", 30, 200, 110), P("Every request needs an API key.", 340, 205, 720),
        P("Webhooks", 30, 250, 130),
        P("note: string", 340, 290, 480),
    ];

    [Fact]
    [Trait("Rule", "G-04")]
    public void SidebarAndContentAreReadOneColumnAtATime()
    {
        Assert.Equal(
            ["Navigation", "Overview", "Settings", "Webhooks", "API Guide", "Send a request to create a note.", "Every request needs an API key.", "note: string"],
            Texts(TextLayout.ReadingLines(SidebarPage)));
    }

    [Fact]
    [Trait("Rule", "G-04")]
    public void RowsJoinPiecesLeftToRightForTheFieldRules()
    {
        Assert.Equal(
            ["Navigation API Guide", "Overview Send a request to create a note.", "Settings Every request needs an API key.", "Webhooks", "note: string"],
            Texts(TextLayout.Rows(SidebarPage)));
    }

    [Fact]
    [Trait("Rule", "G-04")]
    public void TableRowsAndTheirHeaderStayRows()
    {
        TextLine[] page =
        [
            P("Lumen Paper Works", 60, 100, 240), P("Invoice Number LPW-2048", 760, 100, 980),
            P("Unit 7, 18 Quarry Lane", 60, 130, 250), P("Invoice Date March 4, 2026", 760, 130, 980),
            P("Riverton 40210", 60, 160, 190),
            P("Description", 60, 300, 170), P("Qty", 630, 300, 660), P("Unit Price", 870, 300, 960), P("Amount", 1140, 300, 1210),
            P("Letterpress cards", 60, 340, 210), P("3", 650, 340, 660), P("$20.00", 900, 340, 960), P("$60.00", 1150, 340, 1210),
            P("Envelope pack", 60, 375, 190), P("1", 650, 375, 660), P("$25.00", 900, 375, 960), P("$25.00", 1150, 375, 1210),
            P("Sub Total", 820, 430, 900), P("$85.00", 1150, 430, 1210),
            P("Tax", 820, 465, 850), P("$8.50", 1160, 465, 1210),
            P("Total", 820, 500, 860), P("$93.50", 1150, 500, 1210),
        ];

        Assert.Equal(
            [
                "Lumen Paper Works", "Unit 7, 18 Quarry Lane", "Riverton 40210",
                "Invoice Number LPW-2048", "Invoice Date March 4, 2026",
                "Description Qty Unit Price Amount",
                "Letterpress cards 3 $20.00 $60.00",
                "Envelope pack 1 $25.00 $25.00",
                "Sub Total $85.00", "Tax $8.50", "Total $93.50",
            ],
            Texts(TextLayout.ReadingLines(page)));
    }

    [Fact]
    [Trait("Rule", "G-04")]
    public void AnAlignedGridOfShortCellsIsReadByColumnThenSideBySide()
    {
        // Could be two short flows or a parameter table: columns first, then one marked line with the row pairings.
        TextLine[] page =
        [
            P("Name", 30, 100, 90), P("Type", 400, 100, 450),
            P("note", 30, 130, 80), P("string", 400, 130, 460),
            P("items", 30, 160, 90), P("array", 400, 160, 460),
        ];

        Assert.Equal(["Name", "note", "items", "Type", "string", "array"], Texts(TextLayout.ReadingLines(page)));
        Assert.Equal(["(aligned side by side: Name | Type; note | string; items | array)"], Texts(TextLayout.SideBySideLines(page)));
        Assert.Equal(["Name Type", "note string", "items array"], Texts(TextLayout.Rows(page)));
    }

    [Fact]
    [Trait("Rule", "G-04")]
    public void SideBySideLinesNeverSitBetweenTheReadingText()
    {
        // A grid, then a later paragraph and a second page: the reading text holds every piece once, in order.
        var pieces = new List<TextLine>();
        for (var row = 0; row < 5; row++)
            pieces.AddRange([P($"field_{row}", 30, 100 + row * 30, 120), P("string", 400, 100 + row * 30, 460)]);
        pieces.Add(P("A closing paragraph that runs across the whole page width.", 30, 300, 700));
        pieces.Add(P("Page two text", 30, 50, 200, page: 2));

        var read = TextLayout.Read(pieces);

        Assert.Equal("A closing paragraph that runs across the whole page width.", read.Lines[^2].Text);
        Assert.Equal("Page two text", read.Lines[^1].Text);
        Assert.Equal(pieces.Count, read.Lines.Count);
        Assert.DoesNotContain(read.Lines, line => line.Text.StartsWith("(aligned side by side:", StringComparison.Ordinal));
        Assert.Single(read.SideBySide);
    }

    [Fact]
    [Trait("Rule", "G-04")]
    public void AlignedProseColumnsInOneTypeSizeAreReadByColumnOnly()
    {
        TextLine[] page =
        [
            P("Requests are sent over HTTPS only.", 30, 100, 330), P("Responses use JSON with UTF-8 text.", 500, 100, 800),
            P("Each request carries an API key header.", 30, 130, 340), P("Errors include a code and a message.", 500, 130, 790),
            P("Keys can be rotated from the settings page.", 30, 160, 360), P("Retry failed calls after a short delay.", 500, 160, 780),
        ];

        Assert.Equal(
            [
                "Requests are sent over HTTPS only.", "Each request carries an API key header.", "Keys can be rotated from the settings page.",
                "Responses use JSON with UTF-8 text.", "Errors include a code and a message.", "Retry failed calls after a short delay.",
            ],
            Texts(TextLayout.ReadingLines(page)));
    }

    [Fact]
    [Trait("Rule", "G-04")]
    public void AlignedNavigationBesideLargerHeadingsIsReadAsColumns()
    {
        // The review's case: three rows that line up exactly, small navigation items beside larger headings.
        static TextLine H(string text, float x0, float y0, float x1, float height) =>
            new(1, text, x0, y0, x1, y0 + height, TextLineSource.PdfTextLayer);
        TextLine[] page =
        [
            H("Navigation", 30, 100, 150, 18), H("API Guide", 400, 92, 600, 34),
            H("Overview", 30, 160, 130, 18), H("Request body", 400, 152, 640, 34),
            H("Settings", 30, 220, 120, 18), H("Errors", 400, 212, 520, 34),
        ];

        Assert.Equal(["Navigation", "Overview", "Settings", "API Guide", "Request body", "Errors"],
            Texts(TextLayout.ReadingLines(page)));
    }

    [Fact]
    [Trait("Rule", "G-04")]
    public void TwoRowsAreNotEnoughForColumns()
    {
        TextLine[] page = [P("Left one", 30, 100, 120), P("Right one", 400, 100, 500), P("Left two", 30, 130, 120)];

        Assert.Equal(["Left one Right one", "Left two"], Texts(TextLayout.ReadingLines(page)));
    }

    [Fact]
    [Trait("Rule", "G-04")]
    public void AFullWidthHeadingEndsAColumnRegion()
    {
        TextLine[] page =
        [
            P("A1", 30, 100, 120), P("B1", 400, 100, 500),
            P("A2", 30, 130, 120),
            P("A3", 30, 160, 120), P("B3", 400, 160, 500),
            P("A full-width heading that crosses the gutter", 30, 200, 700),
            P("After", 30, 240, 100),
        ];

        Assert.Equal(["A1", "A2", "A3", "B1", "B3", "A full-width heading that crosses the gutter", "After"],
            Texts(TextLayout.ReadingLines(page)));
    }

    [Fact]
    [Trait("Rule", "G-04")]
    public void RotatedPiecesFollowTheirPageAndPagesStayInOrder()
    {
        TextLine[] pieces =
        [
            P("Sub Total $85.00", 800, 100, 1000), P("PAID", 850, 90, 950, rotated: true), P("Total $93.50", 800, 130, 1000),
            P("Page two", 30, 50, 200, page: 2),
        ];

        Assert.Equal(["Sub Total $85.00", "Total $93.50", "PAID", "Page two"], Texts(TextLayout.ReadingLines(pieces)));
        Assert.Equal(["Sub Total $85.00", "Total $93.50", "PAID", "Page two"], Texts(TextLayout.Rows(pieces)));
    }

    [Fact]
    [Trait("Rule", "G-04")]
    public void NoTextIsDropped()
    {
        var all = SidebarPage.Select(piece => piece.Text).Order().ToArray();
        Assert.Equal(all, TextLayout.ReadingLines(SidebarPage).Select(line => line.Text).Order().ToArray());
    }
}
