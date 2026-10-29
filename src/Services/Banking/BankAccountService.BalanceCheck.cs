// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Globalization;
using System.Linq;
using Core.Domain;
using Dto.Banking;

namespace Services.Banking
{
    public partial class BankAccountService
    {
        public const int BalanceCheckRecentLineCount = 10;
        /// <summary>Guards against absurd input before it reaches decimal arithmetic.</summary>
        public const decimal BalanceCheckMaxMagnitude = 1_000_000_000_000m;
        /// <summary>Lets a caller's "today" run a day ahead of UTC without being rejected as a future date.</summary>
        private const int BalanceCheckFutureToleranceDays = 1;

        public BankBalanceCheckDto CheckBalance(int bankId, BankBalanceCheckRequest request)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            var bank = RequireBank(bankId);
            if (HasFinancialsModule())
                throw new InvalidOperationException("Use reconciliation to check this account against a statement.");
            if (!bank.AccountId.HasValue)
                throw new InvalidOperationException("That bank account has no posting account.");
            if (Math.Abs(request.AsOfBalance) > BalanceCheckMaxMagnitude)
                throw new InvalidOperationException("Enter a smaller balance.");

            var latestAllowed = DateTime.UtcNow.Date.AddDays(BalanceCheckFutureToleranceDays);
            var asOfDate = (request.AsOfDate ?? DateTime.UtcNow).Date;
            if (asOfDate > latestAllowed)
                throw new InvalidOperationException("The as-of date cannot be in the future.");

            var accountId = bank.AccountId.Value;
            var movements = (
                from line in _glLines.Table
                join header in _glHeaders.Table on line.GeneralLedgerHeaderId equals header.Id
                where line.AccountId == accountId
                select new RegisterMovement(
                    line.Id,
                    header.Id,
                    header.Date,
                    header.DocumentType,
                    header.SourceDocumentId,
                    header.ReversedGeneralLedgerHeaderId,
                    header.Description,
                    line.DrCr,
                    line.Amount,
                    line.BankReconciliationId)
            ).ToList()
                .Where(m => m.Date.Date <= asOfDate)
                .ToList();

            var bookBalance = Round2(movements.Sum(m => m.DrCr == DrOrCrSide.Dr ? m.Amount : -m.Amount));
            var bankBalance = Round2(request.AsOfBalance);
            var difference = Round2(bankBalance - bookBalance);

            var recent = movements
                .OrderByDescending(m => m.Date)
                .ThenByDescending(m => m.HeaderId)
                .ThenByDescending(m => m.LineId)
                .Take(BalanceCheckRecentLineCount)
                .ToList();
            var enrichment = EnrichRegister(recent, bank);
            var differenceMagnitude = Math.Abs(difference);

            var lines = recent.Select(m =>
            {
                enrichment.TryGetValue(m.HeaderId, out var info);
                var isIn = m.DrCr == DrOrCrSide.Dr;
                return new BankBalanceCheckLineDto
                {
                    LineId = m.LineId,
                    HeaderId = m.HeaderId,
                    Date = m.Date,
                    Type = info?.TypeLabel ?? DocumentTypeLabel(m.DocumentType),
                    Payee = info?.Payee,
                    Reference = info?.Reference,
                    In = isIn ? m.Amount : 0m,
                    Out = isIn ? 0m : m.Amount,
                    MayExplainDifference = differenceMagnitude > 0m && m.Amount == differenceMagnitude
                };
            }).ToList();

            return new BankBalanceCheckDto
            {
                BankId = bank.Id,
                AsOfDate = asOfDate,
                BankBalance = bankBalance,
                BookBalance = bookBalance,
                Difference = difference,
                IsMatch = difference == 0m,
                Message = BalanceCheckMessage(difference),
                RecentLines = lines
            };
        }

        private static string BalanceCheckMessage(decimal difference)
        {
            if (difference == 0m)
                return "Your books match the bank.";

            var amount = Math.Abs(difference).ToString("0.00", CultureInfo.InvariantCulture);
            return difference > 0m
                ? $"The bank shows {amount} more than your books. Look for a deposit you have not recorded or a payment recorded twice."
                : $"The bank shows {amount} less than your books. Look for a payment you have not recorded or a deposit that has not arrived yet.";
        }
    }
}
