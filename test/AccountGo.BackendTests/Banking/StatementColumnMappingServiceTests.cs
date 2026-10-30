// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System.IO;
using Dto.Banking;
using Xunit;

namespace AccountGo.BackendTests.Banking;

public class StatementColumnMappingServiceTests
{
    private static string WiseCorpus() =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Banking", "StatementCorpus", "wise-activity-usd.csv"));

    [Fact]
    public void ResolveMapping_UsesPresetForWiseHeader_ThenSavedAfterCommit()
    {
        using var h = new BankingTestHarness();
        h.SeedCashTree();
        var bank = h.Banks.Create(new CreateBankAccountRequest
        {
            Name = "Wise USD",
            Type = 1,
            CurrencyCode = "USD"
        }, includeFullNumbers: false);

        var content = WiseCorpus();
        var resolved = h.Statements.ResolveMapping(bank.Id, new BankStatementImportRequest
        {
            Source = 1,
            Content = content
        });

        Assert.Equal("preset", resolved.MappingSource);
        Assert.Equal("Wise activity", resolved.PresetName);
        Assert.NotNull(resolved.ColumnMap);
        Assert.Equal(2, resolved.ColumnMap!.DateColumn);

        var commitMap = resolved.ColumnMap;
        commitMap.DateFormat = null;
        var result = h.Statements.Commit(bank.Id, new BankStatementImportRequest
        {
            Source = 1,
            FileName = "wise.csv",
            Content = content,
            ColumnMap = commitMap
        }, actor: "tester");

        Assert.True(result.Imported >= 1);

        var again = h.Statements.ResolveMapping(bank.Id, new BankStatementImportRequest
        {
            Source = 1,
            Content = content
        });
        Assert.Equal("saved", again.MappingSource);
        Assert.NotNull(again.ColumnMap);
        Assert.Equal(commitMap.DateColumn, again.ColumnMap!.DateColumn);
        Assert.Equal(commitMap.DirectionColumn, again.ColumnMap.DirectionColumn);
    }
}
