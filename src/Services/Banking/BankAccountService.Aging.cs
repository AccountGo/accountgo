// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Core.Domain;
using Dto.Banking;

namespace Services.Banking
{
    public partial class BankAccountService
    {
        /// <summary>Last day (inclusive) of the 0-30 bucket.</summary>
        public const int AgingFirstBucketMaxDays = 30;
        public const int AgingSecondBucketMaxDays = 60;
        public const int AgingThirdBucketMaxDays = 90;

        public OutstandingAgingDto GetOutstandingAging(int bankId, DateTime? asOf)
        {
            var bank = RequireBank(bankId);
            if (!bank.AccountId.HasValue)
                throw new InvalidOperationException("That bank account has no posting account.");

            var asOfDate = (asOf ?? DateTime.UtcNow).Date;
            var accountId = bank.AccountId.Value;

            var uncleared = (
                from line in _glLines.Table
                join header in _glHeaders.Table on line.GeneralLedgerHeaderId equals header.Id
                where line.AccountId == accountId && line.BankReconciliationId == null
                select new
                {
                    line.Id,
                    HeaderId = header.Id,
                    header.Date,
                    line.DrCr,
                    line.Amount,
                    header.ReversedGeneralLedgerHeaderId
                }).ToList()
                .Where(l => l.Date.Date <= asOfDate)
                .ToList();

            // The opening balance is a starting balance, not an item waiting on the bank; a voided entry
            // and its reversal net to zero and never reach a statement.
            var headerIds = uncleared.Select(l => l.HeaderId).ToHashSet();
            var excludedHeaders = new HashSet<int>();
            if (bank.OpeningBalanceGeneralLedgerHeaderId.HasValue)
                excludedHeaders.Add(bank.OpeningBalanceGeneralLedgerHeaderId.Value);
            foreach (var line in uncleared)
            {
                if (line.ReversedGeneralLedgerHeaderId is { } original && headerIds.Contains(original))
                {
                    excludedHeaders.Add(line.HeaderId);
                    excludedHeaders.Add(original);
                }
            }

            var open = uncleared.Where(l => !excludedHeaders.Contains(l.HeaderId)).ToList();

            var definitions = new (string Label, int Min, int? Max)[]
            {
                ($"0-{AgingFirstBucketMaxDays}", 0, AgingFirstBucketMaxDays),
                ($"{AgingFirstBucketMaxDays + 1}-{AgingSecondBucketMaxDays}", AgingFirstBucketMaxDays + 1, AgingSecondBucketMaxDays),
                ($"{AgingSecondBucketMaxDays + 1}-{AgingThirdBucketMaxDays}", AgingSecondBucketMaxDays + 1, AgingThirdBucketMaxDays),
                ($"{AgingThirdBucketMaxDays}+", AgingThirdBucketMaxDays + 1, null)
            };

            var buckets = new List<OutstandingAgingBucketDto>(definitions.Length);
            foreach (var (label, min, max) in definitions)
            {
                var inBucket = open.Where(l =>
                {
                    var age = (asOfDate - l.Date.Date).Days;
                    return age >= min && (!max.HasValue || age <= max.Value);
                }).ToList();

                var deposits = Round2(inBucket.Where(l => l.DrCr == DrOrCrSide.Dr).Sum(l => l.Amount));
                var payments = Round2(inBucket.Where(l => l.DrCr == DrOrCrSide.Cr).Sum(l => l.Amount));
                buckets.Add(new OutstandingAgingBucketDto
                {
                    Label = label,
                    MinDays = min,
                    MaxDays = max,
                    Count = inBucket.Count,
                    DepositsInTransit = deposits,
                    OutstandingPayments = payments,
                    Net = Round2(deposits - payments),
                    IsStale = !max.HasValue
                });
            }

            var stale = buckets.Where(b => b.IsStale).Sum(b => b.Count);
            return new OutstandingAgingDto
            {
                BankId = bank.Id,
                AsOfDate = asOfDate,
                Buckets = buckets,
                TotalCount = buckets.Sum(b => b.Count),
                TotalDepositsInTransit = Round2(buckets.Sum(b => b.DepositsInTransit)),
                TotalOutstandingPayments = Round2(buckets.Sum(b => b.OutstandingPayments)),
                StaleCount = stale,
                HasStale = stale > 0
            };
        }

        private static decimal Round2(decimal value) =>
            Math.Round(value, 2, MidpointRounding.AwayFromZero);
    }
}
