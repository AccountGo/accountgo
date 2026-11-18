// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using Api.Filters;
using Api.Service;
using Core.Domain.Security;
using Core.Tenancy;
using Dto.Purchasing;
using Dto.Sales;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers
{
    [Authorize]
    [TenantScoped]
    [ApiController]
    [Route("api/purchasing/credits")]
    public sealed class PurchasingCreditsController : ControllerBase
    {
        private readonly SupplierCreditSettlementService _service;

        public PurchasingCreditsController(SupplierCreditSettlementService service)
        {
            _service = service;
        }

        [HttpGet]
        [RequirePermission(SystemPermissions.PurchasesCreditsView)]
        public IActionResult List([FromQuery] int? vendorId = null, [FromQuery] int? billId = null) =>
            Ok(_service.List(vendorId, billId));

        [HttpGet("{id:int}")]
        [RequirePermission(SystemPermissions.PurchasesCreditsView)]
        public IActionResult Get(int id)
        {
            try
            {
                return Ok(_service.Get(id));
            }
            catch (SettlementCommandException ex)
            {
                return Problem(detail: ex.Message, statusCode: ex.HttpStatus, title: ex.Code);
            }
        }

        [HttpPost("drafts")]
        [RequirePermission(SystemPermissions.PurchasesCreditsCreate)]
        public IActionResult SaveDraft([FromBody] SaveSupplierCreditDraftRequest request)
        {
            try
            {
                return Ok(_service.SaveDraft(request));
            }
            catch (SettlementCommandException ex)
            {
                return Problem(detail: ex.Message, statusCode: ex.HttpStatus, title: ex.Code);
            }
        }

        [HttpPost("{id:int}/submit")]
        [RequirePermission(SystemPermissions.PurchasesCreditsCreate)]
        public IActionResult Submit(int id, [FromQuery] string? idempotencyKey = null)
        {
            try
            {
                return Ok(_service.SubmitForReview(id, idempotencyKey));
            }
            catch (SettlementCommandException ex)
            {
                return Problem(detail: ex.Message, statusCode: ex.HttpStatus, title: ex.Code);
            }
        }

        [HttpPost("{id:int}/post")]
        [RequirePermission(SystemPermissions.PurchasesCreditsPost)]
        public IActionResult Post(int id, [FromBody] PostSupplierCreditRequest request)
        {
            try
            {
                return Ok(_service.Post(id, request));
            }
            catch (SettlementCommandException ex)
            {
                return Problem(detail: ex.Message, statusCode: ex.HttpStatus, title: ex.Code);
            }
        }

        [HttpPost("{id:int}/apply")]
        [RequirePermission(SystemPermissions.FinancialsSettlementsApply)]
        public IActionResult Apply(int id, [FromBody] ApplySupplierCreditRequest request)
        {
            try
            {
                return Ok(_service.Apply(id, request));
            }
            catch (SettlementCommandException ex)
            {
                return Problem(detail: ex.Message, statusCode: ex.HttpStatus, title: ex.Code);
            }
        }

        [HttpPost("applications/{applicationId:int}/reverse")]
        [RequirePermission(SystemPermissions.FinancialsSettlementsReverse)]
        public IActionResult ReverseApplication(int applicationId, [FromBody] ReverseSettlementRequest request)
        {
            try
            {
                return Ok(_service.ReverseApplication(applicationId, request));
            }
            catch (SettlementCommandException ex)
            {
                return Problem(detail: ex.Message, statusCode: ex.HttpStatus, title: ex.Code);
            }
        }

        [HttpPost("{id:int}/reverse")]
        [RequirePermission(SystemPermissions.PurchasesCreditsReverse)]
        public IActionResult Reverse(int id, [FromBody] ReverseSettlementRequest request)
        {
            try
            {
                return Ok(_service.Reverse(id, request));
            }
            catch (SettlementCommandException ex)
            {
                return Problem(detail: ex.Message, statusCode: ex.HttpStatus, title: ex.Code);
            }
        }
    }
}
