// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using Dto.Banking;

namespace Services.Banking
{
    /// <summary>
    /// Built-in starting maps keyed by header signature. Presets are suggestions only — never skip preview (#447 Phase B).
    /// </summary>
    public static class StatementImportPresets
    {
        public sealed class Preset
        {
            public required string Name { get; init; }
            public required string HeaderSignature { get; init; }
            public required Func<BankStatementColumnMap> CreateMap { get; init; }
        }

        private static readonly Preset[] All =
        {
            new()
            {
                Name = "Wise activity",
                HeaderSignature = StatementHeaderSignature.FromHeaders(new[]
                {
                    "transfer_id", "activity_id", "created_on", "finished_on", "status",
                    "direction", "amount", "currency", "source_name", "target_name"
                }),
                CreateMap = () => new BankStatementColumnMap
                {
                    HasHeaderRow = true,
                    DateColumn = 2,
                    ExternalIdColumn = 1,
                    StatusColumn = 4,
                    DirectionColumn = 5,
                    AmountColumn = 6,
                    CurrencyColumn = 7,
                    PayeeColumn = 8,
                    MemoColumn = 9
                }
            },
            new()
            {
                Name = "PayPal activity",
                HeaderSignature = StatementHeaderSignature.FromHeaders(new[]
                {
                    "date", "time", "time_zone", "name", "type", "status", "currency", "gross", "fee", "net"
                }),
                CreateMap = () => new BankStatementColumnMap
                {
                    HasHeaderRow = true,
                    DateColumn = 0,
                    PayeeColumn = 3,
                    StatusColumn = 5,
                    CurrencyColumn = 6,
                    AmountColumn = 7
                }
            },
            new()
            {
                Name = "Revolut statement",
                HeaderSignature = StatementHeaderSignature.FromHeaders(new[]
                {
                    "type", "product", "started_date", "completed_date", "description",
                    "amount", "fee", "currency", "state", "balance"
                }),
                CreateMap = () => new BankStatementColumnMap
                {
                    HasHeaderRow = true,
                    DateColumn = 3,
                    PayeeColumn = 4,
                    AmountColumn = 5,
                    CurrencyColumn = 7,
                    StatusColumn = 8
                }
            }
        };

        public static Preset? Find(string headerSignature)
        {
            if (string.IsNullOrWhiteSpace(headerSignature))
                return null;
            foreach (var preset in All)
            {
                if (string.Equals(preset.HeaderSignature, headerSignature, StringComparison.Ordinal))
                    return preset;
            }
            return null;
        }

        public static IReadOnlyList<Preset> List() => All;
    }
}
