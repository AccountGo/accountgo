// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Globalization;
using System.Linq;
using Core.Domain;
using Core.Domain.Financials;

namespace Services.Banking
{
    public partial class BankAccountService
    {
        internal const string OpeningBalanceDescription = "Opening balance";
        private const string OpeningBalanceEquityCode = "30700";

        /// <summary>
        /// Stores opening balance on the bank; posts DR bank / CR 30700 (or reverse if negative)
        /// unless the workspace is cash basis. Phase 1 keeps the GL header id on Bank (2A moves it to BankTransaction).
        /// </summary>
        private void ApplyOpeningBalance(
            Bank bank,
            decimal? amount,
            DateTime? asOfDate,
            bool accountHasHistory,
            bool omittedMeansNoChange)
        {
            if (omittedMeansNoChange && !amount.HasValue && !asOfDate.HasValue)
                return;

            if (asOfDate.HasValue && !amount.HasValue)
                throw new InvalidOperationException("Opening balance amount is required when a date is set.");

            var rounded = amount.HasValue
                ? decimal.Round(amount.Value, 2, MidpointRounding.AwayFromZero)
                : 0m;

            if (accountHasHistory && rounded != 0m)
                throw new InvalidOperationException(
                    "This account already has ledger history; leave the opening balance empty.");

            var settings = _companySettings.Table.FirstOrDefault();
            var booksStart = settings?.BooksStartDate.Date ?? DateTime.UtcNow.Date;
            var date = (asOfDate ?? booksStart).Date;

            var previousAmount = bank.OpeningBalance;
            var previousDate = bank.OpeningBalanceDate?.Date;
            var previousHeaderId = bank.OpeningBalanceGeneralLedgerHeaderId;

            if (previousAmount == rounded
                && previousDate == date
                && (rounded == 0m || previousHeaderId.HasValue || IsCashBasis(settings)))
            {
                bank.OpeningBalance = rounded == 0m ? null : rounded;
                bank.OpeningBalanceDate = rounded == 0m ? null : date;
                return;
            }

            if (previousHeaderId.HasValue)
            {
                var reversal = _financial.CreateReversingEntry(
                    previousHeaderId.Value,
                    $"Reversal of opening balance for {bank.Name}");
                _financial.SaveGeneralLedgerEntry(reversal);
                bank.OpeningBalanceGeneralLedgerHeaderId = null;
            }

            bank.OpeningBalance = rounded == 0m ? null : rounded;
            bank.OpeningBalanceDate = rounded == 0m ? null : date;

            if (rounded == 0m || IsCashBasis(settings))
                return;

            if (!bank.AccountId.HasValue)
                throw new InvalidOperationException("That bank account has no posting account.");

            var equity = _financial.GetAccountByAccountCode(OpeningBalanceEquityCode)
                ?? throw new InvalidOperationException("Opening Balance Equity (30700) is not set up.");

            // Positive opening balance: money in the bank (DR bank / CR equity). Negative = overdraft (CR bank / DR equity).
            var bankSide = rounded > 0m ? DrOrCrSide.Dr : DrOrCrSide.Cr;
            var equitySide = rounded > 0m ? DrOrCrSide.Cr : DrOrCrSide.Dr;
            var abs = Math.Abs(rounded);

            var header = _financial.CreateGeneralLedgerHeader(
                DocumentTypes.JournalEntry,
                date,
                OpeningBalanceDescription,
                sourceDocumentId: bank.Id);
            header.GeneralLedgerLines.Add(_financial.CreateGeneralLedgerLine(bankSide, bank.AccountId.Value, abs));
            header.GeneralLedgerLines.Add(_financial.CreateGeneralLedgerLine(equitySide, equity.Id, abs));
            _financial.SaveGeneralLedgerEntry(header);
            bank.OpeningBalanceGeneralLedgerHeaderId = header.Id;
        }

        private static bool IsCashBasis(CompanySetting? settings) =>
            settings != null && settings.AccountingBasis == AccountingBasis.Cash;
    }
}
