// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Globalization;
using System.Linq;
using Core.Domain;
using Core.Domain.Financials;
using Dto.Banking;

namespace Services.Banking
{
    public partial class BankAccountService
    {
        public BankCloseStatusDto? GetCloseStatus(DateTime? asOf)
        {
            EnsureCompany();
            if (!HasFinancialsModule())
                return null;

            var companyId = _tenant.RequireCompanyId();
            var today = (asOf ?? DateTime.UtcNow).Date;
            var periodEnd = new DateTime(today.Year, today.Month, 1, 0, 0, 0, today.Kind).AddDays(-1);

            var activeBanks = _banks.Table
                .Where(b => b.CompanyId == companyId && b.ArchivedAt == null)
                .OrderBy(b => b.Name)
                .ToList();
            var activeBankIds = activeBanks.Select(b => b.Id).ToList();

            var latestFinishedByBank = _reconciliations.Table
                .Where(r => r.CompanyId == companyId && r.Status == BankReconciliationStatuses.Finished && activeBankIds.Contains(r.BankId))
                .Select(r => new { r.BankId, r.StatementEndDate })
                .ToList()
                .GroupBy(r => r.BankId)
                .ToDictionary(g => g.Key, g => g.Max(r => r.StatementEndDate));

            var banks = activeBanks.Select(bank =>
            {
                DateTime? through = latestFinishedByBank.TryGetValue(bank.Id, out var end) ? end : null;
                return new BankCloseStatusBankDto
                {
                    BankId = bank.Id,
                    Name = bank.Name,
                    ReconciledThrough = through,
                    IsReconciled = through.HasValue && through.Value.Date >= periodEnd
                };
            }).ToList();

            var reconciled = banks.Count(b => b.IsReconciled);
            return new BankCloseStatusDto
            {
                PeriodEnd = periodEnd,
                PeriodLabel = periodEnd.ToString("MMMM yyyy", CultureInfo.InvariantCulture),
                ReconciledCount = reconciled,
                TotalCount = banks.Count,
                IsComplete = banks.Count > 0 && reconciled == banks.Count,
                Banks = banks
            };
        }
    }
}
