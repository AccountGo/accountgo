// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
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
    [Route("api/sales/write-offs")]
    public sealed class WriteOffsController : ControllerBase
    {
        private readonly WriteOffService _service;

        public WriteOffsController(WriteOffService service) => _service = service;

        [HttpPost("requests")]
        [RequirePermission(SystemPermissions.SalesWriteOffsCreate)]
        public IActionResult RequestWriteOff([FromBody] RequestWriteOffCommand request)
        {
            try { return Ok(_service.RequestWriteOff(request)); }
            catch (SettlementCommandException ex) { return Problem(detail: ex.Message, statusCode: ex.HttpStatus, title: ex.Code); }
        }

        [HttpPost("{id:int}/post")]
        [RequirePermission(SystemPermissions.SalesWriteOffsPost)]
        public IActionResult Post(int id, [FromBody] PostWriteOffCommand request)
        {
            try { return Ok(_service.Post(id, request)); }
            catch (SettlementCommandException ex) { return Problem(detail: ex.Message, statusCode: ex.HttpStatus, title: ex.Code); }
        }

        [HttpPost("{id:int}/reverse")]
        [RequirePermission(SystemPermissions.SalesWriteOffsReverse)]
        public IActionResult Reverse(int id, [FromBody] ReverseSettlementRequest request)
        {
            try { return Ok(_service.Reverse(id, request)); }
            catch (SettlementCommandException ex) { return Problem(detail: ex.Message, statusCode: ex.HttpStatus, title: ex.Code); }
        }

        [HttpPost("{id:int}/recoveries")]
        [RequirePermission(SystemPermissions.SalesWriteOffsPost)]
        public IActionResult RecordRecovery(int id, [FromBody] RecordWriteOffRecoveryCommand request)
        {
            try { return Ok(_service.RecordRecovery(id, request)); }
            catch (SettlementCommandException ex) { return Problem(detail: ex.Message, statusCode: ex.HttpStatus, title: ex.Code); }
        }

        [HttpPost("{writeOffId:int}/recoveries/{recoveryId:int}/reverse")]
        [RequirePermission(SystemPermissions.SalesWriteOffsReverse)]
        public IActionResult ReverseRecovery(int writeOffId, int recoveryId, [FromBody] ReverseSettlementRequest request)
        {
            try { return Ok(_service.ReverseRecovery(writeOffId, recoveryId, request)); }
            catch (SettlementCommandException ex) { return Problem(detail: ex.Message, statusCode: ex.HttpStatus, title: ex.Code); }
        }
    }
}
