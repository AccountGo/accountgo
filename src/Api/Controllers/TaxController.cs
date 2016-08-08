using Microsoft.AspNetCore.Mvc;
using Dto.TaxSystem;
using Services.TaxSystem;
using System.Collections.Generic;
using System.Linq;

namespace Api.Controllers
{
    [Route("api/[controller]")]
    public class TaxController : BaseController
    {
        private readonly ITaxService _taxService;

        public TaxController(ITaxService taxService)
        {
            _taxService = taxService;
        }

        /// <summary>
        /// Based on party type (e.g. Customer/Vendor), get the corresponding tax rates. 
        /// Tax rates are intersection of tax group and item tax group
        /// </summary>
        /// <param name="itemId">Item</param>
        /// <param name="partyId">Customer or Vendor</param>
        /// <returns></returns>
        [HttpGet]
        [Route("[action]")]
        public IActionResult GetTax(int itemId, int partyId, int type = 0)
        {
            //var taxes = _taxService.GetIntersectionTaxes(itemId, partyId);
            //var taxesDto = new List<Tax>();

            //foreach (var tax in taxes) {
            //    var taxDto = new Tax()
            //    {
            //        Id = tax.Id,
            //        TaxCode = tax.TaxCode,
            //        TaxName = tax.TaxName,
            //        Rate = tax.Rate,
            //        IsActive = tax.IsActive
            //    };
            //    taxesDto.Add(taxDto);
            //}

            //return new ObjectResult(taxesDto);

            if (type == 0)
            {
                return new BadRequestObjectResult("Type is zero.");
            }
            else
            {
                var taxes = _taxService.GetIntersectionTaxes(itemId, partyId, (Core.Domain.PartyTypes)type);

                return new ObjectResult(taxes);
            }
        }

        [HttpGet]
        [Route("[action]")]
        public IActionResult TaxGroups()
        {
            var taxGroupsDto = new List<TaxGroup>();
            var taxGroups = _taxService.GetTaxGroups();

            foreach (var group in taxGroups)
            {
                var groupDto = new TaxGroup()
                {
                    Id = group.Id,
                    Description = group.Description,
                    IsActive = group.IsActive,
                    TaxAppliedToShipping = group.TaxAppliedToShipping
                };

                taxGroupsDto.Add(groupDto);
            }

            return new ObjectResult(taxGroupsDto);
        }

        [HttpGet]
        [Route("[action]")]
        public IActionResult ItemTaxGroups()
        {
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

            return new ObjectResult(itemTaxGroupsDto);
        }

        [HttpGet]
        [Route("[action]")]
        public IActionResult Taxes()
        {
            var taxes = _taxService.GetTaxes(true);

            var taxSystemDto = new TaxSystemDto();

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

            taxSystemDto.Taxes = taxesDto;

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

                foreach (var tax in group.TaxGroupTax) {
                    var taxDto = new TaxGroupTax()
                    {
                        Id = tax.Id,
                        TaxId = tax.TaxId,
                        TaxGroupId = tax.TaxGroupId
                    };

                    groupDto.Taxes.Add(taxDto);
                }

                taxGroupsDto.Add(groupDto);
            }

            taxSystemDto.TaxGroups = taxGroupsDto;
            
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

                foreach (var tax in group.ItemTaxGroupTax)
                {
                    var taxDto = new ItemTaxGroupTax()
                    {
                        Id = tax.Id,
                        TaxId = tax.TaxId,
                        ItemTaxGroupId = tax.ItemTaxGroupId
                    };

                    groupDto.Taxes.Add(taxDto);
                }

                itemTaxGroupsDto.Add(groupDto);
            }

            taxSystemDto.ItemTaxGroups = itemTaxGroupsDto;

            return new ObjectResult(taxSystemDto);
        }

       

    }
}
