// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using Api.Filters;
using Api.Service;
using Core.Domain.Security;
using Core.Tenancy;
using Dto.Inventory;
using Dto.Sales;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers
{
    [Authorize]
    [TenantScoped]
    [ApiController]
    [Route("api/returns")]
    public sealed class ReturnsController : ControllerBase
    {
        private readonly ReturnsAndInventoryAdjustmentService _service;

        public ReturnsController(ReturnsAndInventoryAdjustmentService service) => _service = service;

        [HttpPost("customer")]
        [RequirePermission(SystemPermissions.SalesReturnsPost)]
        public IActionResult PostCustomerReturn([FromBody] PostCustomerReturnRequest request)
        {
            try { return Ok(_service.PostCustomerReturn(request)); }
            catch (SettlementCommandException ex) { return Problem(detail: ex.Message, statusCode: ex.HttpStatus, title: ex.Code); }
        }

        [HttpPost("customer/{id}/reverse")]
        [RequirePermission(SystemPermissions.SalesReturnsReverse)]
        public IActionResult ReverseCustomerReturn([Dto.Common.PublicId("crr")] int id, [FromBody] ReverseSettlementRequest request)
        {
            try { return Ok(_service.ReverseCustomerReturn(id, request)); }
            catch (SettlementCommandException ex) { return Problem(detail: ex.Message, statusCode: ex.HttpStatus, title: ex.Code); }
        }

        [HttpPost("supplier/dispatch")]
        [RequirePermission(SystemPermissions.PurchasesReturnsPost)]
        public IActionResult PostSupplierReturnDispatch([FromBody] PostSupplierReturnDispatchRequest request)
        {
            try { return Ok(_service.PostSupplierReturnDispatch(request)); }
            catch (SettlementCommandException ex) { return Problem(detail: ex.Message, statusCode: ex.HttpStatus, title: ex.Code); }
        }

        [HttpPost("supplier/dispatch/{id}/reverse")]
        [RequirePermission(SystemPermissions.PurchasesReturnsReverse)]
        public IActionResult ReverseSupplierReturnDispatch([Dto.Common.PublicId("srd")] int id, [FromBody] ReverseSettlementRequest request)
        {
            try { return Ok(_service.ReverseSupplierReturnDispatch(id, request)); }
            catch (SettlementCommandException ex) { return Problem(detail: ex.Message, statusCode: ex.HttpStatus, title: ex.Code); }
        }

        [HttpPost("supplier/match-credit")]
        [RequirePermission(SystemPermissions.PurchasesCreditsPost)]
        public IActionResult MatchSupplierReturnCredit([FromBody] MatchSupplierReturnCreditRequest request)
        {
            try { return Ok(_service.MatchSupplierReturnCredit(request)); }
            catch (SettlementCommandException ex) { return Problem(detail: ex.Message, statusCode: ex.HttpStatus, title: ex.Code); }
        }

        [HttpPost("supplier/rebate")]
        [RequirePermission(SystemPermissions.PurchasesCreditsPost)]
        public IActionResult PostStockRebate([FromBody] PostStockRebateRequest request)
        {
            try { return Ok(_service.PostStockRebate(request)); }
            catch (SettlementCommandException ex) { return Problem(detail: ex.Message, statusCode: ex.HttpStatus, title: ex.Code); }
        }
    }
}
