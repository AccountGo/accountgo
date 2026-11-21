// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using Dto.Common;
using System.Collections.Generic;

namespace Dto.Banking
{
    public class StatementDescriptionRuleDto
    {
        [PublicId("bsdr")]
        public int Id { get; set; }
        public string DescriptionKey { get; set; } = string.Empty;
        [PublicId("ven")]
        public int? VendorId { get; set; }
        public string? VendorName { get; set; }
        [PublicId("acct")]
        public int? ExpenseAccountId { get; set; }
        public string? ExpenseAccountName { get; set; }
    }

    /// <summary>Bulk record expense: one paid bill per line (#436 I4).</summary>
    public class BulkRecordExpenseFromStatementLinesRequest
    {
        [PublicId("bsl")]
        public IReadOnlyList<int> LineIds { get; set; } = Array.Empty<int>();
        [PublicId("ven")]
        public int? VendorId { get; set; }
        public QuickVendorFromStatementRequest? NewVendor { get; set; }
        [PublicId("acct")]
        public int ExpenseAccountId { get; set; }
        [PublicId("tax")]
        public int? TaxId { get; set; }
        public string? PaymentMethod { get; set; }
        public string? Memo { get; set; }
        /// <summary>When true, upsert a description → vendor + category rule from the first line's payee.</summary>
        public bool RememberRule { get; set; } = true;
    }

    public class BulkRecordExpenseFromStatementLinesResultDto
    {
        public IReadOnlyList<RecordExpenseFromStatementLineResultDto> Results { get; set; } =
            Array.Empty<RecordExpenseFromStatementLineResultDto>();
        public StatementDescriptionRuleDto? Rule { get; set; }
    }
}
