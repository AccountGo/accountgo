// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System.Text.Json;
using Api.DocumentCaptures;
using Api.DocumentCaptures.Mapping;
using Api.DocumentCaptures.Parsing;
using Xunit;

namespace AccountGo.BackendTests.DocumentCapture;

public sealed class CaptureFieldMapperTests
{
    // An invented supplier bill. Every value below is made up.
    private static readonly string[] SourceLines =
    [
        "Northstar Supplies Ltd",
        "TAX INVOICE",
        "Invoice No: NS-20457",
        "Date: 12 Nov 2026",
        "Paper A4 244.48",
        "Toner 162.99",
        "Subtotal 407.47",
        "Tax 8.25% 33.62",
        "Total 441.09",
        "Also quoted: 450.00",
    ];

    private const string RuleFields =
        """{"currency":"USD","date":"2026-11-12","lines":[{"description":"Paper A4","quantity":"1","unitAmount":"244.48"},{"description":"Toner","quantity":"1","unitAmount":"162.99"}],"sourceTotals":{"grandTotal":"441.09","priceBasis":"exclusive","subtotal":"407.47","taxTotal":"33.62"},"vendorInvoiceNo":"NS-20457","vendorName":"Northstar Supplies Ltd"}""";

    private static CaptureMappingGuardResult Guard(string mapped, string rule = RuleFields, string kind = "purchase-invoice", string[]? lines = null) =>
        CaptureMappingGuards.Apply(kind, rule, mapped, lines ?? SourceLines);

    private static string Merge(string baseJson, Action<System.Text.Json.Nodes.JsonObject> change)
    {
        var node = (System.Text.Json.Nodes.JsonObject)System.Text.Json.Nodes.JsonNode.Parse(baseJson)!;
        change(node);
        return node.ToJsonString();
    }

    // ---- request factory ---------------------------------------------------------------------------------

    [Theory]
    [InlineData("bank")]
    [InlineData("assistant-attachment")]
    public void Factory_RefusesKindsThatMustNeverBeMapped(string kind)
    {
        var ex = Assert.Throws<CaptureMappingRefusedException>(() =>
            CaptureMappingRequestFactory.Create(kind, 1, "{}", [new TextLine(1, "x", 0, 0, 1, 1, TextLineSource.Ocr)], "rules-3"));
        Assert.Equal(CaptureMappingRequestFactory.RefusedKind, ex.Reason);
    }

    [Theory]
    [InlineData(CaptureMappingInputMode.TextPlusImageOnWeakOcr)]
    [InlineData(CaptureMappingInputMode.TextPlusImage)]
    public void Factory_RefusesEveryInputModeButText(CaptureMappingInputMode mode)
    {
        var ex = Assert.Throws<CaptureMappingRefusedException>(() =>
            CaptureMappingRequestFactory.Create("purchase-invoice", 1, "{}", [new TextLine(1, "x", 0, 0, 1, 1, TextLineSource.Ocr)], "rules-3", mode));
        Assert.Equal(CaptureMappingRequestFactory.RefusedInputMode, ex.Reason);
    }

    [Fact]
    public void Factory_MasksAccountLikeNumbersAndKeepsEverythingElse()
    {
        // Invented IBAN and account numbers.
        var lines = new[]
        {
            new TextLine(1, "Pay to GB82 WEST 1234 5698 7654 32 today", 1, 2, 3, 4, TextLineSource.Ocr),
            new TextLine(1, "Account 123456789012 ref NS-20457", 5, 6, 7, 8, TextLineSource.Ocr),
            new TextLine(1, "Date 2026-11-12  Total 441.09", 9, 10, 11, 12, TextLineSource.PdfTextLayer),
            new TextLine(1, "   ", 0, 0, 1, 1, TextLineSource.Ocr),
        };

        var request = CaptureMappingRequestFactory.Create("purchase-invoice", 1, RuleFields, lines, "rules-3");

        Assert.Equal(CaptureMappingInputMode.Text, request.InputMode);
        Assert.Equal(3, request.Lines.Count);
        var all = string.Join("\n", request.Lines.Select(l => l.Text));
        Assert.DoesNotContain("GB82", all, StringComparison.Ordinal);
        Assert.DoesNotContain("7654 32", all, StringComparison.Ordinal);
        Assert.DoesNotContain("123456789012", all, StringComparison.Ordinal);
        Assert.Contains("NS-20457", all, StringComparison.Ordinal);
        Assert.Contains("2026-11-12", all, StringComparison.Ordinal);
        Assert.Contains("441.09", all, StringComparison.Ordinal);
        Assert.Equal((5f, 6f, 7f, 8f), (request.Lines[1].X0, request.Lines[1].Y0, request.Lines[1].X1, request.Lines[1].Y1));
    }

