using Dto.Common.Response;
using Dto.Financial;
using Dto.Inventory.Response;
using Dto.Purchasing.Response;
using Dto.TaxSystem;
using System.Reactive.Subjects;

namespace WebBlazor.Rx
{
    public class LookupRx
    {
        public BehaviorSubject<IEnumerable<BaseAccount>> ChartOfAcctsLookup = new(Enumerable.Empty<BaseAccount>());
        
        public BehaviorSubject<IEnumerable<GetItem>> ItemsLookup = new(Enumerable.Empty<GetItem>());

        public BehaviorSubject<IEnumerable<GetItemCategory>> ItemCategoriesLookup = new(Enumerable.Empty<GetItemCategory>());

        public BehaviorSubject<IEnumerable<GetMeasurement>> MeasurementsLookup = new(Enumerable.Empty<GetMeasurement>());

        public BehaviorSubject<IEnumerable<GetVendor>> VendorsLookup = new(Enumerable.Empty<GetVendor>());

        public BehaviorSubject<IEnumerable<GetPaymentTerm>> PaymentTermsLookup = new(Enumerable.Empty<GetPaymentTerm>());
        public BehaviorSubject<IEnumerable<ItemTaxGroup>> ItemTaxGroupLookup = new(Enumerable.Empty<ItemTaxGroup>());
    }
}
