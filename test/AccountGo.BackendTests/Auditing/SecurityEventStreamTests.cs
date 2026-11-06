// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Api.Data;
using Api.Service;
using Core.Domain;
using Core.Domain.Items;
using Core.Domain.Sales;
using Core.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;
using Host = AccountGo.BackendTests.Auditing.AuditTestHost;

namespace AccountGo.BackendTests.Auditing
{
    /// <summary>
    /// PR3 security-event stream: what gets written to <c>SecurityAuditEvents</c> for sign-in, denial,
    /// data-access, membership, invitation and platform-role flows, and what must never be written.
    /// </summary>
    public sealed class SecurityEventStreamTests : IClassFixture<Host>
    {
        private const string Current = "/api/organizations/current";
        private const string SigninSuccess = "auth.signin.success";
        private const string SigninFailure = "auth.signin.failure";
        private const string OtpLocked = "auth.otp.locked";
        private const string AccessDenied = "access.denied";
        private const string ReportView = "data.report.view";
        private const int OtpMaxFailedAttempts = 5;
        private const int RapidDenialCount = 5;
        private const int SecondsPerMinute = 60;
        private const int MinuteBoundaryGuardSeconds = 52;
        private const string InvoiceSecret = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

        private readonly Host _host;

        public SecurityEventStreamTests(Host host) => _host = host;

        private static string Unique(string prefix) => prefix + "-" + Guid.NewGuid().ToString("N")[..8];

        private static JsonElement Metadata(SecurityAuditEvent evt)
        {
            Assert.False(string.IsNullOrWhiteSpace(evt.MetadataJson), "event " + evt.EventType + " has no metadata");
            return JsonDocument.Parse(evt.MetadataJson!).RootElement.Clone();
        }

        private static string WrongCodeFor(string realCode) => realCode == "424242" ? "313131" : "424242";

        // ---- OTP sign-in ------------------------------------------------------------------------------

        [Fact]
        public async Task OtpSignIn_Success_WritesSigninSuccess_WithMethodOutcomeCorrelationAndIp()
        {
            var user = await _host.CreateUser(Unique("otp-ok") + "@example.com");
            var code = await _host.RequestOtpCode(user.Email);

            var response = await _host.VerifyOtp(user.Email, code);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var correlationId = Host.RequestId(response);

            var events = await _host.SettledEvents(e => e.EventType == SigninSuccess && e.CorrelationId == correlationId, 1);
            var evt = Assert.Single(events);
            Assert.Equal("success", evt.Outcome);
            Assert.Equal(user.Id, evt.UserId);
            Assert.Equal(Host.ClientIp, evt.IpAddress);
            Assert.Equal("otp", Metadata(evt).GetProperty("method").GetString());
            Assert.Empty(_host.ReadEvents(e => e.EventType == SigninFailure && e.CorrelationId == correlationId));
        }

        [Fact]
        public async Task OtpSignIn_WrongCode_WritesSigninFailure_WithMethodOutcomeReasonCorrelationAndIp()
        {
            var user = await _host.CreateUser(Unique("otp-bad") + "@example.com");
            var code = await _host.RequestOtpCode(user.Email);

            var response = await _host.VerifyOtp(user.Email, WrongCodeFor(code));
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var correlationId = Host.RequestId(response);

            var events = await _host.SettledEvents(e => e.EventType == SigninFailure && e.CorrelationId == correlationId, 1);
            var evt = Assert.Single(events);
            Assert.Equal("failure", evt.Outcome);
            Assert.Equal(Host.ClientIp, evt.IpAddress);
            var metadata = Metadata(evt);
            Assert.Equal("otp", metadata.GetProperty("method").GetString());
            Assert.Equal("invalid_or_expired_code", metadata.GetProperty("reason").GetString());
            Assert.Empty(_host.ReadEvents(e => e.EventType == SigninSuccess && e.CorrelationId == correlationId));
        }

