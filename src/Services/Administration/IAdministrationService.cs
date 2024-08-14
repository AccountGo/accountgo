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

namespace Services.Administration
{
    public interface IAdministrationService
    {
        ICollection<Tax> GetAllTaxes(bool includeInActive);
        ICollection<ItemTaxGroup> GetItemTaxGroups(bool includeInActive);
        ICollection<TaxGroup> GetTaxGroups(bool includeInActive);
        Result<Dto.TaxSystem.Tax> CreateTax(Dto.TaxSystem.TaxForCreation taxForCreationDto);
        void AddNewTax(Tax tax);
        TaxGroup AddNewTaxGroup(Dto.TaxSystem.TaxGroup taxGroupDto);
        ItemTaxGroup AddNewItemTaxGroup(Dto.TaxSystem.ItemTaxGroup itemTaxGroupDto);
        Result<Dto.TaxSystem.Tax> EditTax(Dto.TaxSystem.TaxForUpdate taxForUpdateDto);
        void UpdateTax(Tax tax);
        Result<Dto.TaxSystem.Tax> DeleteTax(int id);
        Result<Dto.TaxSystem.TaxGroup> DeleteTaxGroup(int id);
        Result<Dto.TaxSystem.ItemTaxGroup> DeleteItemTaxGroup(int id);
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
