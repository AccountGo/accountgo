// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using Dto.Banking;
using System.Collections.Generic;

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

        /// <summary>
        /// Re-reads a file and lists statement lines already stored for rows the current rule would skip.
        /// Writes nothing.
        /// </summary>
        WiseConversionImpactReportDto ReportWiseConversionImpact(int bankId, BankStatementImportRequest request);

        /// <param name="status">unmatched (default), matched, excluded, or all.</param>
        BankStatementLineListDto ListLines(int bankId, string? status, int page = 1, int pageSize = BankStatementImportService.DefaultListPageSize, int? lineId = null);

        BankStatementLineDto Match(int lineId, MatchBankStatementLineRequest request);
        BankStatementLineDto Unmatch(int lineId);
        /// <summary>Reverse a matched owner drawing or contribution and clear the statement match.</summary>
        BankStatementLineDto UndoOwnerMovement(int lineId, string? reason);

        /// <summary>Mark a line excluded (posts nothing). Matched lines must be unmatched first.</summary>
        BankStatementLineDto Exclude(int lineId, ExcludeBankStatementLineRequest request, string actor);

        /// <summary>Clear exclusion and return the line to Unmatched.</summary>
        /// <summary>Excludes every unmatched line dated before the books start date; returns how many.</summary>
        ExcludeBankStatementLineResultDto ExcludeBeforeBooksStart(int bankId, string actor);

        BankStatementLineDto Include(int lineId);

        /// <summary>Available owner actions using current, postable workspace equity accounts.</summary>
        StatementOwnerActionsDto GetOwnerActions();

        /// <summary>Validate a same-direction selection, post each line and optionally remember its description.</summary>
        IReadOnlyList<BankStatementLineDto> RecordOwnerMovement(IReadOnlyList<int> lineIds, string? memo, bool rememberRule);

        /// <summary>Post a bank transaction for an unmatched line and link the new ledger line to it.</summary>
        BankStatementLineDto CreateTransaction(int lineId, CreateTransactionFromStatementLineRequest request);
    }
}
