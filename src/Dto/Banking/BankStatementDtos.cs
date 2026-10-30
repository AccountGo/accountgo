// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;

namespace Dto.Banking
{
    /// <summary>
    /// CSV column positions (0-based). Either <see cref="AmountColumn"/> (signed) or the
    /// <see cref="DepositColumn"/> / <see cref="WithdrawalColumn"/> pair must be mapped.
    /// </summary>
    public class BankStatementColumnMap
    {
        public bool HasHeaderRow { get; set; } = true;
        /// <summary>Single character; null/empty auto-detects comma, semicolon, or tab from the first row.</summary>
        public string? Delimiter { get; set; }
        /// <summary>Optional .NET date format such as <c>dd/MM/yyyy</c>. Default accepts yyyy-MM-dd and M/d/yyyy.</summary>
        public string? DateFormat { get; set; }
        public int DateColumn { get; set; }
        public int? AmountColumn { get; set; }
        public int? DepositColumn { get; set; }
        public int? WithdrawalColumn { get; set; }
        public int? PayeeColumn { get; set; }
        public int? MemoColumn { get; set; }
        public int? ReferenceColumn { get; set; }
        /// <summary>Optional bank transaction id column; enables exact dedupe across overlapping files.</summary>
        public int? ExternalIdColumn { get; set; }
        /// <summary>Flip signs for statements that list payments as positive (for example credit cards).</summary>
        public bool InvertSign { get; set; }
    }

    public class BankStatementImportRequest
    {
        /// <summary>1 CSV, 2 OFX/QFX.</summary>
        public int Source { get; set; }
        public string? FileName { get; set; }
        /// <summary>File text.</summary>
        public string Content { get; set; } = string.Empty;
        /// <summary>Required for CSV.</summary>
        public BankStatementColumnMap? ColumnMap { get; set; }
    }

    public class BankStatementPreviewRowDto
    {
        public int RowNumber { get; set; }
        public DateTime? PostedDate { get; set; }
        public decimal? Amount { get; set; }
        public string? Payee { get; set; }
        public string? Memo { get; set; }
        public string? Reference { get; set; }
        public string? ExternalId { get; set; }
        /// <summary>ok | duplicate | error</summary>
        public string Status { get; set; } = "ok";
        public string? Error { get; set; }
    }

    public class BankStatementPreviewDto
    {
        public int Source { get; set; }
        public int TotalRows { get; set; }
        public int ReadyRows { get; set; }
        public int DuplicateRows { get; set; }
        public int ErrorRows { get; set; }
        /// <summary>First rows for display; counts above cover the whole file.</summary>
        public IReadOnlyList<BankStatementPreviewRowDto> Rows { get; set; } = Array.Empty<BankStatementPreviewRowDto>();
    }

    public class BankStatementCommitResultDto
    {
        public int ImportId { get; set; }
        public int Imported { get; set; }
        public int Duplicates { get; set; }
        public int Errors { get; set; }
        public int AutoMatched { get; set; }
    }

    public class BankStatementMatchCandidateDto
    {
        public int GeneralLedgerLineId { get; set; }
        public int GeneralLedgerHeaderId { get; set; }
        public DateTime Date { get; set; }
        public string? Description { get; set; }
        /// <summary>Signed like the statement: positive deposit, negative payment.</summary>
        public decimal Amount { get; set; }
    }

    public class BankStatementLineDto
    {
        public int Id { get; set; }
        public int BankId { get; set; }
        public int BankStatementImportId { get; set; }
        public int Source { get; set; }
        public string? ExternalId { get; set; }
        public DateTime PostedDate { get; set; }
        public decimal Amount { get; set; }
        public string? Payee { get; set; }
        public string? Memo { get; set; }
        public string? Reference { get; set; }
        public int? MatchedGeneralLedgerLineId { get; set; }
        public bool IsMatched { get; set; }
        /// <summary>Unmatched lines only: uncleared ledger lines within the match window.</summary>
        public IReadOnlyList<BankStatementMatchCandidateDto> Candidates { get; set; } = Array.Empty<BankStatementMatchCandidateDto>();
    }

    public class BankStatementLineListDto
    {
        public IReadOnlyList<BankStatementLineDto> Lines { get; set; } = Array.Empty<BankStatementLineDto>();
        public int UnmatchedCount { get; set; }
        public int MatchedCount { get; set; }
        /// <summary>Rows in the filtered status set before paging.</summary>
        public int TotalCount { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
    }

    public class MatchBankStatementLineRequest
    {
        /// <summary>When omitted the service auto-matches using the unique-candidate rule.</summary>
        public int? GeneralLedgerLineId { get; set; }
    }

    public class CreateTransactionFromStatementLineRequest
    {
        /// <summary>1 MoneyIn, 2 MoneyOut, 4 Fee, 5 Interest, 6 Adjustment.</summary>
        public int Kind { get; set; }
        public int? ContraAccountId { get; set; }
        public string? Memo { get; set; }
    }
}
