// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Linq;
using Core.Data;
using Core.Domain;
using Core.Domain.Financials;
using Core.Tenancy;

namespace Services.Banking
{
    public sealed class ReconciledPeriodGuard : IReconciledPeriodGuard
    {
        private readonly IRepository<Bank> _banks;
        private readonly IRepository<BankReconciliation> _reconciliations;
        private readonly ITenantContext _tenant;

        public ReconciledPeriodGuard(
            IRepository<Bank> banks,
            IRepository<BankReconciliation> reconciliations,
            ITenantContext tenant)
        {
            _banks = banks;
            _reconciliations = reconciliations;
            _tenant = tenant;
        }

        public bool IsConfirmed { get; private set; }

        public void Confirm() => IsConfirmed = true;

        public void EnsurePostingAllowed(GeneralLedgerHeader entry)
        {
            if (entry == null)
                throw new ArgumentNullException(nameof(entry));
            if (IsConfirmed || entry.ReversedGeneralLedgerHeaderId.HasValue)
                return;
            if (!_tenant.IsResolved || !_tenant.CompanyId.HasValue)
                return;

            var accountIds = entry.GeneralLedgerLines.Select(l => l.AccountId).Distinct().ToList();
            if (accountIds.Count == 0)
                return;

            var companyId = _tenant.CompanyId.Value;
            var postingDate = entry.Date.Date;
            var affectedBanks = _banks.Table
                .Where(b => b.CompanyId == companyId && b.AccountId != null && accountIds.Contains(b.AccountId.Value))
                .ToList();

            foreach (var bank in affectedBanks.OrderBy(b => b.Name))
            {
                var latestEnd = _reconciliations.Table
                    .Where(r => r.CompanyId == companyId
                        && r.BankId == bank.Id
                        && r.Status == BankReconciliationStatuses.Finished)
                    .Select(r => (DateTime?)r.StatementEndDate)
                    .OrderByDescending(end => end)
                    .FirstOrDefault();

                if (latestEnd.HasValue && postingDate <= latestEnd.Value.Date)
                    throw new ReconciledPeriodConfirmationRequiredException(bank.Name, latestEnd.Value, postingDate);
            }
        }
    }
}
