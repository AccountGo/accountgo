// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;

namespace Services.Banking
{
    /// <summary>HTTP 409 conflicts for draft resume / concurrent finish / latest-only undo.</summary>
    public sealed class BankReconciliationConflictException : InvalidOperationException
    {
        public const string DraftExistsCode = "DRAFT_EXISTS";
        public const string ConcurrentFinishCode = "CONCURRENT_FINISH";
        public const string NotLatestCode = "NOT_LATEST";

        public BankReconciliationConflictException(string code, string message, int? reconciliationId = null)
            : base(message)
        {
            Code = code;
            ReconciliationId = reconciliationId;
        }

        public string Code { get; }
        public int? ReconciliationId { get; }
    }
}
