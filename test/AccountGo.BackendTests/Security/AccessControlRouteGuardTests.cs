#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Api.Data;
using Core.Domain;
using Core.Domain.TaxSystem;
using Core.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AccountGo.BackendTests.Security
{
    /// <summary>
    /// Route-level allow/deny matrix for the legacy read paths (F2), the donor, donation and tax
    /// mutations (F3) and the retired audit-definition API (F4). Each path is checked with a
    /// workspace Owner holding no functional roles, a member holding an unrelated functional role,
    /// and a member holding the least role that should succeed.
    /// </summary>
    public sealed class AccessControlRouteGuardTests : IClassFixture<AccessControlConformanceTests.Fixture>
    {
        private const string Accountant = "Accountant";
        private const string SalesRep = "Sales Representative";
        private const string PurchasingAgent = "Purchasing Agent";
        private const string Auditor = "Auditor / Viewer";

        private readonly AccessControlConformanceTests.Fixture _fixture;

        public AccessControlRouteGuardTests(AccessControlConformanceTests.Fixture fixture)
        {
            _fixture = fixture;
        }

        public static IEnumerable<object[]> ReadPaths() => new[]
        {
            // path, least permitted role, unrelated role
            new object[] { "/api/Sales/Customers", SalesRep, PurchasingAgent },
            new object[] { "/api/Sales/Customer?id=999999", SalesRep, PurchasingAgent },
            new object[] { "/api/Sales/Quotations", SalesRep, Auditor },
            new object[] { "/api/Sales/Quotation?id=999999", SalesRep, Auditor },
            new object[] { "/api/Sales/SalesReceipts", Accountant, SalesRep },
            new object[] { "/api/Sales/SalesReceipt?id=999999", Accountant, SalesRep },
            new object[] { "/api/Financials/CashBanks", Auditor, SalesRep },
            new object[] { "/api/Financials/Bank/999999", Auditor, SalesRep },
            new object[] { "/api/Financials/ExportAccounts?mode=snapshot", Auditor, SalesRep },
            new object[] { "/api/Financials/ExportAccounts?mode=template", Auditor, SalesRep },
            new object[] { "/api/Financials/ExportAccounts?mode=structure", Accountant, Auditor },
            new object[] { "/api/Administration/AuditLogs", Auditor, Accountant },
            new object[] { "/api/Dashboard/Summary", Auditor, null! },
        };

        [Theory]
        [MemberData(nameof(ReadPaths))]
        public async Task F2_ReadPath_DeniesNoRoleAndUnrelatedRole_AllowsLeastPermittedRole(
            string path, string permittedRole, string? unrelatedRole)
        {
            var ws = await CreateWorkspace("f2");
            var permitted = await AddMember(ws, permittedRole);

            await AssertDenied(HttpMethod.Get, path, null, ws.Owner.Token, ws.Slug);
            if (unrelatedRole != null)
            {
                var unrelated = await AddMember(ws, unrelatedRole);
                await AssertDenied(HttpMethod.Get, path, null, unrelated.Token, ws.Slug);
            }

            var allowed = await _fixture.Send(HttpMethod.Get, path, token: permitted.Token, org: ws.Slug);
            Assert.True(
                allowed.StatusCode is HttpStatusCode.OK or HttpStatusCode.NotFound,
                $"{permittedRole} on {path}: expected 200 or 404, got {(int)allowed.StatusCode}");
        }

        [Fact]
        public async Task F3_DonorAndDonationMutations_DenyBeforeSideEffects_AndAllowManager()
        {
            var ws = await CreateWorkspace("f3d");
            var manager = await AddMember(ws, Accountant);
            var unrelated = await AddMember(ws, SalesRep);

            var created = await _fixture.Send(HttpMethod.Post, "/api/Donors", Donor("Original"), manager.Token, ws.Slug);
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            var donorId = (await AccessControlConformanceTests.Fixture.Json(created)).GetProperty("id").GetInt32();
            var before = DonorSnapshot(ws.CompanyId);

            foreach (var actor in new[] { ws.Owner, unrelated })
            {
                await AssertDenied(HttpMethod.Get, "/api/Donors", null, actor.Token, ws.Slug);
                await AssertDenied(HttpMethod.Get, $"/api/Donors/{donorId}", null, actor.Token, ws.Slug);
                await AssertDenied(HttpMethod.Post, "/api/Donors", Donor("Injected"), actor.Token, ws.Slug);
                await AssertDenied(HttpMethod.Put, $"/api/Donors/{donorId}", Donor("Changed"), actor.Token, ws.Slug);
                await AssertDenied(HttpMethod.Delete, $"/api/Donors/{donorId}", null, actor.Token, ws.Slug);
                await AssertDenied(HttpMethod.Get, "/api/Donations/DonationInvoices", null, actor.Token, ws.Slug);
                await AssertDenied(HttpMethod.Get, "/api/Donations/DonationAccounts", null, actor.Token, ws.Slug);
                await AssertDenied(HttpMethod.Post, "/api/Donations/CreateDonationInvoice", new { donorId, glAccountId = 1 }, actor.Token, ws.Slug);
                await AssertDenied(HttpMethod.Post, "/api/Donations/UpdateDonationInvoice", new { id = 1, donorId, glAccountId = 1 }, actor.Token, ws.Slug);
                await AssertDenied(HttpMethod.Delete, "/api/Donations/DeleteDonationInvoice?id=1", null, actor.Token, ws.Slug);
            }

            Assert.Equal(before, DonorSnapshot(ws.CompanyId));

            var updated = await _fixture.Send(HttpMethod.Put, $"/api/Donors/{donorId}", Donor("Renamed"), manager.Token, ws.Slug);
            Assert.Equal(HttpStatusCode.NoContent, updated.StatusCode);
            Assert.Contains("Renamed", DonorSnapshot(ws.CompanyId));
        }

        [Fact]
        public async Task F3_DonationsModuleOff_DeniesEvenAFullyPermittedMember_BeforeSideEffects()
        {
            var ws = await CreateWorkspace("f3m");
            var manager = await AddMember(ws, Accountant);
            Assert.Equal(HttpStatusCode.OK, (await _fixture.Send(HttpMethod.Get, "/api/Donors", token: manager.Token, org: ws.Slug)).StatusCode);

            SetDisabledModules(ws.CompanyId, "[\"donations\"]");
            try
            {
                var before = DonorSnapshot(ws.CompanyId);
                await AssertDenied(HttpMethod.Get, "/api/Donors", null, manager.Token, ws.Slug);
                await AssertDenied(HttpMethod.Post, "/api/Donors", Donor("Blocked"), manager.Token, ws.Slug);
                await AssertDenied(HttpMethod.Get, "/api/Donations/DonationInvoices", null, manager.Token, ws.Slug);
                await AssertDenied(HttpMethod.Delete, "/api/Donations/DeleteDonationInvoice?id=1", null, manager.Token, ws.Slug);
                Assert.Equal(before, DonorSnapshot(ws.CompanyId));
            }
            finally
            {
                SetDisabledModules(ws.CompanyId, "[]");
            }
        }

        [Fact]
        public async Task F3_DonorFromAnotherWorkspace_CannotBeChangedOrDeleted()
        {
            var home = await CreateWorkspace("f3h");
            var homeManager = await AddMember(home, Accountant);
            var created = await _fixture.Send(HttpMethod.Post, "/api/Donors", Donor("Home"), homeManager.Token, home.Slug);
            var donorId = (await AccessControlConformanceTests.Fixture.Json(created)).GetProperty("id").GetInt32();
            var before = DonorSnapshot(home.CompanyId);

            var foreign = await CreateWorkspace("f3x");
            var foreignManager = await AddMember(foreign, Accountant);
            Assert.Equal(HttpStatusCode.NotFound, (await _fixture.Send(HttpMethod.Put, $"/api/Donors/{donorId}", Donor("Hijacked"), foreignManager.Token, foreign.Slug)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await _fixture.Send(HttpMethod.Delete, $"/api/Donors/{donorId}", token: foreignManager.Token, org: foreign.Slug)).StatusCode);

            Assert.Equal(before, DonorSnapshot(home.CompanyId));
        }

        [Fact]
        public async Task F3_LegacyTaxGroupDeletes_RequireTaxManagement_AndStayInsideTheWorkspace()
        {
            var ws = await CreateWorkspace("f3t");
            var manager = await AddMember(ws, Accountant);
            var unrelated = await AddMember(ws, Auditor);
            var (taxGroupId, itemTaxGroupId) = SeedTaxGroups(ws.CompanyId);

            foreach (var actor in new[] { ws.Owner, unrelated })
            {
                await AssertDenied(HttpMethod.Delete, $"/api/Tax/deletetaxgroup?id={taxGroupId}", null, actor.Token, ws.Slug);
                await AssertDenied(HttpMethod.Delete, $"/api/Tax/deleteitemtaxgroup?id={itemTaxGroupId}", null, actor.Token, ws.Slug);
            }
            Assert.Equal((true, true), TaxGroupsExist(ws.CompanyId, taxGroupId, itemTaxGroupId));

            var foreign = await CreateWorkspace("f3u");
            var foreignManager = await AddMember(foreign, Accountant);
            Assert.NotEqual(HttpStatusCode.NoContent, (await _fixture.Send(HttpMethod.Delete, $"/api/Tax/deletetaxgroup?id={taxGroupId}", token: foreignManager.Token, org: foreign.Slug)).StatusCode);
            Assert.NotEqual(HttpStatusCode.NoContent, (await _fixture.Send(HttpMethod.Delete, $"/api/Tax/deleteitemtaxgroup?id={itemTaxGroupId}", token: foreignManager.Token, org: foreign.Slug)).StatusCode);
            Assert.Equal((true, true), TaxGroupsExist(ws.CompanyId, taxGroupId, itemTaxGroupId));

            Assert.Equal(HttpStatusCode.NoContent, (await _fixture.Send(HttpMethod.Delete, $"/api/Tax/deletetaxgroup?id={taxGroupId}", token: manager.Token, org: ws.Slug)).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await _fixture.Send(HttpMethod.Delete, $"/api/Tax/deleteitemtaxgroup?id={itemTaxGroupId}", token: manager.Token, org: ws.Slug)).StatusCode);
            Assert.Equal((false, false), TaxGroupsExist(ws.CompanyId, taxGroupId, itemTaxGroupId));
        }

        [Fact]
        public async Task F4_AuditDefinitionApi_IsRetired_ForWorkspaceMembersAndPlatformAdministrators()
        {
            var ws = await CreateWorkspace("f4");
            var auditor = await AddMember(ws, Auditor);
            var platformAdmin = await _fixture.CreateUser($"f4-sysadmin-{ws.Slug}@example.com", isSystemAdministrator: true);

            foreach (var token in new[] { ws.Owner.Token, auditor.Token, platformAdmin.Token })
            {
                foreach (var (method, path, body) in new (HttpMethod, string, object?)[]
                {
                    (HttpMethod.Get, "/api/Audit/Entities", null),
                    (HttpMethod.Post, "/api/Audit/Entity", new { id = 0, entityName = "Company", enableAudit = false }),
                    (HttpMethod.Delete, "/api/Audit/Entity/1", null),
                    (HttpMethod.Post, "/api/Audit/Attribute", new { id = 0, auditableEntityId = 1, attributeName = "Name", enableAudit = false }),
                    (HttpMethod.Delete, "/api/Audit/Attribute/1", null),
                })
                {
                    var res = await _fixture.Send(method, path, body, token, ws.Slug);
                    Assert.True(
                        res.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed,
                        $"{method} {path}: expected the route to be gone, got {(int)res.StatusCode}");
                }
            }

            Assert.Null(Type.GetType("Api.Controllers.AuditController, Api"));
        }

        // ---------------------------------------------------------------------

        private sealed record Workspace(int CompanyId, string Slug, AccessControlConformanceTests.Fixture.UserSession Owner);

        private async Task<Workspace> CreateWorkspace(string prefix)
        {
            var slug = $"{prefix}-{Guid.NewGuid():N}"[..(prefix.Length + 9)];
            var owner = await _fixture.CreateUser($"owner-{slug}@example.com");
            var companyId = await _fixture.CreateWorkspaceWithZeroFunctionalRoles(owner.Token, "Route Guards " + slug, slug);
            SetDisabledModules(companyId, "[]");
            return new Workspace(companyId, slug, owner);
        }

        private async Task<AccessControlConformanceTests.Fixture.UserSession> AddMember(Workspace ws, string role)
        {
            var user = await _fixture.CreateUser($"{Guid.NewGuid():N}@example.com");
            await _fixture.AddMemberDirectAsync(ws.CompanyId, user.Id, CompanyMemberRole.Member, new[] { role });
            return user;
        }

        private async Task AssertDenied(HttpMethod method, string path, object? body, string token, string slug)
        {
            var res = await _fixture.Send(method, path, body, token, slug);
            Assert.True(
                res.StatusCode == HttpStatusCode.Forbidden,
                $"{method} {path}: expected 403, got {(int)res.StatusCode}");
            var text = await res.Content.ReadAsStringAsync();
            Assert.DoesNotContain("\"accountCode\"", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("@donor.example", text, StringComparison.OrdinalIgnoreCase);
        }

        private static object Donor(string lastName) => new
        {
            donorType = "Individual",
            firstName = "Pat",
            lastName,
            street = "1 Main St",
            province = "ON",
            postalCode = "K1A 0B1",
            telephone = "613-555-0101",
            email = "pat@donor.example"
        };

        private T WithTenantDb<T>(int companyId, Func<ApiDbContext, T> work)
        {
            using var scope = _fixture.Factory.Services.CreateScope();
            var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
            using var impersonation = tenant.Impersonate(companyId, role: CompanyMemberRole.Owner);
            return work(scope.ServiceProvider.GetRequiredService<ApiDbContext>());
        }

        private string DonorSnapshot(int companyId) => WithTenantDb(companyId, db =>
            string.Join("|", db.Donors.AsNoTracking().OrderBy(d => d.Id)
                .Select(d => d.Id + ":" + d.FirstName + " " + d.LastName)));

        private void SetDisabledModules(int companyId, string json) => WithTenantDb(companyId, db =>
        {
            var company = db.Companies.Single(c => c.Id == companyId);
            company.DisabledModulesJson = json;
            return db.SaveChanges();
        });

        private (int TaxGroupId, int ItemTaxGroupId) SeedTaxGroups(int companyId) => WithTenantDb(companyId, db =>
        {
            var group = new TaxGroup { CompanyId = companyId, Description = "Guard test group", IsActive = true };
            var itemGroup = new ItemTaxGroup { CompanyId = companyId, Name = "Guard test item group" };
            db.TaxGroups.Add(group);
            db.ItemTaxGroups.Add(itemGroup);
            db.SaveChanges();
            return (group.Id, itemGroup.Id);
        });

        private (bool, bool) TaxGroupsExist(int companyId, int taxGroupId, int itemTaxGroupId) => WithTenantDb(companyId, db =>
            (db.TaxGroups.AsNoTracking().Any(g => g.Id == taxGroupId),
             db.ItemTaxGroups.AsNoTracking().Any(g => g.Id == itemTaxGroupId)));
    }
}
