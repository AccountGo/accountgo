using AutoMapper;

namespace Api
{
    public class MappingProfile : Profile
    {
        public MappingProfile()
        {
            CreateMap<Dto.TaxSystem.TaxForCreation, Core.Domain.TaxSystem.Tax>();
        }
    }
}
