// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Api.Data;
using Core.Domain;
using Core.Domain.Financials;
using Core.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Api.Service
{
    public sealed record SettlementAccountRolePreviewDto(
        string RoleKey,
        string RoleLabel,
        int? ConfiguredAccountId,
        string? ConfiguredAccountCode,
        string? ConfiguredAccountName,
        int? SuggestedAccountId,
        string? SuggestedAccountCode,
        string? SuggestedAccountName,
        bool IsArchived,
        bool IsMissing);

    public sealed record SettlementAccountMappingPreviewDto(
        bool ReadyForSalesCredits,
        bool ReadyForSupplierCredits,
        bool ReadyForAdvances,
        bool ReadyForWriteOffs,
        bool ReadyForReturns,
        IReadOnlyList<SettlementAccountRolePreviewDto> Roles);

    public sealed record UpdateSettlementAccountMappingsRequest(
        int? CustomerCreditLiabilityAccountId,
        int? SalesReturnsAllowancesAccountId,
        int? SupplierCreditAssetAccountId,
        int? SupplierAdvanceAccountId,
        int? SupplierReturnClearingAccountId,
        int? ReturnValuationDifferenceAccountId,
        int? ReceivableLossAllowanceAccountId,
        int? ImpairmentExpenseAccountId,
        int? BadDebtRecoveryAccountId);

    public sealed class SettlementAccountMappingService
    {
        private readonly ApiDbContext _db;
        private readonly ITenantContext _tenant;

        public SettlementAccountMappingService(ApiDbContext db, ITenantContext tenant)
        {
            _db = db;
            _tenant = tenant;
        }

        public SettlementAccountMappingPreviewDto GetPreview()
        {
            var companyId = _tenant.RequireCompanyId();
            var gl = _db.GeneralLedgerSettings.FirstOrDefault(g => g.CompanyId == companyId);
            var accounts = _db.Accounts.AsNoTracking()
                .Where(a => a.CompanyId == companyId)
                .OrderBy(a => a.AccountCode)
                .ToList();
            var byId = accounts.ToDictionary(a => a.Id);

            var roles = new List<SettlementAccountRolePreviewDto>
            {
                BuildRole("customer_credit_liability", "Customer Credits Liability", gl?.CustomerCreditLiabilityAccountId, byId,
                    accounts, (int)AccountClasses.Liabilities, "customer credit", "credit liability", "unearned", "advance"),
                BuildRole("sales_returns_allowances", "Sales Returns & Allowances", gl?.SalesReturnsAllowancesAccountId, byId,
                    accounts, (int)AccountClasses.Revenue, "sales return", "return", "allowance", "discount"),
                BuildRole("supplier_credit_asset", "Supplier Credits Receivable", gl?.SupplierCreditAssetAccountId, byId,
                    accounts, (int)AccountClasses.Assets, "supplier credit", "vendor credit", "refunds receivable", "prepaid"),
                BuildRole("supplier_advance", "Supplier Advances", gl?.SupplierAdvanceAccountId, byId,
                    accounts, (int)AccountClasses.Assets, "supplier advance", "vendor advance", "prepaid", "deposit"),
                BuildRole("supplier_return_clearing", "Supplier Return Clearing", gl?.SupplierReturnClearingAccountId, byId,
                    accounts, (int)AccountClasses.Assets, "return clearing", "goods receipt", "clearing"),
                BuildRole("return_valuation_difference", "Return Valuation Difference", gl?.ReturnValuationDifferenceAccountId, byId,
                    accounts, (int)AccountClasses.Expense, "valuation", "variance", "cost of goods", "cogs"),
                BuildRole("receivable_loss_allowance", "Allowance for Receivable Losses", gl?.ReceivableLossAllowanceAccountId, byId,
                    accounts, (int)AccountClasses.Assets, "allowance", "doubtful", "credit loss"),
                BuildRole("impairment_expense", "Impairment / Bad-Debt Expense", gl?.ImpairmentExpenseAccountId, byId,
                    accounts, (int)AccountClasses.Expense, "bad debt", "impairment", "credit loss"),
                BuildRole("bad_debt_recovery", "Recovery of Written-Off Debts", gl?.BadDebtRecoveryAccountId, byId,
                    accounts, (int)AccountClasses.Revenue, "recovery", "bad debt", "other income")
            };

            bool Ready(params string[] keys) => keys.All(k =>
            {
                var r = roles.First(x => x.RoleKey == k);
                return !r.IsMissing && !r.IsArchived;
            });

            return new SettlementAccountMappingPreviewDto(
                ReadyForSalesCredits: Ready("customer_credit_liability", "sales_returns_allowances"),
                ReadyForSupplierCredits: Ready("supplier_credit_asset"),
                ReadyForAdvances: Ready("supplier_advance"),
                ReadyForWriteOffs: Ready("receivable_loss_allowance", "impairment_expense", "bad_debt_recovery"),
                ReadyForReturns: Ready("supplier_return_clearing", "return_valuation_difference"),
                Roles: roles);
        }

        public SettlementAccountMappingPreviewDto UpdateMappings(UpdateSettlementAccountMappingsRequest request)
        {
            var companyId = _tenant.RequireCompanyId();
            var gl = _db.GeneralLedgerSettings.FirstOrDefault(g => g.CompanyId == companyId);
            if (gl == null)
            {
                gl = new GeneralLedgerSetting { CompanyId = companyId };
                _db.GeneralLedgerSettings.Add(gl);
            }

            gl.CustomerCreditLiabilityAccountId = ValidateActiveAccount(companyId, request.CustomerCreditLiabilityAccountId, "Customer Credits Liability");
            gl.SalesReturnsAllowancesAccountId = ValidateActiveAccount(companyId, request.SalesReturnsAllowancesAccountId, "Sales Returns & Allowances");
            gl.SupplierCreditAssetAccountId = ValidateActiveAccount(companyId, request.SupplierCreditAssetAccountId, "Supplier Credits Receivable");
            gl.SupplierAdvanceAccountId = ValidateActiveAccount(companyId, request.SupplierAdvanceAccountId, "Supplier Advances");
            gl.SupplierReturnClearingAccountId = ValidateActiveAccount(companyId, request.SupplierReturnClearingAccountId, "Supplier Return Clearing");
            gl.ReturnValuationDifferenceAccountId = ValidateActiveAccount(companyId, request.ReturnValuationDifferenceAccountId, "Return Valuation Difference");
            gl.ReceivableLossAllowanceAccountId = ValidateActiveAccount(companyId, request.ReceivableLossAllowanceAccountId, "Allowance for Receivable Losses");
            gl.ImpairmentExpenseAccountId = ValidateActiveAccount(companyId, request.ImpairmentExpenseAccountId, "Impairment / Bad-Debt Expense");
            gl.BadDebtRecoveryAccountId = ValidateActiveAccount(companyId, request.BadDebtRecoveryAccountId, "Recovery of Written-Off Debts");

            _db.SaveChanges();
            return GetPreview();
        }

        public int RequireActiveAccount(int companyId, int? accountId, string roleLabel)
        {
            if (!accountId.HasValue)
                throw new SettlementCommandException(
                    SettlementErrorCodes.MissingMapping,
                    $"Account mapping for '{roleLabel}' is not configured. Review and save account mappings first.",
                    422);

            var account = _db.Accounts.AsNoTracking()
                .FirstOrDefault(a => a.CompanyId == companyId && a.Id == accountId.Value);
            if (account == null)
                throw new SettlementCommandException(
                    SettlementErrorCodes.MissingMapping,
                    $"Mapped account for '{roleLabel}' was not found in this workspace.",
                    422);
            if (account.ArchivedAt != null)
                throw new SettlementCommandException(
                    SettlementErrorCodes.MissingMapping,
                    $"Mapped account '{account.AccountCode} {account.AccountName}' for '{roleLabel}' is archived. Configure an active replacement.",
                    422);

            return account.Id;
        }

        private int? ValidateActiveAccount(int companyId, int? accountId, string roleLabel)
        {
            if (!accountId.HasValue)
                return null;
            return RequireActiveAccount(companyId, accountId, roleLabel);
        }

        private static SettlementAccountRolePreviewDto BuildRole(
            string key,
            string label,
            int? configuredId,
            IReadOnlyDictionary<int, Account> byId,
            IReadOnlyList<Account> allAccounts,
            int preferredClassId,
            params string[] nameHints)
        {
            Account? configured = configuredId.HasValue && byId.TryGetValue(configuredId.Value, out var acc) ? acc : null;
            var isArchived = configured?.ArchivedAt != null;
            var isMissing = configured == null;

            Account? suggested = null;
            if (isMissing || isArchived)
            {
                var active = allAccounts.Where(a => a.ArchivedAt == null).ToList();
                suggested = active.FirstOrDefault(a =>
                    a.AccountClassId == preferredClassId &&
                    nameHints.Any(h => a.AccountName.Contains(h, StringComparison.OrdinalIgnoreCase)));
                suggested ??= active.FirstOrDefault(a =>
                    nameHints.Any(h => a.AccountName.Contains(h, StringComparison.OrdinalIgnoreCase)));
            }

            return new SettlementAccountRolePreviewDto(
                RoleKey: key,
                RoleLabel: label,
                ConfiguredAccountId: configured?.Id,
                ConfiguredAccountCode: configured?.AccountCode,
                ConfiguredAccountName: configured?.AccountName,
                SuggestedAccountId: suggested?.Id,
                SuggestedAccountCode: suggested?.AccountCode,
                SuggestedAccountName: suggested?.AccountName,
                IsArchived: isArchived,
                IsMissing: isMissing);
        }
    }
}
