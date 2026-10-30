// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Core.Data;
using Core.Domain;
using Core.Domain.Financials;
using Core.Domain.Sales;
using Core.Tenancy;

namespace Services.Financial
{
    public sealed class DocumentLineAccountCatalog : IDocumentLineAccountCatalog
    {
        /// <summary>Control account for Accounts Receivable — never a bill line target.</summary>
        public const string AccountsReceivableControlCode = "10120";

        private readonly IRepository<Account> _accounts;
        private readonly IRepository<Customer> _customers;
        private readonly ITenantContext _tenant;

        public DocumentLineAccountCatalog(
            IRepository<Account> accounts,
            IRepository<Customer> customers,
            ITenantContext tenant)
        {
            _accounts = accounts;
            _customers = customers;
            _tenant = tenant;
        }

        public IReadOnlyList<Account> ListBillLineAccounts()
        {
            EnsureCompany();
            var companyId = _tenant.RequireCompanyId();
            var receivableIds = ReceivableAccountIds(companyId);
            var parentIsCash = ParentCashLookup(companyId);
            var childParentIds = ChildParentIds(companyId);

            return _accounts.Table
                .Where(a => a.CompanyId == companyId
                    && a.ArchivedAt == null
                    && !a.IsContraAccount
                    && (a.AccountClassId == (int)AccountClasses.Expense
                        || a.AccountClassId == (int)AccountClasses.Assets)
                    && !a.IsCash
                    && a.AccountCode != AccountsReceivableControlCode
                    && !receivableIds.Contains(a.Id))
                .AsEnumerable()
                .Where(a => !childParentIds.Contains(a.Id))
                .Where(a => !a.ParentAccountId.HasValue
                    || !parentIsCash.TryGetValue(a.ParentAccountId.Value, out var cash)
                    || !cash)
                .OrderBy(a => a.AccountClassId == (int)AccountClasses.Expense ? 0 : 1)
                .ThenBy(a => a.AccountCode)
                .ToList();
        }

        public void EnsureBillLineAccount(int accountId)
        {
            if (accountId <= 0)
                throw new InvalidOperationException("Pick an expense or asset category.");
            EnsureCompany();
            var companyId = _tenant.RequireCompanyId();
            var account = _accounts.Table.FirstOrDefault(a => a.Id == accountId && a.CompanyId == companyId)
                ?? throw new TenantEntityNotFoundException(typeof(Account).Name, accountId);
            if (!IsEligibleBillLine(account, companyId))
                throw new InvalidOperationException("Pick an expense or asset category that can take a bill posting.");
        }

        private bool IsEligibleBillLine(Account account, int companyId)
        {
            if (account.ArchivedAt != null || account.IsContraAccount || account.IsCash)
                return false;
            if (account.AccountClassId != (int)AccountClasses.Expense
                && account.AccountClassId != (int)AccountClasses.Assets)
                return false;
            if (account.AccountCode == AccountsReceivableControlCode)
                return false;
            if (ReceivableAccountIds(companyId).Contains(account.Id))
                return false;
            if (_accounts.Table.Any(c => c.ParentAccountId == account.Id && c.CompanyId == companyId))
                return false;
            if (account.ParentAccountId.HasValue)
            {
                var parent = _accounts.Table.FirstOrDefault(p => p.Id == account.ParentAccountId.Value);
                if (parent?.IsCash == true)
                    return false;
            }
            return true;
        }

        private HashSet<int> ReceivableAccountIds(int companyId) =>
            _customers.Table
                .Where(c => c.CompanyId == companyId && c.AccountsReceivableAccountId != null)
                .Select(c => c.AccountsReceivableAccountId!.Value)
                .ToHashSet();

        private Dictionary<int, bool> ParentCashLookup(int companyId) =>
            _accounts.Table
                .Where(a => a.CompanyId == companyId)
                .Select(a => new { a.Id, a.IsCash })
                .ToDictionary(a => a.Id, a => a.IsCash);

        private HashSet<int> ChildParentIds(int companyId) =>
            _accounts.Table
                .Where(a => a.CompanyId == companyId && a.ParentAccountId != null)
                .Select(a => a.ParentAccountId!.Value)
                .ToHashSet();

        private void EnsureCompany()
        {
            if (!_tenant.IsResolved)
                throw new InvalidOperationException("No active workspace.");
            _ = _tenant.RequireCompanyId();
        }
    }
}