        [Fact]
        public async Task OtpSignIn_UnknownEmail_WritesSigninFailure_WithoutRecordingTheEmail()
        {
            var email = Unique("nobody") + "@example.com";

            var response = await _host.VerifyOtp(email, "123456");
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var correlationId = Host.RequestId(response);

            var events = await _host.SettledEvents(e => e.EventType == SigninFailure && e.CorrelationId == correlationId, 1);
            var evt = Assert.Single(events);
            Assert.Null(evt.UserId);
            Assert.Equal("failure", evt.Outcome);
            Assert.DoesNotContain(email, evt.MetadataJson ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task OtpLockout_AfterMaxFailedAttempts_WritesOtpLockedOnce()
        {
            var user = await _host.CreateUser(Unique("otp-lock") + "@example.com");
            var code = await _host.RequestOtpCode(user.Email);
            var wrong = WrongCodeFor(code);

            string? lockingCorrelationId = null;
            var failureCorrelationIds = new List<string>();
            for (var attempt = 1; attempt <= OtpMaxFailedAttempts; attempt++)
            {
                var response = await _host.VerifyOtp(user.Email, wrong);
                Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
                failureCorrelationIds.Add(Host.RequestId(response));
                if (attempt == OtpMaxFailedAttempts)
                    lockingCorrelationId = Host.RequestId(response);
            }

            var locked = await _host.SettledEvents(e => e.EventType == OtpLocked && e.UserId == user.Id, 1);
            var evt = Assert.Single(locked);
            Assert.Equal("failure", evt.Outcome);
            Assert.Equal(lockingCorrelationId, evt.CorrelationId);
            Assert.Equal(Host.ClientIp, evt.IpAddress);
            Assert.Equal(OtpMaxFailedAttempts, Metadata(evt).GetProperty("maxFailedAttempts").GetInt32());

            var failures = _host.ReadEvents(e => e.EventType == SigninFailure && failureCorrelationIds.Contains(e.CorrelationId ?? ""));
            Assert.Equal(OtpMaxFailedAttempts, failures.Count);

            // The locked code is gone: even the right code no longer signs in, and no second lock event appears.
            var afterLock = await _host.VerifyOtp(user.Email, code);
            Assert.Equal(HttpStatusCode.BadRequest, afterLock.StatusCode);
            Assert.Single(await _host.SettledEvents(e => e.EventType == OtpLocked && e.UserId == user.Id));
        }

        [Fact]
        public async Task OtpFlows_NeverWriteTheOtpCode_ToEventsOrAuditLogs()
        {
            var user = await _host.CreateUser(Unique("otp-leak") + "@example.com");
            var code = await _host.RequestOtpCode(user.Email);
            var wrong = WrongCodeFor(code);

            var failed = await _host.VerifyOtp(user.Email, wrong);
            var succeeded = await _host.VerifyOtp(user.Email, code);
            Assert.Equal(HttpStatusCode.BadRequest, failed.StatusCode);
            Assert.Equal(HttpStatusCode.OK, succeeded.StatusCode);
            var correlationIds = new[] { Host.RequestId(failed), Host.RequestId(succeeded) };

            var events = await _host.SettledEvents(e => correlationIds.Contains(e.CorrelationId ?? ""), 2);
            Assert.True(events.Count >= 2);
            foreach (var evt in events)
            {
                var serialized = JsonSerializer.Serialize(evt);
                Assert.DoesNotContain(code, serialized);
                Assert.DoesNotContain(wrong, serialized);
            }

            var auditLogLines = _host.Logs.Entries.Where(l => l.Message.Contains("[SecurityAudit]", StringComparison.Ordinal)).ToList();
            Assert.NotEmpty(auditLogLines);
            Assert.All(auditLogLines, line =>
            {
                Assert.DoesNotContain(code, line.Message);
                Assert.DoesNotContain(wrong, line.Message);
                // Text logs keep type/outcome/request id only — never IP, UA, user id or metadata.
                Assert.DoesNotContain("ip=", line.Message, StringComparison.Ordinal);
                Assert.DoesNotContain("ua=", line.Message, StringComparison.Ordinal);
                Assert.DoesNotContain("userId=", line.Message, StringComparison.Ordinal);
                Assert.DoesNotContain("metadata=", line.Message, StringComparison.Ordinal);
                Assert.DoesNotContain(Host.ClientIp, line.Message, StringComparison.Ordinal);
            });
        }

        [Fact]
        public async Task AccessDenied_IsAwaited_SoTheEventIsPersistedBeforeTheResponseReturns()
        {
            await WaitForSafeMinuteWindow();
            var owner = await _host.CreateUser(Unique("deny-await") + "@example.com");
            var slug = Unique("deny-await");
            await _host.CreateWorkspace(owner.Token, "Denied Await", slug);

            var response = await _host.Send(HttpMethod.Get, "/api/Administration/AuditLogs", token: owner.Token, org: slug);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

            // No polling: an awaited write is visible as soon as the HTTP response completes.
            var immediate = _host.ReadEvents(e => e.EventType == AccessDenied && e.UserId == owner.Id);
            Assert.Single(immediate);
        }

        // ---- access.denied ----------------------------------------------------------------------------

        [Fact]
        public async Task AccessDenied_RapidRepeatedDenials_WriteExactlyOneEventPerUserAndOperation()
        {
            await WaitForSafeMinuteWindow();
            var owner = await _host.CreateUser(Unique("deny") + "@example.com");
            var slug = Unique("deny");
            var companyId = await _host.CreateWorkspace(owner.Token, "Denied Org", slug);

            for (var attempt = 0; attempt < RapidDenialCount; attempt++)
            {
                var response = await _host.Send(HttpMethod.Get, "/api/Administration/AuditLogs", token: owner.Token, org: slug);
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            }

            var events = await _host.SettledEvents(e => e.EventType == AccessDenied && e.UserId == owner.Id, 1);
            var evt = Assert.Single(events);
            Assert.Equal("denied", evt.Outcome);
            Assert.Equal(companyId, evt.CompanyId);
            Assert.Equal(Host.ClientIp, evt.IpAddress);
            Assert.False(string.IsNullOrWhiteSpace(evt.CorrelationId));
            Assert.False(string.IsNullOrWhiteSpace(Metadata(evt).GetProperty("operation").GetString()));

            // A different denied operation for the same user is a separate rate-limit bucket.
            var other = await _host.Send(HttpMethod.Get, "/api/Donors", token: owner.Token, org: slug);
            Assert.Equal(HttpStatusCode.Forbidden, other.StatusCode);
            var afterSecond = await _host.SettledEvents(e => e.EventType == AccessDenied && e.UserId == owner.Id, 2);
            Assert.Equal(2, afterSecond.Count);
            Assert.Equal(2, afterSecond.Select(e => Metadata(e).GetProperty("operation").GetString()).Distinct().Count());
        }

        [Fact]
        public async Task AccessDenied_DifferentUsersSameOperation_EachWriteOneEvent()
        {
            await WaitForSafeMinuteWindow();
            var slug = Unique("deny2");
            var first = await _host.CreateUser(Unique("deny-a") + "@example.com");
            var second = await _host.CreateUser(Unique("deny-b") + "@example.com");
            var companyId = await _host.CreateWorkspace(first.Token, "Denied Two", slug);
            await _host.AddMember(companyId, second.Id, CompanyMemberRole.Member);

            foreach (var user in new[] { first, second })
            {
                for (var attempt = 0; attempt < 3; attempt++)
                    Assert.Equal(HttpStatusCode.Forbidden,
                        (await _host.Send(HttpMethod.Get, "/api/Administration/AuditLogs", token: user.Token, org: slug)).StatusCode);
            }

            Assert.Single(await _host.SettledEvents(e => e.EventType == AccessDenied && e.UserId == first.Id, 1));
            Assert.Single(await _host.SettledEvents(e => e.EventType == AccessDenied && e.UserId == second.Id, 1));
        }

        private static async Task WaitForSafeMinuteWindow()
        {
            // The denial rate limit is keyed per UTC minute; avoid straddling a boundary mid-test.
            var seconds = DateTime.UtcNow.Second;
            if (seconds >= MinuteBoundaryGuardSeconds)
                await Task.Delay(TimeSpan.FromSeconds(SecondsPerMinute - seconds + 1));
        }

        // ---- log hygiene ------------------------------------------------------------------------------

        [Fact]
        public async Task SendOtp_UnknownEmail_LogsOnlyRedactedEmail_AtInformationAndAbove()
        {
            var email = Unique("Ghost") + "@Example.COM";
            var normalized = email.Trim().ToLowerInvariant();
            var expectedRedaction = "email#" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)))[..12].ToLowerInvariant();

