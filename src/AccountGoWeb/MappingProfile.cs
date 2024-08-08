using AutoMapper;

namespace AccountGoWeb
{
    public class MappingProfile : Profile
    {
        public MappingProfile()
        {
            CreateMap<Dto.BaseDto, Models.TaxSystem.BaseViewModel>();
            CreateMap<Models.TaxSystem.BaseViewModel, Dto.BaseDto>();

            #region TaxSystem

            // TaxView Model
            CreateMap<Dto.TaxSystem.Tax, Models.TaxSystem.Tax>().ReverseMap();
            CreateMap<Dto.TaxSystem.TaxGroupTax, Models.TaxSystem.TaxGroupTax>().ReverseMap();
            CreateMap<Dto.TaxSystem.TaxGroup, Models.TaxSystem.TaxGroup>()
                .ForMember(dest => dest.Taxes, opt => opt.MapFrom((src, dest, i, context) =>
                {
                    return context.Mapper.Map<System.Collections.Generic.IEnumerable<Models.TaxSystem.TaxGroupTax>>(src.Taxes);
                }))
                .ReverseMap();
            CreateMap<Dto.TaxSystem.ItemTaxGroupTax, Models.TaxSystem.ItemTaxGroupTax>().ReverseMap();
            CreateMap<Dto.TaxSystem.ItemTaxGroup, Models.TaxSystem.ItemTaxGroup>()
                .ForMember(dest => dest.Taxes, opt => opt.MapFrom((src, dest, i, context) =>
                {
                    return context.Mapper.Map<System.Collections.Generic.IEnumerable<Models.TaxSystem.ItemTaxGroupTax>>(src.Taxes);
                }))
                .ReverseMap();
            CreateMap<Models.TaxSystem.EditTaxViewModel, Dto.TaxSystem.TaxForUpdate>()
                .ForMember(dest => dest.Tax, opt => opt.MapFrom((src, dest, i, context) =>
                {
                    return context.Mapper.Map<Dto.TaxSystem.Tax>(src.Tax);
                }))
                .ReverseMap();

            // TaxSystemViewModel
            CreateMap<Dto.TaxSystem.TaxSystemDto, Models.TaxSystem.TaxSystemViewModel>()
                .ForMember(dest => dest.Taxes, opt => opt.MapFrom((src, dest, i, context) =>
                {
                    return context.Mapper.Map<System.Collections.Generic.IEnumerable<Models.TaxSystem.Tax>>(src.Taxes);
                }))
                .ForMember(dest => dest.TaxGroups, opt => opt.MapFrom((src, dest, i, context) =>
                {
                    return context.Mapper.Map<System.Collections.Generic.IEnumerable<Models.TaxSystem.TaxGroup>>(src.TaxGroups);
                }))
                .ForMember(dest => dest.ItemTaxGroups, opt => opt.MapFrom((src, dest, i, context) =>
                {
                    return context.Mapper.Map<System.Collections.Generic.IEnumerable<Models.TaxSystem.ItemTaxGroup>>(src.ItemTaxGroups);
                })
                )
                .ReverseMap();

            #endregion

        }
    }
}
