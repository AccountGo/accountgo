// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
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
        /// Stores opening balance on the bank and posts DR bank / CR 30700 (or reverse if negative),
        /// including on cash-basis workspaces. Phase 1 keeps the GL header id on Bank (2A moves it to BankTransaction).
        /// </summary>
        private void ApplyOpeningBalance(
            Bank bank,
            decimal? amount,
            DateTime? asOfDate,
            bool omittedMeansNoChange)
        {
            if (omittedMeansNoChange && !amount.HasValue && !asOfDate.HasValue)
                return;

            if (asOfDate.HasValue && !amount.HasValue)
                throw new InvalidOperationException("Opening balance amount is required when a date is set.");

            var rounded = amount.HasValue
                ? decimal.Round(amount.Value, 2, MidpointRounding.AwayFromZero)
                : 0m;

            var settings = _companySettings.Table.FirstOrDefault();
            var booksStart = settings?.BooksStartDate.Date ?? DateTime.UtcNow.Date;
            var date = (asOfDate ?? booksStart).Date;

            var previousAmount = bank.OpeningBalance;
            var previousDate = bank.OpeningBalanceDate?.Date;
            var previousHeaderId = bank.OpeningBalanceGeneralLedgerHeaderId;

            if (previousAmount == rounded
                && previousDate == date
                && (rounded == 0m || previousHeaderId.HasValue))
            {
                bank.OpeningBalance = rounded == 0m ? null : rounded;
                bank.OpeningBalanceDate = rounded == 0m ? null : date;
                return;
            }

            // Changing (or clearing) an OB whose date sits outside an open fiscal year needs an adjustment instead.
            if (previousDate.HasValue && !IsInActiveFiscalYear(previousDate.Value))
                throw new InvalidOperationException("Post an adjustment instead.");

            if (!bank.AccountId.HasValue)
                throw new InvalidOperationException("That bank account has no posting account.");

            EnsureOpeningBalanceDateAllowed(bank.AccountId.Value, date, booksStart, previousHeaderId);

            if (previousHeaderId.HasValue)
            {
                var reversalDate = previousDate ?? date;
                var reversal = _financial.CreateReversingEntry(
                    previousHeaderId.Value,
                    $"Reversal of opening balance for {bank.Name}",
                    reversalDate);
                _financial.SaveGeneralLedgerEntry(reversal);
                bank.OpeningBalanceGeneralLedgerHeaderId = null;
            }

            bank.OpeningBalance = rounded == 0m ? null : rounded;
            bank.OpeningBalanceDate = rounded == 0m ? null : date;

            if (rounded == 0m)
                return;

            var equity = _financial.GetAccountByAccountCode(OpeningBalanceEquityCode)
                ?? throw new InvalidOperationException("Opening Balance Equity (30700) is not set up.");

            // Positive opening balance: money in the bank (DR bank / CR equity). Negative = overdraft (CR bank / DR equity).
            var bankSide = rounded > 0m ? DrOrCrSide.Dr : DrOrCrSide.Cr;
            var equitySide = rounded > 0m ? DrOrCrSide.Cr : DrOrCrSide.Dr;
            var abs = Math.Abs(rounded);

            var header = _financial.CreateGeneralLedgerHeader(
                DocumentTypes.BankTransaction,
                date,
                OpeningBalanceDescription,
                sourceDocumentId: null);
            header.GeneralLedgerLines.Add(_financial.CreateGeneralLedgerLine(bankSide, bank.AccountId.Value, abs));
            header.GeneralLedgerLines.Add(_financial.CreateGeneralLedgerLine(equitySide, equity.Id, abs));
            _financial.SaveGeneralLedgerEntry(header);
            bank.OpeningBalanceGeneralLedgerHeaderId = header.Id;
        }

        private void EnsureOpeningBalanceDateAllowed(
            int accountId,
            DateTime date,
            DateTime booksStart,
            int? currentOpeningBalanceHeaderId)
        {
            if (date < booksStart)
                throw new BooksStartViolationException(date, booksStart);

            var earliestOther = EarliestNonOpeningBalanceLineDate(accountId, currentOpeningBalanceHeaderId);
            if (earliestOther.HasValue && date > earliestOther.Value)
                throw new InvalidOperationException(
                    "Opening balance date must be on or before the first other transaction on this account.");
        }

        /// <summary>
        /// Earliest GL date on the account, not counting opening-balance posts
        /// (<see cref="Bank.OpeningBalanceGeneralLedgerHeaderId"/> / description) or their reversals
        /// (<see cref="GeneralLedgerHeader.ReversedGeneralLedgerHeaderId"/>).
        /// </summary>
        private DateTime? EarliestNonOpeningBalanceLineDate(int accountId, int? currentOpeningBalanceHeaderId)
        {
            var openingBalanceHeaderIds = _glHeaders.Table
                .Where(h => h.DocumentType == DocumentTypes.BankTransaction
                            && h.Description == OpeningBalanceDescription)
                .Select(h => h.Id)
                .ToList();
            if (currentOpeningBalanceHeaderId.HasValue
                && !openingBalanceHeaderIds.Contains(currentOpeningBalanceHeaderId.Value))
                openingBalanceHeaderIds.Add(currentOpeningBalanceHeaderId.Value);

            if (openingBalanceHeaderIds.Count == 0)
            {
                var any = _glLines.Table
                    .Where(l => l.AccountId == accountId)
                    .Join(_glHeaders.Table, l => l.GeneralLedgerHeaderId, h => h.Id, (l, h) => h.Date);
                return any.Any() ? any.Min().Date : null;
            }

            var query =
                from line in _glLines.Table
                join header in _glHeaders.Table on line.GeneralLedgerHeaderId equals header.Id
                where line.AccountId == accountId
                      && !openingBalanceHeaderIds.Contains(header.Id)
                      && (header.ReversedGeneralLedgerHeaderId == null
                          || !openingBalanceHeaderIds.Contains(header.ReversedGeneralLedgerHeaderId.Value))
                select header.Date;

            if (!query.Any())
                return null;
            return query.Min().Date;
        }

        private bool IsInActiveFiscalYear(DateTime date) =>
            _fiscalYears.Table.Any(fy =>
                fy.IsActive
                && date.Date >= fy.StartDate.Date
                && date.Date <= fy.EndDate.Date);
    }
}
