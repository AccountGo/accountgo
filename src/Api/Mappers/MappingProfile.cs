using AutoMapper;

namespace Api
{
    public class MappingProfile : Profile
    {
        public MappingProfile()
        {
            CreateMap<Dto.TaxSystem.TaxForCreation, Core.Domain.TaxSystem.Tax>()
                .ForMember(dest => dest.SalesAccountId, opt => opt.Ignore())
                .ForMember(dest => dest.PurchasingAccountId, opt => opt.Ignore())
                .ForMember(dest => dest.SalesAccount, opt => opt.Ignore())
                .ForMember(dest => dest.PurchasingAccount, opt => opt.Ignore())
                .ForMember(dest => dest.TaxGroupTaxes, opt => opt.MapFrom(
                    (src, dest) =>
                    {
                        dest.TaxGroupTaxes.Add(new Core.Domain.TaxSystem.TaxGroupTax
                        {
                            TaxId = dest.Id,
                            TaxGroupId = src.TaxGroupId
                        });
                        return dest.TaxGroupTaxes;
                    }))
                .ForMember(dest => dest.ItemTaxGroupTaxes, opt => opt.MapFrom(
                    (src, dest) =>
                    {
                        dest.ItemTaxGroupTaxes.Add(new Core.Domain.TaxSystem.ItemTaxGroupTax
                        {
                            TaxId = dest.Id,
                            ItemTaxGroupId = src.ItemTaxGroupId
                        });
                        return dest.ItemTaxGroupTaxes;
                    }));
        }
    }
}
