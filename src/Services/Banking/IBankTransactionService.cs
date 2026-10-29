// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using Dto.Banking;

namespace Services.Banking
{
    public interface IBankTransactionService
    {
        IReadOnlyList<BankTransactionDto> List(int bankId, DateTime? from, DateTime? to);
        BankTransactionDto Get(int id);
        BankTransactionDto Create(CreateBankTransactionRequest request);
        BankTransactionDto Void(int id, VoidBankTransactionRequest request);
    }
}
