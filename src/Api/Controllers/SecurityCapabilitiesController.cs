// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using System.Threading;
using System.Threading.Tasks;
using Api.ActionFilters;
using Api.Data;
using Api.Service;
using Core.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

[TenantFree]
[Authorize]
[ApiController]
[Route("api/security")]
public class SecurityCapabilitiesController : ControllerBase
{
    private readonly ISecurityCapabilities _capabilities;
    private readonly UserManager<ApplicationUser> _userManager;

    public SecurityCapabilitiesController(
        ISecurityCapabilities capabilities,
        UserManager<ApplicationUser> userManager)
    {
        _capabilities = capabilities;
        _userManager = userManager;
    }

    [HttpGet("capabilities")]
    public async Task<IActionResult> GetCapabilities(CancellationToken ct)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null)
            return Unauthorized();

        var result = await _capabilities.GetAsync(user, ct);
        return Ok(result);
    }
}
