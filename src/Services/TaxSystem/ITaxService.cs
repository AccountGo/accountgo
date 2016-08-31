using Core.Domain.Purchases;
using Core.Domain.Sales;
using System.Collections.Generic;

namespace Services.TaxSystem
{
    public interface ITaxService
    {
        IEnumerable<Core.Domain.TaxSystem.Tax> GetTaxes(bool includeInActive = false);
        IEnumerable<Core.Domain.TaxSystem.TaxGroup> GetTaxGroups();
        IEnumerable<Core.Domain.TaxSystem.ItemTaxGroup> GetItemTaxGroups();
        IEnumerable<Core.Domain.TaxSystem.Tax> GetIntersectionTaxes(int itemId, int partyId, Core.Domain.PartyTypes partyType);
        IEnumerable<Core.Domain.TaxSystem.Tax> GetIntersectionTaxes(int itemId, int partyId);
        decimal GetSalesLineTaxAmount(decimal quantity, decimal amount, decimal discount, IEnumerable<Core.Domain.TaxSystem.Tax> taxes);
        List<KeyValuePair<int, decimal>> GetPurchaseTaxes(int vendorId, IEnumerable<PurchaseInvoiceLine> purchaseInvoiceLines);
        List<KeyValuePair<int, decimal>> GetPurchaseTaxes(int vendorId, int itemId, decimal quantity, decimal amount, decimal discount);
        List<KeyValuePair<int, decimal>> GetSalesTaxes(int customerId, IEnumerable<SalesInvoiceLine> salesInvoiceLines);
        List<KeyValuePair<int, decimal>> GetSalesTaxes(int customerId, int itemId, decimal quantity, decimal amount, decimal discount);
        decimal GetItemPrice(int itemId);
        decimal GetItemCost(int itemId);
        /// <summary>
        /// Deduct tax from a price which includes tax
        /// </summary>
        /// <param name="itemId"></param>
        /// <returns>Net price</returns>
        decimal PriceBeforeTax(int itemId);
        /// <summary>
        /// Add tax to a price.
        /// </summary>
        /// <param name="itemId"></param>
        /// <returns>Gross price</returns>
        decimal PriceAfterTax(int itemId);
    }
}
