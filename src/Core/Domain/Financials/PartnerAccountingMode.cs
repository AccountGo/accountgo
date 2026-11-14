// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using Core.Domain;

namespace Core.Domain.Financials;

/// <summary>Identifies workspaces using partner-specific capital and drawings accounts.</summary>
public static class PartnerAccountingMode
{
    /// <summary>Returns true after partner accounts have been set up for a partnership.</summary>
    public static bool IsEnabled(CompanySetting? setting) =>
        setting?.EntityType == EntityType.Partnership && setting.PartnerAccountsFrom.HasValue;
}
