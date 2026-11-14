// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;

namespace Dto.Financial;

/// <summary>Reconciles each equity component from opening to closing for the selected period.</summary>
public sealed class EquityStatementDto
{
    public string EntityType { get; set; } = "";
    public DateTime From { get; set; }
    public DateTime To { get; set; }
    public string Basis { get; set; } = "Accrual";
    public bool PartnerMode { get; set; }
    public IReadOnlyList<EquityStatementColumnDto> Columns { get; set; } = Array.Empty<EquityStatementColumnDto>();
    public IReadOnlyList<EquityStatementRowDto> Rows { get; set; } = Array.Empty<EquityStatementRowDto>();
    public decimal BalanceSheetEquity { get; set; }
    public decimal DifferenceToBalanceSheet { get; set; }
    public EquityStatementDto? Comparative { get; set; }
}

/// <summary>An equity component, selected according to the workspace's current entity type.</summary>
public sealed record EquityStatementColumnDto(string Key, string Label);

/// <summary>Signed changes in currency units; credits increase equity.</summary>
public sealed class EquityStatementRowDto
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public Dictionary<string, decimal> Amounts { get; set; } = new();
    public decimal Total { get; set; }
}
