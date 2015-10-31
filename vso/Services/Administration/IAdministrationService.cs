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
    }
}