            var response = await _host.Send(HttpMethod.Post, "/api/auth/send-otp", new { email }, scoped: false);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var visible = _host.Logs.Entries.Where(l => l.Level >= LogLevel.Information).ToList();
            Assert.DoesNotContain(visible, l => l.Message.Contains(email, StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(visible, l => l.Message.Contains(normalized, StringComparison.OrdinalIgnoreCase));
            Assert.Contains(visible, l =>
                l.Category.Contains(nameof(EmailOtpService), StringComparison.Ordinal)
                && l.Message.Contains(expectedRedaction, StringComparison.Ordinal));
        }

        // ---- data access ------------------------------------------------------------------------------

        [Fact]
        public async Task TrialBalance_Success_WritesDataReportView_AndDeniedRequestDoesNot()
        {
            var slug = Unique("report");
            var viewer = await _host.CreateUser(Unique("viewer") + "@example.com");
            var companyId = await _host.CreateWorkspace(viewer.Token, "Report Org", slug);
            await _host.SetFunctionalRoles(companyId, viewer.Id, "Auditor / Viewer");

            var ok = await _host.Send(HttpMethod.Get, "/api/Financials/trialbalance", token: viewer.Token, org: slug);
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
            var correlationId = Host.RequestId(ok);

            var events = await _host.SettledEvents(e => e.EventType == ReportView && e.CompanyId == companyId, 1);
            var evt = Assert.Single(events);
            Assert.Equal(viewer.Id, evt.UserId);
            Assert.Equal("success", evt.Outcome);
            Assert.Equal(correlationId, evt.CorrelationId);
            Assert.Equal(Host.ClientIp, evt.IpAddress);
            Assert.Equal("trial_balance", Metadata(evt).GetProperty("name").GetString());

            var deniedSlug = Unique("report-denied");
            var zeroRole = await _host.CreateUser(Unique("zero") + "@example.com");
            var deniedCompanyId = await _host.CreateWorkspace(zeroRole.Token, "Zero Role Org", deniedSlug);
            var denied = await _host.Send(HttpMethod.Get, "/api/Financials/trialbalance", token: zeroRole.Token, org: deniedSlug);
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            Assert.Empty(await _host.SettledEvents(e => e.EventType == ReportView && e.CompanyId == deniedCompanyId));
        }

        // ---- public invoice PDF -----------------------------------------------------------------------

        [Fact]
        public async Task PublicInvoicePdf_WritesEvent_WithoutLinkTokenOrRecipientInMetadata()
        {
            var slug = Unique("pdf");
            var recipientEmail = Unique("billing") + "@customer.example";
            var owner = await _host.CreateUser(Unique("pdf-owner") + "@example.com");
            var companyId = await _host.CreateWorkspace(owner.Token, "Invoice Org", slug);
            await _host.SetFunctionalRoles(companyId, owner.Id, "Accountant", "Sales Representative");
            var invoiceId = await CreateIssuedInvoice(owner.Token, slug, companyId, recipientEmail);

            var link = InvoicePdfLinkSigner.CreateUrl("https://api.example", companyId, invoiceId, recipientEmail, DateTimeOffset.UtcNow.AddDays(1), InvoiceSecret);
            var uri = new Uri(link);
            var response = await _host.Send(HttpMethod.Get, uri.PathAndQuery, scoped: false);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
            var correlationId = Host.RequestId(response);

            var events = await _host.SettledEvents(e => e.EventType == "invoice.pdf.public" && e.CompanyId == companyId, 1);
            var evt = Assert.Single(events);
            Assert.Null(evt.UserId);
            Assert.Equal("success", evt.Outcome);
            Assert.Equal(correlationId, evt.CorrelationId);
            Assert.Equal(Host.ClientIp, evt.IpAddress);

            var metadata = Metadata(evt);
            Assert.Equal(invoiceId, metadata.GetProperty("invoiceId").GetInt32());
            var names = metadata.EnumerateObject().Select(p => p.Name).ToList();
            Assert.DoesNotContain(names, name => SecurityAuditMetadata.IsForbidden(name));
            var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
            var signature = query["sig"]!;
            var serialized = JsonSerializer.Serialize(evt);
            Assert.DoesNotContain(signature, serialized);
            Assert.DoesNotContain(Uri.EscapeDataString(signature), serialized);
            Assert.DoesNotContain(recipientEmail, serialized, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("sig=", serialized);
        }

        private async Task<int> CreateIssuedInvoice(string token, string slug, int companyId, string customerEmail)
        {
            Dto.Sales.SalesInvoice invoice;
            using (var scope = _host.Factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
                using var impersonation = scope.ServiceProvider.GetRequiredService<ITenantContext>()
                    .Impersonate(companyId, slug, CompanyMemberRole.Owner);
                var advancesAccount = db.Accounts.FirstOrDefault(a => a.AccountCode == "20120") ?? db.Accounts.First(a => a.AccountClassId == 2);
                var customer = new Customer
                {
                    No = Guid.NewGuid().ToString("N"),
                    Party = new Party { Name = "Audit customer", PartyType = PartyTypes.Customer, Email = customerEmail },
                    PrimaryContact = new Contact { FirstName = "Customer", LastName = "Contact", Party = new Party { Name = "Contact", PartyType = PartyTypes.Contact } },
                    AccountsReceivableAccount = db.Accounts.Single(a => a.AccountCode == "10120"),
                    SalesAccount = db.Accounts.Single(a => a.AccountCode == "40100"),
                    CustomerAdvancesAccount = advancesAccount,
                    CustomerAdvancesAccountId = advancesAccount.Id
                };
                var glSetting = db.GeneralLedgerSettings.FirstOrDefault();
                if (glSetting != null && !glSetting.ShippingChargeAccountId.HasValue)
                    glSetting.ShippingChargeAccountId = customer.SalesAccount.Id;
                var serviceCategory = db.ItemCategories.FirstOrDefault(c => c.ItemType == ItemTypes.Service);
                if (serviceCategory == null)
                {
                    serviceCategory = new ItemCategory
                    {
                        Name = "Services",
                        ItemType = ItemTypes.Service,
                        MeasurementId = db.Measurements.First().Id,
                        SalesAccountId = customer.SalesAccount.Id
                    };
                    db.ItemCategories.Add(serviceCategory);
                }
                var item = new Item
                {
                    No = Guid.NewGuid().ToString("N"),
                    Code = "CONSULTING",
                    Description = "Consulting",
                    Price = 100,
                    ItemCategory = serviceCategory,
                    SmallestMeasurementId = db.Measurements.First().Id,
                    SalesAccount = customer.SalesAccount
                };
                db.Customers.Add(customer);
                db.Items.Add(item);
                await db.SaveChangesAsync();
                invoice = new Dto.Sales.SalesInvoice
                {
                    CustomerId = customer.Id,
                    InvoiceDate = DateTime.UtcNow,
                    Status = "Draft",
                    SalesInvoiceLines = new List<Dto.Sales.SalesInvoiceLine>
                    {
                        new() { ItemId = item.Id, Quantity = 1, Amount = 100, MeasurementId = item.SmallestMeasurementId!.Value }
                    }
                };
            }

            var saved = await _host.Send(HttpMethod.Post, "/api/Sales/SaveSalesInvoice", invoice, token, slug);
            Assert.True(saved.IsSuccessStatusCode, await saved.Content.ReadAsStringAsync());
            var invoiceId = (await Host.Json(saved)).GetProperty("id").GetInt32();
            var issued = await _host.Send(HttpMethod.Post, $"/api/Sales/IssueSalesInvoice?id={invoiceId}", token: token, org: slug);
            Assert.True(issued.IsSuccessStatusCode, await issued.Content.ReadAsStringAsync());
            return invoiceId;
        }

        // ---- membership, invitations, platform roles --------------------------------------------------

        private sealed record Team(
            string Slug,
            int CompanyId,
            Host.UserSession Owner,
            Host.UserSession Admin,
            Host.UserSession Target,
            int TargetMemberId,
            int AccountantRoleId);

        private async Task<Team> CreateTeam()
        {
            var slug = Unique("team");
            var owner = await _host.CreateUser($"owner-{slug}@example.com");
            var admin = await _host.CreateUser($"admin-{slug}@example.com");
            var target = await _host.CreateUser($"target-{slug}@example.com");
            var companyId = await _host.CreateWorkspace(owner.Token, "Team Org", slug);
            await _host.AddMember(companyId, admin.Id, CompanyMemberRole.Admin);
            var targetMemberId = await _host.AddMember(companyId, target.Id, CompanyMemberRole.Member);
            return new Team(slug, companyId, owner, admin, target, targetMemberId, _host.SecurityRoleId("Accountant"));
        }

        private async Task<string> GrantApproval(Team team)
        {
            var approval = await _host.Send(HttpMethod.Post, Current + "/members/access-approvals",
                new { targetMemberId = team.TargetMemberId, targetUserId = team.Target.Id, securityRoleIds = new[] { team.AccountantRoleId } },
                team.Owner.Token, team.Slug);
            Assert.Equal(HttpStatusCode.Created, approval.StatusCode);
            return (await Host.Json(approval)).GetProperty("approvalRequestId").GetString()!;
        }

        [Fact]
        public async Task ApprovalGrant_WritesOneEvent_WithTargetAndRoles()
        {
            var team = await CreateTeam();
            var approvalId = await GrantApproval(team);

            var events = await _host.SettledEvents(e => e.EventType == "approval.grant" && e.CompanyId == team.CompanyId, 1);
            var evt = Assert.Single(events);
            Assert.Equal(team.Owner.Id, evt.UserId);
            Assert.Equal("success", evt.Outcome);
            Assert.Equal(Host.ClientIp, evt.IpAddress);
            Assert.False(string.IsNullOrWhiteSpace(evt.CorrelationId));
            var metadata = Metadata(evt);
            Assert.Equal(approvalId, metadata.GetProperty("requestId").GetString());
            Assert.Equal(team.Target.Id, metadata.GetProperty("targetUserId").GetString());
            Assert.Equal(team.AccountantRoleId, metadata.GetProperty("securityRoleIds")[0].GetInt32());
        }

        [Fact]
        public async Task ApprovalRevoke_WritesOneEvent()
        {
            var team = await CreateTeam();
            var approvalId = await GrantApproval(team);

            var revoke = await _host.Send(HttpMethod.Delete, Current + "/members/access-approvals/" + approvalId, token: team.Owner.Token, org: team.Slug);
            Assert.Equal(HttpStatusCode.NoContent, revoke.StatusCode);

            var events = await _host.SettledEvents(e => e.EventType == "approval.revoke" && e.CompanyId == team.CompanyId, 1);
            var evt = Assert.Single(events);
            Assert.Equal(team.Owner.Id, evt.UserId);
            Assert.Equal("success", evt.Outcome);
            Assert.Equal(approvalId, Metadata(evt).GetProperty("requestId").GetString());
            Assert.Single(await _host.SettledEvents(e => e.EventType == "approval.grant" && e.CompanyId == team.CompanyId));
        }

        [Fact]
        public async Task MemberRoleGrant_WithApproval_WritesOneMemberRoleChangeEvent()
        {
            var team = await CreateTeam();
            var approvalId = await GrantApproval(team);

            var patch = await _host.Send(HttpMethod.Patch, Current + "/members/" + team.TargetMemberId,
                new { securityRoleIds = new[] { team.AccountantRoleId }, approvalRequestId = approvalId },
                team.Admin.Token, team.Slug);
            Assert.Equal(HttpStatusCode.NoContent, patch.StatusCode);

            var events = await _host.SettledEvents(e => e.EventType == "member.role.change" && e.CompanyId == team.CompanyId, 1);
            var evt = Assert.Single(events);
            Assert.Equal(team.Admin.Id, evt.UserId);
            Assert.Equal("success", evt.Outcome);
            var metadata = Metadata(evt);
            Assert.Equal(team.Target.Id, metadata.GetProperty("targetUserId").GetString());
            Assert.Equal(team.AccountantRoleId, metadata.GetProperty("newSecurityRoleIds")[0].GetInt32());
            Assert.Equal(0, metadata.GetProperty("oldSecurityRoleIds").GetArrayLength());
        }

        [Fact]
        public async Task MemberRemoval_WritesOneMemberRemoveEvent()
        {
            var team = await CreateTeam();

            var remove = await _host.Send(HttpMethod.Delete, Current + "/members/" + team.TargetMemberId, token: team.Owner.Token, org: team.Slug);
            Assert.Equal(HttpStatusCode.NoContent, remove.StatusCode);

            var events = await _host.SettledEvents(e => e.EventType == "member.remove" && e.CompanyId == team.CompanyId, 1);
            var evt = Assert.Single(events);
            Assert.Equal(team.Owner.Id, evt.UserId);
            Assert.Equal(team.Target.Id, Metadata(evt).GetProperty("targetUserId").GetString());
        }

        [Fact]
        public async Task InvitationAccept_WritesOneEvent_WithoutTheInvitationToken()
        {
            var team = await CreateTeam();
            var invitee = await _host.CreateUser($"invitee-{team.Slug}@example.com");
            var viewerRoleId = _host.SecurityRoleId("Auditor / Viewer");

            var invite = await _host.Send(HttpMethod.Post, Current + "/invitations",
                new { email = invitee.Email, role = "Member", securityRoleIds = new[] { viewerRoleId } },
                team.Admin.Token, team.Slug);
            Assert.Equal(HttpStatusCode.Created, invite.StatusCode);
            var rawToken = LastInvitationToken();

            var accept = await _host.Send(HttpMethod.Post, $"/api/invitations/{rawToken}/accept", token: invitee.Token, scoped: false);
            Assert.Equal(HttpStatusCode.OK, accept.StatusCode);

            var events = await _host.SettledEvents(e => e.EventType == "invitation.accept" && e.CompanyId == team.CompanyId, 1);
            var evt = Assert.Single(events);
            Assert.Equal(invitee.Id, evt.UserId);
            Assert.Equal("success", evt.Outcome);
            Assert.Equal(Host.ClientIp, evt.IpAddress);
            Assert.Equal(team.Admin.Id, Metadata(evt).GetProperty("invitedByUserId").GetString());
            Assert.DoesNotContain(rawToken, JsonSerializer.Serialize(evt));
        }

        [Fact]
        public async Task PlatformRoleChange_WritesOnePlatformEvent_WithoutWorkspace()
        {
            var admin = await _host.CreateUser(Unique("platform") + "@example.com", isSystemAdministrator: true);
            var roleName = Unique("audit-role");
            var slug = Unique("platform");
            await _host.CreateWorkspace(admin.Token, "Platform Admin Org", slug);
            int permissionId;
            using (var scope = _host.Factory.Services.CreateScope())
            {
                permissionId = scope.ServiceProvider.GetRequiredService<ApiDbContext>().SecurityPermissions.OrderBy(p => p.Id).First().Id;
            }

            var save = await _host.Send(HttpMethod.Post, "/api/Administration/roles",
                new { id = 0, name = roleName, displayName = roleName, permissionIds = new[] { permissionId } },
                admin.Token, slug);
            Assert.True(save.StatusCode == HttpStatusCode.OK, await save.Content.ReadAsStringAsync());

            var events = await _host.SettledEvents(e => e.EventType == "platform.role.change" && e.UserId == admin.Id, 1);
            var evt = Assert.Single(events);
            Assert.Null(evt.CompanyId);
            Assert.Equal("success", evt.Outcome);
            var metadata = Metadata(evt);
            Assert.Equal("create", metadata.GetProperty("operation").GetString());
            Assert.Equal(roleName, metadata.GetProperty("roleName").GetString());
            Assert.Equal(permissionId, metadata.GetProperty("permissionsAdded")[0].GetProperty("id").GetInt32());
        }

        private string LastInvitationToken()
        {
            var html = _host.Emails.Messages.Last(m => m.Subject.Contains(" invited you to ", StringComparison.Ordinal)).Html;
            return Regex.Match(html, @"/invite/([A-Za-z0-9_-]{43})").Groups[1].Value;
        }
    }
}
