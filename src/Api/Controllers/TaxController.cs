using Microsoft.AspNetCore.Mvc;
using Dto.TaxSystem;
using Services.TaxSystem;
using System.Collections.Generic;
using System.Linq;
using Services.Administration;
using Services.Financial;

namespace Api.Controllers
{
    [Route("api/[controller]")]
    public class TaxController : BaseController
    {
        private readonly ITaxService _taxService;
        private readonly IAdministrationService _adminService;
        private readonly IFinancialService _financialService;

        public TaxController(ITaxService taxService, IAdministrationService adminService, IFinancialService financialService)
        {
            _taxService = taxService;
            _adminService = adminService;
            _financialService = financialService;
        }

        /// <summary>
        /// Based on party type (e.g. Customer/Vendor), get the corresponding tax rates. 
        /// Tax rates are intersection of tax group and item tax group
        /// </summary>
        /// <param name="itemId">Item</param>
        /// <param name="partyId">Customer or Vendor</param>
        /// <returns></returns>
        [HttpGet]
        [Route("GetTax")]
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
        [Route("TaxGroups")] // api/Tax/TaxGroups
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
        [Route("ItemTaxGroups")] // api/Tax/ItemTaxGroups
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
        [Route("Taxes")] // api/Tax/Taxes
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

        [HttpPost]
        [Route("addnewtax")]
        public IActionResult AddNewTax([FromBody] TaxForCreation taxForCreationDto)
        {
            try
            {
                var salesTaxAccount = _financialService.GetAccountByAccountCode(taxForCreationDto.SalesAccountId.ToString());
                var purchaseTaxAccount = _financialService.GetAccountByAccountCode(taxForCreationDto.PurchaseAccountId.ToString());

                // Tax
                var tax = new Core.Domain.TaxSystem.Tax()
                {
                    TaxCode = taxForCreationDto.TaxCode,
                    TaxName = taxForCreationDto.TaxName,
                    Rate = taxForCreationDto.Rate,
                    IsActive = taxForCreationDto.IsActive,

                    SalesAccountId = salesTaxAccount.Id,
                    PurchasingAccountId = purchaseTaxAccount.Id,
                };

                // TaxGroup
                tax.TaxGroupTaxes.Add(new Core.Domain.TaxSystem.TaxGroupTax()
                {
                    TaxId = tax.Id,
                    TaxGroupId = taxForCreationDto.TaxGroupId
                });

                // Item Tax Group
                tax.ItemTaxGroupTaxes.Add(new Core.Domain.TaxSystem.ItemTaxGroupTax()
                {
                    TaxId = tax.Id,
                    ItemTaxGroupId = taxForCreationDto.ItemTaxGroupId
                });

                _adminService.AddNewTax(tax);

                return new ObjectResult(taxForCreationDto);
            }
            catch(Exception ex) {
                return new ObjectResult(ex);
            }
        }

    }
}
