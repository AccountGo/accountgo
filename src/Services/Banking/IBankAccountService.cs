// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System.Collections.Generic;
using Core.Domain.Financials;
using Dto.Banking;

namespace Services.Banking
{
    public interface IBankAccountService
    {
        BankAccountsListDto List(bool includeArchived, bool includeFullNumbers);
        BankAccountDto Get(int id, bool includeFullNumbers);
        IReadOnlyList<BankAccountPickerItemDto> Picker(bool includeOtherCashAccounts);
        IReadOnlyList<UnassignedCashDto> GetCashHistory(bool includeCodes);
        IReadOnlyList<object> EligiblePostingAccounts();
        BankAccountDto Create(CreateBankAccountRequest request, bool includeFullNumbers);
        BankAccountDto Update(int id, UpdateBankAccountRequest request, bool includeFullNumbers);
        BankAccountDto Archive(int id, bool includeFullNumbers);
        BankAccountDto Unarchive(int id, bool includeFullNumbers);
        BankAccountDto SetDefault(int id, string forKind, bool includeFullNumbers);
        /// <summary>Hard-delete only when there is no GL / bank-transaction history; otherwise archives (D9).</summary>
        void DeleteOrArchive(int id);
        Account EnsureEligiblePostingAccount(int accountId, int? exceptBankId);
        void SyncBankNameFromAccount(Account account);
        void EnsureAccountMayChangeWhileBackingBank(Account account, Account? proposedParent, string? proposedCode, bool becomingSummary, bool archiving);
    }
}
