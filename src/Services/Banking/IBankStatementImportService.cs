// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using Dto.Banking;

namespace Services.Banking
{
    public interface IBankStatementImportService
    {
        /// <summary>Parse a file and report what a commit would do. Writes nothing.</summary>
        BankStatementPreviewDto Preview(int bankId, BankStatementImportRequest request);

        /// <summary>Store new statement lines (deduped by ExternalId) and auto-match unique candidates.</summary>
        BankStatementCommitResultDto Commit(int bankId, BankStatementImportRequest request, string actor);

        /// <param name="status">unmatched (default), matched, or all.</param>
        BankStatementLineListDto ListLines(int bankId, string? status);

        BankStatementLineDto Match(int lineId, MatchBankStatementLineRequest request);
        BankStatementLineDto Unmatch(int lineId);

        /// <summary>Post a bank transaction for an unmatched line and link the new ledger line to it.</summary>
        BankStatementLineDto CreateTransaction(int lineId, CreateTransactionFromStatementLineRequest request);
    }
}
