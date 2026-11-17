// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Core.Domain;
using Core.Domain.Financials;
using Core.Domain.Purchases;
using Core.Domain.Sales;
using Microsoft.EntityFrameworkCore;

namespace Api.Data
{
    public partial class ApiDbContext
    {
        private bool _advancingCloseRevision;

        /// <summary>
        /// Mutation inventory that advances <see cref="CompanyCloseState.AccountingRevision"/> in the same save:
        /// ledger headers and lines, journals, fiscal years and books lock, accounts and ledger settings,
        /// invoices, bills, allocations and vendor payments, bank reconciliations and statement lines,
        /// partners and shares, company settings, and issuance declarations.
        /// Close evidence rows are append-only and do not by themselves represent a new accounting fact,
        /// but a close also updates the fiscal year and lock, which does advance the revision.
        /// SaveChanges-time advancement invalidates a preview taken before the commit. The close command
        /// still re-reads the revision inside its transaction and rejects a mismatched preview hash.
        /// </summary>
        private void AdvanceAccountingRevision()
        {
            if (_advancingCloseRevision) return;
            var companyIds = new SortedSet<int>();
            foreach (var entry in ChangeTracker.Entries())
            {
                if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
                    continue;
                if (entry.Entity is not ICompanyOwned owned || !TouchesCloseReadiness(entry.Entity))
                    continue;
                if (owned.CompanyId > 0)
                    companyIds.Add(owned.CompanyId);
            }
            if (companyIds.Count == 0) return;

            _advancingCloseRevision = true;
            try
            {
                foreach (var companyId in companyIds)
                {
                    // One atomic increment per company for this save. A read-then-write in memory
                    // would drop a concurrent writer's update.
                    var updated = Database.ExecuteSqlRaw(RevisionIncrementSql(), companyId);
                    var tracked = CompanyCloseStates.Local.FirstOrDefault(s => s.CompanyId == companyId);
                    if (updated == 0)
                    {
                        if (tracked == null)
                        {
                            CompanyCloseStates.Add(new CompanyCloseState
                            {
                                CompanyId = companyId,
                                AccountingRevision = 1
                            });
                        }
                    }
                    else if (tracked != null)
                    {
                        var property = Entry(tracked).Property(s => s.AccountingRevision);
                        property.CurrentValue = property.CurrentValue + 1;
                        property.OriginalValue = property.CurrentValue;
                    }
                }
            }
            finally
            {
                _advancingCloseRevision = false;
            }
        }

        private string RevisionIncrementSql()
        {
            var provider = Database.ProviderName ?? "";
            if (provider.Contains("SqlServer", StringComparison.Ordinal))
                return "UPDATE [CompanyCloseState] SET [AccountingRevision] = [AccountingRevision] + 1 WHERE [CompanyId] = {0}";
            return "UPDATE \"CompanyCloseState\" SET \"AccountingRevision\" = \"AccountingRevision\" + 1 WHERE \"CompanyId\" = {0}";
        }

        private static bool IsAppendOnlyEvidence(object entity) =>
            entity is YearEndCloseRun
            || entity is YearEndCloseEvidence
            || entity is YearEndLifecycleEvent
            || entity is YearEndCommandReceipt
            || entity is YearEndIssuanceDeclaration;

        private static bool TouchesCloseReadiness(object entity) => entity is
            GeneralLedgerHeader or GeneralLedgerLine or JournalEntryHeader or JournalEntryLine
            or FinancialYear or BooksLockChange or Account or GeneralLedgerSetting or CompanySetting
            or SalesInvoiceHeader or PurchaseInvoiceHeader or CustomerAllocation or VendorPayment
            or BankReconciliation or BankStatementLine or Partner or PartnerShare
            or YearEndIssuanceDeclaration;

        private void EnforceYearEndEvidenceImmutability()
        {
            foreach (var entry in ChangeTracker.Entries())
            {
                if (entry.State is not (EntityState.Modified or EntityState.Deleted))
                    continue;
                if (IsAppendOnlyEvidence(entry.Entity))
                    throw new InvalidOperationException("Year-end close evidence is append-only.");
            }
        }
    }
}
