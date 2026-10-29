// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using Core.Domain.Financials;
using Dto.Banking;

namespace Services.Banking
{
    public interface IBankAccountService
    {
        BankAccountsListDto List(bool includeArchived, bool includeFullNumbers, bool includeCashHistoryCodes);
        BankAccountDto Get(int id, bool includeFullNumbers);
        BankRegisterDto GetRegister(int bankId, DateTime? from, DateTime? to, int page, int pageSize);

        /// <summary>Uncleared ledger lines dated on or before <paramref name="asOf"/> (default today), bucketed by age in days (3B).</summary>
        OutstandingAgingDto GetOutstandingAging(int bankId, DateTime? asOf);
        IReadOnlyList<BankAccountPickerItemDto> Picker(bool includeOtherCashAccounts);
        IReadOnlyList<UnassignedCashDto> GetCashHistory(bool includeCodes);
        IReadOnlyList<object> EligiblePostingAccounts();
        BankAccountDto Create(CreateBankAccountRequest request, bool includeFullNumbers);
        BankAccountDto Update(int id, UpdateBankAccountRequest request, bool includeFullNumbers);
        BankAccountDto Archive(int id, bool includeFullNumbers, bool confirmNonZeroBalance = false);
        BankAccountDto Unarchive(int id, bool includeFullNumbers);
        BankAccountDto SetDefault(int id, string forKind, bool includeFullNumbers);
        /// <summary>
        /// Hard-delete the bank row when it has no GL history; otherwise archives (D9).
        /// Never deletes the linked GL account in 1B (takeover renames make "dedicated" unsafe).
        /// Returns <c>true</c> when the bank was archived instead of deleted.
        /// </summary>
        bool DeleteOrArchive(int id);
        Account EnsureEligiblePostingAccount(int accountId, int? exceptBankId);
        void SyncBankNameFromAccount(Account account);
        void EnsureAccountMayChangeWhileBackingBank(Account account, Account? proposedParent, string? proposedCode, bool becomingSummary, bool archiving);

        /// <summary>
        /// GL cash account to debit for an invoice/customer receipt (1C).
        /// Explicit bank → receipt default → unlinked cash only when no banks exist.
        /// </summary>
        int ResolveDepositAccountId(int? bankAccountId);

        /// <summary>
        /// GL cash account to credit for a bill payment (1C).
        /// Explicit bank → optional Accounting GL account (Financials only) → payment default → unlinked cash only when no banks exist.
        /// </summary>
        int ResolvePaymentAccountId(int? bankAccountId, int? accountId);

        /// <summary>Active bank used as AccountToDebitId on legacy sales receipts; refuses archived banks.</summary>
        Bank RequireActiveBank(int bankId);
    }
}
