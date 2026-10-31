using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Api.Filters;
using Api.Service;
using Core.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

/// <summary>Describes email options for the server-resolved current organization.</summary>
[Authorize]
[TenantScoped]
[ApiController]
[Route("api/email/capabilities")]
public sealed class EmailCapabilitiesController(IEmailCapabilities capabilities) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var actor = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (string.IsNullOrEmpty(actor)) return Unauthorized();
        try { return Ok(await capabilities.GetAsync(actor, ct)); }
        catch (EmailPolicyException ex) { return StatusCode(ex.StatusCode, new { code = ex.Code, message = ex.Message }); }
    }
}
