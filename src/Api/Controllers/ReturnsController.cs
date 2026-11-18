// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using Api.Filters;
using Api.Service;
using Core.Domain.Security;
using Dto.Inventory;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers
{
    [Authorize]
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

        [HttpPost("supplier/dispatch")]
        [RequirePermission(SystemPermissions.PurchasesReturnsPost)]
        public IActionResult PostSupplierReturnDispatch([FromBody] PostSupplierReturnDispatchRequest request)
        {
            try { return Ok(_service.PostSupplierReturnDispatch(request)); }
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
