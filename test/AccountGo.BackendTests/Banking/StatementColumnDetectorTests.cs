// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System.Collections.Generic;
using System.Linq;
using Services.Banking;
using Xunit;

namespace AccountGo.BackendTests.Banking;

public class StatementColumnDetectorTests
{
    [Fact]
    public void DetectDateFormat_Iso8601WithFractionAndZ()
    {
        var values = new[]
        {
            "2024-03-07T04:59:09.447Z",
            "2024-03-08T12:00:00.000Z",
            "2024-03-09T08:15:30.123+00:00"
        };

        var detection = StatementColumnDetector.DetectDateFormat(values);

        Assert.Equal(StatementColumnDetector.DateKind.Iso8601, detection.Kind);
        Assert.False(detection.Ambiguous);
        Assert.Equal("ISO 8601", detection.Summary);
    }

    [Fact]
    public void DetectDateFormat_DayFirstWhenAnyPartExceeds12()
    {
        var detection = StatementColumnDetector.DetectDateFormat(new[] { "13/01/2024", "02/03/2024", "31/12/2024" });

        Assert.Equal(StatementColumnDetector.DateKind.DayFirstNumeric, detection.Kind);
        Assert.Equal("d/M/yyyy", detection.Format);
        Assert.False(detection.Ambiguous);
    }

    [Fact]
    public void DetectDateFormat_MonthFirstWhenSecondPartExceeds12()
    {
        var detection = StatementColumnDetector.DetectDateFormat(new[] { "01/13/2024", "03/02/2024", "12/31/2024" });

        Assert.Equal(StatementColumnDetector.DateKind.MonthFirstNumeric, detection.Kind);
        Assert.Equal("M/d/yyyy", detection.Format);
        Assert.False(detection.Ambiguous);
    }

    [Fact]
    public void DetectDateFormat_AmbiguousWhenEveryPartAtMost12_NeverGuesses()
    {
        var detection = StatementColumnDetector.DetectDateFormat(new[] { "03/04/2024", "05/06/2024", "01/02/2024" });

        Assert.Equal(StatementColumnDetector.DateKind.AmbiguousNumeric, detection.Kind);
        Assert.True(detection.Ambiguous);
        Assert.Null(detection.Format);
    }

    [Fact]
    public void DetectDateFormat_ExcelSerialAndEpochOnlyWhenWholeColumnMatches()
    {
        var serial = StatementColumnDetector.DetectDateFormat(new[] { "45358", "45359", "45360" });
        Assert.Equal(StatementColumnDetector.DateKind.ExcelSerial, serial.Kind);

        var epochS = StatementColumnDetector.DetectDateFormat(new[] { "1709769600", "1709856000" });
        Assert.Equal(StatementColumnDetector.DateKind.EpochSeconds, epochS.Kind);

        var epochMs = StatementColumnDetector.DetectDateFormat(new[] { "1709769600000", "1709856000000" });
        Assert.Equal(StatementColumnDetector.DateKind.EpochMilliseconds, epochMs.Kind);

        // Mixed with ordinary dates → not epoch/serial.
        var mixed = StatementColumnDetector.DetectDateFormat(new[] { "45358", "2024-01-01" });
        Assert.NotEqual(StatementColumnDetector.DateKind.ExcelSerial, mixed.Kind);
    }

    [Fact]
    public void DetectHeaderRow_SkipsPreambleBankInfoLines()
    {
        var records = new List<IReadOnlyList<string>>
        {
            new[] { "Account Name: Demo", "" },
            new[] { "Account Number:", "****1234" },
            new[] { "Exported:", "2024-06-01" },
            new[] { "Date", "Amount", "Description" },
            new[] { "2024-06-02", "10.00", "Deposit" }
        };

        Assert.Equal(3, StatementColumnDetector.DetectHeaderRow(records));
    }

    [Fact]
    public void NormalizeHeaderToken_CollapsesPunctuation()
    {
        Assert.Equal("created_on", StatementColumnDetector.NormalizeHeaderToken("Created On"));
        Assert.Equal("activity_id", StatementColumnDetector.NormalizeHeaderToken("activity-id"));
    }
}
