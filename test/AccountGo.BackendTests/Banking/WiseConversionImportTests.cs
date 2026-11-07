// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Core.Domain;
using Core.Domain.Financials;
using Dto.Banking;
using Services.Banking;
using Xunit;

namespace AccountGo.BackendTests.Banking;

/// <summary>
/// Wise neutral/internal conversions are not statement lines (#447).
/// Amount sign and row eligibility are decided separately.
/// </summary>
public class WiseConversionImportTests
{
    private static string WiseCorpus() =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Banking", "StatementCorpus", "wise-activity-usd.csv"));

    private static BankStatementColumnMap SignedPresetMap(string content)
    {
        var records = BankStatementParser.SplitCsvRecords(content, ',');
        var headerIndex = StatementColumnDetector.DetectHeaderRow(records)
            ?? throw new InvalidOperationException("Wise header missing.");
        var preset = StatementImportPresets.FindMatching(records[headerIndex])
            ?? throw new InvalidOperationException("Wise preset missing.");
        return preset.Bind(records[headerIndex]);
    }

    /// <summary>Same Wise layout, but the amount column is the unsigned <c>amount</c> field.</summary>
    private static BankStatementColumnMap UnsignedAmountMap(string content)
    {
        var map = SignedPresetMap(content);
        map.AmountColumn = 13;
        map.AmountCarriesSign = false;
        return map;
    }

    /// <summary>A map saved before direction eligibility existed: signed amount, no direction, no skip list.</summary>
    private static BankStatementColumnMap LegacySignedMap() => new()
    {
        HasHeaderRow = true,
        DateColumn = 4,
        AmountColumn = 15,
        ExternalIdColumn = 3,
        PayeeColumn = 10,
        MemoColumn = 11,
        CurrencyColumn = 14,
        StatusColumn = 7
    };

    private static BankStatementImportRequest Csv(string content, BankStatementColumnMap map, string name = "wise.csv") =>
        new()
        {
            Source = (int)BankStatementSources.Csv,
            FileName = name,
            Content = content,
            ColumnMap = map
        };

    private static (BankingTestHarness Harness, BankAccountDto Bank) OpenBank()
    {
        var harness = new BankingTestHarness();
        harness.SeedCashTree();
        var bank = harness.Banks.Create(new CreateBankAccountRequest
        {
            Name = "Wise USD",
            Type = 1,
            CurrencyCode = "USD"
        }, includeFullNumbers: false);
        return (harness, bank);
    }

