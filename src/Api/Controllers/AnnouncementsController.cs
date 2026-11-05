// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using Api.Announcements;
using Core.Domain;
using Core.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Api.Controllers;

/// <summary>Workspace announcement strip. Aggregates optional <see cref="IAnnouncementSource"/> registrations.</summary>
[ApiController]
[Authorize]
[TenantScoped]
[Route("api/announcements")]
public sealed class AnnouncementsController : BaseController
{
    private static readonly Dictionary<string, int> SeverityRank = new(StringComparer.OrdinalIgnoreCase)
    {
        ["critical"] = 0,
        ["warning"] = 1,
        ["info"] = 2,
    };

    private readonly IEnumerable<IAnnouncementSource> _sources;
    private readonly ITenantContext _tenant;
    private readonly ILogger<AnnouncementsController> _logger;

    public AnnouncementsController(
        IEnumerable<IAnnouncementSource> sources,
        ITenantContext tenant,
        ILogger<AnnouncementsController> logger)
    {
        _sources = sources ?? Array.Empty<IAnnouncementSource>();
        _tenant = tenant;
        _logger = logger;
    }

    [HttpGet]
    public IActionResult Get()
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value;
        if (string.IsNullOrWhiteSpace(userId))
            return Unauthorized();

        var companyId = _tenant.RequireCompanyId();
        var role = _tenant.Role?.ToString() ?? string.Empty;
        var ctx = new AnnouncementContext(userId, companyId, role);
        var now = DateTimeOffset.UtcNow;
        var isMember = _tenant.Role == CompanyMemberRole.Member;

        var items = new List<AnnouncementDto>();
        foreach (var source in _sources)
        {
            try
            {
                items.AddRange(source.GetAnnouncements(ctx));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Announcement source {Source} failed.", source.GetType().FullName);
            }
        }

        var filtered = items
            .Where(a => a.ExpiresAt == null || a.ExpiresAt > now)
            .Where(a => !isMember
                || !string.Equals(a.Audience, "owner_admin", StringComparison.OrdinalIgnoreCase))
            .OrderBy(a => SeverityRank.TryGetValue(a.Severity, out var rank) ? rank : 99)
            .ThenBy(a => a.Id, StringComparer.Ordinal)
            .ToList();

        return Ok(filtered);
    }
}
