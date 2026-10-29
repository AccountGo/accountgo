//-----------------------------------------------------------------------
// <copyright file="IAdministrationService.cs" company="AccountGo">
// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only
// <author>Marvin Perez</author>
// <date>1/11/2015 9:48:38 AM</date>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Generic;
using Core.Domain;
using Core.Domain.TaxSystem;
using Core.Domain.Financials;
using Core.Domain.Security;
using Core.Domain.Auditing;
using Core.Domain.Error;
using Dto.Subscription;
using System.Threading.Tasks;

namespace Services.Administration
{
    public interface IAdministrationService
    {
        ICollection<Tax> GetAllTaxes(bool includeInActive);
        IReadOnlyList<Dto.TaxSystem.TaxSetting> ListTaxSettings();
        Task<TaxSettingsOutcome> CreateTaxSettingAsync(Dto.TaxSystem.TaxSettingWrite write);
        Task<TaxSettingsOutcome> UpdateTaxSettingAsync(int id, Dto.TaxSystem.TaxSettingWrite write);
        Task<TaxSettingsOutcome> DeleteTaxSettingAsync(int id);
        ICollection<ItemTaxGroup> GetItemTaxGroups(bool includeInActive);
        ICollection<TaxGroup> GetTaxGroups(bool includeInActive);
        void AddNewTax(Tax tax);
        TaxGroup AddNewTaxGroup(Dto.TaxSystem.TaxGroup taxGroupDto);
        ItemTaxGroup AddNewItemTaxGroup(Dto.TaxSystem.ItemTaxGroup itemTaxGroupDto);
        Task<Result<Dto.TaxSystem.TaxGroup>> DeleteTaxGroupAsync(int id);
        Task<Result<Dto.TaxSystem.ItemTaxGroup>> DeleteItemTaxGroupAsync(int id);
        void InitializeCompany();
        ICollection<Company> GetCompanies();
        Company? GetCompanyBySlug(string slug);
        ICollection<PaymentTerm> GetPaymentTerms();
        ICollection<FinancialYear> GetFinancialYears();
        void SaveCompany(Company company);
        bool IsSystemInitialized();
        void SaveUser(User user);
        User GetUser(string username);
        IEnumerable<AuditLog> AuditLogs();

        TenantSubscriptionDto GetCurrentSubscription();
        TenantSubscriptionDto UpdateSubscription(UpdateSubscriptionDto dto);
        bool IsModuleEnabled(string moduleKey);
        List<ModuleDefinitionDto> GetModuleDefinitions();
    }
}
