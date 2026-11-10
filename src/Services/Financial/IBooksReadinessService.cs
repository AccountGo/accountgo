// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;

namespace Services.Financial
{
    /// <summary>
    /// Checks that every issued invoice and approved bill in the current workspace has its ledger entry,
    /// so general-ledger reports agree with receivables and payables when the Financials module turns on.
    /// </summary>
    public interface IBooksReadinessService
    {
        BooksReadiness Check();
    }

    public sealed record UnpostedDocument(string Type, int Id, string? Number, DateTime Date);

    public sealed record BooksReadiness(IReadOnlyList<UnpostedDocument> UnpostedInvoices, IReadOnlyList<UnpostedDocument> UnpostedBills)
    {
        public bool IsReady => UnpostedInvoices.Count == 0 && UnpostedBills.Count == 0;
    }
}
