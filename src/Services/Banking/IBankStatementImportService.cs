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

        /// <summary>
        /// Look up a saved mapping or built-in preset for this bank + CSV header.
        /// Never skips preview — callers still must confirm (#447 Phase B).
        /// </summary>
        BankStatementResolveMappingDto ResolveMapping(int bankId, BankStatementImportRequest request);

        /// <summary>Store new statement lines (deduped by ExternalId) and auto-match unique candidates.</summary>
        BankStatementCommitResultDto Commit(int bankId, BankStatementImportRequest request, string actor);

        /// <param name="status">unmatched (default), matched, excluded, or all.</param>
        BankStatementLineListDto ListLines(int bankId, string? status, int page = 1, int pageSize = BankStatementImportService.DefaultListPageSize);

        BankStatementLineDto Match(int lineId, MatchBankStatementLineRequest request);
        BankStatementLineDto Unmatch(int lineId);

        /// <summary>Mark a line excluded (posts nothing). Matched lines must be unmatched first.</summary>
        BankStatementLineDto Exclude(int lineId, ExcludeBankStatementLineRequest request, string actor);

        /// <summary>Clear exclusion and return the line to Unmatched.</summary>
        BankStatementLineDto Include(int lineId);

        /// <summary>Post a bank transaction for an unmatched line and link the new ledger line to it.</summary>
        BankStatementLineDto CreateTransaction(int lineId, CreateTransactionFromStatementLineRequest request);
    }
}
