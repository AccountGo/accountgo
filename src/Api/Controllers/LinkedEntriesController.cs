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
    public IActionResult Create([FromBody] CreateLinkedEntryHttpRequest request)
    {
        var companyId = _tenant.RequireCompanyId();
        int personalAccountId = 0;
        if (!string.IsNullOrWhiteSpace(request.PersonalAccountId))
        {
            if (!Core.Domain.PublicIdGenerator.TryParse(request.PersonalAccountId.Trim(), out var prefix, out _))
                return BadRequest(new { message = "IDs are strings like 'acct_…'.", code = Api.PublicIds.PublicIdErrorCodes.InvalidId });
            if (!string.Equals(prefix, "acct", StringComparison.Ordinal))
                return BadRequest(new { message = "Expected an 'acct_…' ID.", code = Api.PublicIds.PublicIdErrorCodes.InvalidId });

            var link = _db.WorkspaceLinks.AsNoTracking()
                .FirstOrDefault(l => l.OwnerUserId == UserId && l.WorkCompanyId == companyId);
            if (link != null)
            {
                var resolved = _db.Accounts.IgnoreQueryFilters().AsNoTracking()
                    .Where(a => a.CompanyId == link.PersonalCompanyId && a.PublicId == request.PersonalAccountId.Trim())
                    .Select(a => (int?)a.Id)
                    .FirstOrDefault();
                if (!resolved.HasValue)
                    return NotFound(new { message = Api.PublicIds.PublicIdErrorCodes.NotFoundMessage, code = Api.PublicIds.PublicIdErrorCodes.UnknownId });
                personalAccountId = resolved.Value;
            }
        }

        var serviceRequest = new CreateLinkedEntryRequest
        {
            Kind = request.Kind,
            Date = request.Date,
            Amount = request.Amount,
            WorkBankAccountId = request.WorkBankAccountId,
            PersonalAccountId = personalAccountId,
            MirrorAmount = request.MirrorAmount,
            Memo = request.Memo,
            StatementLineId = request.StatementLineId,
            SplitWorkExpenseAccountId = request.SplitWorkExpenseAccountId,
            SplitWorkAmount = request.SplitWorkAmount,
            IdempotencyKey = request.IdempotencyKey
        };

        var result = _linked.Create(companyId, UserId, serviceRequest);
        if (result.Failure is { } failure)
            return Problem(statusCode: failure.StatusCode, title: failure.Code, detail: failure.Message, extensions: new System.Collections.Generic.Dictionary<string, object?> { ["code"] = failure.Code });
        return Ok(new { pair = ToDto(result.Pair!), warning = result.Warning });
    }

    [HttpGet]
    [RequireWorkspaceRole(CompanyMemberRole.Owner)]
    public IActionResult List([FromQuery] [Dto.Common.PublicId("ws")] int? companyId = null)
    {
        var currentCompanyId = _tenant.RequireCompanyId();
        var requestedCompanyId = companyId ?? currentCompanyId;
        if (requestedCompanyId != currentCompanyId) return Forbid();
        var link = _db.WorkspaceLinks.AsNoTracking().FirstOrDefault(l => l.OwnerUserId == UserId &&
            (l.WorkCompanyId == currentCompanyId || l.PersonalCompanyId == currentCompanyId));
        if (link == null) return Ok(new { pairs = Array.Empty<LinkedEntryPairDto>(), otherWorkspace = (object?)null });
        var otherId = link.WorkCompanyId == currentCompanyId ? link.PersonalCompanyId : link.WorkCompanyId;
        var other = _db.Companies.IgnoreQueryFilters().Where(c => c.Id == otherId)
            .Select(c => new { id = c.PublicId, name = c.Name, slug = c.Slug }).Single();
        return Ok(new { pairs = _linked.List(currentCompanyId, UserId).Select(ToDto).ToList(), otherWorkspace = other });
    }

    [HttpGet("accounts")]
    [RequireWorkspaceRole(CompanyMemberRole.Owner)]
    public IActionResult Accounts() => Ok(_linked.GetPersonalAccounts(_tenant.RequireCompanyId(), UserId));

    [HttpPost("{id}/reverse")]
    [RequireWorkspaceRole(CompanyMemberRole.Owner)]
    public IActionResult Reverse([Dto.Common.PublicId("lep")] int id, [FromBody] ReverseLinkedEntryRequest request)
    {
        var companyId = _tenant.RequireCompanyId();
        var result = _linked.Reverse(companyId, UserId, id, request.Date, request.Reason);
        if (result.Failure is { } failure)
            return Problem(statusCode: failure.StatusCode, title: failure.Code, detail: failure.Message,
                extensions: new System.Collections.Generic.Dictionary<string, object?> { ["code"] = failure.Code });
        return Ok(new { pair = ToDto(result.Pair!) });
    }

    private static LinkedEntryPairDto ToDto(LinkedEntryPair p)
    {
        var cancelled = string.Equals(p.State, "Cancelled", StringComparison.Ordinal);
        return new(
            p.Id, p.OwnerUserId, p.Kind,
            p.SourceCompanyId, cancelled ? null : p.SourceJournalId, p.SourceAmount, p.SourceCurrency,
            p.MirrorCompanyId, cancelled ? null : p.MirrorJournalId, p.MirrorAmount, p.MirrorCurrency,
            p.WorkBankAccountId, p.ExchangeRate, p.StatementLineId, p.State, p.IdempotencyKey,
            p.CreatedAtUtc);
    }

    private string UserId => User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "";
}

public sealed record LinkedEntryPairDto(
    [property: Dto.Common.PublicId("lep")] int Id,
    string OwnerUserId,
    string Kind,
    [property: Dto.Common.PublicId("ws")] int SourceCompanyId,
    [property: Dto.Common.PublicId("je")] int? SourceJournalId,
    decimal SourceAmount,
    string SourceCurrency,
    [property: Dto.Common.PublicId("ws")] int MirrorCompanyId,
    [property: Dto.Common.PublicId("je")] int? MirrorJournalId,
    decimal MirrorAmount,
    string MirrorCurrency,
    [property: Dto.Common.PublicId("acct")] int WorkBankAccountId,
    decimal ExchangeRate,
    [property: Dto.Common.PublicId("bsl")] int? StatementLineId,
    string State,
    string? IdempotencyKey,
    DateTime CreatedAtUtc);

public sealed class CreateLinkedEntryHttpRequest
{
    public string Kind { get; set; } = "";
    public DateTime Date { get; set; }
    public decimal Amount { get; set; }
    [Dto.Common.PublicId("ba")]
    public int WorkBankAccountId { get; set; }
    /// <summary>Public ID (<c>acct_…</c>) of the target account in the caller's linked personal workspace.</summary>
    public string PersonalAccountId { get; set; } = "";
    public decimal? MirrorAmount { get; set; }
    public string? Memo { get; set; }
    [Dto.Common.PublicId("bsl")]
    public int? StatementLineId { get; set; }
    [Dto.Common.PublicId("acct")]
    public int? SplitWorkExpenseAccountId { get; set; }
    public decimal? SplitWorkAmount { get; set; }
    public string? IdempotencyKey { get; set; }
}

public sealed class ReverseLinkedEntryRequest
{
    public DateTime Date { get; set; }
    public string? Reason { get; set; }
}
