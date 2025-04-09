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

        [HttpGet("Taxes/{id:int}")]
        public IActionResult GetTaxById(int id)
        {
            var tax = _taxService.GetTaxById(id);
            if (tax == null)
            {
                return NotFound($"Tax with ID {id} not found.");
            }

            var taxDto = new Tax
            {
                Id = tax.Id,
                TaxCode = tax.TaxCode,
                TaxName = tax.TaxName,
                Rate = tax.Rate,
                IsActive = tax.IsActive
            };

            return Ok(taxDto);
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

        [HttpGet("TaxGroupTaxes/{taxId:int}")]
        public IActionResult GetTaxGroupTaxes(int taxId)
        {
            var taxGroupTaxes = _taxService.GetTaxGroupTaxesByTaxId(taxId);
            return Ok(taxGroupTaxes);
        }

        [HttpGet("ItemTaxGroupTaxes/{taxId:int}")]
        public IActionResult GetItemTaxGroupTaxes(int taxId)
        {
            var itemTaxGroupTaxes = _taxService.GetItemTaxGroupTaxesByTaxId(taxId);
            return Ok(itemTaxGroupTaxes);
        }

        [HttpGet("TaxAccounts/{taxId:int}")]
        public IActionResult GetTaxAccounts(int taxId)
        {
            try
            {
                var (salesAccountId, purchasingAccountId) = _taxService.GetTaxAccountsByTaxId(taxId);
                return Ok(new
                {
                    SalesAccountId = salesAccountId,
                    PurchasingAccountId = purchasingAccountId
                });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(ex.Message);
            }
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
            try
            {
                Console.WriteLine("EditTax API called with data:");
                Console.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(taxForUpdateDto));

                var result = await _adminService.EditTaxAsync(taxForUpdateDto);
                if (result.IsFailure)
                {
                    Console.WriteLine($"EditTax failed: {result.Error.Message}");
                    return BadRequest(result.Error.Message);
                }

                return Ok(result.Value);
            }
            catch (Exception ex)
            {
                // Log the exception
                Console.WriteLine($"Error in EditTax API: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
                return StatusCode(500, "An error occurred while updating the tax.");
            }
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
