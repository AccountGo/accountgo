// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System.Linq;
using Services.Banking;
using Xunit;

namespace AccountGo.BackendTests.Banking;

public class StatementImportPresetsTests
{
    [Fact]
    public void WisePreset_MatchesWhenRequiredHeadersAreSubset_AndBindsByName()
    {
        var headers = new[]
        {
            "profile_id", "profile_type", "profile_name", "activity_id", "created_on", "updated_on",
            "type", "status", "resource_type", "resource_id", "title", "description", "direction",
            "amount", "currency", "signed_amount", "secondary_amount", "primary_amount_raw"
        };

        var preset = StatementImportPresets.FindMatching(headers);
        Assert.NotNull(preset);
        Assert.Equal("Wise activity", preset!.Name);

        var map = preset.Bind(headers);
        Assert.Equal(4, map.DateColumn); // created_on
        Assert.Equal(15, map.AmountColumn); // signed_amount preferred
        Assert.Equal(3, map.ExternalIdColumn); // activity_id
        Assert.Null(map.DirectionColumn); // signed_amount already encodes direction
        Assert.Equal(14, map.CurrencyColumn);
        Assert.Equal(10, map.PayeeColumn); // title
        Assert.Equal(11, map.MemoColumn); // description
    }

    [Fact]
    public void PayPalPreset_UsesNet_BalanceImpact_AndSkipsMemo()
    {
        var headers = new[]
        {
            "Date", "Name", "Type", "Status", "Currency", "Gross", "Fee", "Net",
            "Balance Impact", "Transaction ID"
        };
        var preset = StatementImportPresets.FindMatching(headers);
        Assert.NotNull(preset);
        Assert.Equal("PayPal activity", preset!.Name);
        var map = preset.Bind(headers);
        Assert.Equal(7, map.AmountColumn); // Net
        Assert.Equal(8, map.DirectionColumn); // Balance Impact
        Assert.Equal(2, map.TypeColumn);
        Assert.Contains("Memo", map.SkippedTypes!);
        Assert.Equal(1, map.DirectionValueMap!["credit"]);
        Assert.Equal(-1, map.DirectionValueMap["debit"]);
    }

    [Fact]
    public void RevolutPreset_MapsAmountAndFee()
    {
        var headers = new[]
        {
            "Type", "Product", "Started Date", "Completed Date", "Description",
            "Amount", "Fee", "Currency", "State", "Balance"
        };
        var preset = StatementImportPresets.FindMatching(headers);
        Assert.NotNull(preset);
        Assert.Equal("Revolut statement", preset!.Name);
        var map = preset.Bind(headers);
        Assert.Equal(3, map.DateColumn);
        Assert.Equal(5, map.AmountColumn);
        Assert.Equal(6, map.FeeColumn);
    }

    [Fact]
    public void UnknownHeaders_ReturnsNull()
    {
        Assert.Null(StatementImportPresets.FindMatching(new[] { "date", "mystery_col" }));
    }
}
