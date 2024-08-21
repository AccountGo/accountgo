using AutoMapper;
using Dto.Sales;

namespace Api
{
    public class MappingProfile : Profile
    {
        public MappingProfile()
        {
            #region TaxSystem
            CreateMap<Core.Domain.TaxSystem.Tax, Dto.TaxSystem.Tax>();

            CreateMap<Dto.TaxSystem.TaxForCreation, Core.Domain.TaxSystem.Tax>()
                .ForMember(dest => dest.SalesAccountId, opt => opt.Ignore())
                .ForMember(dest => dest.PurchasingAccountId, opt => opt.Ignore())
                .ForMember(dest => dest.SalesAccount, opt => opt.Ignore())
                .ForMember(dest => dest.PurchasingAccount, opt => opt.Ignore())
                .ForMember(dest => dest.TaxGroupTaxes, opt => opt.Ignore())
                .ForMember(dest => dest.ItemTaxGroupTaxes, opt => opt.Ignore());

            CreateMap<Dto.TaxSystem.TaxForUpdate, Core.Domain.TaxSystem.Tax>()
                .ForMember(dest => dest.SalesAccountId, opt => opt.Ignore())
                .ForMember(dest => dest.PurchasingAccountId, opt => opt.Ignore())
                .ForMember(dest => dest.SalesAccount, opt => opt.Ignore())
                .ForMember(dest => dest.PurchasingAccount, opt => opt.Ignore())
                .ForMember(dest => dest.TaxName, opt => opt.MapFrom(src => src.Tax!.TaxName))
                .ForMember(dest => dest.TaxCode, opt => opt.MapFrom(src => src.Tax!.TaxCode))
                .ForMember(dest => dest.Rate, opt => opt.MapFrom(src => src.Tax!.Rate))
                .ForMember(dest => dest.IsActive, opt => opt.MapFrom(src => src.Tax!.IsActive))
                .ForMember(dest => dest.TaxGroupTaxes, opt => opt.Ignore())
                .ForMember(dest => dest.ItemTaxGroupTaxes, opt => opt.Ignore());

            CreateMap<Dto.TaxSystem.TaxGroup, Core.Domain.TaxSystem.TaxGroup>()
                .ForMember(dest => dest.TaxGroupTax, opt => opt.MapFrom(src => src.Taxes));

            CreateMap<Dto.TaxSystem.ItemTaxGroup, Core.Domain.TaxSystem.ItemTaxGroup>()
                .ForMember(dest => dest.ItemTaxGroupTax, opt => opt.MapFrom(src => src.Taxes));
            #endregion

            #region SalesSystem

            CreateMap<Dto.Sales.SalesInvoice, Core.Domain.Sales.SalesOrderHeader>()
                .ForMember(dest => dest.Date, opt => opt.MapFrom(src => src.InvoiceDate))
                .ForMember(dest => dest.Status, opt => opt.MapFrom(src => Core.Domain.SalesOrderStatus.FullyInvoiced));

            CreateMap<Dto.Sales.SalesInvoiceLine, Core.Domain.Sales.SalesOrderLine>()
                .ForMember(dest => dest.Amount, opt => opt.MapFrom(src => src.Amount.GetValueOrDefault()))
                .ForMember(dest => dest.Discount, opt => opt.MapFrom(src => src.Discount.GetValueOrDefault()))
                .ForMember(dest => dest.Quantity, opt => opt.MapFrom(src => src.Quantity.GetValueOrDefault()))
                .ForMember(dest => dest.ItemId, opt => opt.MapFrom(src => src.ItemId.GetValueOrDefault()))
                .ForMember(dest => dest.MeasurementId, opt => opt.MapFrom(src => src.MeasurementId.GetValueOrDefault()));

            CreateMap<Dto.Sales.SalesInvoice, Core.Domain.Sales.SalesInvoiceHeader>()
                .ForMember(dest => dest.Date, opt => opt.MapFrom(src => src.InvoiceDate))
                .ForMember(dest => dest.SalesInvoiceLines, opt => opt.MapFrom<CustomSalesInvoiceResolver>());

            CreateMap<Dto.Sales.SalesInvoiceLine, Core.Domain.Sales.SalesInvoiceLine>()
                .ForMember(dest => dest.Amount, opt => opt.MapFrom(src => src.Amount.GetValueOrDefault()))
                .ForMember(dest => dest.Discount, opt => opt.MapFrom(src => src.Discount.GetValueOrDefault()))
                .ForMember(dest => dest.Quantity, opt => opt.MapFrom(src => src.Quantity.GetValueOrDefault()))
                .ForMember(dest => dest.ItemId, opt => opt.MapFrom(src => src.ItemId.GetValueOrDefault()))
                .ForMember(dest => dest.MeasurementId, opt => opt.MapFrom(src => src.MeasurementId.GetValueOrDefault()));

            #endregion
        }
    }

    public class CustomSalesInvoiceResolver : IValueResolver<Dto.Sales.SalesInvoice, Core.Domain.Sales.SalesInvoiceHeader, System.Collections.Generic.ICollection<Core.Domain.Sales.SalesInvoiceLine>>
    {
        public System.Collections.Generic.ICollection<Core.Domain.Sales.SalesInvoiceLine> Resolve(Dto.Sales.SalesInvoice source, Core.Domain.Sales.SalesInvoiceHeader destination, System.Collections.Generic.ICollection<Core.Domain.Sales.SalesInvoiceLine> destMember, ResolutionContext context)
        {
            destMember = new List<Core.Domain.Sales.SalesInvoiceLine>();

            foreach (var lineDto in source.SalesInvoiceLines!)
            {
                var salesInvoiceLine = context.Mapper.Map<Core.Domain.Sales.SalesInvoiceLine>(lineDto);
              
                // line.Id here is referring to SalesOrderLineId. It is pre-populated when you create a new sales invoice from sales order.
                if (lineDto.Id != 0)
                {
                    salesInvoiceLine.SalesOrderLineId = lineDto.Id;
                }
                else
                {
                    var salesOrderLine = context.Mapper.Map<Core.Domain.Sales.SalesOrderLine>(lineDto);

                    salesInvoiceLine.SalesOrderLineId = 0; // set to 0 to indicate this line item is newly added to invoice which is not originally in sales order.
                    salesInvoiceLine.SalesOrderLine = salesOrderLine;
                }

                destMember.Add(salesInvoiceLine);
            }

            return destMember;
        }
    }
}