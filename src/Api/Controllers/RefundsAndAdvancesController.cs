// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using Api.Filters;
using Api.Service;
using Core.Domain;
using Core.Domain.Security;
using Core.Tenancy;
using Dto.Financial;
using Dto.Sales;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers
{
    [Authorize]
    [TenantScoped]
    [ApiController]
    [Route("api/settlements")]
    public sealed class RefundsAndAdvancesController : ControllerBase
    {
        private readonly RefundAndAdvanceService _service;

        public RefundsAndAdvancesController(RefundAndAdvanceService service)
        {
            _service = service;
        }

        [HttpPost("advances/drafts")]
        [RequirePermission(SystemPermissions.PurchasesAdvancesCreate)]
        public IActionResult SaveAdvanceDraft([FromBody] SaveSupplierAdvanceDraftRequest request)
        {
            try { return Ok(_service.SaveAdvanceDraft(request)); }
            catch (SettlementCommandException ex) { return Problem(detail: ex.Message, statusCode: ex.HttpStatus, title: ex.Code); }
        }

        [HttpPost("advances/{id}/submit")]
        [RequirePermission(SystemPermissions.PurchasesAdvancesCreate)]
        public IActionResult SubmitAdvance([Dto.Common.PublicId("adv")] int id, [FromQuery] string? idempotencyKey = null)
        {
            try { return Ok(_service.SubmitAdvance(id, idempotencyKey)); }
            catch (SettlementCommandException ex) { return Problem(detail: ex.Message, statusCode: ex.HttpStatus, title: ex.Code); }
        }

        [HttpPost("advances/{id}/post")]
        [RequirePermission(SystemPermissions.PurchasesAdvancesPost)]
        public IActionResult PostAdvance([Dto.Common.PublicId("adv")] int id, [FromBody] PostPartyRefundRequest request)
        {
            try { return Ok(_service.PostAdvance(id, request)); }
            catch (SettlementCommandException ex) { return Problem(detail: ex.Message, statusCode: ex.HttpStatus, title: ex.Code); }
        }

        [HttpPost("advances/{id}/apply")]
        [RequirePermission(SystemPermissions.FinancialsSettlementsApply)]
        public IActionResult ApplyAdvance([Dto.Common.PublicId("adv")] int id, [FromBody] ApplySupplierAdvanceRequest request)
        {
            try { return Ok(_service.ApplyAdvance(id, request)); }
            catch (SettlementCommandException ex) { return Problem(detail: ex.Message, statusCode: ex.HttpStatus, title: ex.Code); }
        }

        [HttpPost("advances/{id}/reverse")]
        [RequirePermission(SystemPermissions.PurchasesAdvancesReverse)]
        public IActionResult ReverseAdvance([Dto.Common.PublicId("adv")] int id, [FromBody] ReverseSettlementRequest request)
        {
            try { return Ok(_service.ReverseAdvance(id, request)); }
            catch (SettlementCommandException ex) { return Problem(detail: ex.Message, statusCode: ex.HttpStatus, title: ex.Code); }
        }

        [HttpPost("refunds/drafts")]
        [RequirePermission(SystemPermissions.SalesRefundsCreate)]
        public IActionResult SaveCustomerRefundDraft([FromBody] SavePartyRefundDraftRequest request)
        {
            try { return Ok(_service.SaveRefundDraft(request, PartyRefundDirection.CustomerPayout)); }
            catch (SettlementCommandException ex) { return Problem(detail: ex.Message, statusCode: ex.HttpStatus, title: ex.Code); }
        }

        [HttpPost("refunds/supplier/drafts")]
        [RequirePermission(SystemPermissions.PurchasesRefundsCreate)]
        public IActionResult SaveSupplierRefundDraft([FromBody] SavePartyRefundDraftRequest request)
        {
            try { return Ok(_service.SaveRefundDraft(request, PartyRefundDirection.SupplierInflow)); }
            catch (SettlementCommandException ex) { return Problem(detail: ex.Message, statusCode: ex.HttpStatus, title: ex.Code); }
        }

        [HttpPost("refunds/{id}/submit")]
        [RequirePermission(SystemPermissions.SalesRefundsCreate)]
        public IActionResult SubmitRefund([Dto.Common.PublicId("rf")] int id, [FromQuery] string? idempotencyKey = null)
        {
            try { return Ok(_service.SubmitRefund(id, idempotencyKey, PartyRefundDirection.CustomerPayout)); }
            catch (SettlementCommandException ex) { return Problem(detail: ex.Message, statusCode: ex.HttpStatus, title: ex.Code); }
        }

        [HttpPost("refunds/supplier/{id}/submit")]
        [RequirePermission(SystemPermissions.PurchasesRefundsCreate)]
        public IActionResult SubmitSupplierRefund([Dto.Common.PublicId("rf")] int id, [FromQuery] string? idempotencyKey = null)
        {
            try { return Ok(_service.SubmitRefund(id, idempotencyKey, PartyRefundDirection.SupplierInflow)); }
            catch (SettlementCommandException ex) { return Problem(detail: ex.Message, statusCode: ex.HttpStatus, title: ex.Code); }
        }

        [HttpPost("refunds/{id}/post")]
        [RequirePermission(SystemPermissions.SalesRefundsPost)]
        public IActionResult PostCustomerRefund([Dto.Common.PublicId("rf")] int id, [FromBody] PostPartyRefundRequest request)
        {
            try { return Ok(_service.PostRefund(id, request, PartyRefundDirection.CustomerPayout)); }
            catch (SettlementCommandException ex) { return Problem(detail: ex.Message, statusCode: ex.HttpStatus, title: ex.Code); }
        }

        [HttpPost("refunds/supplier/{id}/post")]
        [RequirePermission(SystemPermissions.PurchasesRefundsPost)]
        public IActionResult PostSupplierRefund([Dto.Common.PublicId("rf")] int id, [FromBody] PostPartyRefundRequest request)
        {
            try { return Ok(_service.PostRefund(id, request, PartyRefundDirection.SupplierInflow)); }
            catch (SettlementCommandException ex) { return Problem(detail: ex.Message, statusCode: ex.HttpStatus, title: ex.Code); }
        }

        [HttpPost("refunds/{id}/reverse")]
        [RequirePermission(SystemPermissions.SalesRefundsReverse)]
        public IActionResult ReverseRefund([Dto.Common.PublicId("rf")] int id, [FromBody] ReverseSettlementRequest request)
        {
            try { return Ok(_service.ReverseRefund(id, request, PartyRefundDirection.CustomerPayout)); }
            catch (SettlementCommandException ex) { return Problem(detail: ex.Message, statusCode: ex.HttpStatus, title: ex.Code); }
        }

        [HttpPost("refunds/supplier/{id}/reverse")]
        [RequirePermission(SystemPermissions.PurchasesRefundsReverse)]
        public IActionResult ReverseSupplierRefund([Dto.Common.PublicId("rf")] int id, [FromBody] ReverseSettlementRequest request)
        {
            try { return Ok(_service.ReverseRefund(id, request, PartyRefundDirection.SupplierInflow)); }
            catch (SettlementCommandException ex) { return Problem(detail: ex.Message, statusCode: ex.HttpStatus, title: ex.Code); }
        }
    }
}
