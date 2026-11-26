// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using System.Text.Json;
using Api.DocumentCaptures;
using Microsoft.Extensions.Options;
using Xunit;
using Xunit.Abstractions;

namespace AccountGo.BackendTests.DocumentCapture;

/// <summary>
/// Rule G-04 through the real capture worker, on invented fixtures in <c>Fixtures/DocumentCapture/Layout</c>
/// (regenerate with <c>gen_layout.py</c>). Opt-in: set <c>ACCOUNTGO_TEST_OCR_WORKER</c>.
/// </summary>
[Collection(DocumentCaptureResourceCollection.Name)]
[Trait("Category", "LocalOcr")]
public sealed class LocalOcrLayoutTests(ITestOutputHelper output)
{
    private static readonly string[] Sidebar = ["Navigation", "Overview", "Authentication", "Settings", "Webhooks", "Errors"];

    private static readonly string[] Content =
    [
        "API Guide",
        "Send a request to create a note.",
        "Every request needs an API key in the header.",
        "Request body",
        "{",
        "\"note\": \"string\",",
        "\"items\": [1, 2]",
        "}",
        "Response schema",
        "total: integer",
        "tax_rate: number",
        "invoice_id: string",
        "Errors return a JSON object with a message.",
    ];

    [LocalOcrTheory]
    [Trait("Rule", "G-04")]
    [InlineData("api-guide.png")]
    [InlineData("api-guide.pdf")]
    public async Task ApiGuideIsReadSidebarFirstThenContent(string file)
    {
        var result = await ExtractAsync(file, "assistant-attachment");
        var lines = result.RawText!.Split('\n');
        output.WriteLine(result.RawText);

        var sidebarStart = Array.IndexOf(lines, Sidebar[0]);
        Assert.True(sidebarStart >= 0, "Sidebar heading not found.");
        Assert.Equal(Sidebar, lines.Skip(sidebarStart).Take(Sidebar.Length).ToArray());
        var contentStart = Array.IndexOf(lines, Content[0]);
        Assert.True(contentStart > sidebarStart);
        Assert.Equal(Content, lines.Skip(contentStart).Take(Content.Length).ToArray());

        // No financial evidence: no fields at all, so nothing is labelled as a bill.
        Assert.Equal("{\"fields\":[]}", result.FieldsJson);
    }

    [LocalOcrTheory]
    [Trait("Rule", "G-04")]
    [InlineData("two-column-invoice.png")]
    [InlineData("two-column-invoice.pdf")]
    public async Task InvoiceKeepsTableRowsAndTotalsAndSeparatesTheStamp(string file)
    {
        foreach (var kind in new[] { "purchase-invoice", "assistant-attachment" })
        {
            var result = await ExtractAsync(file, kind);
            var lines = result.RawText!.Split('\n');
            output.WriteLine($"{kind}:\n{result.RawText}");

            Assert.Contains("Letterpress cards 3 $20.00 $60.00", lines);
            Assert.Contains("Envelope pack 1 $25.00 $25.00", lines);
            Assert.Contains("Sub Total $85.00", lines);
            Assert.Contains("Tax $8.50", lines);
            Assert.Contains("Total $93.50", lines);
            Assert.Equal("PAID", lines[^1]);
        }

        var invoice = await ExtractAsync(file, "purchase-invoice");
        using var fields = JsonDocument.Parse(invoice.FieldsJson);
        var totals = fields.RootElement.GetProperty("sourceTotals");
        Assert.Equal("85.00", totals.GetProperty("subtotal").GetString());
        Assert.Equal("8.50", totals.GetProperty("taxTotal").GetString());
        Assert.Equal("93.50", totals.GetProperty("grandTotal").GetString());
        Assert.Equal(2, fields.RootElement.GetProperty("lines").GetArrayLength());
        Assert.Equal("LPW-2048", fields.RootElement.GetProperty("vendorInvoiceNo").GetString());
    }

    [LocalOcrTheory]
    [Trait("Rule", "G-04")]
    [InlineData("two-column-invoice.png")]
    [InlineData("two-column-invoice.pdf")]
    public async Task AssistantTextReadsTheAddressColumnBeforeTheInvoiceDetails(string file)
    {
        var lines = (await ExtractAsync(file, "assistant-attachment")).RawText!.Split('\n');

        string[] address = ["Lumen Paper Works", "Unit 7, 18 Quarry Lane", "Riverton 40210"];
        var start = Array.IndexOf(lines, address[0]);
        Assert.True(start >= 0);
        Assert.Equal(address, lines.Skip(start).Take(address.Length).ToArray());
        Assert.True(Array.IndexOf(lines, "Invoice Number LPW-2048") > start + address.Length);
    }

