using Microsoft.AspNetCore.Mvc;
using Dto.TaxSystem;
using Services.TaxSystem;
using System.Collections.Generic;
using System.Linq;

namespace Api.Controllers
{
    [Route("api/[controller]")]
    public class TaxController : Controller
    {
        private readonly ITaxService _taxService;

        public TaxController(ITaxService taxService)
        {
            _taxService = taxService;
        }

        [HttpGet]
        [Route("[action]")]
        public IActionResult Taxes()
        {
            var taxes = _taxService.GetTaxes(true);

            var taxSystemDtoDto = new TaxSystemDto();

            var taxesDto = new List<Tax>();

            foreach (var tax in taxes) {
                taxesDto.Add(new Tax() {
                    Id = tax.Id,
                    TaxCode = tax.TaxCode,
                    TaxName = tax.TaxName,
                    Rate = tax.Rate,
                    IsActive = tax.IsActive
                });                
            }

            taxSystemDtoDto.Taxes = taxesDto.AsEnumerable();

            var taxGroupsDto = new List<TaxGroup>();
            var taxGroups = _taxService.GetTaxGroups();

            foreach (var group in taxGroups) {
                var groupDto = new TaxGroup()
                {
                    Id = group.Id,
                    Description = group.Description,
                    IsActive = group.IsActive,
                    TaxAppliedToShipping = group.TaxAppliedToShipping
                };
                                
                taxGroupsDto.Add(groupDto);
            }

            taxSystemDtoDto.TaxGroups = taxGroupsDto.AsEnumerable();
            
            var itemTaxGroupsDto = new List<ItemTaxGroup>();
            var itemTaxGroups = _taxService.GetItemTaxGroups();

            foreach (var group in itemTaxGroups)
            {
                var groupDto = new ItemTaxGroup()
                {
                    Id = group.Id,
                    Name = group.Name,
                    IsFullyExempt = group.IsFullyExempt
                };

                itemTaxGroupsDto.Add(groupDto);
            }

            taxSystemDtoDto.ItemTaxGroups = itemTaxGroupsDto.AsEnumerable();

            return new ObjectResult(taxSystemDtoDto);
        }
    }
}
