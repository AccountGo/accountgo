// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;

namespace Dto.Banking
{
    public class BankAccountDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int Type { get; set; }
        public string? AccountNumberMasked { get; set; }
        public string? AccountNumber { get; set; }
        public string? BankName { get; set; }
        public string? RoutingNumber { get; set; }
        public string? Iban { get; set; }
        public string? Swift { get; set; }
        public int? AccountId { get; set; }
        public string? AccountCode { get; set; }
        public string? AccountName { get; set; }
        public bool IsDefaultForReceipts { get; set; }
        public bool IsDefaultForPayments { get; set; }
        public decimal BookBalance { get; set; }
        public DateTime? ReconciledThrough { get; set; }
        public DateTime? ArchivedAt { get; set; }
        public decimal? OpeningBalance { get; set; }
        public DateTime? OpeningBalanceDate { get; set; }
        public string? CurrencyCode { get; set; }
    }

    public class BankAccountPickerItemDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? AccountNumberMasked { get; set; }
        public bool IsDefaultForReceipts { get; set; }
        public bool IsDefaultForPayments { get; set; }
        public string? Group { get; set; }
        public int? AccountId { get; set; }
    }

    public class UnassignedCashDto
    {
        public int AccountId { get; set; }
        public string? AccountCode { get; set; }
        public string? AccountName { get; set; }
        public string Description { get; set; } = string.Empty;
        public int ReceiptCount { get; set; }
        public int PaymentCount { get; set; }
        public int OtherLineCount { get; set; }
        public decimal Balance { get; set; }
    }

    public class BankAccountsListDto
    {
        public IReadOnlyList<BankAccountDto> Accounts { get; set; } = Array.Empty<BankAccountDto>();
        public IReadOnlyList<UnassignedCashDto> UnassignedCash { get; set; } = Array.Empty<UnassignedCashDto>();
    }

    public class CreateBankAccountRequest
    {
        public string Name { get; set; } = string.Empty;
        public int Type { get; set; } = 1;
        public string? BankName { get; set; }
        public string? AccountNumber { get; set; }
        public string? RoutingNumber { get; set; }
        public string? Iban { get; set; }
        public string? Swift { get; set; }
        public bool IsDefaultForReceipts { get; set; }
        public bool IsDefaultForPayments { get; set; }
        public decimal? OpeningBalance { get; set; }
        public DateTime? OpeningBalanceDate { get; set; }
        public int? AdoptAccountId { get; set; }
        public int? PostingAccountId { get; set; }
        public string? CurrencyCode { get; set; }
    }

    public class UpdateBankAccountRequest
    {
        public string Name { get; set; } = string.Empty;
        public int Type { get; set; }
        public string? BankName { get; set; }
        public string? AccountNumber { get; set; }
        public string? RoutingNumber { get; set; }
        public string? Iban { get; set; }
        public string? Swift { get; set; }
        public bool IsDefaultForReceipts { get; set; }
        public bool IsDefaultForPayments { get; set; }
        public int? PostingAccountId { get; set; }
        public string? CurrencyCode { get; set; }
        public decimal? OpeningBalance { get; set; }
        public DateTime? OpeningBalanceDate { get; set; }
    }

    public class BankRegisterDto
    {
        public decimal BroughtForward { get; set; }
        public IReadOnlyList<BankRegisterLineDto> Lines { get; set; } = Array.Empty<BankRegisterLineDto>();
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalCount { get; set; }
    }

    public class BankRegisterLineDto
    {
        public int LineId { get; set; }
        public int HeaderId { get; set; }
        public DateTime Date { get; set; }
        public string Type { get; set; } = string.Empty;
        public string? Payee { get; set; }
        public string? Reference { get; set; }
        public BankRegisterSourceDocDto? SourceDoc { get; set; }
        public decimal In { get; set; }
        public decimal Out { get; set; }
        public decimal Balance { get; set; }
        public int? ReconciliationId { get; set; }
    }

    public class BankRegisterSourceDocDto
    {
        public string Type { get; set; } = string.Empty;
        public int Id { get; set; }
        public string? No { get; set; }
    }
}