    [Fact]
    public void Request_CarriesOnlyTheDocumentAndRuleFields()
    {
        // The seam has no place for images or the workspace's parties.
        var names = typeof(CaptureMappingRequest).GetProperties().Select(p => p.Name).OrderBy(n => n).ToArray();
        Assert.Equal(["InputMode", "Kind", "Lines", "RuleFieldsJson", "RulesVersion", "SchemaVersion"], names);
    }

    // ---- default mapper and wiring -------------------------------------------------------------------------

    [Fact]
    [Trait("Rule", "AI-05")]
    public async Task RuleOnlyMapper_ReturnsTheRuleFieldsByteForByteWithNoWarnings()
    {
        var outcome = await CaptureFieldMappingRunner.RunAsync(new RuleOnlyFieldMapper(), "purchase-invoice", 1, RuleFields,
            SourceLines.Select(t => new TextLine(1, t, 0, 0, 1, 1, TextLineSource.Ocr)).ToArray(), "rules-3", CancellationToken.None);

        Assert.Equal(RuleFields, outcome.FieldsJson);
        Assert.Empty(outcome.Warnings);
        Assert.Empty(outcome.RuleValueHints);
        Assert.Equal("rules", outcome.MapperName);
        Assert.Equal("mapper-contract-1", outcome.MapperVersion);
    }

