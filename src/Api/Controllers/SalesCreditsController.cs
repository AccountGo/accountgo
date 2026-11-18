// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using Api.Filters;
using Api.Service;
using Core.Domain.Security;
using Dto.Sales;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/sales/credits")]
    public sealed class SalesCreditsController : ControllerBase
    {
        private readonly CustomerCreditSettlementService _service;
        private readonly SettlementAccountMappingService _mappings;

        public SalesCreditsController(
            CustomerCreditSettlementService service,
            SettlementAccountMappingService mappings)
        {
            _service = service;
            _mappings = mappings;
        }

        [HttpGet("mappings/preview")]
        [RequirePermission(SystemPermissions.FinancialsAccountsView)]
        public IActionResult GetMappingsPreview() => Ok(_mappings.GetPreview());

        [HttpPut("mappings")]
        [RequirePermission(SystemPermissions.FinancialsAccountsManage)]
        public IActionResult UpdateMappings([FromBody] UpdateSettlementAccountMappingsRequest request)
        {
            try
            {
                return Ok(_mappings.UpdateMappings(request));
            }
            catch (SettlementCommandException ex)
            {
                return Problem(detail: ex.Message, statusCode: ex.HttpStatus, title: ex.Code);
            }
        }

        [HttpGet]
        [RequirePermission(SystemPermissions.SalesCreditsView)]
        public IActionResult List([FromQuery] int? customerId = null, [FromQuery] int? invoiceId = null) =>
            Ok(_service.List(customerId, invoiceId));

        [HttpGet("{id:int}")]
        [RequirePermission(SystemPermissions.SalesCreditsView)]
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

        [HttpPost("preview")]
        [RequirePermission(SystemPermissions.SalesCreditsView)]
        public IActionResult Preview([FromBody] SaveCustomerCreditDraftRequest request)
        {
            try
            {
                return Ok(_service.Preview(request));
            }
            catch (SettlementCommandException ex)
            {
                return Problem(detail: ex.Message, statusCode: ex.HttpStatus, title: ex.Code);
            }
        }

        [HttpPost("drafts")]
        [RequirePermission(SystemPermissions.SalesCreditsCreate)]
        public IActionResult SaveDraft([FromBody] SaveCustomerCreditDraftRequest request)
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
        [RequirePermission(SystemPermissions.SalesCreditsCreate)]
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
        [RequirePermission(SystemPermissions.SalesCreditsPost)]
        public IActionResult Post(int id, [FromBody] PostCustomerCreditRequest request)
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
        public IActionResult Apply(int id, [FromBody] ApplyCustomerCreditRequest request)
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
        [RequirePermission(SystemPermissions.SalesCreditsReverse)]
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
