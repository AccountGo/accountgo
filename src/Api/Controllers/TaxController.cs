using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dto.TaxSystem;
using Microsoft.AspNetCore.Mvc;
using Services.Administration;
using Services.TaxSystem;

namespace Api.Controllers
{
    [Route("api/[controller]")]
    public class TaxController : BaseController
    {
        private readonly ITaxService _taxService;
        private readonly IAdministrationService _adminService;

        public TaxController(ITaxService taxService, IAdministrationService adminService)
        {
            _taxService = taxService;
            _adminService = adminService;
        }

        [HttpGet("Taxes")]
        public IActionResult GetTaxes()
        {
            var taxes = _taxService.GetTaxes(true);
            var taxesDto = taxes.Select(t => new Tax
            {
                Id = t.Id,
                TaxCode = t.TaxCode,
                TaxName = t.TaxName,
                Rate = t.Rate,
                IsActive = t.IsActive
            }).ToList();

            return Ok(taxesDto);
        }

        [HttpGet("TaxGroups")]
        public IActionResult GetTaxGroups()
        {
            var taxGroups = _taxService.GetTaxGroups();
            var taxGroupsDto = taxGroups.Select(g => new TaxGroup
            {
                Id = g.Id,
                Description = g.Description,
                TaxAppliedToShipping = g.TaxAppliedToShipping,
                IsActive = g.IsActive
            }).ToList();

            return Ok(taxGroupsDto);
        }

        [HttpGet("ItemTaxGroups")]
        public IActionResult GetItemTaxGroups()
        {
            var itemTaxGroups = _taxService.GetItemTaxGroups();
            var itemTaxGroupsDto = itemTaxGroups.Select(g => new ItemTaxGroup
            {
                Id = g.Id,
                Name = g.Name,
                IsFullyExempt = g.IsFullyExempt
            }).ToList();

            return Ok(itemTaxGroupsDto);
        }

        [HttpPost("AddNewTax")]
        public async Task<IActionResult> AddNewTax([FromBody] TaxForCreation taxForCreationDto)
        {
            var result = await _adminService.CreateTaxAsync(taxForCreationDto);
            if (result.IsFailure)
                return BadRequest(result.Error.Message);

            return Ok(result.Value);
        }

        [HttpPut("EditTax")]
        public async Task<IActionResult> EditTax([FromBody] TaxForUpdate taxForUpdateDto)
        {
            var result = await _adminService.EditTaxAsync(taxForUpdateDto);
            if (result.IsFailure)
                return BadRequest(result.Error.Message);

            return Ok(result.Value);
        }

        [HttpDelete("DeleteTax/{id:int}")]
        public async Task<IActionResult> DeleteTax(int id)
        {
            var result = await _adminService.DeleteTaxAsync(id);
            if (result.IsFailure)
                return BadRequest(result.Error.Message);

            return NoContent();
        }

        [HttpDelete("DeleteTaxGroup/{id:int}")]
        public async Task<IActionResult> DeleteTaxGroup(int id)
        {
            var result = await _adminService.DeleteTaxGroupAsync(id);
            if (result.IsFailure)
                return BadRequest(result.Error.Message);

            return NoContent();
        }

        [HttpDelete("DeleteItemTaxGroup/{id:int}")]
        public async Task<IActionResult> DeleteItemTaxGroup(int id)
        {
            var result = await _adminService.DeleteItemTaxGroupAsync(id);
            if (result.IsFailure)
                return BadRequest(result.Error.Message);

            return NoContent();
        }
    }
}
