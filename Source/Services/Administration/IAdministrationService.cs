//-----------------------------------------------------------------------
// <copyright file="IAdministrationService.cs" company="AccountGo">
// Copyright (c) AccountGo. All rights reserved.
// <author>Marvin Perez</author>
// <date>1/11/2015 9:48:38 AM</date>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Generic;
using Core.Domain;

namespace Services.Administration
{
    public interface IAdministrationService
    {
        ICollection<Tax> GetAllTaxes(bool includeInActive);
        ICollection<ItemTaxGroup> GetItemTaxGroups(bool includeInActive);
        ICollection<TaxGroup> GetTaxGroups(bool includeInActive);
        void AddNewTax(Tax tax);
        void UpdateTax(Tax tax);
        void DeleteTax(int id);
        void InitializeCompany();
        Company GetDefaultCompany();
    }
}
