//-----------------------------------------------------------------------
// <copyright file="IAdministrationService.cs" company="AccountGo">
// Copyright (c) AccountGo. All rights reserved.
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
using System.Threading.Tasks;

namespace Services.Administration
{
    public interface IAdministrationService
    {
        ICollection<Tax> GetAllTaxes(bool includeInActive);
        ICollection<ItemTaxGroup> GetItemTaxGroups(bool includeInActive);
        ICollection<TaxGroup> GetTaxGroups(bool includeInActive);
        Task<Result<Dto.TaxSystem.Tax>> CreateTaxAsync(Dto.TaxSystem.TaxForCreation taxForCreationDto);
        void AddNewTax(Tax tax);
        Task AddNewTaxAsync(Tax tax);
        TaxGroup AddNewTaxGroup(Dto.TaxSystem.TaxGroup taxGroupDto);
        ItemTaxGroup AddNewItemTaxGroup(Dto.TaxSystem.ItemTaxGroup itemTaxGroupDto);
        Task<Result<Dto.TaxSystem.Tax>> EditTaxAsync(Dto.TaxSystem.TaxForUpdate taxForUpdateDto);
        Task UpdateTaxAsync(Tax tax);
        Task<Result<Dto.TaxSystem.Tax>> DeleteTaxAsync(int id);
        Task<Result<Dto.TaxSystem.TaxGroup>> DeleteTaxGroupAsync(int id);
        Task<Result<Dto.TaxSystem.ItemTaxGroup>> DeleteItemTaxGroupAsync(int id);
        void InitializeCompany();
        Company GetDefaultCompany();
        ICollection<PaymentTerm> GetPaymentTerms();
        ICollection<FinancialYear> GetFinancialYears();
        void SaveCompany(Company company);
        bool IsSystemInitialized();
        void SaveUser(User user);
        User GetUser(string username);
        IEnumerable<AuditLog> AuditLogs();
    }
}
