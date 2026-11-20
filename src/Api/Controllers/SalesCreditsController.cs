// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using Api.Filters;
using Api.Service;
using Core.Domain.Security;
using Core.Tenancy;
using Dto.Sales;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers
{
    [Authorize]
    [TenantScoped]
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
        public IActionResult List([FromQuery] [Dto.Common.PublicId("cus")] int? customerId = null, [FromQuery] [Dto.Common.PublicId("inv")] int? invoiceId = null) =>
            Ok(_service.List(customerId, invoiceId));

        [HttpGet("{id}")]
        [RequirePermission(SystemPermissions.SalesCreditsView)]
        public IActionResult Get([Dto.Common.PublicId("cn")] int id)
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

        [HttpPost("{id}/submit")]
        [RequirePermission(SystemPermissions.SalesCreditsCreate)]
        public IActionResult Submit([Dto.Common.PublicId("cn")] int id, [FromQuery] string? idempotencyKey = null)
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

        [HttpPost("{id}/post")]
        [RequirePermission(SystemPermissions.SalesCreditsPost)]
        public IActionResult Post([Dto.Common.PublicId("cn")] int id, [FromBody] PostCustomerCreditRequest request)
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

        [HttpPost("{id}/apply")]
        [RequirePermission(SystemPermissions.FinancialsSettlementsApply)]
        public IActionResult Apply([Dto.Common.PublicId("cn")] int id, [FromBody] ApplyCustomerCreditRequest request)
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

        [HttpPost("applications/{applicationId}/reverse")]
        [RequirePermission(SystemPermissions.FinancialsSettlementsReverse)]
        public IActionResult ReverseApplication([Dto.Common.PublicId("sapp")] int applicationId, [FromBody] ReverseSettlementRequest request)
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

        [HttpPost("{id}/reverse")]
        [RequirePermission(SystemPermissions.SalesCreditsReverse)]
        public IActionResult Reverse([Dto.Common.PublicId("cn")] int id, [FromBody] ReverseSettlementRequest request)
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
