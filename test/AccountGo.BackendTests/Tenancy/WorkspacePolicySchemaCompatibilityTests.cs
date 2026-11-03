// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Api.Data;
using Api.Service;
using Core.Authorization;
using Core.Domain;
using Core.Domain.Security;
using Core.Tenancy;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AccountGo.BackendTests.Tenancy
{
    public class WorkspacePolicySchemaCompatibilityTests : IAsyncLifetime
    {
        private readonly string _dbPath;
        private WebApplicationFactory<Program> _factory = null!;
        private HttpClient _client = null!;
        private string _userToken = string.Empty;
        private int _companyId;
        private const string CompanySlug = "schema-test-co";

        public WorkspacePolicySchemaCompatibilityTests()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"accountgo_schema_{Guid.NewGuid():N}.db");
        }

        public async Task InitializeAsync()
        {
            _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.UseSetting("DatabaseProvider", "Sqlite");
                builder.UseSetting("ConnectionStrings:SqliteConnection", $"Data Source={_dbPath}");
                builder.UseSetting("ENVIRONMENT", "Development");
                builder.UseSetting("SECRET_KEY", "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef");
            });

            _client = _factory.CreateClient();

            var health = await _client.GetAsync("/health");
            Assert.True(health.IsSuccessStatusCode, "Failed to boot app for schema compatibility tests.");

            await SeedCompanyAndUserAsync();
        }

        public Task DisposeAsync()
        {
            _client.Dispose();
            _factory.Dispose();
            if (File.Exists(_dbPath))
            {
                try { File.Delete(_dbPath); } catch { }
            }
            return Task.CompletedTask;
        }

        private async Task SeedCompanyAndUserAsync()
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            var userMgr = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var authService = scope.ServiceProvider.GetRequiredService<IAuthenticationService>();

            db.SuppressTenantSecurityCheck = true;

            var user = new ApplicationUser
            {
                UserName = "owner@schematest.dev",
                Email = "owner@schematest.dev",
                EmailConfirmed = true
            };
            var createResult = await userMgr.CreateAsync(user, "Password123!");
            Assert.True(createResult.Succeeded);

            var company = new Company
            {
                Name = "Schema Test Co",
                Slug = CompanySlug,
                CreatedByUserId = user.Id
            };
            db.Companies.Add(company);
            await db.SaveChangesAsync();
            _companyId = company.Id;

            var member = new CompanyMember
            {
                CompanyId = company.Id,
                UserId = user.Id,
                Role = CompanyMemberRole.Owner,
                Status = CompanyMemberStatus.Active,
                JoinedAt = DateTime.UtcNow
            };
            db.CompanyMembers.Add(member);
            await db.SaveChangesAsync();

            var functionalRoleIds = await db.SecurityRoles
                .Where(r => !r.SysAdmin && r.Name != "SystemAdministrators")
                .Select(r => r.Id)
                .ToListAsync();
            foreach (var roleId in functionalRoleIds)
            {
                db.CompanyMemberSecurityRoles.Add(new CompanyMemberSecurityRole
                {
                    CompanyMemberId = member.Id,
                    SecurityRoleId = roleId
                });
            }

            var settings = new WorkspaceOperatingSettings
            {
                CompanyId = company.Id,
                Purpose = WorkspacePurpose.Business,
                CollaborationIntent = WorkspaceCollaborationIntent.JustMe,
                CollaborationState = WorkspaceCollaborationState.SoloActive,
                PolicyRevision = 1,
                PolicySchemaVersion = WorkspaceOperatingPolicySchema.CurrentVersion
            };
            db.WorkspaceOperatingSettings.Add(settings);
            await db.SaveChangesAsync();

            var token = await authService.CreateToken(user, true);
            Assert.NotNull(token);
            _userToken = token.AccessToken;
        }

        [Fact]
        public async Task TenantRequest_WhenSchemaVersionCurrent_Succeeds()
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/api/Sales/Customers");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _userToken);
            request.Headers.Add("X-AccountGo-Org", CompanySlug);

            var response = await _client.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task TenantRequest_WhenSchemaVersionExceedsCurrent_Returns503IncompatiblePolicySchema()
        {
            // Simulate a future migration or writer updating the schema version beyond this binary's supported version
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
                db.SuppressTenantSecurityCheck = true;
                var settings = await db.WorkspaceOperatingSettings.IgnoreQueryFilters().SingleAsync(s => s.CompanyId == _companyId);
                settings.PolicySchemaVersion = WorkspaceOperatingPolicySchema.CurrentVersion + 1;
                await db.SaveChangesAsync();
            }

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, "/api/Sales/Customers");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _userToken);
                request.Headers.Add("X-AccountGo-Org", CompanySlug);

                var response = await _client.SendAsync(request);
                Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);

                var content = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(content);
                var root = doc.RootElement;

                Assert.Equal(503, root.GetProperty("status").GetInt32());
                Assert.Equal("Incompatible Policy Schema", root.GetProperty("title").GetString());
                Assert.Equal(WorkspacePolicyReasonCodes.IncompatiblePolicySchema, root.GetProperty("reasonCode").GetString());
            }
            finally
            {
                // Revert for subsequent test runs if any
                using var scope = _factory.Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
                db.SuppressTenantSecurityCheck = true;
                var settings = await db.WorkspaceOperatingSettings.IgnoreQueryFilters().SingleAsync(s => s.CompanyId == _companyId);
                settings.PolicySchemaVersion = WorkspaceOperatingPolicySchema.CurrentVersion;
                await db.SaveChangesAsync();
            }
        }
    }
}