    [Fact]
    public void Fixture_PreviewThenImport_SkipsNeutral_AndKeepsOrdinaryUsdSigns()
    {
        using var harness = OpenBank().Harness;
        var bank = harness.Repos.Banks.Table.Single();
        var content = WiseCorpus();
        var map = SignedPresetMap(content);

        var preview = harness.Statements.Preview(bank.Id, Csv(content, map));
        Assert.Equal(5, preview.TotalRows);
        Assert.Equal(2, preview.ReadyRows);
        Assert.Equal(1, preview.SkippedRows);
        Assert.Equal(2, preview.ErrorRows);
        Assert.Contains("skip direction neutral, internal", preview.Detection!.RowRule, StringComparison.OrdinalIgnoreCase);

        var neutral = preview.Rows.Single(r => r.ExternalId == "ACT-INT-001");
        Assert.Equal("skipped", neutral.Status);
        Assert.Null(neutral.Amount);
        Assert.Contains("neutral conversion skipped", neutral.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains($"row {neutral.RowNumber}", neutral.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(7, neutral.RowNumber);

        var php = preview.Rows.Single(r => r.ExternalId == "ACT-PHP-001");
        Assert.Equal("error", php.Status);
        Assert.Contains("PHP row skipped", php.Error, StringComparison.OrdinalIgnoreCase);

        var pending = preview.Rows.Single(r => r.ExternalId == "ACT-PEND-001");
        Assert.Equal("error", pending.Status);
        Assert.Contains("pending row skipped", pending.Error, StringComparison.OrdinalIgnoreCase);

        var depositGl = harness.Post(bank.AccountId!.Value, DrOrCrSide.Dr, 1250.50m, BankingTestHarness.Utc(2024, 3, 7));
        var conversionGl = harness.Post(bank.AccountId!.Value, DrOrCrSide.Dr, 100m, BankingTestHarness.Utc(2024, 3, 10));
        var glCount = harness.Repos.GlLines.Table.Count();

        var commit = harness.Statements.Commit(bank.Id, Csv(content, map), "tester");
        Assert.Equal(2, commit.Imported);
        Assert.Equal(1, commit.Skipped);
        Assert.Equal(2, commit.Errors);
        Assert.Equal(1, commit.AutoMatched);
        Assert.Equal(glCount, harness.Repos.GlLines.Table.Count());

        var lines = harness.Statements.ListLines(bank.Id, "all").Lines;
        Assert.Equal(2, lines.Count);
        Assert.Equal(1250.50m, lines.Single(l => l.ExternalId == "ACT-USD-001").Amount);
        Assert.Equal(-40.00m, lines.Single(l => l.ExternalId == "ACT-USD-002").Amount);
        Assert.DoesNotContain(lines, l => l.ExternalId == "ACT-INT-001" || l.Amount is 100m or -100m);
        Assert.Equal(depositGl.Id, lines.Single(l => l.ExternalId == "ACT-USD-001").MatchedGeneralLedgerLineId);
        Assert.DoesNotContain(lines, l => l.MatchedGeneralLedgerLineId == conversionGl.Id);
        Assert.Null(conversionGl.BankReconciliationId);
    }

    [Fact]
    public void UnsignedAmountColumn_StillSkipsNeutral_AndSignsOrdinaryRowsFromDirection()
    {
        var content = WiseCorpus();
        var resolved = StatementImportResolver.Resolve(content, UnsignedAmountMap(content), "USD");
        Assert.Contains("unsigned + direction", resolved.Summary.Amount, StringComparison.Ordinal);
        Assert.Contains("neutral", resolved.Summary.RowRule, StringComparison.OrdinalIgnoreCase);

        var rows = BankStatementParser.ParseCsv(content, resolved.Map, resolved.Records);
        Assert.Equal(1250.50m, rows.Single(r => r.ExternalId == "ACT-USD-001").Amount);
        Assert.Equal(-40.00m, rows.Single(r => r.ExternalId == "ACT-USD-002").Amount);

        var neutral = rows.Single(r => r.ExternalId == "ACT-INT-001");
        Assert.True(neutral.IsSkipped);
        Assert.Null(neutral.Amount);
        Assert.Contains("neutral conversion skipped", neutral.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SavedLegacyMap_PreviewAndImport_ApplyTheWiseSkipRule()
    {
        using var harness = OpenBank().Harness;
        var bank = harness.Repos.Banks.Table.Single();
        var content = WiseCorpus();
        var records = BankStatementParser.SplitCsvRecords(content, ',');
        var header = records[StatementColumnDetector.DetectHeaderRow(records)!.Value];
        var legacy = LegacySignedMap();

        harness.Repos.ColumnMappings.Insert(new BankStatementColumnMapping
        {
            CompanyId = 1,
            BankId = bank.Id,
            HeaderSignature = StatementHeaderSignature.FromRecord(header),
            ColumnMapJson =
                """
                {"hasHeaderRow":true,"dateColumn":4,"amountColumn":15,"externalIdColumn":3,"payeeColumn":10,"memoColumn":11,"currencyColumn":14,"statusColumn":7}
                """,
            UpdatedAt = DateTime.UtcNow
        });

        var resolved = harness.Statements.ResolveMapping(bank.Id, new BankStatementImportRequest
        {
            Source = (int)BankStatementSources.Csv,
            Content = content
        });
        Assert.Equal("saved", resolved.MappingSource);
        Assert.Equal("Wise activity", resolved.PresetName);
        Assert.Equal(12, resolved.ColumnMap!.DirectionColumn);
        Assert.Contains("neutral", resolved.ColumnMap.SkippedDirections!);
        Assert.True(resolved.ColumnMap.AmountCarriesSign);

        var preview = harness.Statements.Preview(bank.Id, Csv(content, legacy));
        Assert.Equal("skipped", preview.Rows.Single(r => r.ExternalId == "ACT-INT-001").Status);
        Assert.Contains("neutral conversion skipped", preview.Rows.Single(r => r.ExternalId == "ACT-INT-001").Error, StringComparison.OrdinalIgnoreCase);

        var commit = harness.Statements.Commit(bank.Id, Csv(content, legacy), "tester");
        Assert.Equal(2, commit.Imported);
        Assert.Equal(1, commit.Skipped);
        Assert.DoesNotContain(harness.Repos.StatementLines.Table, l => l.ExternalId == "ACT-INT-001");
    }

    [Fact]
    public void OverlappingReimport_DoesNotCreateTheConversionLine()
    {
        using var harness = OpenBank().Harness;
        var bank = harness.Repos.Banks.Table.Single();
        var request = Csv(WiseCorpus(), SignedPresetMap(WiseCorpus()));

        var first = harness.Statements.Commit(bank.Id, request, "tester");
        var second = harness.Statements.Commit(bank.Id, request, "tester");

        Assert.Equal(2, first.Imported);
        Assert.Equal(0, second.Imported);
        Assert.Equal(2, second.Duplicates);
        Assert.Equal(1, second.Skipped);
        Assert.Equal(2, harness.Repos.StatementLines.Table.Count());
        Assert.DoesNotContain(harness.Repos.StatementLines.Table, l => l.Amount is 100m or -100m);
    }

    [Fact]
    public void UnknownAndConflictingDirection_AreReported_NotImported()
    {
        const string csv =
            "profile_id,profile_type,profile_name,activity_id,created_on,updated_on,type,status,resource_type,resource_id,title,description,direction,amount,currency,signed_amount\n" +
            "111,PERSONAL,Demo,ACT-OK,2024-03-07T00:00:00Z,2024-03-07T00:00:00Z,TRANSFER,completed,transfer,R,Client,Invoice payment,in,10.00,USD,10.00\n" +
            "111,PERSONAL,Demo,ACT-FEE,2024-03-08T00:00:00Z,2024-03-08T00:00:00Z,TRANSFER,completed,transfer,R,Wise,FX conversion fee,out,8.25,USD,-8.25\n" +
            "111,PERSONAL,Demo,ACT-WORD,2024-03-08T00:00:00Z,2024-03-08T00:00:00Z,TRANSFER,completed,transfer,R,Client,Received after conversion,in,15.00,USD,15.00\n" +
            "111,PERSONAL,Demo,ACT-NEU,2024-03-10T00:00:00Z,2024-03-10T00:00:00Z,TRANSFER,completed,transfer,R,Wise,Balance conversion,neutral,100.00,USD,100.00\n" +
            "111,PERSONAL,Demo,ACT-INT,2024-03-10T00:00:00Z,2024-03-10T00:00:00Z,TRANSFER,completed,transfer,R,Wise,Balance move,internal,50.00,USD,50.00\n" +
            "111,PERSONAL,Demo,ACT-PHP,2024-03-09T00:00:00Z,2024-03-09T00:00:00Z,TRANSFER,completed,transfer,R,Sender,PHP transfer,in,10.00,PHP,10.00\n" +
            "111,PERSONAL,Demo,ACT-PEND,2024-03-11T00:00:00Z,2024-03-11T00:00:00Z,TRANSFER,pending,transfer,R,Pending,Waiting,out,12.00,USD,-12.00\n" +
            "111,PERSONAL,Demo,ACT-UNK,2024-03-12T00:00:00Z,2024-03-12T00:00:00Z,TRANSFER,completed,transfer,R,Mystery,Sideways,sideways,5.00,USD,5.00\n" +
            "111,PERSONAL,Demo,ACT-BAD,2024-03-13T00:00:00Z,2024-03-13T00:00:00Z,TRANSFER,completed,transfer,R,Conflict,Disagrees,out,100.00,USD,100.00\n" +
            "111,PERSONAL,Demo,ACT-MISS,2024-03-14T00:00:00Z,2024-03-14T00:00:00Z,TRANSFER,completed,transfer,R,Missing,Blank direction,,3.00,USD,3.00\n";

        using var harness = OpenBank().Harness;
        var bank = harness.Repos.Banks.Table.Single();
        var map = SignedPresetMap(csv);
        var preview = harness.Statements.Preview(bank.Id, Csv(csv, map, "edges.csv"));

        Assert.Equal(10.00m, preview.Rows.Single(r => r.ExternalId == "ACT-OK").Amount);
        Assert.Equal(-8.25m, preview.Rows.Single(r => r.ExternalId == "ACT-FEE").Amount);
        Assert.Equal("ok", preview.Rows.Single(r => r.ExternalId == "ACT-FEE").Status);
        Assert.Contains("conversion", preview.Rows.Single(r => r.ExternalId == "ACT-FEE").Memo, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(15.00m, preview.Rows.Single(r => r.ExternalId == "ACT-WORD").Amount);

        Assert.Equal("skipped", preview.Rows.Single(r => r.ExternalId == "ACT-NEU").Status);
        Assert.Contains("neutral conversion skipped", preview.Rows.Single(r => r.ExternalId == "ACT-NEU").Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("skipped", preview.Rows.Single(r => r.ExternalId == "ACT-INT").Status);
        Assert.Contains("internal conversion skipped", preview.Rows.Single(r => r.ExternalId == "ACT-INT").Error, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("PHP row skipped", preview.Rows.Single(r => r.ExternalId == "ACT-PHP").Error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pending row skipped", preview.Rows.Single(r => r.ExternalId == "ACT-PEND").Error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("unknown direction 'sideways'", preview.Rows.Single(r => r.ExternalId == "ACT-UNK").Error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("conflicting direction 'out' and amount 100.00", preview.Rows.Single(r => r.ExternalId == "ACT-BAD").Error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Direction is missing", preview.Rows.Single(r => r.ExternalId == "ACT-MISS").Error, StringComparison.OrdinalIgnoreCase);

        var commit = harness.Statements.Commit(bank.Id, Csv(csv, map, "edges.csv"), "tester");
        Assert.Equal(3, commit.Imported);
        var lines = harness.Statements.ListLines(bank.Id, "all").Lines;
        Assert.Equal(new[] { "ACT-FEE", "ACT-OK", "ACT-WORD" }, lines.Select(l => l.ExternalId).OrderBy(id => id).ToArray());
        Assert.Equal(0, harness.Repos.GlLines.Table.Count());
    }

    [Fact]
    public void ExplicitEmptySkipList_IsKept_AndDoesNotSilentlyTreatUnknownDirectionAsMoney()
    {
        var content = WiseCorpus();
        var map = SignedPresetMap(content);
        map.SkippedDirections = new List<string>();

        var resolved = StatementImportResolver.Resolve(content, map, "USD");
        Assert.Empty(resolved.Map.SkippedDirections);
        Assert.Null(resolved.Summary.RowRule);

        var neutral = BankStatementParser.ParseCsv(content, resolved.Map, resolved.Records)
            .Single(r => r.ExternalId == "ACT-INT-001");
        Assert.False(neutral.IsSkipped);
        Assert.False(neutral.IsValid);
        Assert.Null(neutral.Amount);
        Assert.Contains("unknown direction 'neutral'", neutral.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExplicitEmptySkipList_WithoutDirectionColumn_KeepsTheSignedAmount()
    {
        var content = WiseCorpus();
        var map = LegacySignedMap();
        map.SkippedDirections = new List<string>();

        var resolved = StatementImportResolver.Resolve(content, map, "USD");
        Assert.Null(resolved.Map.DirectionColumn);
        Assert.True(resolved.Map.AmountCarriesSign);

        var neutral = BankStatementParser.ParseCsv(content, resolved.Map, resolved.Records)
            .Single(r => r.ExternalId == "ACT-INT-001");
        Assert.True(neutral.IsValid, neutral.Error);
        Assert.Equal(100.00m, neutral.Amount);
    }

    [Fact]
    public void ImpactReport_ListsStoredCandidates_AndWritesNothing()
    {
        using var harness = OpenBank().Harness;
        var bank = harness.Repos.Banks.Table.Single();
        var other = harness.Banks.Create(new CreateBankAccountRequest
        {
            Name = "Other",
            Type = 1,
            CurrencyCode = "USD"
        }, includeFullNumbers: false);

        var import = new BankStatementImport
        {
            CompanyId = 1,
            BankId = bank.Id,
            Source = BankStatementSources.Csv,
            FileName = "old-wise.csv",
            ImportedAt = new DateTime(2024, 4, 1, 0, 0, 0, DateTimeKind.Utc),
            ImportedBy = "operator"
        };
        harness.Repos.StatementImports.Insert(import);

        var reconciled = harness.Post(bank.AccountId!.Value, DrOrCrSide.Dr, 100m, BankingTestHarness.Utc(2024, 3, 10));
        reconciled.BankReconciliationId = 42;
        var draftOnly = harness.Post(bank.AccountId!.Value, DrOrCrSide.Dr, 50m, BankingTestHarness.Utc(2024, 3, 10));

        harness.Repos.StatementLines.Insert(new BankStatementLine
        {
            CompanyId = 1,
            BankId = bank.Id,
            BankStatementImportId = import.Id,
            Source = BankStatementSources.Csv,
            ExternalId = "ACT-INT-001",
            PostedDate = BankingTestHarness.Utc(2024, 3, 10),
            Amount = 100m,
            Payee = "Converted USD to EUR",
            MatchedGeneralLedgerLineId = reconciled.Id,
            CreatedAt = DateTime.UtcNow
        });
        harness.Repos.StatementLines.Insert(new BankStatementLine
        {
            CompanyId = 1,
            BankId = bank.Id,
            BankStatementImportId = import.Id,
            Source = BankStatementSources.Csv,
            ExternalId = "ACT-REAL",
            PostedDate = BankingTestHarness.Utc(2024, 3, 7),
            Amount = 1250.50m,
            CreatedAt = DateTime.UtcNow
        });
        harness.Repos.StatementLines.Insert(new BankStatementLine
        {
            CompanyId = 1,
            BankId = other.Id,
            BankStatementImportId = import.Id,
            Source = BankStatementSources.Csv,
            ExternalId = "ACT-INT-001",
            PostedDate = BankingTestHarness.Utc(2024, 3, 10),
            Amount = 100m,
            CreatedAt = DateTime.UtcNow
        });
        harness.Repos.StatementLines.Insert(new BankStatementLine
        {
            CompanyId = 2,
            BankId = bank.Id,
            BankStatementImportId = import.Id,
            Source = BankStatementSources.Csv,
            ExternalId = "ACT-INT-001",
            PostedDate = BankingTestHarness.Utc(2024, 3, 10),
            Amount = 100m,
            CreatedAt = DateTime.UtcNow
        });

        var beforeLines = harness.Repos.StatementLines.Table.Count();
        var beforeMatches = harness.Repos.StatementLines.Table.ToList()
            .Select(l => (l.Id, l.MatchedGeneralLedgerLineId, l.Amount, l.ExcludedAt))
            .ToList();

        var report = harness.Statements.ReportWiseConversionImpact(bank.Id, new BankStatementImportRequest
        {
            Source = (int)BankStatementSources.Csv,
            FileName = "wise-activity-usd.csv",
            Content = WiseCorpus()
        });

        Assert.Equal(1, report.CompanyId);
        Assert.Equal(bank.Id, report.BankId);
        Assert.Equal("Wise USD", report.BankName);
        Assert.Equal(1, report.SkippedInFile);
        var candidate = Assert.Single(report.Candidates);
        Assert.Equal(import.Id, candidate.ImportId);
        Assert.Equal("old-wise.csv", candidate.ImportFileName);
        Assert.Equal("ACT-INT-001", candidate.ExternalId);
        Assert.Equal(7, candidate.SourceRowNumber);
        Assert.Equal(100m, candidate.Amount);
        Assert.Equal("matched", candidate.MatchStatus);
        Assert.Equal("reconciled", candidate.PostingStatus);
        Assert.Equal(reconciled.Id, candidate.MatchedGeneralLedgerLineId);
        Assert.Equal(42, candidate.BankReconciliationId);
        Assert.NotEqual(draftOnly.Id, candidate.MatchedGeneralLedgerLineId);

        Assert.Equal(beforeLines, harness.Repos.StatementLines.Table.Count());
        Assert.Equal(beforeMatches, harness.Repos.StatementLines.Table.ToList()
            .Select(l => (l.Id, l.MatchedGeneralLedgerLineId, l.Amount, l.ExcludedAt))
            .ToList());
        Assert.Equal(42, reconciled.BankReconciliationId);
        Assert.Equal(1, harness.Repos.StatementImports.Table.Count());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void InvertedDirectionMap_NeverImportsAnAmountOppositeToDirection(bool signed)
    {
        var content = WiseCorpus();
        var map = signed ? SignedPresetMap(content) : UnsignedAmountMap(content);
        map.InvertSign = true;
        var resolved = StatementImportResolver.Resolve(content, map, "USD");
        var rows = BankStatementParser.ParseCsv(content, resolved.Map, resolved.Records);
        Assert.False(rows.Single(r => r.ExternalId == "ACT-USD-001").IsValid);
        Assert.False(rows.Single(r => r.ExternalId == "ACT-USD-002").IsValid);
    }

    [Fact]
    public void ImpactReport_ReviewsArchivedBankAndExcludesOtherSources()
    {
        using var harness = OpenBank().Harness;
        var bank = harness.Repos.Banks.Table.Single();
        bank.ArchivedAt = DateTime.UtcNow;
        var import = new BankStatementImport { CompanyId = 1, BankId = bank.Id, Source = BankStatementSources.Ofx, ImportedAt = DateTime.UtcNow };
        harness.Repos.StatementImports.Insert(import);
        harness.Repos.StatementLines.Insert(new BankStatementLine {
            CompanyId = 1, BankId = bank.Id, BankStatementImportId = import.Id, Source = BankStatementSources.Ofx,
            ExternalId = "ACT-INT-001", PostedDate = BankingTestHarness.Utc(2024, 3, 10), Amount = 100m, CreatedAt = DateTime.UtcNow
        });
        var report = harness.Statements.ReportWiseConversionImpact(bank.Id, Csv(WiseCorpus(), SignedPresetMap(WiseCorpus())));
        Assert.Equal(1, report.SkippedInFile);
        Assert.Empty(report.Candidates);
    }

    [Fact]
    public void Commit_AllSkippedRows_ReturnsEveryRowNumberAndReason()
    {
        using var harness = OpenBank().Harness;
        var bank = harness.Repos.Banks.Table.Single();
        var lines = WiseCorpus().Split('\n');
        var neutral = string.Join("\n", lines.Take(3).Concat(lines.Where(line => line.Contains("ACT-INT-001"))));
        var result = harness.Statements.Commit(bank.Id, Csv(neutral, SignedPresetMap(neutral)), "tester");
        Assert.Equal(0, result.Imported);
        Assert.Equal(1, result.Skipped);
        var detail = Assert.Single(result.SkippedRowDetails);
        Assert.Equal(4, detail.RowNumber);
        Assert.Contains("neutral", detail.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(harness.Repos.StatementImports.Table);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(-1)]
    [InlineData(999)]
    public void ActiveSkipRule_WithMissingOrInvalidDirection_RejectsPreviewAndImport(int? direction)
    {
        using var harness = OpenBank().Harness;
        var bank = harness.Repos.Banks.Table.Single();
        var content = WiseCorpus();
        var map = SignedPresetMap(content);
        map.DirectionColumn = direction;
        var request = Csv(content, map);
        var previewError = Assert.Throws<InvalidOperationException>(() => harness.Statements.Preview(bank.Id, request));
        var importError = Assert.Throws<InvalidOperationException>(() => harness.Statements.Commit(bank.Id, request, "tester"));
        Assert.Contains("direction column", previewError.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("direction column", importError.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(harness.Repos.StatementLines.Table);
    }

    [Fact]
    public void SavedMap_WithActiveSkipRuleAndMissingDirection_RejectsPreviewAndImport()
    {
        using var harness = OpenBank().Harness;
        var bank = harness.Repos.Banks.Table.Single();
        var content = WiseCorpus();
        var records = BankStatementParser.SplitCsvRecords(content, ',');
        var header = records[StatementColumnDetector.DetectHeaderRow(records)!.Value];
        var map = SignedPresetMap(content);
        map.DirectionColumn = null;
        harness.Repos.ColumnMappings.Insert(new BankStatementColumnMapping {
            CompanyId = 1, BankId = bank.Id, HeaderSignature = StatementHeaderSignature.FromRecord(header),
            ColumnMapJson = System.Text.Json.JsonSerializer.Serialize(map), UpdatedAt = DateTime.UtcNow
        });
        var saved = harness.Statements.ResolveMapping(bank.Id, new BankStatementImportRequest { Source = 1, Content = content });
        Assert.Equal("saved", saved.MappingSource);
        var request = Csv(content, saved.ColumnMap!);
        Assert.Throws<InvalidOperationException>(() => harness.Statements.Preview(bank.Id, request));
        Assert.Throws<InvalidOperationException>(() => harness.Statements.Commit(bank.Id, request, "tester"));
        Assert.Empty(harness.Repos.StatementLines.Table);
    }

    [Fact]
    public void ImpactReport_RejectsANonSpreadsheet()
    {
        using var harness = OpenBank().Harness;
        var bank = harness.Repos.Banks.Table.Single();
        var ex = Assert.Throws<InvalidOperationException>(() =>
            harness.Statements.ReportWiseConversionImpact(bank.Id, new BankStatementImportRequest
            {
                Source = (int)BankStatementSources.Ofx,
                Content = "<OFX></OFX>"
            }));
        Assert.Contains("CSV or Excel", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
