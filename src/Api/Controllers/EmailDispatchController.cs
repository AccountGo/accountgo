using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Api.Service;
using Core.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

/// <summary>Acceptance certainty and safe abandonment are scoped to the current actor and invoice.</summary>
[Authorize]
[TenantScoped]
[ApiController]
[Route("api/email/dispatches/{dispatchId:guid}")]
public sealed class EmailDispatchController(IEmailDispatchService dispatch, ITenantContext tenant) : ControllerBase
{
    [HttpGet]
    public Task<IActionResult> Get(Guid dispatchId, int invoiceId, string purpose, CancellationToken ct) =>
        Execute(dispatchId, invoiceId, purpose, false, ct);

    [HttpPost("abandon")]
    public Task<IActionResult> Abandon(Guid dispatchId, int invoiceId, string purpose, CancellationToken ct) =>
        Execute(dispatchId, invoiceId, purpose, true, ct);

    private async Task<IActionResult> Execute(Guid id, int invoice, string purpose, bool abandon, CancellationToken ct)
    {
        var actor = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (string.IsNullOrEmpty(actor)) return Unauthorized();
        var context = new EmailDispatchContext(tenant.RequireCompanyId(), actor, invoice, purpose, "custom", DispatchId: id);
        try { return Ok(abandon ? await dispatch.AbandonAsync(context, ct) : await dispatch.GetDispositionAsync(context, ct)); }
        catch (EmailPolicyException ex) { return StatusCode(ex.StatusCode, new { code = ex.Code, message = ex.Message }); }
    }
}
