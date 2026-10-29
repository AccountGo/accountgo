// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;

namespace Dto.Banking
{
    public class StartBankReconciliationRequest
    {
        public int BankId { get; set; }
        public DateTime StatementEndDate { get; set; }
        public decimal StatementEndingBalance { get; set; }
    }

    public class UpdateBankReconciliationItemsRequest
    {
        public IReadOnlyList<int> Add { get; set; } = Array.Empty<int>();
        public IReadOnlyList<int> Remove { get; set; } = Array.Empty<int>();
        public long Version { get; set; }
    }

    public class FinishBankReconciliationRequest
    {
        public long Version { get; set; }
    }

    public class UndoBankReconciliationRequest
    {
        public string Reason { get; set; } = string.Empty;
    }

    public class CreateReconciliationAdjustmentRequest
    {
        /// <summary>4 Fee, 5 Interest, 6 Adjustment.</summary>
        public int Kind { get; set; }
        public decimal Amount { get; set; }
        public DateTime Date { get; set; }
        public int? ContraAccountId { get; set; }
        /// <summary>Required when Kind is Adjustment.</summary>
        public bool? IncreaseBalance { get; set; }
        public string? Memo { get; set; }
        public string? ReferenceNo { get; set; }
        public long Version { get; set; }
    }

    public class BankReconciliationListItemDto
    {
        public int Id { get; set; }
        public int BankId { get; set; }
        public string? BankName { get; set; }
        public DateTime StatementEndDate { get; set; }
        public decimal StatementEndingBalance { get; set; }
        public decimal BeginningBalance { get; set; }
        public int Status { get; set; }
        public string StatusLabel { get; set; } = string.Empty;
        public DateTime? FinishedAt { get; set; }
        public string? FinishedBy { get; set; }
        public string? ReviewedBy { get; set; }
        public DateTime? ReviewedAt { get; set; }
        public long Version { get; set; }
    }

    public class BankReconciliationCandidateLineDto
    {
        public int LineId { get; set; }
        public int HeaderId { get; set; }
        public DateTime Date { get; set; }
        public string Type { get; set; } = string.Empty;
        public string? Payee { get; set; }
        public string? Reference { get; set; }
        public decimal In { get; set; }
        public decimal Out { get; set; }
        public bool Ticked { get; set; }
        public bool IsOpeningBalance { get; set; }
    }

    public class BankReconciliationDto
    {
        public int Id { get; set; }
        public int BankId { get; set; }
        public string? BankName { get; set; }
        public DateTime StatementEndDate { get; set; }
        public decimal StatementEndingBalance { get; set; }
        public decimal BeginningBalance { get; set; }
        public int Status { get; set; }
        public string StatusLabel { get; set; } = string.Empty;
        public long Version { get; set; }
        public decimal ClearedDeposits { get; set; }
        public decimal ClearedPayments { get; set; }
        public decimal ClearedBalance { get; set; }
        public decimal Difference { get; set; }
        public string? CreatedBy { get; set; }
        public DateTime CreatedAt { get; set; }
        public string? FinishedBy { get; set; }
        public DateTime? FinishedAt { get; set; }
        public string? ReviewedBy { get; set; }
        public DateTime? ReviewedAt { get; set; }
        public IReadOnlyList<BankReconciliationCandidateLineDto> Deposits { get; set; } = Array.Empty<BankReconciliationCandidateLineDto>();
        public IReadOnlyList<BankReconciliationCandidateLineDto> Payments { get; set; } = Array.Empty<BankReconciliationCandidateLineDto>();
    }
}
