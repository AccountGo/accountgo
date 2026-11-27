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
using System.Text.Json;
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
        bool initializedSignup = false,
        bool addDeletedCompany = false,
        string[]? extraIdentityRoles = null)
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

        var identityRolesToAdd = new List<string> { "Member" };
        if (extraIdentityRoles != null)
            identityRolesToAdd.AddRange(extraIdentityRoles);

        foreach (var roleNameToEnsure in identityRolesToAdd.Distinct(StringComparer.Ordinal))
        {
            if (!await roleManager.RoleExistsAsync(roleNameToEnsure))
                await roleManager.CreateAsync(new IdentityRole(roleNameToEnsure));
            await userManager.AddToRoleAsync(user, roleNameToEnsure);
        }

        securityService.EnsureDefaultPermissionsAndRoles();
        var domainRoleName = isSysAdmin ? "SystemAdministrators" : "Accountant";
        var role = await apiDb.SecurityRoles.SingleAsync(r => r.Name == domainRoleName);
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

            if (pendingSignup || initializedSignup)
            {
                var tenantContext = (TenantContext)scope.ServiceProvider.GetRequiredService<ITenantContext>();
                tenantContext.SetTenant(company.Id, company.Slug!, CompanyMemberRole.Owner, Array.Empty<string>(), user.Id);
                WorkspaceSignupContinuationEvidence.Record(apiDb, company.Id, user.Id);
                apiDb.WorkspaceOperatingSettings.Add(new WorkspaceOperatingSettings
                {
                    CompanyId = company.Id,
                    CollaborationState = initializedSignup
                        ? WorkspaceCollaborationState.SoloActive
                        : WorkspaceCollaborationState.LegacyUnclassified,
                    InitializedAtUtc = initializedSignup ? DateTime.UtcNow.AddMinutes(-5) : null,
                    PolicySchemaVersion = WorkspaceOperatingPolicySchema.CurrentVersion
                });
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
    public void OrganizationDto_WithoutMayNeedSetup_SerializesAsNull()
    {
        var dto = new OrganizationDto { Slug = "acme", Name = "Acme", Role = "Owner" };
        Assert.Null(dto.MayNeedSetup);

        var json = JsonSerializer.Serialize(dto, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        using var doc = JsonDocument.Parse(json);
        Assert.True(
            !doc.RootElement.TryGetProperty("mayNeedSetup", out var prop) || prop.ValueKind == JsonValueKind.Null,
            $"Expected mayNeedSetup to be null or absent when omitted, got: {json}");
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
        var (initializedUser, initializedCompany) = await SeedUserAsync(
            $"perf-init-{Guid.NewGuid():N}@example.com",
            withWorkspace: true,
            initializedSignup: true);
        var (nonOwnerMember, _) = await SeedUserAsync(
            $"perf-nonowner-{Guid.NewGuid():N}@example.com",
            withWorkspace: false);
        var (noOrgUser, _) = await SeedUserAsync(
            $"perf-noorg-{Guid.NewGuid():N}@example.com",
            withWorkspace: false);
        var (sysAdminUser, _) = await SeedUserAsync(
            $"perf-admin-{Guid.NewGuid():N}@example.com",
            withWorkspace: true,
            isSysAdmin: true);

        // Add nonOwnerMember as an active non-owner on the pending uninitialized signup workspace.
        using (var scope = _factory.Services.CreateScope())
        {
            var apiDb = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            apiDb.CompanyMembers.Add(new CompanyMember
            {
                CompanyId = pendingCompany!.Id,
                UserId = nonOwnerMember.Id,
                Role = CompanyMemberRole.Member,
                Status = CompanyMemberStatus.Active
            });
            await apiDb.SaveChangesAsync();
        }

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
        var (initializedQueries, initializedResp) = await MeasureAsync(initializedUser);
        var (_, nonOwnerResp) = await MeasureAsync(nonOwnerMember);
        var (noOrgQueries, noOrgResp) = await MeasureAsync(noOrgUser);
        var (adminQueries, _) = await MeasureAsync(sysAdminUser);

        _output.WriteLine(
            $"CreateSessionAsync DB commands: regular={regQueries}, pending={pendingQueries}, initialized={initializedQueries}, noOrg={noOrgQueries}, sysadmin={adminQueries}");

        // Baseline on untouched main was: regular=6 (without Organizations/mayNeedSetup), sysadmin=7.
        Assert.True(regQueries <= 5, $"Expected regular CreateSessionAsync <= 5 DB commands, got {regQueries}");
        Assert.True(noOrgQueries <= 4, $"Expected no-workspace CreateSessionAsync <= 4 DB commands, got {noOrgQueries}");
        Assert.True(adminQueries <= 6, $"Expected sysadmin CreateSessionAsync <= 6 DB commands, got {adminQueries}");

        // Deleted company must be excluded; active company has MayNeedSetup=false.
        var regOrgs = regResp.Organizations.ToList();
        Assert.Single(regOrgs);
        Assert.Equal(regularCompany!.Slug, regOrgs[0].Slug);
        Assert.Equal("Owner", regOrgs[0].Role);
        Assert.Equal(false, regOrgs[0].MayNeedSetup);

        // Pending + uninitialized signup workspace carries MayNeedSetup=true for its original owner.
        var pendingOrgs = pendingResp.Organizations.ToList();
        Assert.Single(pendingOrgs);
        Assert.Equal(pendingCompany!.Slug, pendingOrgs[0].Slug);
        Assert.Equal(true, pendingOrgs[0].MayNeedSetup);

        // Initialized signup workspace carries MayNeedSetup=false even though the immutable marker exists.
        var initOrgs = initializedResp.Organizations.ToList();
        Assert.Single(initOrgs);
        Assert.Equal(initializedCompany!.Slug, initOrgs[0].Slug);
        Assert.Equal(false, initOrgs[0].MayNeedSetup);

        // Non-owner active member of a pending uninitialized workspace gets MayNeedSetup=false.
        var nonOwnerOrgs = nonOwnerResp.Organizations.ToList();
        Assert.Single(nonOwnerOrgs);
        Assert.Equal(pendingCompany.Slug, nonOwnerOrgs[0].Slug);
        Assert.Equal(false, nonOwnerOrgs[0].MayNeedSetup);

        Assert.Empty(noOrgResp.Organizations);
    }

    [Fact]
    public async Task CreateSessionAsync_TokenClaimsAndUserDto_MatchIndependentDbExpectations()
    {
        var cases = new[]
        {
            // Regular user whose Identity role ("Accountant") overlaps with Domain role ("Accountant") to cover dedupe.
            (Label: "regular-overlap", IsSysAdmin: false, ExtraIdentityRoles: new[] { "Accountant" }),
            (Label: "sysadmin", IsSysAdmin: true, ExtraIdentityRoles: Array.Empty<string>())
        };

        var dynamicClaimTypes = new HashSet<string>(StringComparer.Ordinal)
        {
            JwtRegisteredClaimNames.Jti,
            JwtRegisteredClaimNames.Iat,
            JwtRegisteredClaimNames.Nbf,
            JwtRegisteredClaimNames.Exp,
            JwtRegisteredClaimNames.AuthTime,
            "mfa_ts"
        };

        foreach (var tc in cases)
        {
            var (user, _) = await SeedUserAsync(
                $"perf-parity-{tc.Label}-{Guid.NewGuid():N}@example.com",
                isSysAdmin: tc.IsSysAdmin,
                extraIdentityRoles: tc.ExtraIdentityRoles);

            var beforeUtc = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            using var scope = _factory.Services.CreateScope();
            var sessionService = CreateDirectSessionService(scope.ServiceProvider);
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var apiDb = scope.ServiceProvider.GetRequiredService<ApiDbContext>();

            var sessionResult = await sessionService.CreateSessionAsync(
                user,
                "127.0.0.1",
                "TestAgent",
                mfaMethod: "totp",
                amr: new[] { "otp" },
                mfaSatisfied: true);
            var afterUtc = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            Assert.True(sessionResult.Success);
            var response = sessionResult.Response!;

            // Compute expected roles, profile, and permissions independently from Identity + ApiDbContext.
            var expectedIdentityRoles = await userManager.GetRolesAsync(user);
            var dbUser = await apiDb.Users
                .AsNoTracking()
                .Include(u => u.Roles)
                .ThenInclude(ur => ur.SecurityRole)
                .ThenInclude(r => r.Permissions)
                .ThenInclude(rp => rp.SecurityPermission)
                .SingleAsync(u => u.UserName == user.UserName);

            var expectedRoles = expectedIdentityRoles
                .Concat(dbUser.Roles.Select(r => r.SecurityRole!.Name!))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(r => r, StringComparer.Ordinal)
                .ToList();

            var expectedPermissions = tc.IsSysAdmin
                ? (await apiDb.SecurityPermissions
                    .AsNoTracking()
                    .Where(p => p.Name != null)
                    .Select(p => p.Name!)
                    .ToListAsync())
                    .OrderBy(p => p, StringComparer.Ordinal)
                    .ToList()
                : dbUser.Roles
                    .SelectMany(r => r.SecurityRole!.Permissions)
                    .Select(rp => rp.SecurityPermission!.Name!)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(p => p, StringComparer.Ordinal)
                    .ToList();

            // Verify full JWT claim set (all non-dynamic claims) + valid mfa_ts.
            var jwt = new JwtSecurityTokenHandler().ReadJwtToken(response.AccessToken);
            var mfaTsClaim = jwt.Claims.Single(c => c.Type == "mfa_ts");
            var mfaTs = long.Parse(mfaTsClaim.Value, CultureInfo.InvariantCulture);
            Assert.InRange(mfaTs, beforeUtc, afterUtc);

            var actualClaims = jwt.Claims
                .Where(c => !dynamicClaimTypes.Contains(c.Type))
                .Select(c => (c.Type, c.Value))
                .OrderBy(c => c.Type, StringComparer.Ordinal)
                .ThenBy(c => c.Value, StringComparer.Ordinal)
                .ToList();

            var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
            var expectedIssuer = config["JwtSettings:validIssuer"];
            var expectedAudience = config["JwtSettings:validAudience"];

            var expectedClaims = new List<(string Type, string Value)>
            {
                (ClaimTypes.NameIdentifier, user.Id),
                (ClaimTypes.Name, user.UserName!),
                (ClaimTypes.Email, user.Email!),
                ("purpose", "access"),
                ("amr", "otp mfa totp"),
                ("mfa", "true")
            };
            if (!string.IsNullOrEmpty(expectedIssuer))
                expectedClaims.Add((JwtRegisteredClaimNames.Iss, expectedIssuer));
            if (!string.IsNullOrEmpty(expectedAudience))
                expectedClaims.Add((JwtRegisteredClaimNames.Aud, expectedAudience));
            expectedClaims.AddRange(expectedRoles.Select(r => (ClaimTypes.Role, r)));
            expectedClaims = expectedClaims
                .OrderBy(c => c.Type, StringComparer.Ordinal)
                .ThenBy(c => c.Value, StringComparer.Ordinal)
                .ToList();

            Assert.Equal(expectedClaims, actualClaims);

            // Verify no duplicate role claims when an Identity role and Domain role share a name.
            var actualRoleClaims = jwt.Claims.Where(c => c.Type == ClaimTypes.Role).Select(c => c.Value).ToList();
            Assert.Equal(actualRoleClaims.Distinct(StringComparer.Ordinal).Count(), actualRoleClaims.Count);

            // Verify UserDto fields against independent DB expectations.
            Assert.Equal(user.Id, response.User.Id);
            Assert.Equal(user.Email, response.User.Email);
            Assert.Equal(dbUser.Firstname, response.User.FirstName);
            Assert.Equal(dbUser.Lastname, response.User.LastName);
            Assert.Equal(!string.IsNullOrEmpty(user.PasswordHash), response.User.HasPassword);
            Assert.Equal(user.MfaReEnrollRequired, response.User.MfaReEnrollRequired);
            Assert.Equal(expectedRoles, response.User.Roles.OrderBy(r => r, StringComparer.Ordinal).ToList());
            Assert.Equal(
                expectedPermissions,
                (response.User.Permissions ?? Array.Empty<string>()).OrderBy(p => p, StringComparer.Ordinal).ToList());
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
        Assert.Equal(true, orgs[0].MayNeedSetup);
    }
}