    [LocalOcrTheory]
    [Trait("Rule", "G-04")]
    [InlineData("aligned-flows.png")]
    [InlineData("aligned-flows.pdf")]
    public async Task AlignedNavigationBesideHeadingsIsReadAsColumns(string file)
    {
        var lines = (await ExtractAsync(file, "assistant-attachment")).RawText!.Split('\n');
        output.WriteLine(string.Join('\n', lines));

        Assert.Equal(["Navigation", "Overview", "Settings", "API Guide", "Request body", "Errors"], lines);
    }

    [LocalOcrTheory]
    [Trait("Rule", "G-04")]
    [InlineData("aligned-grid.png")]
    [InlineData("aligned-grid.pdf")]
    public async Task AlignedShortGridInOneTypeSizeIsReadByColumnThenSideBySide(string file)
    {
        // The review's three-row reproduction in one type size: column order first, pairings on one marked line.
        var lines = (await ExtractAsync(file, "assistant-attachment")).RawText!.Split('\n');
        output.WriteLine(string.Join('\n', lines));

        Assert.Equal(
            [
                "Navigation", "Overview", "Settings", "API Guide", "Request body", "Errors",
                "(aligned side by side: Navigation | API Guide; Overview | Request body; Settings | Errors)",
            ],
            lines);
    }

    [LocalOcrTheory]
    [Trait("Rule", "G-04")]
    [InlineData("aligned-prose.png")]
    [InlineData("aligned-prose.pdf")]
    public async Task AlignedProseColumnsInOneTypeSizeAreReadByColumn(string file)
    {
        var lines = (await ExtractAsync(file, "assistant-attachment")).RawText!.Split('\n');
        output.WriteLine(string.Join('\n', lines));

        Assert.Equal(
            [
                "Requests are sent over HTTPS only.", "Each request carries an API key header.", "Keys can be rotated from the settings page.",
                "Responses use JSON with UTF-8 text.", "Errors include a code and a message.", "Retry failed calls after a short delay.",
            ],
            lines);
    }

    [LocalOcrTheory]
    [Trait("Rule", "G-04")]
    [InlineData("parameter-table.png")]
    [InlineData("parameter-table.pdf")]
    public async Task ParameterTableKeepsEachRowTogetherOnTheSideBySideLine(string file)
    {
        var result = await ExtractAsync(file, "assistant-attachment");
        var lines = result.RawText!.Split('\n');
        output.WriteLine(result.RawText);

        Assert.Equal(["Name", "note", "items", "pinned"], lines[..4]);
        Assert.Equal(
            "(aligned side by side: Name | Type | Description; note | string | Text of the note.; "
                + "items | array | Item ids to attach.; pinned | boolean | Keep it at the top.)",
            lines[^1]);
        Assert.Equal("{\"fields\":[]}", result.FieldsJson);
    }

    [LocalOcrTheory]
    [Trait("Rule", "A-02")]
    [InlineData("bank-statement.png")]
    [InlineData("bank-statement.pdf")]
    public async Task StatementWithATotalHasABankFieldThatSurvivesMasking(string file)
    {
        var result = await ExtractAsync(file, "assistant-attachment");
        using var fields = JsonDocument.Parse(result.FieldsJson);
        var labels = fields.RootElement.GetProperty("fields").EnumerateArray().Select(f => f.GetProperty("label").GetString()).ToList();
        output.WriteLine(result.FieldsJson);

        Assert.Contains(labels, label => label is not null && Api.DocumentCaptures.Parsing.AssistantFinancialEvidence.IsBankLabel(label));
    }

    private static async Task<DocumentExtractionResult> ExtractAsync(string file, string kind)
    {
        var options = new DocumentCaptureOptions { Extractor = "local-ocr" };
        options.OcrWorker.WorkerPath = Environment.GetEnvironmentVariable("ACCOUNTGO_TEST_OCR_WORKER");
        options.OcrWorker.ValidateFiles();
        using var extractor = new LocalOcrDocumentFieldExtractor(Options.Create(options));

        var bytes = await File.ReadAllBytesAsync(Path.Combine(FixtureDirectory, file));
        var contentType = "application/pdf";
        if (file.EndsWith(".png", StringComparison.Ordinal))
        {
            var normalized = await DocumentCaptureImageNormalizer.NormalizeAsync(bytes, "image/png");
            (bytes, contentType) = (normalized.Bytes, normalized.ContentType);
        }
        return await extractor.ExtractAsync(new DocumentExtractionRequest(kind, 1, contentType, bytes, 1), CancellationToken.None);
    }

    internal static string FixtureDirectory => Path.Combine(
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../")),
        "test", "AccountGo.BackendTests", "Fixtures", "DocumentCapture", "Layout");

    internal sealed class LocalOcrTheoryAttribute : TheoryAttribute
    {
        public LocalOcrTheoryAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ACCOUNTGO_TEST_OCR_WORKER")))
                Skip = "Set ACCOUNTGO_TEST_OCR_WORKER to the built local OCR worker.";
        }
    }
}
