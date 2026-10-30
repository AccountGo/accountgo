// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using Services.Banking;
using Xunit;

namespace AccountGo.BackendTests.Banking;

public class StatementImportPresetsTests
{
    [Fact]
    public void WisePreset_MatchesCorpusHeaderSignature()
    {
        var signature = StatementHeaderSignature.FromHeaders(new[]
        {
            "transfer_id", "activity_id", "created_on", "finished_on", "status",
            "direction", "amount", "currency", "source_name", "target_name"
        });

        var preset = StatementImportPresets.Find(signature);
        Assert.NotNull(preset);
        Assert.Equal("Wise activity", preset!.Name);
        Assert.Equal(2, preset.CreateMap().DateColumn);
        Assert.Equal(5, preset.CreateMap().DirectionColumn);
        Assert.Equal(1, preset.CreateMap().ExternalIdColumn);
    }

    [Fact]
    public void UnknownSignature_ReturnsNull()
    {
        Assert.Null(StatementImportPresets.Find("date|mystery_col"));
    }
}
