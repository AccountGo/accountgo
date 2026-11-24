// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using Api.Data;
using Api.DocumentCaptures;
using Api.DocumentCaptures.Mapping;
using Api.Filters;
using Api.Service;
using Core.Domain;
using Core.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Api.Controllers;

/// <summary>
/// The workspace owner's consent to send scanned-document text to the field mapper. Whether the setting is offered at all is the host's
/// call (<see cref="ICaptureAiMappingAvailability"/>); whether a given job may run is the host policy's call. This controller only
/// records and reports consent.
/// </summary>
[ApiController]
[Authorize]
[TenantScoped]
[Route("api/document-captures/ai-mapping/settings")]
public sealed class CaptureAiMappingSettingsController(
    ITenantContext tenant,
    ICaptureAiMappingAvailability availability,
    ICaptureAiMappingConsentStore consents,
    IOptions<DocumentCaptureOptions> options,
    ApplicationIdentityDbContext identity,
    ISecurityAuditService audit) : ControllerBase
{
    public sealed record UpdateRequest(bool? Enabled);

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var companyId = tenant.RequireCompanyId();
        var offered = await GetAvailabilityAsync(companyId, cancellationToken).ConfigureAwait(false);
        return Ok(await BuildViewAsync(companyId, offered, cancellationToken).ConfigureAwait(false));
    }

    [HttpPut]
    [RequestSizeLimit(1024)]
    public async Task<IActionResult> Put([FromBody] UpdateRequest request, CancellationToken cancellationToken)
    {
        if (request.Enabled is null)
            return Problem(400, "AI_MAPPING_INVALID");
        if (tenant.Role != CompanyMemberRole.Owner || string.IsNullOrEmpty(tenant.UserId))
            return Problem(403, "AI_MAPPING_OWNER_ONLY");

        var companyId = tenant.RequireCompanyId();
        var offered = await GetAvailabilityAsync(companyId, cancellationToken).ConfigureAwait(false);
        var userId = tenant.UserId!;
        if (request.Enabled.Value)
        {
            if (!offered.Available)
                return Problem(409, "AI_MAPPING_UNAVAILABLE");
            if (await consents.GrantAsync(companyId, userId, cancellationToken).ConfigureAwait(false))
                await audit.LogEventAsync("document.capture.ai-consent.granted", "success", userId, companyId,
                    new { consentVersion = CaptureAiMappingConsentNotice.CurrentVersion }).ConfigureAwait(false);
        }
        else if (await consents.RevokeAsync(companyId, userId, cancellationToken).ConfigureAwait(false))
        {
            // Turning it off is always allowed, even when the host no longer offers the setting.
            await audit.LogEventAsync("document.capture.ai-consent.revoked", "success", userId, companyId, new { }).ConfigureAwait(false);
        }

        return Ok(await BuildViewAsync(companyId, offered, cancellationToken).ConfigureAwait(false));
    }

    private async Task<CaptureAiMappingAvailability> GetAvailabilityAsync(int companyId, CancellationToken cancellationToken) =>
        options.Value.Enabled
            ? await availability.GetAsync(companyId, cancellationToken).ConfigureAwait(false)
            : CaptureAiMappingAvailability.Unavailable(CaptureAiMappingUnavailableReasons.NotOffered);

    private async Task<object> BuildViewAsync(int companyId, CaptureAiMappingAvailability offered, CancellationToken cancellationToken)
    {
        var consent = await consents.GetAsync(companyId, cancellationToken).ConfigureAwait(false);
        string? consentedBy = null;
        if (consent is { IsCurrent: true } && tenant.Role is CompanyMemberRole.Owner or CompanyMemberRole.Admin)
        {
            var user = await identity.Users.AsNoTracking().Where(u => u.Id == consent.ConsentedByUserId)
                .Select(u => u.Email ?? u.UserName).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            consentedBy = string.IsNullOrWhiteSpace(user) ? null : user;
        }

        return new
        {
            available = offered.Available,
            unavailableReason = offered.Available ? null : offered.UnavailableReason,
            enabled = consent?.IsCurrent == true,
            consentedAt = consent?.IsCurrent == true ? consent.ConsentedAt : (DateTime?)null,
            consentedBy,
            consentVersion = CaptureAiMappingConsentNotice.CurrentVersion,
            needsReconsent = consent?.NeedsReconsent == true,
            canManage = tenant.Role == CompanyMemberRole.Owner
        };
    }

    private ObjectResult Problem(int status, string code)
    {
        var problem = new ProblemDetails { Status = status, Title = code };
        problem.Extensions["code"] = code;
        return new ObjectResult(problem) { StatusCode = status };
    }
}
