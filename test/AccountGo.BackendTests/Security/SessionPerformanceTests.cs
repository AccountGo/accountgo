// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.IO;
using System.Linq;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Api.Data;
using Api.Middleware;
using Api.Observability;
using Api.Service;
using Api.Tenancy;
using Core.Domain;
using Core.Tenancy;
using Dto.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Services.Security;
using Xunit;
using Xunit.Abstractions;

namespace AccountGo.BackendTests.Security;

public sealed class SessionPerformanceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly ITestOutputHelper _output;

    public SessionPerformanceTests(ITestOutputHelper output)
    {
        _output = output;
        _dbPath = Path.Combine(Path.GetTempPath(), "accountgo_session_perf_" + Guid.NewGuid().ToString("N") + ".db");
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("DatabaseProvider", "Sqlite");
            builder.UseSetting("ConnectionStrings:SqliteConnection", "Data Source=" + _dbPath);
            builder.UseSetting("SECRET_KEY", "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef");
            builder.UseSetting(ServerTimingOptions.EnvKey, "true");
        });
    }

    public void Dispose()
    {
        _factory.Dispose();
        if (File.Exists(_dbPath))
        {
            try { File.Delete(_dbPath); } catch { }
        }
    }

    private async Task<(ApplicationUser User, Company? Company)> SeedUserAsync(
        string email,
        bool withWorkspace = true,
        bool isSysAdmin = false,
        bool pendingSignup = false,
        bool addDeletedCompany = false)
    {
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var apiDb = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        var securityService = scope.ServiceProvider.GetRequiredService<ISecurityService>();

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true
        };
        var createResult = await userManager.CreateAsync(user);
        Assert.True(createResult.Succeeded, string.Join(", ", createResult.Errors.Select(e => e.Description)));

        if (!await roleManager.RoleExistsAsync("Member"))
            await roleManager.CreateAsync(new IdentityRole("Member"));
        await userManager.AddToRoleAsync(user, "Member");

        securityService.EnsureDefaultPermissionsAndRoles();
        var roleName = isSysAdmin ? "SystemAdministrators" : "Accountant";
        var role = await apiDb.SecurityRoles.SingleAsync(r => r.Name == roleName);
        securityService.AddUser(email, email, "Pat", "Perf", new[] { role.Id });

        Company? company = null;
        if (withWorkspace)
        {
            company = new Company
            {
                Name = "Perf Co",
                Slug = "perf-" + Guid.NewGuid().ToString("N")[..8],
                CompanyCode = "PC" + Guid.NewGuid().ToString("N")[..4]
            };
            apiDb.Companies.Add(company);
            await apiDb.SaveChangesAsync();

            apiDb.CompanyMembers.Add(new CompanyMember
            {
                CompanyId = company.Id,
                UserId = user.Id,
                Role = CompanyMemberRole.Owner,
                Status = CompanyMemberStatus.Active
            });

            if (pendingSignup)
            {
                var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>() as TenantContext;
                tenantContext!.SetTenant(company.Id, company.Slug!, CompanyMemberRole.Owner, Array.Empty<string>(), user.Id);
                WorkspaceSignupContinuationEvidence.Record(apiDb, company.Id, user.Id);
            }
        }

        if (addDeletedCompany)
        {
            var deleted = new Company
            {
                Name = "Deleted Co",
                Slug = "deleted-" + Guid.NewGuid().ToString("N")[..8],
                CompanyCode = "DC" + Guid.NewGuid().ToString("N")[..4],
                DeletedAt = DateTime.UtcNow.AddDays(-1)
            };
            apiDb.Companies.Add(deleted);
            await apiDb.SaveChangesAsync();

            apiDb.CompanyMembers.Add(new CompanyMember
            {
                CompanyId = deleted.Id,
                UserId = user.Id,
                Role = CompanyMemberRole.Owner,
                Status = CompanyMemberStatus.Active
            });
        }

        await apiDb.SaveChangesAsync();
        return (user, company);
    }

    private SessionService CreateDirectSessionService(IServiceProvider sp) =>
        new SessionService(
            sp.GetRequiredService<UserManager<ApplicationUser>>(),
            sp.GetRequiredService<ApplicationIdentityDbContext>(),
            sp.GetRequiredService<IAuthenticationService>(),
            sp.GetRequiredService<ISecurityService>(),
            sp.GetRequiredService<IConfiguration>(),
            sp.GetRequiredService<ILogger<SessionService>>(),
            sp.GetRequiredService<ApiDbContext>(),
            sp.GetService<ISecurityAuditService>(),
            sp.GetRequiredService<IHttpContextAccessor>());

    private static int ParseServerTimingQueryCount(string header)
    {
        var match = Regex.Match(header, @"desc=""(\d+)\s+quer(?:y|ies)""");
        Assert.True(match.Success, $"Expected query count in Server-Timing header, got: {header}");
        return int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
    }

    [Fact]
    public async Task CreateSessionAsync_ReducesDbCommands_AndPopulatesOrganizationsWithMayNeedSetup()
    {
        var (regularUser, regularCompany) = await SeedUserAsync(
            $"perf-reg-{Guid.NewGuid():N}@example.com",
            withWorkspace: true,
            addDeletedCompany: true);
        var (pendingUser, pendingCompany) = await SeedUserAsync(
            $"perf-pend-{Guid.NewGuid():N}@example.com",
            withWorkspace: true,
            pendingSignup: true);
        var (noOrgUser, _) = await SeedUserAsync(
            $"perf-noorg-{Guid.NewGuid():N}@example.com",
            withWorkspace: false);
        var (sysAdminUser, _) = await SeedUserAsync(
            $"perf-admin-{Guid.NewGuid():N}@example.com",
            withWorkspace: true,
            isSysAdmin: true);

        async Task<(int Queries, AuthResponse Response)> MeasureAsync(ApplicationUser u)
        {
            using var scope = _factory.Services.CreateScope();
            var httpAccessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
            var ctx = new DefaultHttpContext();
            var acc = new ServerTimingAccumulator();
            ctx.Items[ServerTimingAccumulator.HttpContextItemsKey] = acc;
            httpAccessor.HttpContext = ctx;
            try
            {
                var sessionService = CreateDirectSessionService(scope.ServiceProvider);
                var result = await sessionService.CreateSessionAsync(
                    u,
                    "127.0.0.1",
                    "TestAgent",
                    mfaMethod: "webauthn",
                    mfaSatisfied: true);
                Assert.True(result.Success);
                Assert.NotNull(result.Response);
                return (acc.DbQueryCount, result.Response!);
            }
            finally
            {
                httpAccessor.HttpContext = null;
            }
        }

        var (regQueries, regResp) = await MeasureAsync(regularUser);
        var (pendingQueries, pendingResp) = await MeasureAsync(pendingUser);
        var (noOrgQueries, noOrgResp) = await MeasureAsync(noOrgUser);
        var (adminQueries, _) = await MeasureAsync(sysAdminUser);

        _output.WriteLine(
            $"CreateSessionAsync DB commands: regular={regQueries}, pending={pendingQueries}, noOrg={noOrgQueries}, sysadmin={adminQueries}");

        // Baseline on untouched main was: regular=6 (without Organizations/mayNeedSetup), sysadmin=7.
        Assert.True(regQueries <= 5, $"Expected regular CreateSessionAsync <= 5 DB commands, got {regQueries}");
        Assert.True(noOrgQueries <= 4, $"Expected no-workspace CreateSessionAsync <= 4 DB commands, got {noOrgQueries}");
        Assert.True(adminQueries <= 6, $"Expected sysadmin CreateSessionAsync <= 6 DB commands, got {adminQueries}");

        // Deleted company must be excluded; active company has MayNeedSetup=false.
        var regOrgs = regResp.Organizations.ToList();
        Assert.Single(regOrgs);
        Assert.Equal(regularCompany!.Slug, regOrgs[0].Slug);
        Assert.Equal("Owner", regOrgs[0].Role);
        Assert.False(regOrgs[0].MayNeedSetup);

        // Pending signup workspace carries MayNeedSetup=true.
        var pendingOrgs = pendingResp.Organizations.ToList();
        Assert.Single(pendingOrgs);
        Assert.Equal(pendingCompany!.Slug, pendingOrgs[0].Slug);
        Assert.True(pendingOrgs[0].MayNeedSetup);

        Assert.Empty(noOrgResp.Organizations);
    }

    [Fact]
    public async Task CreateSessionAsync_TokenClaimsAndUserDto_MatchStandardAuthLoad()
    {
        foreach (var isSysAdmin in new[] { false, true })
        {
            var (user, _) = await SeedUserAsync(
                $"perf-parity-{(isSysAdmin ? "admin" : "reg")}-{Guid.NewGuid():N}@example.com",
                isSysAdmin: isSysAdmin);

            using var scope = _factory.Services.CreateScope();
            var sessionService = CreateDirectSessionService(scope.ServiceProvider);
            var authService = scope.ServiceProvider.GetRequiredService<IAuthenticationService>();

            var sessionResult = await sessionService.CreateSessionAsync(
                user,
                "127.0.0.1",
                "TestAgent",
                mfaMethod: "totp",
                amr: new[] { "otp" },
                mfaSatisfied: true);
            Assert.True(sessionResult.Success);
            var sessionResponse = sessionResult.Response!;

            var directToken = await authService.CreateToken(
                user,
                purpose: "access",
                lifetime: null,
                amr: new[] { "otp", "mfa", "totp" },
                mfa: true,
                mfaTimestamp: DateTimeOffset.UtcNow);
            var directUserDto = await sessionService.BuildUserDtoAsync(user);

            var handler = new JwtSecurityTokenHandler();
            var sessionJwt = handler.ReadJwtToken(sessionResponse.AccessToken);
            var directJwt = handler.ReadJwtToken(directToken.AccessToken);

            var sessionRoles = sessionJwt.Claims
                .Where(c => c.Type == ClaimTypes.Role)
                .Select(c => c.Value)
                .OrderBy(v => v, StringComparer.Ordinal)
                .ToList();
            var directRoles = directJwt.Claims
                .Where(c => c.Type == ClaimTypes.Role)
                .Select(c => c.Value)
                .OrderBy(v => v, StringComparer.Ordinal)
                .ToList();

            Assert.Equal(directRoles, sessionRoles);
            Assert.Contains("Member", sessionRoles);
            Assert.Contains(isSysAdmin ? "SystemAdministrators" : "Accountant", sessionRoles);

            Assert.Equal(
                directJwt.Claims.First(c => c.Type == ClaimTypes.NameIdentifier).Value,
                sessionJwt.Claims.First(c => c.Type == ClaimTypes.NameIdentifier).Value);
            Assert.Equal(
                directJwt.Claims.First(c => c.Type == ClaimTypes.Email).Value,
                sessionJwt.Claims.First(c => c.Type == ClaimTypes.Email).Value);
            Assert.Equal(
                directJwt.Claims.First(c => c.Type == "purpose").Value,
                sessionJwt.Claims.First(c => c.Type == "purpose").Value);
            Assert.Equal(
                directJwt.Claims.First(c => c.Type == "mfa").Value,
                sessionJwt.Claims.First(c => c.Type == "mfa").Value);

            Assert.Equal(directUserDto.Id, sessionResponse.User.Id);
            Assert.Equal(directUserDto.Email, sessionResponse.User.Email);
            Assert.Equal(directUserDto.FirstName, sessionResponse.User.FirstName);
            Assert.Equal(directUserDto.LastName, sessionResponse.User.LastName);
            Assert.Equal(directUserDto.HasPassword, sessionResponse.User.HasPassword);
            Assert.Equal(directUserDto.MfaReEnrollRequired, sessionResponse.User.MfaReEnrollRequired);
            Assert.Equal(
                directUserDto.Roles.OrderBy(r => r, StringComparer.Ordinal),
                sessionResponse.User.Roles.OrderBy(r => r, StringComparer.Ordinal));
            Assert.Equal(
                (directUserDto.Permissions ?? Array.Empty<string>()).OrderBy(p => p, StringComparer.Ordinal),
                (sessionResponse.User.Permissions ?? Array.Empty<string>()).OrderBy(p => p, StringComparer.Ordinal));
        }
    }

    [Fact]
    public async Task VerifyOtp_ServerTiming_ReducesQueryCount_AndReturnsOrganizationsWithMayNeedSetup()
    {
        using var client = _factory.CreateClient();
        var otpEmail = $"perf-otp-{Guid.NewGuid():N}@example.com";
        var (_, company) = await SeedUserAsync(otpEmail, withWorkspace: true, pendingSignup: true, addDeletedCompany: true);

        var sendRes = await client.PostAsJsonAsync("/api/auth/send-otp", new { email = otpEmail });
        sendRes.EnsureSuccessStatusCode();
        var sendJson = await sendRes.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        var devCode = sendJson!["devCode"]!.ToString()!;

        var verifyRes = await client.PostAsJsonAsync("/api/auth/verify-otp", new { email = otpEmail, otp = devCode });
        verifyRes.EnsureSuccessStatusCode();

        Assert.True(verifyRes.Headers.TryGetValues(ServerTimingMiddleware.HeaderName, out var timingValues));
        var timingHeader = string.Join(", ", timingValues!);
        var verifyQueries = ParseServerTimingQueryCount(timingHeader);
        _output.WriteLine($"POST /api/auth/verify-otp Server-Timing: {timingHeader} (queries={verifyQueries})");

        // Baseline on untouched main was 14 queries.
        Assert.True(verifyQueries <= 11, $"Expected verify-otp <= 11 queries, got {verifyQueries} ({timingHeader})");

        var auth = await verifyRes.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(auth);
        var orgs = auth!.Organizations.ToList();
        Assert.Single(orgs);
        Assert.Equal(company!.Slug, orgs[0].Slug);
        Assert.True(orgs[0].MayNeedSetup);
    }
}
