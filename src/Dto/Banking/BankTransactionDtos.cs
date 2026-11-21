// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using Dto.Common;
using System.Collections.Generic;

namespace Dto.Banking
{
    public class BankTransactionDto
    {
        [PublicId("btx")]
        public int Id { get; set; }
        public int Kind { get; set; }
        public string KindLabel { get; set; } = string.Empty;
        [PublicId("ba")]
        public int BankId { get; set; }
        public string? BankName { get; set; }
        [PublicId("ba")]
        public int? ToBankId { get; set; }
        public string? ToBankName { get; set; }
        public DateTime Date { get; set; }
        public decimal Amount { get; set; }
        [PublicId("acct")]
        public int? ContraAccountId { get; set; }
        public string? ContraAccountCode { get; set; }
        public string? ContraAccountName { get; set; }
        [PublicId("pty")]
        public int? PartyId { get; set; }
        public string? PartyName { get; set; }
        public string No { get; set; } = string.Empty;
        public string? ReferenceNo { get; set; }
        public string? Memo { get; set; }
        [PublicId("gl")]
        public int? GeneralLedgerHeaderId { get; set; }
        public bool IsVoid { get; set; }
        [PublicId("gl")]
        public int? VoidGeneralLedgerHeaderId { get; set; }
    }

    public class CreateBankTransactionRequest
    {
        /// <summary>1 MoneyIn, 2 MoneyOut, 3 Transfer, 4 Fee, 5 Interest, 6 Adjustment. Opening balance is set on the bank account.</summary>
        public int Kind { get; set; }
        [PublicId("ba")]
        public int BankId { get; set; }
        [PublicId("ba")]
        public int? ToBankId { get; set; }
        public DateTime Date { get; set; }
        public decimal Amount { get; set; }
        [PublicId("acct")]
        public int? ContraAccountId { get; set; }
        [PublicId("pty")]
        public int? PartyId { get; set; }
        public string? ReferenceNo { get; set; }
        public string? Memo { get; set; }
        /// <summary>
        /// Required for <c>Adjustment</c>: true increases the bank balance (DR bank), false decreases it (CR bank).
        /// Ignored for other kinds (Fee/MoneyOut decrease; Interest/MoneyIn increase).
        /// </summary>
        public bool? IncreaseBalance { get; set; }
    }

    public class VoidBankTransactionRequest
    {
        public DateTime? VoidDate { get; set; }
        public string? Reason { get; set; }
    }

    public class BankTransactionListDto
    {
        public IReadOnlyList<BankTransactionDto> Transactions { get; set; } = Array.Empty<BankTransactionDto>();
    }
}
