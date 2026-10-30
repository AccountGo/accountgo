#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Api.Data;
using Api.Service;
using Core.Domain;
using Core.Domain.Items;
using Core.Domain.Sales;
using Core.Domain.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace AccountGo.BackendTests
{
    public class AccountDeletionTests : IAsyncLifetime
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), "accountgo_acct_" + Guid.NewGuid().ToString("N") + ".db");
        private readonly FakeEmailSender _emails = new FakeEmailSender();
        private WebApplicationFactory<Program> _factory;
        private HttpClient _client;
        private const string OwnerEmail = "owner@example.com";
        private const string OtherEmail = "other@example.com";

        public async Task InitializeAsync()
        {
            _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");
                builder.UseSetting("DatabaseProvider", "Sqlite");
                builder.UseSetting("ConnectionStrings:SqliteConnection", "Data Source=" + _path);
                builder.UseSetting("SECRET_KEY", "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef");
                builder.UseSetting("PUBLIC_WEB_URL", "https://workspace.example");
                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<IEmailSender>();
                    services.AddSingleton<IEmailSender>(_emails);
                });
            });
            _client = _factory.CreateClient();
            await Task.CompletedTask;
        }

        public Task DisposeAsync()
        {
            _client.Dispose();
            _factory.Dispose();
            foreach (var file in new[] { _path, _path + "-wal", _path + "-shm" })
                if (File.Exists(file))
                    File.Delete(file);
            return Task.CompletedTask;
        }

        [Fact]
        public async Task SoleOwner_Delete_Conflict_LeavesCompanyAndMembership()
        {
            var owner = await CreateUser(OwnerEmail);
            var companyId = await CreateWorkspace(owner.Token, "Ws One", "ws-one");

            var response = await Delete(owner.Token, OwnerEmail);
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            var problem = await Json(response);
            Assert.Equal("Transfer ownership or delete these workspaces first", problem.GetProperty("title").GetString());
            Assert.Equal("ws-one", problem.GetProperty("workspaces")[0].GetProperty("slug").GetString());

            var blockers = await Json(await Send(HttpMethod.Get, "/api/auth/account/deletion-blockers", token: owner.Token));
            Assert.Equal("ws-one", blockers.GetProperty("workspaces")[0].GetProperty("slug").GetString());
            Assert.False(blockers.GetProperty("platformAdministrator").GetBoolean());

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            var identity = scope.ServiceProvider.GetRequiredService<ApplicationIdentityDbContext>();
            Assert.Null(identity.Users.Single(u => u.Id == owner.Id).DeletedAt);
            Assert.Contains(db.CompanyMembers, m => m.CompanyId == companyId && m.UserId == owner.Id && m.Role == CompanyMemberRole.Owner);
            Assert.Null(db.Companies.IgnoreQueryFilters().Single(c => c.Id == companyId).DeletedAt);
        }

        [Fact]
        public async Task OnlyActiveMember_Delete_Conflict()
        {
            var owner = await CreateUser(OwnerEmail);
            var other = await CreateUser(OtherEmail);
            await CreateWorkspace(owner.Token, "Ws One", "ws-one");
            await Join(owner.Token, OtherEmail, other.Token);
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
                var members = db.CompanyMembers.Where(m => m.Company.Slug == "ws-one").ToList();
                members.Single(m => m.UserId == owner.Id).Role = CompanyMemberRole.Admin;
                db.CompanyMembers.Remove(members.Single(m => m.UserId == other.Id));
                db.SaveChanges();
            }

            var response = await Delete(owner.Token, OwnerEmail);
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal("ws-one", (await Json(response)).GetProperty("workspaces")[0].GetProperty("slug").GetString());
            using var check = _factory.Services.CreateScope();
            Assert.Null(check.ServiceProvider.GetRequiredService<ApplicationIdentityDbContext>().Users.Single(u => u.Id == owner.Id).DeletedAt);
            Assert.Single(check.ServiceProvider.GetRequiredService<ApiDbContext>().CompanyMembers.Where(m => m.UserId == owner.Id));
        }

        [Fact]
        public async Task SuspendedCoOwner_DoesNotCount_Conflict()
        {
            var owner = await CreateUser(OwnerEmail);
            var other = await CreateUser(OtherEmail);
            await CreateWorkspace(owner.Token, "Ws One", "ws-one");
            await Join(owner.Token, OtherEmail, other.Token);
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
                var otherMember = db.CompanyMembers.Single(m => m.UserId == other.Id);
                otherMember.Role = CompanyMemberRole.Owner;
                otherMember.Status = CompanyMemberStatus.Suspended;
                db.SaveChanges();
            }

            Assert.Equal(HttpStatusCode.Conflict, (await Delete(owner.Token, OwnerEmail)).StatusCode);
            using var check = _factory.Services.CreateScope();
            Assert.Null(check.ServiceProvider.GetRequiredService<ApplicationIdentityDbContext>().Users.Single(u => u.Id == owner.Id).DeletedAt);
        }

        [Fact]
        public async Task CoOwner_Delete_RemovesMembership_KeepsBooksHistory()
        {
            var owner = await CreateUser(OwnerEmail);
            var other = await CreateUser(OtherEmail);
            var companyId = await CreateWorkspace(owner.Token, "Ws One", "ws-one");
            await Join(owner.Token, OtherEmail, other.Token);
            int emailId;
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
                var tenant = scope.ServiceProvider.GetRequiredService<Core.Tenancy.ITenantContext>();
                db.CompanyMembers.Single(m => m.UserId == other.Id).Role = CompanyMemberRole.Owner;
                db.Companies.Single(c => c.Id == companyId).CreatedByUserId = owner.Id;
                var security = scope.ServiceProvider.GetRequiredService<Services.Security.ISecurityService>();
                security.AddUser(OwnerEmail, OwnerEmail, "Owner", "Person", new List<int>());
                using (tenant.Impersonate(companyId, "ws-one", CompanyMemberRole.Owner))
                {
                    var customer = new Customer
                    {
                        No = "C-1",
                        Party = new Party { Name = "Kept customer", PartyType = PartyTypes.Customer },
                        PrimaryContact = new Contact { FirstName = "Pat", LastName = "Client", Party = new Party { Name = "Pat Client", PartyType = PartyTypes.Contact } },
                        AccountsReceivableAccount = db.Accounts.Single(a => a.AccountCode == "10120"),
                        SalesAccount = db.Accounts.Single(a => a.AccountCode == "40100")
                    };
                    db.Customers.Add(customer);
                    db.SaveChanges();
                    var header = new SalesInvoiceHeader
                    {
                        CustomerId = customer.Id, No = "INV-KEEP", Date = DateTime.UtcNow, ReferenceNo = "", ShippingHandlingCharge = 0
                    };
                    db.SalesInvoiceHeaders.Add(header);
                    db.SaveChanges();
                    var sent = new SalesInvoiceEmail
                    {
                        SalesInvoiceHeaderId = header.Id, ToEmail = "customer@example.com", Subject = "Invoice INV-KEEP",
                        SentAt = DateTime.UtcNow, SentByUserId = owner.Id, SentByEmail = OwnerEmail
                    };
                    db.SalesInvoiceEmails.Add(sent);
                    db.SaveChanges();
                    emailId = sent.Id;
                }
                db.SaveChanges();
            }
            AddSession(owner.Id, "refresh-before-delete");

            var response = await Delete(owner.Token, OwnerEmail);
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

            using var check = _factory.Services.CreateScope();
            var dbCheck = check.ServiceProvider.GetRequiredService<ApiDbContext>();
            var identity = check.ServiceProvider.GetRequiredService<ApplicationIdentityDbContext>();
            var closed = identity.Users.Single(u => u.Id == owner.Id);
            Assert.NotNull(closed.DeletedAt);
            Assert.Equal(OwnerEmail, closed.Email);
            Assert.Contains(dbCheck.Users, u => u.EmailAddress == OwnerEmail);
            Assert.Null(dbCheck.Companies.Single(c => c.Id == companyId).DeletedAt);
            Assert.Equal(owner.Id, dbCheck.Companies.Single(c => c.Id == companyId).CreatedByUserId);
            Assert.DoesNotContain(dbCheck.CompanyMembers, m => m.UserId == owner.Id);
            Assert.Contains(dbCheck.CompanyMembers, m => m.UserId == other.Id && m.Role == CompanyMemberRole.Owner);
            Assert.Equal(owner.Id, dbCheck.SalesInvoiceEmails.IgnoreQueryFilters().Single(e => e.Id == emailId).SentByUserId);
            Assert.Equal(companyId, dbCheck.SalesInvoiceHeaders.IgnoreQueryFilters().Single(h => h.No == "INV-KEEP").CompanyId);
            Assert.DoesNotContain(identity.RefreshTokens.Where(t => t.UserId == owner.Id), t => t.RevokedAt == null);
            Assert.Equal(HttpStatusCode.Unauthorized, (await Send(HttpMethod.Get, "/api/auth/me", token: owner.Token)).StatusCode);
        }

        [Fact]
        public async Task DeletedWorkspace_DoesNotBlock()
        {
            var owner = await CreateUser(OwnerEmail);
            await CreateWorkspace(owner.Token, "Ws One", "ws-one");
            Assert.Equal(HttpStatusCode.NoContent, (await Send(HttpMethod.Delete, "/api/organizations/current", new { confirmSlug = "ws-one" }, owner.Token)).StatusCode);

            Assert.Equal(HttpStatusCode.NoContent, (await Delete(owner.Token, OwnerEmail)).StatusCode);
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            Assert.NotNull(db.Companies.IgnoreQueryFilters().Single(c => c.Slug == "ws-one").DeletedAt);
            Assert.Empty(db.CompanyMembers.Where(m => m.UserId == owner.Id));
            Assert.NotNull(scope.ServiceProvider.GetRequiredService<ApplicationIdentityDbContext>().Users.Single(u => u.Id == owner.Id).DeletedAt);
        }

        [Fact]
        public async Task EmailMismatch_BadRequest_LeavesAccount()
        {
            var owner = await CreateUser(OwnerEmail);
            await CreateWorkspace(owner.Token, "Ws One", "ws-one");
            var other = await CreateUser(OtherEmail);
            await Join(owner.Token, OtherEmail, other.Token);
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
                db.CompanyMembers.Single(m => m.UserId == other.Id).Role = CompanyMemberRole.Owner;
                db.SaveChanges();
            }

            var response = await Delete(owner.Token, "someone-else@example.com");
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("Email confirmation does not match", (await Json(response)).GetProperty("title").GetString());
            using var check = _factory.Services.CreateScope();
            Assert.Null(check.ServiceProvider.GetRequiredService<ApplicationIdentityDbContext>().Users.Single(u => u.Id == owner.Id).DeletedAt);
            Assert.Equal(2, check.ServiceProvider.GetRequiredService<ApiDbContext>().CompanyMembers.Count(m => m.Company.Slug == "ws-one"));
        }

        [Fact]
        public async Task LastPlatformAdministrator_Conflict()
        {
            var owner = await CreateUser(OwnerEmail);
            var other = await CreateUser(OtherEmail);
            await CreateWorkspace(owner.Token, "Ws One", "ws-one");
            await Join(owner.Token, OtherEmail, other.Token);
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
                db.CompanyMembers.Single(m => m.UserId == other.Id).Role = CompanyMemberRole.Owner;
                var roleId = db.SecurityRoles.Single(r => r.Name == "SystemAdministrators").Id;
                db.SecurityUserRoles.RemoveRange(db.SecurityUserRoles.Where(r => r.SecurityRoleId == roleId));
                var domain = new User { UserName = OwnerEmail, EmailAddress = OwnerEmail, Firstname = "Owner", Lastname = "Person" };
                db.Users.Add(domain);
                db.SaveChanges();
                db.SecurityUserRoles.Add(new SecurityUserRole { UserId = domain.Id, SecurityRoleId = roleId });
                db.SaveChanges();
            }

            var response = await Delete(owner.Token, OwnerEmail);
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            var problem = await Json(response);
            Assert.Equal("The last platform administrator cannot delete their account", problem.GetProperty("title").GetString());
            Assert.True(problem.GetProperty("platformAdministrator").GetBoolean());
            using var check = _factory.Services.CreateScope();
            Assert.Null(check.ServiceProvider.GetRequiredService<ApplicationIdentityDbContext>().Users.Single(u => u.Id == owner.Id).DeletedAt);
            Assert.Contains(check.ServiceProvider.GetRequiredService<ApiDbContext>().CompanyMembers, m => m.UserId == owner.Id);
        }

        [Fact]
        public async Task ClosedAccount_RejectsSignInRefreshReuse_AndKeepsTheEmail()
        {
            var owner = await CreateUser(OwnerEmail);
            AddSession(owner.Id, "live-refresh");
            var rotated = "rotated-" + Guid.NewGuid().ToString("N");
            var replacement = "replacement-" + Guid.NewGuid().ToString("N");
            using (var scope = _factory.Services.CreateScope())
            {
                var identity = scope.ServiceProvider.GetRequiredService<ApplicationIdentityDbContext>();
                identity.RefreshTokens.Add(new RefreshTokenSession
                {
                    UserId = owner.Id, TokenHash = Hash(rotated), CreatedAt = DateTime.UtcNow, ExpiresAt = DateTime.UtcNow.AddDays(7),
                    RevokedAt = DateTime.UtcNow, ReplacedByTokenHash = Hash(replacement)
                });
                identity.SaveChanges();
            }
            var before = _emails.Messages.Count;
            Assert.Equal(HttpStatusCode.NoContent, (await Delete(owner.Token, OwnerEmail)).StatusCode);

            var otp = await Send(HttpMethod.Post, "/api/auth/send-otp", new { email = OwnerEmail }, token: "", scoped: false);
            Assert.Equal(HttpStatusCode.OK, otp.StatusCode);
            Assert.Equal(before, _emails.Messages.Count);
            Assert.Equal(HttpStatusCode.BadRequest, (await Send(HttpMethod.Post, "/api/auth/verify-otp", new { email = OwnerEmail, otp = "000000" }, token: "", scoped: false)).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await Send(HttpMethod.Post, "/api/auth/refresh", new { refreshToken = "live-refresh" }, token: "", scoped: false)).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await Send(HttpMethod.Post, "/api/auth/refresh", new { refreshToken = rotated }, token: "", scoped: false)).StatusCode);
            using (var scope = _factory.Services.CreateScope())
            {
                var identity = scope.ServiceProvider.GetRequiredService<ApplicationIdentityDbContext>();
                Assert.DoesNotContain(identity.RefreshTokens.Where(t => t.UserId == owner.Id), t => t.RevokedAt == null);
            }

            var signup = await Send(HttpMethod.Post, "/api/auth/signup", new
            {
                email = OwnerEmail, firstName = "Again", lastName = "Owner", organizationName = "Second life", organizationSlug = "second-life"
            }, token: "", scoped: false);
            Assert.Equal(HttpStatusCode.BadRequest, signup.StatusCode);
            Assert.Equal("Account Already Exists", (await Json(signup)).GetProperty("title").GetString());
            using var check = _factory.Services.CreateScope();
            Assert.False(check.ServiceProvider.GetRequiredService<ApiDbContext>().Companies.Any(c => c.Slug == "second-life"));
            Assert.Equal(HttpStatusCode.Unauthorized, (await Send(HttpMethod.Get, "/api/auth/me", token: owner.Token)).StatusCode);
        }

        [Fact]
        public async Task Invitations_ToClosedEmailAreRevoked_SentInvitationsRemain_ReinviteRejected()
        {
            var owner = await CreateUser(OwnerEmail);
            var other = await CreateUser(OtherEmail);
            await CreateWorkspace(owner.Token, "Ws One", "ws-one");
            await Join(owner.Token, OtherEmail, other.Token);
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
                db.CompanyMembers.Single(m => m.UserId == other.Id).Role = CompanyMemberRole.Owner;
                db.SaveChanges();
            }
            var sentId = await Invite(owner.Token, "pending@example.com", "ws-one");
            await CreateWorkspace(other.Token, "Beta", "beta");
            var inboundId = await Invite(other.Token, OwnerEmail, "beta");

            Assert.Equal(HttpStatusCode.NoContent, (await Delete(owner.Token, OwnerEmail)).StatusCode);

            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
                Assert.Null(db.CompanyInvitations.Single(i => i.Id == sentId).RevokedAt);
                Assert.Equal(owner.Id, db.CompanyInvitations.Single(i => i.Id == sentId).InvitedByUserId);
                Assert.NotNull(db.CompanyInvitations.Single(i => i.Id == inboundId).RevokedAt);
            }

            var again = await Send(HttpMethod.Post, "/api/organizations/current/invitations", new { email = OwnerEmail, role = "Member", securityRoleIds = Array.Empty<int>() }, other.Token, "beta");
            Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
            Assert.Equal("This email belongs to a closed account", (await Json(again)).GetProperty("title").GetString());
        }

        private async Task<UserSession> CreateUser(string email)
        {
            using var scope = _factory.Services.CreateScope();
            var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true };
            Assert.True((await manager.CreateAsync(user)).Succeeded);
            var token = await scope.ServiceProvider.GetRequiredService<IAuthenticationService>().CreateToken(user, true);
            return new UserSession(token.AccessToken, user.Id);
        }

        private async Task<int> CreateWorkspace(string token, string name, string slug)
        {
            var response = await Send(HttpMethod.Post, "/api/organizations", new { name, slug }, token, scoped: false);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            return int.Parse((await Json(response)).GetProperty("id").GetString());
        }

        private async Task Join(string ownerToken, string email, string memberToken)
        {
            await Invite(ownerToken, email, "ws-one");
            Assert.Equal(HttpStatusCode.OK, (await Send(HttpMethod.Post, "/api/invitations/" + InvitationToken() + "/accept", token: memberToken, scoped: false)).StatusCode);
        }

        private async Task<int> Invite(string token, string email, string slug)
        {
            var response = await Send(HttpMethod.Post, "/api/organizations/current/invitations", new { email, role = "Member", securityRoleIds = Array.Empty<int>() }, token, slug);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            return (await Json(response)).GetProperty("id").GetInt32();
        }

        private Task<HttpResponseMessage> Delete(string token, string confirmEmail)
        {
            return Send(HttpMethod.Delete, "/api/auth/account", new { confirmEmail }, token, scoped: false);
        }

        private void AddSession(string userId, string rawToken)
        {
            using var scope = _factory.Services.CreateScope();
            var identity = scope.ServiceProvider.GetRequiredService<ApplicationIdentityDbContext>();
            identity.RefreshTokens.Add(new RefreshTokenSession
            {
                UserId = userId, TokenHash = Hash(rawToken), CreatedAt = DateTime.UtcNow, ExpiresAt = DateTime.UtcNow.AddDays(7)
            });
            identity.SaveChanges();
        }

        private static string Hash(string rawToken)
        {
            return Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));
        }

        private string InvitationToken()
        {
            var email = _emails.Messages.Last(m => m.Subject.Contains(" invited you to "));
            return Regex.Match(email.Html, @"/invite/([A-Za-z0-9_-]{43})").Groups[1].Value;
        }

        private async Task<HttpResponseMessage> Send(HttpMethod method, string path, object body = null, string token = null, string org = null, bool scoped = true)
        {
            using var request = new HttpRequestMessage(method, path);
            if (!string.IsNullOrEmpty(token)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            if (scoped) request.Headers.Add("X-AccountGo-Org", org ?? "ws-one");
            if (body != null) request.Content = JsonContent.Create(body);
            return await _client.SendAsync(request);
        }

        private static async Task<JsonElement> Json(HttpResponseMessage response)
        {
            return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
        }

        private sealed record UserSession(string Token, string Id);

        private class FakeEmailSender : IEmailSender
        {
            public List<Message> Messages { get; } = new List<Message>();
            public Task<string> SendAsync(EmailMessage message, CancellationToken ct = default)
            {
                Messages.Add(new Message { Subject = message.Subject, Html = message.Html });
                return Task.FromResult("fake-" + Messages.Count);
            }
        }

        private class Message
        {
            public string Subject { get; set; }
            public string Html { get; set; }
        }
    }
}
