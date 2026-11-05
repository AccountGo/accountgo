#nullable enable
using System;
using System.Linq;
using Api.Data;
using Api.Filters;
using Api.Service;
using Core.Authorization;
using Core.Domain;
using Core.Domain.Security;
using Core.Tenancy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Api.Controllers;

[ApiController]
[TenantScoped]
[Route("api/linked-entries")]
public sealed class LinkedEntriesController : ControllerBase
{
    private readonly LinkedEntryService _linked;
    private readonly ApiDbContext _db;
    private readonly ITenantContext _tenant;
    public LinkedEntriesController(LinkedEntryService linked, ApiDbContext db, ITenantContext tenant)
    { _linked = linked; _db = db; _tenant = tenant; }

    [HttpPost]
    [RequireWorkspaceRole(CompanyMemberRole.Owner)]
    [RequirePermission(SystemPermissions.FinancialsJournalCreate)]
    [RequireOperation(AccessOperationCatalog.LinkedEntriesCreate)]
    public IActionResult Create([FromBody] CreateLinkedEntryRequest request)
    {
        var companyId = _tenant.RequireCompanyId();
        var result = _linked.Create(companyId, UserId, request);
        if (result.Failure is { } failure)
            return Problem(statusCode: failure.StatusCode, title: failure.Code, detail: failure.Message, extensions: new System.Collections.Generic.Dictionary<string, object?> { ["code"] = failure.Code });
        return Ok(new { pair = result.Pair, warning = result.Warning });
    }

    [HttpGet]
    [RequireWorkspaceRole(CompanyMemberRole.Owner)]
    public IActionResult List([FromQuery] int? companyId = null)
    {
        var currentCompanyId = _tenant.RequireCompanyId();
        var requestedCompanyId = companyId ?? currentCompanyId;
        if (requestedCompanyId != currentCompanyId) return Forbid();
        var link = _db.WorkspaceLinks.AsNoTracking().FirstOrDefault(l => l.OwnerUserId == UserId &&
            (l.WorkCompanyId == currentCompanyId || l.PersonalCompanyId == currentCompanyId));
        if (link == null) return Ok(new { pairs = Array.Empty<LinkedEntryPair>(), otherWorkspace = (object?)null });
        var otherId = link.WorkCompanyId == currentCompanyId ? link.PersonalCompanyId : link.WorkCompanyId;
        var other = _db.Companies.IgnoreQueryFilters().Where(c => c.Id == otherId)
            .Select(c => new { id = c.Id, name = c.Name, slug = c.Slug }).Single();
        return Ok(new { pairs = _linked.List(currentCompanyId, UserId), otherWorkspace = other });
    }

    [HttpGet("accounts")]
    [RequireWorkspaceRole(CompanyMemberRole.Owner)]
    public IActionResult Accounts() => Ok(_linked.GetPersonalAccounts(_tenant.RequireCompanyId(), UserId));

    [HttpPost("{id:int}/reverse")]
    [RequireWorkspaceRole(CompanyMemberRole.Owner)]
    public IActionResult Reverse(int id, [FromBody] ReverseLinkedEntryRequest request)
    {
        var companyId = _tenant.RequireCompanyId();
        var result = _linked.Reverse(companyId, UserId, id, request.Date, request.Reason);
        if (result.Failure is { } failure)
            return Problem(statusCode: failure.StatusCode, title: failure.Code, detail: failure.Message,
                extensions: new System.Collections.Generic.Dictionary<string, object?> { ["code"] = failure.Code });
        return Ok(new { pair = result.Pair });
    }

    private string UserId => User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "";
}

public sealed class ReverseLinkedEntryRequest
{
    public DateTime Date { get; set; }
    public string? Reason { get; set; }
}