    [Fact]
    [Trait("Rule", "AI-05")]
    public async Task RuleOnlyMapper_LeavesEverySyntheticFixtureByteIdentical()
    {
        var truthPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "test", "AccountGo.BackendTests", "Fixtures", "DocumentCapture", "Synthetic", "truth.json"));
        using var doc = JsonDocument.Parse(File.ReadAllText(truthPath));
        var entries = doc.RootElement.EnumerateArray().ToList();
        Assert.Equal(18, entries.Count);

        foreach (var entry in entries)
        {
            var vendor = entry.GetProperty("vendor").GetString()!;
            var invoiceNo = entry.GetProperty("invoiceNo").GetString()!;
            var isoDate = entry.GetProperty("date").GetString()!;
            var sym = entry.GetProperty("currency").GetString() switch { "EUR" => "€", "SGD" => "S$", "THB" => "฿", _ => "$" };
            var kind = entry.GetProperty("layout").GetString() == "invoice" ? "purchase-invoice" : "expense-receipt";
            var lines = new[]
            {
                new TextLine(1, vendor, 0, 20, 100, 46, TextLineSource.Ocr),
                new TextLine(1, $"Invoice No.: {invoiceNo}", 0, 92, 100, 108, TextLineSource.Ocr),
                new TextLine(1, $"Date: {isoDate}", 0, 110, 100, 126, TextLineSource.Ocr),
                new TextLine(1, $"Subtotal: {sym}{entry.GetProperty("subtotal").GetString()}", 0, 180, 100, 196, TextLineSource.Ocr),
                new TextLine(1, $"Tax: {sym}{entry.GetProperty("tax").GetString()}", 0, 198, 100, 214, TextLineSource.Ocr),
                new TextLine(1, $"TOTAL: {sym}{entry.GetProperty("total").GetString()}", 0, 220, 100, 242, TextLineSource.Ocr),
            };
            var parsed = DocumentTextFieldParser.Parse(kind, lines);

            var outcome = await CaptureFieldMappingRunner.RunAsync(new RuleOnlyFieldMapper(), kind, 1, parsed.FieldsJson, lines,
                DocumentTextFieldParser.RulesVersion, CancellationToken.None);

            Assert.Equal(parsed.FieldsJson, outcome.FieldsJson);
            Assert.Empty(outcome.Warnings);
        }
    }

    [Theory]
    [InlineData("bank")]
    [InlineData("assistant-attachment")]
    public async Task Runner_NeverCallsTheMapperForBankOrAssistantCaptures(string kind)
    {
        var mapper = new ScriptedMapper(_ => throw new InvalidOperationException("must not be called"));
        var outcome = await CaptureFieldMappingRunner.RunAsync(mapper, kind, 1, "{\"name\":\"x\"}",
            [new TextLine(1, "x", 0, 0, 1, 1, TextLineSource.Ocr)], "rules-3", CancellationToken.None);

        Assert.Equal(0, mapper.Calls);
        Assert.Equal("{\"name\":\"x\"}", outcome.FieldsJson);
        Assert.Empty(outcome.Warnings);
    }

    // ---- G-AI-01: values must be in the source ----------------------------------------------------------------

    [Fact]
    [Trait("Rule", "AI-01")]
    public void Guard_DropsAnInventedInvoiceNumberAndKeepsTheRuleValue()
    {
        var mapped = Merge(RuleFields, o => o["vendorInvoiceNo"] = "NS-99999");

        var result = Guard(mapped);

        Assert.Equal(RuleFields, result.FieldsJson);
        Assert.Equal([CaptureMappingGuards.WarningNotInSource], result.Warnings);
        Assert.DoesNotContain("fieldSources", result.FieldsJson, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Rule", "AI-01")]
    public void Guard_DropsATotalThatIsNotPrintedOnTheDocument()
    {
        var mapped = Merge(RuleFields, o => o["sourceTotals"]!["grandTotal"] = "500.00");

        var result = Guard(mapped);

        Assert.Equal(RuleFields, result.FieldsJson);
        Assert.Contains(CaptureMappingGuards.WarningNotInSource, result.Warnings);
    }

    [Fact]
    [Trait("Rule", "AI-01")]
    public void Guard_AcceptsADateWrittenInAnotherFormatThatAppearsInTheText()
    {
        var rule = Merge(RuleFields, o => o.Remove("date"));
        var mapped = RuleFields; // the mapper read "12 Nov 2026" as 2026-11-12

        var result = Guard(mapped, rule);

        using var doc = JsonDocument.Parse(result.FieldsJson);
        Assert.Equal("2026-11-12", doc.RootElement.GetProperty("date").GetString());
        Assert.Equal("ai", doc.RootElement.GetProperty("fieldSources").GetProperty("date").GetString());
        Assert.Empty(result.Warnings);
    }

    [Fact]
    [Trait("Rule", "AI-01")]
    public void Guard_RejectsADateThatIsNotWrittenInTheText()
    {
        var mapped = Merge(RuleFields, o => o["date"] = "2026-12-01");

        var result = Guard(mapped);

        Assert.Equal(RuleFields, result.FieldsJson);
        Assert.Contains(CaptureMappingGuards.WarningNotInSource, result.Warnings);
    }

    [Fact]
    [Trait("Rule", "AI-01")]
    public void Guard_AcceptsADocumentNumberTheRulesMissedWhenItIsInTheText()
    {
        var rule = Merge(RuleFields, o => o.Remove("vendorInvoiceNo"));

        var result = Guard(RuleFields, rule);

        using var doc = JsonDocument.Parse(result.FieldsJson);
        Assert.Equal("NS-20457", doc.RootElement.GetProperty("vendorInvoiceNo").GetString());
        Assert.Equal("ai", doc.RootElement.GetProperty("fieldSources").GetProperty("vendorInvoiceNo").GetString());
        Assert.Empty(result.Warnings);
    }

    [Fact]
    [Trait("Rule", "AI-01")]
    public void Guard_DoesNotAcceptAPartOfALongerDocumentNumber()
    {
        var mapped = Merge(RuleFields, o => o["vendorInvoiceNo"] = "NS-2045");

        var result = Guard(mapped);

        Assert.Equal(RuleFields, result.FieldsJson);
        Assert.Contains(CaptureMappingGuards.WarningNotInSource, result.Warnings);
    }

    // ---- review fixes: party names, whole-number totals, line descriptions -----------------------------------------

    [Fact]
    [Trait("Rule", "AI-01")]
    public void Guard_DropsAnInventedPartyNameAndKeepsTheRuleName()
    {
        var mapped = Merge(RuleFields, o => o["vendorName"] = "Harborview Traders");

        var result = Guard(mapped);

        Assert.Equal(RuleFields, result.FieldsJson);
        Assert.Equal([CaptureMappingGuards.WarningNotInSource], result.Warnings);
    }

    [Fact]
    [Trait("Rule", "AI-01")]
    public void Guard_DropsANameWithOneWordTheDocumentDoesNotPrint()
    {
        var mapped = Merge(RuleFields, o => o["vendorName"] = "Northstar Supplies Limited");

        var result = Guard(mapped);

        Assert.Equal(RuleFields, result.FieldsJson);
        Assert.Contains(CaptureMappingGuards.WarningNotInSource, result.Warnings);
    }

    [Fact]
    [Trait("Rule", "AI-01")]
    public void Guard_AcceptsANameReassembledFromAMergedTwoColumnLine()
    {
        string[] lines =
        [
            "Northstar   Supplies   Ltd        Invoice No: NS-20457",
            "Date: 12 Nov 2026",
            "Subtotal 407.47",
            "Tax 8.25% 33.62",
            "Total 441.09",
        ];
        var rule = Merge(RuleFields, o => o["vendorName"] = "Northstar Supplies Ltd Invoice No NS");

        var result = Guard(Merge(RuleFields, o => o["vendorName"] = "Northstar Supplies Ltd"), rule, lines: lines);

        using var doc = JsonDocument.Parse(result.FieldsJson);
        Assert.Equal("Northstar Supplies Ltd", doc.RootElement.GetProperty("vendorName").GetString());
        Assert.Equal("ai", doc.RootElement.GetProperty("fieldSources").GetProperty("vendorName").GetString());
    }

    [Fact]
    [Trait("Rule", "AI-01")]
    public void Guard_DoesNotLetAQuantityBecomeATotal()
    {
        string[] lines = ["Northstar Supplies Ltd", "Paper A4 qty 3 at 12.00", "Subtotal 36.00", "Tax 0.00", "Total 36.00"];
        var rule = """{"sourceTotals":{"grandTotal":"36.00","subtotal":"36.00","taxTotal":"0.00"},"vendorName":"Northstar Supplies Ltd"}""";

        var result = Guard(Merge(rule, o => o["sourceTotals"]!["grandTotal"] = "3"), rule, lines: lines);

        Assert.Equal(rule, result.FieldsJson);
        Assert.Contains(CaptureMappingGuards.WarningNotInSource, result.Warnings);
    }

    [Fact]
    [Trait("Rule", "AI-01")]
    public void Guard_AcceptsAWholeNumberTotalPrintedOnATotalsLine()
    {
        string[] lines = ["Northstar Supplies Ltd", "Paper A4 qty 3 at 12", "Subtotal 36", "Tax 0", "Total 36"];
        var rule = """{"vendorName":"Northstar Supplies Ltd"}""";

        var result = Guard(Merge(rule, o => o["sourceTotals"] = System.Text.Json.Nodes.JsonNode.Parse("""{"subtotal":"36","taxTotal":"0","grandTotal":"36"}""")), rule, lines: lines);

        using var doc = JsonDocument.Parse(result.FieldsJson);
        Assert.Equal("36", doc.RootElement.GetProperty("sourceTotals").GetProperty("grandTotal").GetString());
        Assert.Equal("ai", doc.RootElement.GetProperty("fieldSources").GetProperty("sourceTotals.grandTotal").GetString());
    }

    [Fact]
    [Trait("Rule", "AI-01")]
    public void Guard_RejectsAWholeNumberTotalThatIsOnlyPrintedOnAnOrdinaryLine()
    {
        string[] lines = ["Northstar Supplies Ltd", "Paper A4 qty 36", "Subtotal 36.50"];
        var rule = """{"vendorName":"Northstar Supplies Ltd"}""";

        var result = Guard(Merge(rule, o => o["sourceTotals"] = System.Text.Json.Nodes.JsonNode.Parse("""{"grandTotal":"36"}""")), rule, lines: lines);

        Assert.Equal(rule, result.FieldsJson);
        Assert.Contains(CaptureMappingGuards.WarningNotInSource, result.Warnings);
    }

    [Fact]
    [Trait("Rule", "AI-01")]
    public void Guard_RejectsInventedLineDescriptionsAsAGroup()
    {
        var mapped = Merge(RuleFields, o => o["lines"] = System.Text.Json.Nodes.JsonNode.Parse(
            """[{"description":"Premium consulting package","quantity":"1","unitAmount":"244.48"},{"description":"Toner","quantity":"1","unitAmount":"162.99"}]"""));

        var result = Guard(mapped);

        Assert.Equal(RuleFields, result.FieldsJson);
        Assert.Contains(CaptureMappingGuards.WarningNotInSource, result.Warnings);
    }

    [Theory]
    [InlineData("Paper A4 sheets", true)]   // 2 of 3 words printed = 67%
    [InlineData("Paper sheets boxes", false)] // 1 of 3 words printed = 33%
    [InlineData("A4 Paper", true)]
    [InlineData("Toner cartridge magenta XL", false)] // 1 of 4 = 25%
    [InlineData("Toner cartridge", false)] // 1 of 2 = 50%, below 60%
    public void Guard_RequiresSixtyPercentOfADescriptionsWordsToBePrinted(string description, bool accepted)
    {
        var mapped = Merge(RuleFields, o => o["lines"] = System.Text.Json.Nodes.JsonNode.Parse(
            $$"""[{"description":"{{description}}","quantity":"1","unitAmount":"244.48"},{"description":"Toner","quantity":"1","unitAmount":"162.99"}]"""));

        var result = Guard(mapped);

        Assert.Equal(accepted, result.FieldsJson != RuleFields);
        Assert.Equal(accepted, !result.Warnings.Contains(CaptureMappingGuards.WarningNotInSource));
        Assert.Equal(0.6, CaptureMappingGuards.MinimumDescriptionWordShare);
    }

    [Fact]
    public async Task Runner_GivesTheMapperOnlyMaskedLines()
    {
        var mapper = new ScriptedMapper(request =>
        {
            var seen = string.Join('\n', request.Lines.Select(l => l.Text));
            Assert.DoesNotContain("123456789012", seen, StringComparison.Ordinal);
            Assert.DoesNotContain("GB82", seen, StringComparison.Ordinal);
            Assert.Contains("NS-20457", seen, StringComparison.Ordinal);
            return Result(RuleFields);
        });

        var outcome = await CaptureFieldMappingRunner.RunAsync(mapper, "purchase-invoice", 1, RuleFields,
        [
            new TextLine(1, "Invoice No: NS-20457", 0, 0, 1, 1, TextLineSource.Ocr),
            new TextLine(1, "Account 123456789012 IBAN GB82 WEST 1234 5698 7654 32", 0, 2, 1, 3, TextLineSource.Ocr),
        ], "rules-3", CancellationToken.None);

        Assert.Equal(1, mapper.Calls);
        Assert.Equal(RuleFields, outcome.FieldsJson);
    }

    // ---- G-AI-02: arithmetic ----------------------------------------------------------------------------------

    [Fact]
    [Trait("Rule", "AI-02")]
    public void Guard_RejectsMappedTotalsThatDoNotAddUp_EvenWhenEveryAmountIsPrinted()
    {
        // 450.00 is printed ("Also quoted") but 407.47 + 33.62 is not 450.00.
        var mapped = Merge(RuleFields, o => o["sourceTotals"]!["grandTotal"] = "450.00");

        var result = Guard(mapped);

        Assert.Equal(RuleFields, result.FieldsJson);
        Assert.Equal([CaptureMappingGuards.WarningTotalsInconsistent], result.Warnings);
    }

    [Fact]
    [Trait("Rule", "AI-02")]
    public void Guard_RejectsMappedLinesThatDoNotSumToTheSubtotal()
    {
        var mapped = Merge(RuleFields, o => o["lines"] = System.Text.Json.Nodes.JsonNode.Parse(
            """[{"description":"Paper A4","quantity":"1","unitAmount":"244.48"},{"description":"Toner","quantity":"1","unitAmount":"450.00"}]"""));

        var result = Guard(mapped);

        Assert.Equal(RuleFields, result.FieldsJson);
        Assert.Contains(CaptureMappingGuards.WarningTotalsInconsistent, result.Warnings);
    }

    [Fact]
    [Trait("Rule", "AI-02")]
    public void Guard_AcceptsCleanedLineDescriptionsWhenTheAmountsAreSourcedAndAddUp()
    {
        var mapped = Merge(RuleFields, o => o["lines"] = System.Text.Json.Nodes.JsonNode.Parse(
            """[{"description":"A4 copier paper","quantity":"1","unitAmount":"244.48"},{"description":"Toner","quantity":"1","unitAmount":"162.99"}]"""));

        var result = Guard(mapped);

        using var doc = JsonDocument.Parse(result.FieldsJson);
        Assert.Equal("A4 copier paper", doc.RootElement.GetProperty("lines")[0].GetProperty("description").GetString());
        var sources = doc.RootElement.GetProperty("fieldSources");
        Assert.Equal("ai", sources.GetProperty("lines[0].description").GetString());
        Assert.False(sources.TryGetProperty("lines[1].description", out _));
        Assert.Contains(CaptureMappingGuards.WarningDisagreesWithRules, result.Warnings);
        Assert.Equal("Paper A4", result.RuleValueHints["lines[0].description"]);
    }

    [Fact]
    [Trait("Rule", "AI-02")]
    public void Guard_RejectsLinesWithAnInventedAmount()
    {
        var mapped = Merge(RuleFields, o => o["lines"] = System.Text.Json.Nodes.JsonNode.Parse(
            """[{"description":"Paper A4","quantity":"1","unitAmount":"244.48"},{"description":"Toner","quantity":"1","unitAmount":"162.98"}]"""));

        var result = Guard(mapped);

        Assert.Equal(RuleFields, result.FieldsJson);
        Assert.Contains(CaptureMappingGuards.WarningNotInSource, result.Warnings);
    }

    // ---- G-AI-03: disagreement is a suggestion, never silent ----------------------------------------------------

    [Fact]
    [Trait("Rule", "AI-03")]
    public void Guard_KeepsTheMapperVendorNameAsASuggestionAndRecordsWhatTheRulesFound()
    {
        var mapped = Merge(RuleFields, o => o["vendorName"] = "Northstar Supplies");

        var result = Guard(mapped);

        using var doc = JsonDocument.Parse(result.FieldsJson);
        Assert.Equal("Northstar Supplies", doc.RootElement.GetProperty("vendorName").GetString());
        Assert.Equal("ai", doc.RootElement.GetProperty("fieldSources").GetProperty("vendorName").GetString());
        Assert.Equal([CaptureMappingGuards.WarningDisagreesWithRules], result.Warnings);
        Assert.Equal("Northstar Supplies Ltd", result.RuleValueHints["vendorName"]);
    }

    [Fact]
    [Trait("Rule", "AI-03")]
    public void Guard_DoesNotOverwriteARuleValueWithAMapperValueThatFailedTheSourceCheck()
    {
        var mapped = Merge(RuleFields, o =>
        {
            o["vendorName"] = "Northstar Supplies";
            o["vendorInvoiceNo"] = "ZZ-1";
        });

        var result = Guard(mapped);

        using var doc = JsonDocument.Parse(result.FieldsJson);
        Assert.Equal("NS-20457", doc.RootElement.GetProperty("vendorInvoiceNo").GetString());
        Assert.False(doc.RootElement.GetProperty("fieldSources").TryGetProperty("vendorInvoiceNo", out _));
        Assert.Contains(CaptureMappingGuards.WarningNotInSource, result.Warnings);
        Assert.Contains(CaptureMappingGuards.WarningDisagreesWithRules, result.Warnings);
    }

    // ---- G-AI-04: nothing outside the schema, no paid status ---------------------------------------------------

    [Fact]
    [Trait("Rule", "AI-04")]
    public void Guard_StripsBankFieldsAndStrayProperties()
    {
        var mapped = Merge(RuleFields, o =>
        {
            o["iban"] = "GB82WEST12345698765432";
            o["bank"] = System.Text.Json.Nodes.JsonNode.Parse("""{"accountNumber":"123456789012"}""");
            o["routingNumber"] = "021000021";
            o["somethingElse"] = "x";
        });

        var result = Guard(mapped);

        Assert.Equal(RuleFields, result.FieldsJson);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    [Trait("Rule", "AI-04")]
    public void Guard_NeverTakesAPaidStatusFromTheMapper()
    {
        var mapped = Merge(RuleFields, o => o["paymentHints"] = System.Text.Json.Nodes.JsonNode.Parse(
            """{"paymentStatus":"paid","amountPaid":"441.09","paymentMethod":"card"}"""));

        var result = Guard(mapped);

        Assert.Equal(RuleFields, result.FieldsJson);
        Assert.DoesNotContain("paid", result.FieldsJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Rule", "AI-04")]
    public void Guard_IgnoresAFieldSourcesMapThatTheMapperSuppliesItself()
    {
        var mapped = Merge(RuleFields, o => o["fieldSources"] = System.Text.Json.Nodes.JsonNode.Parse("""{"vendorName":"ai","date":"ai"}"""));

        var result = Guard(mapped);

        Assert.Equal(RuleFields, result.FieldsJson);
    }

    [Theory]
    [InlineData("bank")]
    [InlineData("assistant-attachment")]
    public void Guard_LeavesBankAndAssistantFieldsExactlyAsTheRulesFoundThem(string kind)
    {
        var result = CaptureMappingGuards.Apply(kind, """{"name":"A"}""", """{"name":"B","iban":"GB82WEST12345698765432"}""", ["B"]);

        Assert.Equal("""{"name":"A"}""", result.FieldsJson);
        Assert.Empty(result.Warnings);
    }

    // ---- G-AI-05: a failing mapper never fails the capture ---------------------------------------------------------

    [Fact]
    [Trait("Rule", "AI-05")]
    public async Task Runner_ReturnsTheRuleFieldsWithAiUnavailableWhenTheMapperThrows_WithoutKeepingTheMessage()
    {
        var mapper = new ScriptedMapper(_ => throw new InvalidOperationException("secret invoice text NS-20457 4111111111111111"));

        var outcome = await RunAsync(mapper);

        Assert.Equal(RuleFields, outcome.FieldsJson);
        Assert.Equal([CaptureMappingGuards.WarningUnavailable], outcome.Warnings);
        Assert.DoesNotContain("secret", string.Join(' ', outcome.Warnings), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Rule", "AI-05")]
    public async Task Runner_AbandonsAMapperThatIsTooSlow()
    {
        var mapper = new ScriptedMapper(async (_, token) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), token);
            return Result(RuleFields);
        });

        var outcome = await RunAsync(mapper, timeout: TimeSpan.FromMilliseconds(50));

        Assert.Equal(RuleFields, outcome.FieldsJson);
        Assert.Equal([CaptureMappingGuards.WarningUnavailable], outcome.Warnings);
    }

    [Fact]
    [Trait("Rule", "AI-05")]
    public async Task Runner_DoesNotSwallowTheCallersOwnCancellation()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        var mapper = new ScriptedMapper(async (_, token) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), token);
            return Result(RuleFields);
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => RunAsync(mapper, token: cancelled.Token));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[1,2]")]
    public async Task Runner_TreatsAnUnusableMapperResultAsUnavailable(string fieldsJson)
    {
        var outcome = await RunAsync(new ScriptedMapper(_ => Result(fieldsJson)));

        Assert.Equal(RuleFields, outcome.FieldsJson);
        Assert.Equal([CaptureMappingGuards.WarningUnavailable], outcome.Warnings);
    }

    [Fact]
    public async Task Runner_StoresAGuardedMapperSuggestionAndNamesTheMapper()
    {
        var mapped = Merge(RuleFields, o => o["vendorName"] = "Northstar Supplies");
        var outcome = await RunAsync(new ScriptedMapper(_ => Result(mapped, "test-mapper", "7")));

        Assert.Equal("test-mapper", outcome.MapperName);
        Assert.Equal("7", outcome.MapperVersion);
        Assert.Contains("\"vendorName\":\"Northstar Supplies\"", outcome.FieldsJson, StringComparison.Ordinal);
        Assert.Contains(CaptureMappingGuards.WarningDisagreesWithRules, outcome.Warnings);
    }

    // ---- per-field source in the schema -------------------------------------------------------------------------

    [Fact]
    public void FieldSources_SanitizeKeepsValidPathsAndDropsUnknownOnes()
    {
        var json = """{"vendorName":"A","fieldSources":{"vendorName":"ai","lines[2].description":"rules","sourceTotals.grandTotal":"ai","iban":"ai","lines[200].description":"ai","vendorInvoiceNo":"human","paymentHints.paymentStatus":"ai"}}""";

        var sanitized = DocumentCaptureFieldSchemas.SanitizeProviderFields("purchase-invoice", json);

        using var doc = JsonDocument.Parse(sanitized);
        var sources = doc.RootElement.GetProperty("fieldSources").EnumerateObject().Select(p => (p.Name, p.Value.GetString())).ToArray();
        Assert.Equal(
            [("lines[2].description", "rules"), ("paymentHints.paymentStatus", "ai"), ("sourceTotals.grandTotal", "ai"), ("vendorName", "ai")],
            sources);
    }

    [Fact]
    public void FieldSources_SalesInvoicesUseTheirOwnPaths()
    {
        var json = """{"customerName":"A","fieldSources":{"customerName":"ai","vendorName":"ai","paymentHints.paymentStatus":"ai","sourceInvoiceNo":"rules"}}""";

        using var doc = JsonDocument.Parse(DocumentCaptureFieldSchemas.SanitizeProviderFields("sales-invoice", json));

        var names = doc.RootElement.GetProperty("fieldSources").EnumerateObject().Select(p => p.Name).ToArray();
        Assert.Equal(["customerName", "sourceInvoiceNo"], names);
    }

    [Fact]
    public void FieldSources_ValidateAcceptsAValidMapAndRejectsABadValueOrPathOrSize()
    {
        static bool Valid(string kind, string json)
        {
            using var doc = JsonDocument.Parse(json);
            return DocumentCaptureFieldSchemas.TryValidate(kind, 1, doc.RootElement, out _, out _);
        }

        Assert.True(Valid("purchase-invoice", """{"vendorName":"A","fieldSources":{"vendorName":"ai","lines[0].unitAmount":"rules"}}"""));
        Assert.True(Valid("sales-invoice", """{"customerName":"A","fieldSources":{"customerName":"ai"}}"""));
        Assert.False(Valid("purchase-invoice", """{"fieldSources":{"vendorName":"human"}}"""));
        Assert.False(Valid("purchase-invoice", """{"fieldSources":{"iban":"ai"}}"""));
        Assert.False(Valid("purchase-invoice", """{"fieldSources":["vendorName"]}"""));
        Assert.False(Valid("bank", """{"name":"A","fieldSources":{"name":"ai"}}"""));
    }

    [Fact]
    public void FieldSources_AreBoundedAtTwoHundredFiftySixEntries()
    {
        Assert.Equal(256, DocumentCaptureFieldSchemas.MaxFieldSources);
        // Only 100 lines x 8 line fields plus the totals can ever be valid paths, and each path appears once in a JSON object.
        var all = Enumerable.Range(0, 100).SelectMany(i => new[] { "description", "quantity", "unitAmount", "sourceLineTotal", "taxHint", "discount", "priceBasis", "missingQuantityDefaulted" }
            .Select(f => $"lines[{i}].{f}")).ToList();
        Assert.True(all.Count > DocumentCaptureFieldSchemas.MaxFieldSources);
        var json = "{\"fieldSources\":{" + string.Join(",", all.Select(p => $"\"{p}\":\"ai\"")) + "}}";
        using var doc = JsonDocument.Parse(json);
        Assert.False(DocumentCaptureFieldSchemas.TryValidate("purchase-invoice", 1, doc.RootElement, out _, out _));
        using var sanitized = JsonDocument.Parse(DocumentCaptureFieldSchemas.SanitizeProviderFields("purchase-invoice", json));
        Assert.Equal(256, sanitized.RootElement.GetProperty("fieldSources").EnumerateObject().Count());
    }

    [Fact]
    public void FieldSources_AreAbsentFromRuleOnlyParserOutput()
    {
        var parsed = DocumentTextFieldParser.Parse("purchase-invoice",
            SourceLines.Select(t => new TextLine(1, t, 0, 0, 1, 1, TextLineSource.Ocr)).ToArray());

        Assert.DoesNotContain("fieldSources", parsed.FieldsJson, StringComparison.Ordinal);
    }

    [Fact]
    public void WarningCodes_KeepTheMapperWarningsInsteadOfDowngradingThem()
    {
        foreach (var code in new[] { "AI_VALUE_NOT_IN_SOURCE", "AI_TOTALS_INCONSISTENT", "AI_DISAGREES_WITH_RULES", "AI_UNAVAILABLE" })
            Assert.Equal(code, DocumentCaptureWarningCodes.Sanitize(code));
        Assert.Equal("REVIEW_REQUIRED", DocumentCaptureWarningCodes.Sanitize("AI_SOMETHING_NEW"));
    }

    // ---- helpers ----------------------------------------------------------------------------------------------

    private static CaptureMappingResult Result(string fieldsJson, string name = "scripted", string version = "1") =>
        new(fieldsJson, name, version, Array.Empty<string>());

    private static Task<CaptureMappingOutcome> RunAsync(ICaptureFieldMapper mapper, TimeSpan? timeout = null, CancellationToken token = default) =>
        CaptureFieldMappingRunner.RunAsync(mapper, "purchase-invoice", 1, RuleFields,
            SourceLines.Select(t => new TextLine(1, t, 0, 0, 1, 1, TextLineSource.Ocr)).ToArray(), "rules-3", token, timeout);

    private sealed class ScriptedMapper : ICaptureFieldMapper
    {
        private readonly Func<CaptureMappingRequest, CancellationToken, Task<CaptureMappingResult>> _map;
        public int Calls { get; private set; }

        public ScriptedMapper(Func<CaptureMappingRequest, CaptureMappingResult> map) => _map = (request, _) => Task.FromResult(map(request));
        public ScriptedMapper(Func<CaptureMappingRequest, CancellationToken, Task<CaptureMappingResult>> map) => _map = map;

        public Task<CaptureMappingResult> MapAsync(CaptureMappingRequest request, CancellationToken cancellationToken)
        {
            Calls++;
            return _map(request, cancellationToken);
        }
    }
}
