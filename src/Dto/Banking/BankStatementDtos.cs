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
        /// <summary>
        /// 0-based record index of the header when <see cref="HasHeaderRow"/> is true.
        /// Null → detect the first header-like row (skipping bank-info preamble).
        /// </summary>
        public int? HeaderRowIndex { get; set; }
        /// <summary>Single character; null/empty auto-detects comma, semicolon, or tab from the first row.</summary>
        public string? Delimiter { get; set; }
        /// <summary>
        /// Optional .NET date format such as <c>dd/MM/yyyy</c>. Wins over detection.
        /// Required when the date column is ambiguous (every part ≤ 12).
        /// </summary>
        public string? DateFormat { get; set; }
        /// <summary>
        /// Decimal separator for amount columns: <c>.</c> or <c>,</c>. Wins over detection.
        /// Required when the amount column is ambiguous (e.g. only <c>1.500</c>-style values).
        /// </summary>
        public string? DecimalSeparator { get; set; }
        public int DateColumn { get; set; }
        public int? AmountColumn { get; set; }
        public int? DepositColumn { get; set; }
        public int? WithdrawalColumn { get; set; }
        /// <summary>Optional fee column (e.g. Revolut); subtracted from the amount magnitude.</summary>
        public int? FeeColumn { get; set; }
        /// <summary>Optional direction column when the amount is unsigned (<c>in</c>/<c>out</c>, <c>CR</c>/<c>DR</c>, …).</summary>
        public int? DirectionColumn { get; set; }
        /// <summary>Optional overrides for direction tokens → +1 (in) or −1 (out).</summary>
        public Dictionary<string, int>? DirectionValueMap { get; set; }
        public int? PayeeColumn { get; set; }
        public int? MemoColumn { get; set; }
        public int? ReferenceColumn { get; set; }
        /// <summary>Optional bank transaction id column; enables exact dedupe across overlapping files.</summary>
        public int? ExternalIdColumn { get; set; }
        /// <summary>Optional ISO currency column; rows that differ from the bank account are skipped.</summary>
        public int? CurrencyColumn { get; set; }
        /// <summary>Optional status column; only <see cref="AllowedStatuses"/> (default completed/posted/booked/settled/succeeded/paid/cleared) import.</summary>
        public int? StatusColumn { get; set; }
        public List<string>? AllowedStatuses { get; set; }
        /// <summary>Flip signs for statements that list payments as positive (for example credit cards).</summary>
        public bool InvertSign { get; set; }
    }

    public class BankStatementImportRequest
    {
        /// <summary>1 CSV, 2 OFX/QFX.</summary>
        public int Source { get; set; }
        public string? FileName { get; set; }
        /// <summary>File text (UTF-8). Ignored when <see cref="ContentBase64"/> is set.</summary>
        public string Content { get; set; } = string.Empty;
        /// <summary>
        /// Raw file bytes as base64. Preferred for CSV so the server can detect UTF-8 / UTF-16 / Windows-1252 (#447).
        /// </summary>
        public string? ContentBase64 { get; set; }
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

    public class BankStatementDetectionSummaryDto
    {
        public string? Date { get; set; }
        public string? Amount { get; set; }
        public string? Currency { get; set; }
        public string? Status { get; set; }
        public string? Header { get; set; }
        public bool NeedsDateFormatChoice { get; set; }
        public bool NeedsAmountFormatChoice { get; set; }
    }

    public class BankStatementPreviewDto
    {
        public int Source { get; set; }
        public int TotalRows { get; set; }
        public int ReadyRows { get; set; }
        public int DuplicateRows { get; set; }
        public int ErrorRows { get; set; }
        /// <summary>Per-column detection notes (#447). Null for OFX.</summary>
        public BankStatementDetectionSummaryDto? Detection { get; set; }
        /// <summary>saved | preset | request | none — where the column map came from (#447 Phase B).</summary>
        public string? MappingSource { get; set; }
        public string? HeaderSignature { get; set; }
        public string? PresetName { get; set; }
        /// <summary>First rows for display; counts above cover the whole file.</summary>
        public IReadOnlyList<BankStatementPreviewRowDto> Rows { get; set; } = Array.Empty<BankStatementPreviewRowDto>();
    }

    /// <summary>Resolve a saved map or built-in preset for a CSV before preview (#447 Phase B).</summary>
    public class BankStatementResolveMappingDto
    {
        public BankStatementColumnMap? ColumnMap { get; set; }
        /// <summary>saved | preset | none</summary>
        public string MappingSource { get; set; } = "none";
        public string? HeaderSignature { get; set; }
        public string? PresetName { get; set; }
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
        public bool IsExcluded { get; set; }
        public DateTime? ExcludedAt { get; set; }
        public string? ExcludedBy { get; set; }
        /// <summary>1 Personal, 2 TransferBetweenAccounts, 3 Duplicate, 4 Other.</summary>
        public int? ExclusionReason { get; set; }
        public string? ExclusionNote { get; set; }
        /// <summary>Unmatched lines only: uncleared ledger lines within the match window.</summary>
        public IReadOnlyList<BankStatementMatchCandidateDto> Candidates { get; set; } = Array.Empty<BankStatementMatchCandidateDto>();
    }

    public class BankStatementLineListDto
    {
        public IReadOnlyList<BankStatementLineDto> Lines { get; set; } = Array.Empty<BankStatementLineDto>();
        public int UnmatchedCount { get; set; }
        public int MatchedCount { get; set; }
        public int ExcludedCount { get; set; }
        /// <summary>Rows in the filtered status set before paging.</summary>
        public int TotalCount { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
    }

    public class ExcludeBankStatementLineRequest
    {
        /// <summary>1 Personal, 2 TransferBetweenAccounts, 3 Duplicate, 4 Other.</summary>
        public int Reason { get; set; }
        public string? Note { get; set; }
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
