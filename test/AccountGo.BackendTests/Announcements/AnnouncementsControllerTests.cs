// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using Api.Announcements;
using Api.Controllers;
using Core.Domain;
using Core.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace AccountGo.BackendTests.Announcements;

public sealed class AnnouncementsControllerTests
{
    [Fact]
    public void Get_NoSources_ReturnsEmptyList()
    {
        var result = Assert.IsType<OkObjectResult>(Build(CompanyMemberRole.Owner).Get());
        Assert.Empty(Assert.IsAssignableFrom<IEnumerable<AnnouncementDto>>(result.Value!));
    }

    [Fact]
    public void Get_DropsExpired_AndOwnerAdminForMembers_OrdersBySeverity()
    {
        var source = new FakeSource(
            new AnnouncementDto("info-1", "info", "Info", null, true, "all", null),
            new AnnouncementDto("warn-1", "warning", "Warn", null, false, "all", null),
            new AnnouncementDto("crit-1", "critical", "Crit", null, false, "all", null),
            new AnnouncementDto("expired", "critical", "Gone", null, false, "all", DateTimeOffset.UtcNow.AddHours(-1)),
            new AnnouncementDto("owners", "warning", "Owners only", null, false, "owner_admin", null));

        var owner = Assert.IsType<OkObjectResult>(Build(CompanyMemberRole.Owner, source).Get());
        var ownerIds = Assert.IsAssignableFrom<IEnumerable<AnnouncementDto>>(owner.Value!).Select(a => a.Id).ToList();
        Assert.Equal(new[] { "crit-1", "owners", "warn-1", "info-1" }, ownerIds);

        var member = Assert.IsType<OkObjectResult>(Build(CompanyMemberRole.Member, source).Get());
        var memberIds = Assert.IsAssignableFrom<IEnumerable<AnnouncementDto>>(member.Value!).Select(a => a.Id).ToList();
        Assert.Equal(new[] { "crit-1", "warn-1", "info-1" }, memberIds);
        Assert.DoesNotContain("owners", memberIds);
        Assert.DoesNotContain("expired", memberIds);
    }

    [Fact]
    public void Get_ThrowingSource_IsIsolated()
    {
        var good = new FakeSource(new AnnouncementDto("ok", "info", "Still here", null, true, "all", null));
        var bad = new ThrowingSource();
        var result = Assert.IsType<OkObjectResult>(Build(CompanyMemberRole.Owner, bad, good).Get());
        var items = Assert.IsAssignableFrom<IEnumerable<AnnouncementDto>>(result.Value!).ToList();
        Assert.Single(items);
        Assert.Equal("ok", items[0].Id);
    }

    [Fact]
    public void Controller_IsTenantScopedOnly()
    {
        var type = typeof(AnnouncementsController);
        Assert.NotNull(Attribute.GetCustomAttribute(type, typeof(TenantScopedAttribute)));
        Assert.Null(Attribute.GetCustomAttribute(type, typeof(TenantFreeAttribute)));
    }

    private static AnnouncementsController Build(CompanyMemberRole role, params IAnnouncementSource[] sources)
    {
        var tenant = new Mock<ITenantContext>();
        tenant.SetupGet(t => t.CompanyId).Returns(42);
        tenant.Setup(t => t.RequireCompanyId()).Returns(42);
        tenant.SetupGet(t => t.Role).Returns(role);

        var controller = new AnnouncementsController(sources, tenant.Object, NullLogger<AnnouncementsController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                    {
                        new Claim(ClaimTypes.NameIdentifier, "user-1")
                    }, "test"))
                }
            }
        };
        return controller;
    }

    private sealed class FakeSource : IAnnouncementSource
    {
        private readonly AnnouncementDto[] _items;
        public FakeSource(params AnnouncementDto[] items) => _items = items;
        public IReadOnlyList<AnnouncementDto> GetAnnouncements(AnnouncementContext ctx) => _items;
    }

    private sealed class ThrowingSource : IAnnouncementSource
    {
        public IReadOnlyList<AnnouncementDto> GetAnnouncements(AnnouncementContext ctx) =>
            throw new InvalidOperationException("boom");
    }
}
