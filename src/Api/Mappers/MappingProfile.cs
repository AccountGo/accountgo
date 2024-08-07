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
                .ForMember(dest => dest.TaxGroupTaxes, opt => opt.Ignore())
                .ForMember(dest => dest.ItemTaxGroupTaxes, opt => opt.Ignore());

            CreateMap<Dto.TaxSystem.TaxForUpdate, Core.Domain.TaxSystem.Tax>()
                .ForMember(dest => dest.SalesAccountId, opt => opt.Ignore())
                .ForMember(dest => dest.PurchasingAccountId, opt => opt.Ignore())
                .ForMember(dest => dest.SalesAccount, opt => opt.Ignore())
                .ForMember(dest => dest.PurchasingAccount, opt => opt.Ignore())
                .ForMember(dest => dest.TaxName, opt => opt.MapFrom(src => src.Tax.TaxName))
                .ForMember(dest => dest.TaxCode, opt => opt.MapFrom(src => src.Tax.TaxCode))
                .ForMember(dest => dest.Rate, opt => opt.MapFrom(src => src.Tax.Rate))
                .ForMember(dest => dest.IsActive, opt => opt.MapFrom(src => src.Tax.IsActive))
                .ForMember(dest => dest.TaxGroupTaxes, opt => opt.Ignore())
                .ForMember(dest => dest.ItemTaxGroupTaxes, opt => opt.Ignore());

            CreateMap<Dto.TaxSystem.TaxGroup, Core.Domain.TaxSystem.TaxGroup>()
                .ForMember(dest => dest.TaxGroupTax, opt => opt.MapFrom(src => src.Taxes));

            CreateMap<Dto.TaxSystem.ItemTaxGroup, Core.Domain.TaxSystem.ItemTaxGroup>()
                .ForMember(dest => dest.ItemTaxGroupTax, opt => opt.MapFrom(src => src.Taxes));
        }
    }
}