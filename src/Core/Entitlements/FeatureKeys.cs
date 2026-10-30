// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable

namespace Core.Entitlements
{
    /// <summary>Hosted feature keys that appear in entitlement snapshots.</summary>
    public static class FeatureKeys
    {
        /// <summary>
        /// CSV/OFX statement import and matching (#436). Hosted paid Invoicing and Accounting plans grant this key;
        /// community edition is allowed by the compound gate without emitting it.
        /// </summary>
        public const string BankingStatementImport = "banking.statement_import";
    }

    /// <summary>Well-known plan keys shared by community and hosted entitlement engines.</summary>
    public static class PlanKeys
    {
        public const string Community = "community";
    }
}
