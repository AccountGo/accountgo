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
using Core.Tenancy;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;
using Xunit.Abstractions;

namespace AccountGo.BackendTests
{
    public class MultiTenancyPhase3Tests : IAsyncLifetime
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), "accountgo_p3_" + Guid.NewGuid().ToString("N") + ".db");
        private readonly FakeEmailSender _emails = new FakeEmailSender();
        private WebApplicationFactory<Program> _factory;
        private HttpClient _client;
        private string _ownerToken;
        private string _ownerId;
        private string _otherToken;
        private string _otherId;
        private string _slug;
        private int _companyId;
        private const string Current = "/api/organizations/current";
        private readonly ITestOutputHelper _output;

        public MultiTenancyPhase3Tests(ITestOutputHelper output)
        {
            _output = output;
        }

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
            var owner = await CreateUser("phase3-owner@example.com");
            _ownerToken = owner.Item1;
            _ownerId = owner.Item2;
            var other = await CreateUser("phase3-other@example.com");
            _otherToken = other.Item1;
            _otherId = other.Item2;
            _slug = "phase-three";
            var response = await Send(HttpMethod.Post, "/api/organizations", new { name = "Phase Three", slug = _slug }, scoped: false);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var created = await Json(response);
            _companyId = int.Parse(created.GetProperty("id").GetString());
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
        public async Task Create_ProvisionsScopedCleanBooks_Idempotently_AndInvoiceWorks()
        {
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
                var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
                using (tenant.Impersonate(_companyId, _slug, CompanyMemberRole.Owner))
                {
                    Assert.True(db.Accounts.Count() > 50);
                    Assert.All(db.Accounts.Include(a => a.ParentAccount), a =>
                    {
                        Assert.Equal(_companyId, a.CompanyId);
                        if (a.ParentAccount != null) Assert.Equal(_companyId, a.ParentAccount.CompanyId);
                    });
                    Assert.Equal("USD", db.CompanySettings.Single().Currency);
                    Assert.Single(db.GeneralLedgerSettings);
                    Assert.Equal(Enum.GetValues(typeof(SequenceNumberTypes)).Length, db.SequenceNumbers.Count());
                    Assert.Contains(db.FinancialYears, y => y.StartDate.Year == DateTime.UtcNow.Year && y.IsActive);
                    Assert.NotEmpty(db.PaymentTerms);
                    Assert.NotEmpty(db.Measurements);
                    Assert.NotEmpty(db.Taxes);
                    Assert.Empty(db.Customers);
                    Assert.Empty(db.SalesInvoiceHeaders);
                    var count = db.Accounts.Count();
                    scope.ServiceProvider.GetRequiredService<CompanyProvisioningService>().Provision(db.Companies.Single(c => c.Id == _companyId));
                    Assert.Equal(count, db.Accounts.Count());
                    Assert.Equal("USD", db.CompanySettings.Single().Currency);
                    Assert.Single(db.CompanyMembers.Where(m => m.CompanyId == _companyId && m.UserId == _ownerId && m.Role == CompanyMemberRole.Owner));
                }
            }
            var invoice = await NewInvoice();
            var response = await Send(HttpMethod.Post, "/api/Sales/CreateSalesInvoice", invoice);
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            Assert.NotEmpty(SyncSql(response));
        }

        [Fact]
        public async Task SaveSalesInvoice_FreeTextLine_InNewWorkspace_CreatesItemAndPosts()
        {
            var invoice = await NewInvoice();
            var line = invoice.SalesInvoiceLines.Single();
            line.ItemId = null;
            line.MeasurementId = null;
            line.ItemDescription = "Website design";
            invoice.Status = "Open";
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
                using (scope.ServiceProvider.GetRequiredService<ITenantContext>().Impersonate(_companyId, _slug, CompanyMemberRole.Owner))
                {
                    // A fresh workspace has no items at all.
                    db.Items.RemoveRange(db.Items);
                    db.SaveChanges();
                }
            }

            for (var i = 0; i < 2; i++)
            {
                var response = await Send(HttpMethod.Post, "/api/Sales/SaveSalesInvoice", invoice);
                Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            }

            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
                using (scope.ServiceProvider.GetRequiredService<ITenantContext>().Impersonate(_companyId, _slug, CompanyMemberRole.Owner))
                {
                    var item = db.Items.Single(i => i.Description == "Website design");
                    var lines = db.SalesInvoiceLines.Include(l => l.SalesInvoiceHeader).Where(l => l.ItemId == item.Id).ToList();
                    Assert.Equal(2, lines.Count);
                    Assert.All(lines, l => Assert.NotNull(l.SalesInvoiceHeader.GeneralLedgerHeaderId));
                }
            }
        }

        [Fact]
        public async Task RecordPayment_InNewWorkspace_DebitsPostableCashAccount()
        {
            var invoice = await NewInvoice();
            invoice.Status = "Open";
            var saved = await Send(HttpMethod.Post, "/api/Sales/SaveSalesInvoice", invoice);
            Assert.True(saved.IsSuccessStatusCode, await saved.Content.ReadAsStringAsync());
            var invoiceId = (await saved.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();

            var paid = await Send(HttpMethod.Post, "/api/Sales/RecordPayment",
                new Dto.Sales.RecordInvoicePaymentDto { InvoiceId = invoiceId, Amount = 100, PaymentDate = DateTime.UtcNow });
            Assert.True(paid.IsSuccessStatusCode, await paid.Content.ReadAsStringAsync());
            Assert.Equal(100m, await DashboardKpi("moneyInBank"));

            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
                using (scope.ServiceProvider.GetRequiredService<ITenantContext>().Impersonate(_companyId, _slug, CompanyMemberRole.Owner))
                {
                    Assert.Empty(db.Banks);
                    var receipt = db.SalesReceiptHeaders.Include(r => r.AccountToDebit).Single();
                    Assert.Equal("10111", receipt.AccountToDebit.AccountCode);
                    Assert.NotNull(receipt.GeneralLedgerHeaderId);
                }
            }
        }

        [Fact]
        public async Task SaveSalesInvoice_PersistsNotesAndPaymentInstructions()
        {
            var invoice = await NewInvoice();
            invoice.Status = "Draft";
            invoice.Notes = "  Thank you!\nSee you next month.  ";
            invoice.PaymentInstructions = "Bank: Example Bank\nAccount: 0001-2345";
            var saved = await Send(HttpMethod.Post, "/api/Sales/SaveSalesInvoice", invoice);
            Assert.True(saved.IsSuccessStatusCode, await saved.Content.ReadAsStringAsync());
            invoice.Id = (await saved.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();

            var created = await GetInvoice(invoice.Id);
            Assert.Equal("Thank you!\nSee you next month.", created.Notes);
            Assert.Equal("Bank: Example Bank\nAccount: 0001-2345", created.PaymentInstructions);

            // Re-send what the edit page sends: the loaded invoice, lines keep their ids.
            var lineCount = created.SalesInvoiceLines.Count;
            invoice = created;
            invoice.Status = "Draft";
            invoice.Notes = "Updated note";
            invoice.PaymentInstructions = "Pay by check";
            var updated = await Send(HttpMethod.Post, "/api/Sales/SaveSalesInvoice", invoice);
            Assert.True(updated.IsSuccessStatusCode, await updated.Content.ReadAsStringAsync());
            var afterUpdate = await GetInvoice(invoice.Id);
            Assert.Equal("Updated note", afterUpdate.Notes);
            Assert.Equal("Pay by check", afterUpdate.PaymentInstructions);
            Assert.Equal(lineCount, afterUpdate.SalesInvoiceLines.Count);

            invoice.Notes = "";
            invoice.PaymentInstructions = "   ";
            var cleared = await Send(HttpMethod.Post, "/api/Sales/SaveSalesInvoice", invoice);
            Assert.True(cleared.IsSuccessStatusCode, await cleared.Content.ReadAsStringAsync());
            var afterClear = await GetInvoice(invoice.Id);
            Assert.Null(afterClear.Notes);
            Assert.Null(afterClear.PaymentInstructions);
        }

        private async Task<Dto.Sales.SalesInvoice> GetInvoice(int id)
        {
            var response = await Send(HttpMethod.Get, $"/api/Sales/SalesInvoice?id={id}");
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            return await response.Content.ReadFromJsonAsync<Dto.Sales.SalesInvoice>();
        }

        [Fact]
        public async Task SalesInvoicePdf_ReturnsPdf_OnlyInsideTheWorkspace()
        {
            var invoice = await NewInvoice();
            invoice.Status = "Open";
            var saved = await Send(HttpMethod.Post, "/api/Sales/SaveSalesInvoice", invoice);
            Assert.True(saved.IsSuccessStatusCode, await saved.Content.ReadAsStringAsync());
            var invoiceId = (await saved.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
            var paid = await Send(HttpMethod.Post, "/api/Sales/RecordPayment",
                new Dto.Sales.RecordInvoicePaymentDto { InvoiceId = invoiceId, Amount = 40, PaymentDate = DateTime.UtcNow });
            Assert.True(paid.IsSuccessStatusCode, await paid.Content.ReadAsStringAsync());

            var pdf = await Send(HttpMethod.Get, $"/api/Sales/SalesInvoicePdf?id={invoiceId}");
            Assert.Equal(HttpStatusCode.OK, pdf.StatusCode);
            Assert.Equal("application/pdf", pdf.Content.Headers.ContentType.MediaType);
            Assert.StartsWith("Invoice-", pdf.Content.Headers.ContentDisposition.FileName.Trim('"'));
            var bytes = await pdf.Content.ReadAsByteArrayAsync();
            Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));

            Assert.Equal(HttpStatusCode.NotFound, (await Send(HttpMethod.Get, $"/api/Sales/SalesInvoicePdf?id={invoiceId + 999}")).StatusCode);
            Assert.False((await Send(HttpMethod.Get, $"/api/Sales/SalesInvoicePdf?id={invoiceId}", token: _otherToken)).IsSuccessStatusCode);
        }

        [Fact]
        public async Task BusinessProfile_FirstSave_TrimsAndValidates_AndIsAdminOnly()
        {
            var empty = await Json(await Send(HttpMethod.Get, Current + "/business-profile"));
            Assert.Equal(JsonValueKind.Null, empty.GetProperty("legalName").ValueKind);

            // A new workspace has no address yet; the first save must create it.
            var saved = await Send(HttpMethod.Put, Current + "/business-profile", new
            {
                legalName = "  Juan Dela Cruz ",
                businessEmail = " juan@example.com ",
                businessPhone = "",
                address = new { line1 = "12 Main St", line2 = "  ", city = "Makati", region = "Metro Manila", postalCode = "1200", countryCode = "Philippines" }
            });
            Assert.True(saved.IsSuccessStatusCode, await saved.Content.ReadAsStringAsync());
            var profile = await Json(await Send(HttpMethod.Get, Current + "/business-profile"));
            Assert.Equal("Juan Dela Cruz", profile.GetProperty("legalName").GetString());
            Assert.Equal("juan@example.com", profile.GetProperty("businessEmail").GetString());
            Assert.Equal(JsonValueKind.Null, profile.GetProperty("businessPhone").ValueKind);
            Assert.Equal("Makati", profile.GetProperty("address").GetProperty("city").GetString());
            Assert.Equal(JsonValueKind.Null, profile.GetProperty("address").GetProperty("line2").ValueKind);

            Assert.Equal(HttpStatusCode.BadRequest, (await Send(HttpMethod.Put, Current + "/business-profile",
                new { legalName = "Juan", businessEmail = "not-an-email" })).StatusCode);

            // Clearing every address field removes the address.
            Assert.True((await Send(HttpMethod.Put, Current + "/business-profile", new { legalName = "Juan Dela Cruz" })).IsSuccessStatusCode);
            var cleared = await Json(await Send(HttpMethod.Get, Current + "/business-profile"));
            Assert.Equal(JsonValueKind.Null, cleared.GetProperty("address").GetProperty("line1").ValueKind);
            Assert.Equal(JsonValueKind.Null, cleared.GetProperty("businessEmail").ValueKind);

            await JoinOther();
            Assert.Equal(HttpStatusCode.OK, (await Send(HttpMethod.Get, Current + "/business-profile", token: _otherToken)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await Send(HttpMethod.Put, Current + "/business-profile",
                new { legalName = "Hijacked" }, token: _otherToken)).StatusCode);
            Assert.Equal("Juan Dela Cruz", (await Json(await Send(HttpMethod.Get, Current + "/business-profile"))).GetProperty("legalName").GetString());
        }

        [Fact]
        public async Task SalesInvoiceSettings_RoundTrip_Validate_AdminOnly_AndStayInTheirWorkspace()
        {
            var defaults = await Json(await Send(HttpMethod.Get, Current + "/sales-invoice-settings"));
            Assert.Equal("TradingName", defaults.GetProperty("sellerName").GetString());
            Assert.True(defaults.GetProperty("showNotes").GetBoolean());

            var settings = new
            {
                sellerName = "LegalName", showSellerAddress = false, showCustomerEmail = false, showNotes = false,
                showPaymentInstructions = true, defaultNotes = "  Thanks!  ", defaultPaymentInstructions = " "
            };
            var saved = await Send(HttpMethod.Put, Current + "/sales-invoice-settings", settings);
            Assert.True(saved.IsSuccessStatusCode, await saved.Content.ReadAsStringAsync());
            var read = await Json(await Send(HttpMethod.Get, Current + "/sales-invoice-settings"));
            Assert.Equal("LegalName", read.GetProperty("sellerName").GetString());
            Assert.False(read.GetProperty("showSellerAddress").GetBoolean());
            Assert.False(read.GetProperty("showCustomerEmail").GetBoolean());
            Assert.False(read.GetProperty("showNotes").GetBoolean());
            Assert.True(read.GetProperty("showPaymentInstructions").GetBoolean());
            Assert.Equal("Thanks!", read.GetProperty("defaultNotes").GetString());
            Assert.Equal(JsonValueKind.Null, read.GetProperty("defaultPaymentInstructions").ValueKind);

            Assert.Equal(HttpStatusCode.BadRequest, (await Send(HttpMethod.Put, Current + "/sales-invoice-settings",
                new { sellerName = "Nickname" })).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await Send(HttpMethod.Put, Current + "/sales-invoice-settings",
                new { sellerName = "TradingName", defaultNotes = new string('x', 2001) })).StatusCode);

            await JoinOther();
            Assert.Equal("Thanks!", (await Json(await Send(HttpMethod.Get, Current + "/sales-invoice-settings", token: _otherToken)))
                .GetProperty("defaultNotes").GetString());
            Assert.Equal(HttpStatusCode.Forbidden, (await Send(HttpMethod.Put, Current + "/sales-invoice-settings",
                new { sellerName = "TradingName" }, token: _otherToken)).StatusCode);

            // The other user's own workspace starts from defaults and cannot see this one's settings.
            var ownerSlug = _slug;
            Assert.Equal(HttpStatusCode.Created, (await Send(HttpMethod.Post, "/api/organizations",
                new { name = "Other Books", slug = "other-books" }, token: _otherToken, scoped: false)).StatusCode);
            _slug = "other-books";
            var otherSettings = await Json(await Send(HttpMethod.Get, Current + "/sales-invoice-settings", token: _otherToken));
            Assert.Equal("TradingName", otherSettings.GetProperty("sellerName").GetString());
            Assert.Equal(JsonValueKind.Null, otherSettings.GetProperty("defaultNotes").ValueKind);
            _slug = ownerSlug;
            Assert.Equal("LegalName", (await Json(await Send(HttpMethod.Get, Current + "/sales-invoice-settings"))).GetProperty("sellerName").GetString());
        }

        [Fact]
        public async Task SalesInvoice_ResolvesSellerAndDisplayFromSettings_AndPdfStillRenders()
        {
            var invoice = await NewInvoice();
            invoice.Notes = "Note";
            invoice.PaymentInstructions = "Pay";
            var saved = await Send(HttpMethod.Post, "/api/Sales/SaveSalesInvoice", invoice);
            Assert.True(saved.IsSuccessStatusCode, await saved.Content.ReadAsStringAsync());
            var invoiceId = (await saved.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();

            // No settings yet: same output as before the feature.
            var before = await GetInvoice(invoiceId);
            Assert.Equal("Phase Three", before.Seller.Name);
            Assert.Empty(before.Seller.AddressLines);
            Assert.True(before.Display.ShowCustomerEmail && before.Display.ShowNotes && before.Display.ShowPaymentInstructions);

            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
                db.Companies.Single(c => c.Id == _companyId).CRA = "123-456-789";
                db.SaveChanges();
            }
            Assert.True((await Send(HttpMethod.Put, Current + "/business-profile", new
            {
                legalName = "Juan Dela Cruz", businessEmail = "juan@example.com", businessPhone = "+63 917 000 0000",
                address = new { line1 = "12 Main St", city = "Makati", postalCode = "1200", countryCode = "Philippines" }
            })).IsSuccessStatusCode);
            Assert.True((await Send(HttpMethod.Put, Current + "/sales-invoice-settings", new
            {
                sellerName = "LegalName", showSellerAddress = true, showCustomerEmail = false, showNotes = false, showPaymentInstructions = true
            })).IsSuccessStatusCode);

            var after = await GetInvoice(invoiceId);
            Assert.Equal("Juan Dela Cruz", after.Seller.Name);
            Assert.Equal("123-456-789", after.Seller.TaxId);
            Assert.Equal("juan@example.com", after.Seller.Email);
            Assert.Equal("+63 917 000 0000", after.Seller.Phone);
            Assert.Equal(new[] { "12 Main St", "Makati 1200", "Philippines" }, after.Seller.AddressLines);
            Assert.False(after.Display.ShowCustomerEmail);
            Assert.False(after.Display.ShowNotes);
            Assert.True(after.Display.ShowPaymentInstructions);
            // The flags only affect rendering; the editor still receives the invoice's own values.
            Assert.Equal("Note", after.Notes);

            var pdf = await Send(HttpMethod.Get, $"/api/Sales/SalesInvoicePdf?id={invoiceId}");
            Assert.Equal(HttpStatusCode.OK, pdf.StatusCode);
            Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(await pdf.Content.ReadAsByteArrayAsync(), 0, 4));

            // Legal name selected but cleared: fall back to the trading name rather than print a blank heading.
            Assert.True((await Send(HttpMethod.Put, Current + "/sales-invoice-settings", new { sellerName = "LegalName", showSellerAddress = false })).IsSuccessStatusCode);
            Assert.True((await Send(HttpMethod.Put, Current + "/business-profile", new { legalName = "  " })).IsSuccessStatusCode);
            var noLegalName = await GetInvoice(invoiceId);
            Assert.Equal("Phase Three", noLegalName.Seller.Name);
            Assert.Empty(noLegalName.Seller.AddressLines);
        }

        [Fact]
        public async Task DashboardReceivables_ExcludeDraftInvoices()
        {
            var invoice = await NewInvoice();
            invoice.Status = "Draft";
            var draft = await Send(HttpMethod.Post, "/api/Sales/SaveSalesInvoice", invoice);
            Assert.True(draft.IsSuccessStatusCode, await draft.Content.ReadAsStringAsync());
            Assert.Equal(0m, await OutstandingReceivables());

            invoice.Status = "Open";
            var issued = await Send(HttpMethod.Post, "/api/Sales/SaveSalesInvoice", invoice);
            Assert.True(issued.IsSuccessStatusCode, await issued.Content.ReadAsStringAsync());
            Assert.Equal(100m, await OutstandingReceivables());
        }

        private Task<decimal> OutstandingReceivables() => DashboardKpi("outstandingReceivables");

        private async Task<decimal> DashboardKpi(string name)
        {
            var response = await Send(HttpMethod.Get, "/api/Dashboard/Summary");
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("kpis").GetProperty(name).GetDecimal();
        }

        [Fact]
        public async Task Invite_EmailHashAndSecurityRoles_WrongEmailDenied_SingleUse()
        {
            var roleId = ViewerRoleId();
            var invitation = await Invite("phase3-other@example.com", securityRoleIds: new[] { roleId });
            var token = InvitationToken();
            Assert.Matches("^[a-zA-Z0-9_-]{43}$", token);
            using (var scope = _factory.Services.CreateScope())
            {
                var row = scope.ServiceProvider.GetRequiredService<ApiDbContext>().CompanyInvitations.Single(i => i.Id == invitation);
                Assert.Equal(WorkspaceInvitationService.HashToken(token), row.TokenHash);
                Assert.DoesNotContain(token, row.TokenHash);
                Assert.InRange(row.ExpiresAt, DateTime.UtcNow.AddDays(6.99), DateTime.UtcNow.AddDays(7.01));
            }
            var preview = await Send(HttpMethod.Get, "/api/invitations/" + token, token: "", scoped: false);
            Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
            Assert.DoesNotContain("phase3-other@example.com", await preview.Content.ReadAsStringAsync());
            Assert.Equal(HttpStatusCode.Forbidden, (await Send(HttpMethod.Post, "/api/invitations/" + token + "/accept", scoped: false)).StatusCode);
            var accepted = await Send(HttpMethod.Post, "/api/invitations/" + token + "/accept", token: _otherToken, scoped: false);
            Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
            Assert.Equal(HttpStatusCode.Gone, (await Send(HttpMethod.Post, "/api/invitations/" + token + "/accept", token: _otherToken, scoped: false)).StatusCode);
            var members = await Json(await Send(HttpMethod.Get, Current + "/members"));
            var other = members.EnumerateArray().Single(m => m.GetProperty("userId").GetString() == _otherId);
            Assert.Equal("Member", other.GetProperty("role").GetString());
            Assert.Equal(roleId, other.GetProperty("securityRoles")[0].GetProperty("id").GetInt32());
            Assert.Equal(HttpStatusCode.Conflict, (await Send(HttpMethod.Post, Current + "/invitations", new { email = "phase3-other@example.com", role = "Member" })).StatusCode);
        }

        [Theory]
        [InlineData("expired", HttpStatusCode.Gone)]
        [InlineData("revoked", HttpStatusCode.NotFound)]
        public async Task Accept_InvalidInvitation_IsRejected(string state, HttpStatusCode expected)
        {
            var id = await Invite("phase3-other@example.com");
            var token = InvitationToken();
            if (state == "revoked")
                Assert.Equal(HttpStatusCode.NoContent, (await Send(HttpMethod.Delete, Current + "/invitations/" + id)).StatusCode);
            else
                using (var scope = _factory.Services.CreateScope())
                {
                    var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
                    db.CompanyInvitations.Single(i => i.Id == id).ExpiresAt = DateTime.UtcNow.AddSeconds(-1);
                    db.SaveChanges();
                }
            Assert.Equal(expected, (await Send(HttpMethod.Post, "/api/invitations/" + token + "/accept", token: _otherToken, scoped: false)).StatusCode);
        }

        [Fact]
        public async Task ReinviteAndResend_RotateTokens_AndEnforceDailyLimit()
        {
            var id = await Invite("phase3-other@example.com");
            var first = InvitationToken();
            id = await Invite("phase3-other@example.com");
            Assert.Equal(HttpStatusCode.NotFound, (await Send(HttpMethod.Get, "/api/invitations/" + first, token: "", scoped: false)).StatusCode);
            var previous = InvitationToken();
            for (var i = 0; i < 5; i++)
                Assert.Equal(HttpStatusCode.NoContent, (await Send(HttpMethod.Post, Current + "/invitations/" + id + "/resend")).StatusCode);
            Assert.NotEqual(previous, InvitationToken());
            Assert.Equal(HttpStatusCode.NotFound, (await Send(HttpMethod.Get, "/api/invitations/" + previous, token: "", scoped: false)).StatusCode);
            Assert.Equal(HttpStatusCode.TooManyRequests, (await Send(HttpMethod.Post, Current + "/invitations/" + id + "/resend")).StatusCode);
        }

        [Fact]
        public async Task Invite_WorkspaceHourlyLimit_IsExact()
        {
            for (var i = 0; i < 20; i++)
                await Invite("invite" + i + "@example.com");
            Assert.Equal(HttpStatusCode.TooManyRequests, (await Send(HttpMethod.Post, Current + "/invitations", new { email = "twenty-first@example.com", role = "Member" })).StatusCode);
        }

        [Fact]
        public async Task LastOwner_LeaveRemoveDemote_Conflict_TransferRevokesGovernanceImmediately()
        {
            var ownerMemberId = MemberId(_ownerId);
            Assert.Equal(HttpStatusCode.Conflict, (await Send(HttpMethod.Delete, Current + "/members/" + ownerMemberId)).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, (await Send(HttpMethod.Patch, Current + "/members/" + ownerMemberId, new { role = "Member" })).StatusCode);
            await JoinOther();
            var target = MemberId(_otherId);
            Assert.Equal(HttpStatusCode.NoContent, (await Send(HttpMethod.Post, Current + "/transfer-ownership", new { memberId = target })).StatusCode);
            var current = await Json(await Send(HttpMethod.Get, Current));
            Assert.Equal("Admin", current.GetProperty("role").GetString());
            Assert.Equal(HttpStatusCode.Forbidden, (await Send(HttpMethod.Post, Current + "/transfer-ownership", new { memberId = ownerMemberId })).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await Send(HttpMethod.Delete, Current + "/members/" + target)).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, (await Send(HttpMethod.Delete, Current + "/members/" + target, token: _otherToken)).StatusCode);
        }

        [Fact]
        public async Task Admin_CannotGrantAdmin_ByInvitationOrPromotion()
        {
            await JoinOther("Admin");
            Assert.Equal(HttpStatusCode.Forbidden, (await Send(HttpMethod.Post, Current + "/invitations",
                new { email = "third@example.com", role = "Admin" }, token: _otherToken)).StatusCode);
            var third = await CreateUser("third@example.com");
            await Invite("third@example.com");
            await Send(HttpMethod.Post, "/api/invitations/" + InvitationToken() + "/accept", token: third.Item1, scoped: false);
            Assert.Equal(HttpStatusCode.Forbidden, (await Send(HttpMethod.Patch, Current + "/members/" + MemberId(third.Item2),
                new { role = "Admin" }, token: _otherToken)).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await Send(HttpMethod.Patch, Current + "/members/" + MemberId(third.Item2),
                new { securityRoleIds = new[] { ViewerRoleId() } }, token: _otherToken)).StatusCode);
        }

        [Fact]
        public async Task MembershipChanges_RemovePermissionsAndAccessOnNextRequest()
        {
            await JoinOther("Admin");
            var invoice = await NewInvoice();
            Assert.True((await Send(HttpMethod.Post, "/api/Sales/CreateSalesInvoice", invoice, _otherToken)).IsSuccessStatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await Send(HttpMethod.Patch, Current + "/members/" + MemberId(_otherId),
                new { role = "Member", securityRoleIds = new[] { ViewerRoleId() } })).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await Send(HttpMethod.Patch, Current + "/members/" + MemberId(_otherId),
                new { securityRoleIds = new[] { ViewerRoleId() } })).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await Send(HttpMethod.Post, "/api/Sales/CreateSalesInvoice", invoice, _otherToken)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await Send(HttpMethod.Get, Current + "/members", token: _otherToken)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await Send(HttpMethod.Get, Current + "/invitations", token: _otherToken)).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await Send(HttpMethod.Delete, Current + "/members/" + MemberId(_otherId))).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await Send(HttpMethod.Get, Current, token: _otherToken)).StatusCode);
        }

        [Fact]
        public async Task SecurityRoles_RejectPlatformRoles_AndCrossWorkspaceIdsAreHidden()
        {
            int globalRole;
            using (var scope = _factory.Services.CreateScope())
                globalRole = scope.ServiceProvider.GetRequiredService<ApiDbContext>().SecurityRoles.Single(r => r.Name == "SystemAdministrators").Id;
            Assert.Equal(HttpStatusCode.BadRequest, (await Send(HttpMethod.Post, Current + "/invitations",
                new { email = "phase3-other@example.com", role = "Member", securityRoleIds = new[] { globalRole } })).StatusCode);
            var otherOrg = await Json(await Send(HttpMethod.Post, "/api/organizations", new { name = "Second", slug = "second" }, token: _otherToken, scoped: false));
            var secondId = int.Parse(otherOrg.GetProperty("id").GetString());
            int memberId;
            using (var scope = _factory.Services.CreateScope())
                memberId = scope.ServiceProvider.GetRequiredService<ApiDbContext>().CompanyMembers.Single(m => m.CompanyId == secondId).Id;
            Assert.Equal(HttpStatusCode.NotFound, (await Send(HttpMethod.Delete, Current + "/members/" + memberId)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await Send(HttpMethod.Patch, Current + "/members/" + memberId, new { role = "Member" })).StatusCode);
        }

        [Fact]
        public async Task ZeroMemberships_AndDeletedWorkspaces_AreNotAccessible()
        {
            Assert.Equal(0, (await Json(await Send(HttpMethod.Get, "/api/organizations", token: _otherToken, scoped: false))).GetArrayLength());
            Assert.Equal(HttpStatusCode.NotFound, (await Send(HttpMethod.Get, Current, token: _otherToken)).StatusCode);
            var invitation = await Invite("phase3-other@example.com");
            var invitationToken = InvitationToken();
            Assert.Equal(HttpStatusCode.BadRequest, (await Send(HttpMethod.Delete, Current, new { confirmSlug = "wrong" })).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await Send(HttpMethod.Delete, Current, new { confirmSlug = _slug })).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await Send(HttpMethod.Get, Current)).StatusCode);
            Assert.Equal(0, (await Json(await Send(HttpMethod.Get, "/api/organizations", scoped: false))).GetArrayLength());
            Assert.Equal(HttpStatusCode.NotFound, (await Send(HttpMethod.Get, "/api/invitations/" + invitationToken, token: "", scoped: false)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await Send(HttpMethod.Post, "/api/invitations/" + invitationToken + "/accept", token: _otherToken, scoped: false)).StatusCode);
        }

        [Fact]
        public async Task Signup_WithOrganization_ProvisionedOwner_NoGlobalRole_AndOtpListsOrganizations()
        {
            var response = await Send(HttpMethod.Post, "/api/auth/signup",
                new { email = "signup@example.com", firstName = "New", lastName = "Owner", organizationName = "Signup Org", organizationSlug = "signup-org" },
                token: "", scoped: false);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var signupHeaderBytes = TotalResponseHeaderBytes(response);
            _output.WriteLine($"POST /api/auth/signup: total response header block = {signupHeaderBytes} bytes.");
            Assert.True(signupHeaderBytes < 32768, $"Signup response headers total {signupHeaderBytes} bytes; must be under 32 KB.");
            var signup = await Json(response);
            using (var scope = _factory.Services.CreateScope())
            {
                var identity = scope.ServiceProvider.GetRequiredService<ApplicationIdentityDbContext>();
                var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
                var user = identity.Users.Single(u => u.Email == "signup@example.com");
                Assert.Empty(await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().GetRolesAsync(user));
                Assert.Empty(db.SecurityUserRoles.Where(r => r.User.EmailAddress == user.Email));
                var company = db.Companies.Single(c => c.Slug == "signup-org");
                Assert.Contains(db.CompanyMembers, m => m.CompanyId == company.Id && m.UserId == user.Id && m.Role == CompanyMemberRole.Owner);
                Assert.NotEmpty(db.Accounts.IgnoreQueryFilters().Where(a => a.CompanyId == company.Id));
            }
            var verify = await Send(HttpMethod.Post, "/api/auth/verify-otp", new { email = "signup@example.com", otp = OtpCode() }, token: "", scoped: false);
            Assert.Equal(HttpStatusCode.OK, verify.StatusCode);
            Assert.Equal("signup-org", (await Json(verify)).GetProperty("organizations")[0].GetProperty("slug").GetString());
        }

        [Fact]
        public async Task Signup_WithInvitation_DoesNotCreateWorkspace_ThenVerifiedUserAccepts()
        {
            var id = await Invite("invite-signup@example.com");
            var token = InvitationToken();
            var signup = await Send(HttpMethod.Post, "/api/auth/signup", new
            {
                email = "invite-signup@example.com", firstName = "Invited", lastName = "User",
                invitationToken = token, organizationName = "Must Not Exist", organizationSlug = "must-not-exist"
            }, token: "", scoped: false);
            Assert.Equal(HttpStatusCode.OK, signup.StatusCode);
            var body = await Json(signup);
            var verify = await Send(HttpMethod.Post, "/api/auth/verify-otp",
                new { email = "invite-signup@example.com", otp = OtpCode() }, token: "", scoped: false);
            Assert.Equal(HttpStatusCode.OK, verify.StatusCode);
            var auth = await Json(verify);
            Assert.Equal(0, auth.GetProperty("organizations").GetArrayLength());
            var accessToken = auth.GetProperty("accessToken").GetString();
            var pending = await Json(await Send(HttpMethod.Get, "/api/invitations/pending", token: accessToken, scoped: false));
            Assert.Equal(id, pending[0].GetProperty("id").GetInt32());
            Assert.Equal(HttpStatusCode.OK, (await Send(HttpMethod.Post, "/api/invitations/pending/" + id + "/accept", token: accessToken, scoped: false)).StatusCode);
            using (var scope = _factory.Services.CreateScope())
                Assert.False(scope.ServiceProvider.GetRequiredService<ApiDbContext>().Companies.Any(c => c.Slug == "must-not-exist"));
        }

        [Fact]
        public async Task Signup_EmailFailure_RollsBackIdentityDomainAndWorkspace()
        {
            _emails.Fail = true;
            var result = await Send(HttpMethod.Post, "/api/auth/signup", new
            {
                email = "failed-signup@example.com", firstName = "Failed", lastName = "Signup", organizationName = "Rolled back", organizationSlug = "rolled-back"
            }, token: "", scoped: false);
            Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
            using (var scope = _factory.Services.CreateScope())
            {
                Assert.False(scope.ServiceProvider.GetRequiredService<ApplicationIdentityDbContext>().Users.Any(u => u.Email == "failed-signup@example.com"));
                var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
                Assert.False(db.Users.Any(u => u.EmailAddress == "failed-signup@example.com"));
                Assert.False(db.Companies.Any(c => c.Slug == "rolled-back"));
            }
        }

        [Fact]
        public async Task Invite_EmailFailure_DoesNotPersistInvitationOrInvalidatePreviousLink()
        {
            await Invite("phase3-other@example.com");
            var token = InvitationToken();
            _emails.Fail = true;
            var result = await Send(HttpMethod.Post, Current + "/invitations", new { email = "phase3-other@example.com", role = "Member" });
            Assert.Equal(HttpStatusCode.InternalServerError, result.StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await Send(HttpMethod.Get, "/api/invitations/" + token, token: "", scoped: false)).StatusCode);
            using (var scope = _factory.Services.CreateScope())
                Assert.Single(scope.ServiceProvider.GetRequiredService<ApiDbContext>().CompanyInvitations);
        }

        [Fact]
        public async Task LegacyGlobalUserRoutes_AreRetired()
        {
            foreach (var method in new[] { HttpMethod.Get, HttpMethod.Post, HttpMethod.Put, HttpMethod.Delete })
                Assert.Equal(HttpStatusCode.NotFound, (await Send(method, "/api/Administration/users" + (method == HttpMethod.Put || method == HttpMethod.Delete ? "/1" : ""))).StatusCode);
        }

        [Fact]
        public async Task GlobalRoleListing_IsPlatformOnly_WorkspaceRolesCarryPermissionsAndMemberCounts()
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await Send(HttpMethod.Get, "/api/Administration/roles")).StatusCode);

            var viewerId = ViewerRoleId();
            await Invite("phase3-other@example.com", securityRoleIds: new[] { viewerId });
            Assert.Equal(HttpStatusCode.OK, (await Send(HttpMethod.Post, "/api/invitations/" + InvitationToken() + "/accept", token: _otherToken, scoped: false)).StatusCode);
            // A role held in another workspace must not count here.
            await Send(HttpMethod.Post, "/api/organizations", new { name = "Elsewhere", slug = "elsewhere" }, token: _otherToken, scoped: false);

            var roles = (await Json(await Send(HttpMethod.Get, Current + "/security-roles"))).EnumerateArray().ToList();
            Assert.DoesNotContain(roles, r => r.GetProperty("name").GetString() == "SystemAdministrators");
            var viewer = roles.Single(r => r.GetProperty("id").GetInt32() == viewerId);
            Assert.Equal(1, viewer.GetProperty("memberCount").GetInt32());
            Assert.True(viewer.GetProperty("permissions").GetArrayLength() > 0);
            Assert.All(roles.Where(r => r.GetProperty("id").GetInt32() != viewerId),
                r => Assert.Equal(0, r.GetProperty("memberCount").GetInt32()));
        }

        [Fact]
        public async Task Create_SyncPayload_HeadersUnder32KB_AndReplaysAllDefaults()
        {
            using (var source = new SqliteConnection("Data Source=" + _path))
            using (var replica = new SqliteConnection("Data Source=:memory:;Foreign Keys=True"))
            {
                source.Open();
                replica.Open();
                source.BackupDatabase(replica);
                var response = await Send(HttpMethod.Post, "/api/organizations",
                    new { name = "Durable Workspace", slug = "durable-workspace", currency = "EUR" }, scoped: false);
                Assert.Equal(HttpStatusCode.Created, response.StatusCode);
                Assert.Equal("gzip", response.Headers.GetValues("x-d1-sync-encoding").Single());
                var syncHeaderBytes = response.Headers
                    .Where(h => h.Key.StartsWith("x-d1-sync-", StringComparison.OrdinalIgnoreCase))
                    .Sum(h => Encoding.UTF8.GetByteCount(h.Key + ": " + string.Join(", ", h.Value) + "\r\n"));
                var totalHeaderBytes = TotalResponseHeaderBytes(response);
                _output.WriteLine($"POST /api/organizations: sync headers = {syncHeaderBytes} bytes; total response headers = {totalHeaderBytes} bytes.");
                Assert.True(syncHeaderBytes < 32768, $"Sync headers total {syncHeaderBytes} bytes; must be under 32 KB.");
                Assert.True(totalHeaderBytes < 32768, $"Response headers total {totalHeaderBytes} bytes; must be under 32 KB.");
                var id = int.Parse((await Json(response)).GetProperty("id").GetString());
                var statements = AllSyncStatements(response);
                Assert.Contains(statements, s => s.StartsWith("INSERT INTO \"Company\""));
                Assert.Contains(statements, s => s.StartsWith("INSERT INTO \"CompanyMember\""));
                using (var tx = replica.BeginTransaction())
                using (var command = replica.CreateCommand())
                {
                    command.Transaction = tx;
                    command.CommandText = "PRAGMA defer_foreign_keys=ON";
                    command.ExecuteNonQuery();
                    foreach (var statement in statements)
                    {
                        command.CommandText = statement;
                        command.ExecuteNonQuery();
                    }
                    tx.Commit();
                }
                using (var command = replica.CreateCommand())
                {
                    command.CommandText = "SELECT COUNT(*) FROM Account WHERE CompanyId=" + id;
                    Assert.True(Convert.ToInt32(command.ExecuteScalar()) > 50);
                    command.CommandText = "SELECT Currency FROM CompanySetting WHERE CompanyId=" + id;
                    Assert.Equal("EUR", command.ExecuteScalar());
                    command.CommandText = "SELECT COUNT(*) FROM SequenceNumber WHERE CompanyId=" + id;
                    Assert.Equal(Enum.GetValues(typeof(SequenceNumberTypes)).Length, Convert.ToInt32(command.ExecuteScalar()));
                    command.CommandText = "SELECT COUNT(*) FROM GeneralLedgerSetting WHERE CompanyId=" + id;
                    Assert.Equal(1, Convert.ToInt32(command.ExecuteScalar()));
                }
            }
        }

        [Fact]
        public async Task Create_ProvisioningFailure_RollsBackCompanyAndMembership()
        {
            using (var scope = _factory.Services.CreateScope())
                scope.ServiceProvider.GetRequiredService<ApiDbContext>().Database.ExecuteSqlRaw(
                    "CREATE TRIGGER reject_provisioning BEFORE INSERT ON GeneralLedgerSetting BEGIN SELECT RAISE(ABORT, 'simulated provisioning failure'); END;");
            var response = await Send(HttpMethod.Post, "/api/organizations", new { name = "Rejected", slug = "rejected" }, scoped: false);
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            Assert.False(response.Headers.Contains("x-d1-sync-sql"));
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
                Assert.False(db.Companies.Any(c => c.Slug == "rejected"));
                Assert.Equal(1, db.CompanyMembers.Count(m => m.UserId == _ownerId));
            }
        }

        [Fact]
        public async Task ChartOfAccounts_ExposesNormalSideClassAndHeaderFlags()
        {
            var tree = await Json(await Send(HttpMethod.Get, "/api/Financials/Accounts"));
            JsonElement Find(JsonElement list, string code)
            {
                foreach (var account in list.EnumerateArray())
                {
                    if (account.GetProperty("accountCode").GetString() == code)
                        return account;
                    var match = Find(account.GetProperty("childAccounts"), code);
                    if (match.ValueKind != JsonValueKind.Undefined)
                        return match;
                }
                return default;
            }

            var fixedAssets = Find(tree, "10300");
            Assert.False(fixedAssets.GetProperty("canPost").GetBoolean());
            Assert.Equal("Assets", fixedAssets.GetProperty("accountClassName").GetString());
            var depreciation = Find(tree, "14001");
            Assert.True(depreciation.GetProperty("canPost").GetBoolean());
            Assert.True(depreciation.GetProperty("isContraAccount").GetBoolean());
            Assert.Equal("Cr", depreciation.GetProperty("normalBalance").GetString());
            Assert.Equal("Dr", Find(tree, "10111").GetProperty("normalBalance").GetString());
        }

        [Fact]
        public async Task Workspace_UpdateSlugSettings_AndReserveDeletedSlug()
        {
            Assert.Equal(HttpStatusCode.OK, (await Send(HttpMethod.Patch, Current,
                new { name = "Renamed", slug = "renamed", currency = "EUR" })).StatusCode);
            _slug = "renamed";
            var current = await Json(await Send(HttpMethod.Get, Current));
            Assert.Equal("Renamed", current.GetProperty("name").GetString());
            Assert.Equal("EUR", current.GetProperty("currency").GetString());
            Assert.Equal(HttpStatusCode.NoContent, (await Send(HttpMethod.Delete, Current, new { confirmSlug = _slug })).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, (await Send(HttpMethod.Post, "/api/organizations",
                new { name = "Collision", slug = _slug }, scoped: false)).StatusCode);
        }

        [Fact]
        public async Task InvitationPreview_IsRateLimitedPerIp()
        {
            await Invite("phase3-other@example.com");
            var path = "/api/invitations/" + InvitationToken();
            for (var i = 0; i < 30; i++)
                Assert.Equal(HttpStatusCode.OK, (await Send(HttpMethod.Get, path, token: "", scoped: false)).StatusCode);
            Assert.Equal(HttpStatusCode.TooManyRequests, (await Send(HttpMethod.Get, path, token: "", scoped: false)).StatusCode);
        }

        private async Task<Tuple<string, string>> CreateUser(string email)
        {
            using (var scope = _factory.Services.CreateScope())
            {
                var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
                var user = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true };
                Assert.True((await manager.CreateAsync(user)).Succeeded);
                var token = await scope.ServiceProvider.GetRequiredService<IAuthenticationService>().CreateToken(user, true);
                return Tuple.Create(token.AccessToken, user.Id);
            }
        }

        private async Task<int> Invite(string email, string role = "Member", int[] securityRoleIds = null)
        {
            var response = await Send(HttpMethod.Post, Current + "/invitations", new { email, role, securityRoleIds = securityRoleIds ?? new int[0] });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            return (await Json(response)).GetProperty("id").GetInt32();
        }

        private async Task JoinOther(string role = "Member")
        {
            await Invite("phase3-other@example.com", role);
            Assert.Equal(HttpStatusCode.OK, (await Send(HttpMethod.Post, "/api/invitations/" + InvitationToken() + "/accept", token: _otherToken, scoped: false)).StatusCode);
        }

        private string InvitationToken()
        {
            var email = _emails.Messages.Last(m => m.Subject.Contains(" invited you to "));
            return Regex.Match(email.Html, @"/invite/([A-Za-z0-9_-]{43})").Groups[1].Value;
        }

        private string OtpCode()
        {
            return Regex.Match(_emails.Messages.Last(m => m.Subject.StartsWith("Welcome to AccountGo!", StringComparison.Ordinal)).Subject, @"\d{6}").Value;
        }

        private int ViewerRoleId()
        {
            using (var scope = _factory.Services.CreateScope())
                return scope.ServiceProvider.GetRequiredService<ApiDbContext>().SecurityRoles.Single(r => r.Name == "Auditor / Viewer").Id;
        }

        private int MemberId(string userId)
        {
            using (var scope = _factory.Services.CreateScope())
                return scope.ServiceProvider.GetRequiredService<ApiDbContext>().CompanyMembers.Single(m => m.UserId == userId && m.CompanyId == _companyId).Id;
        }

        private async Task<Dto.Sales.SalesInvoice> NewInvoice()
        {
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
                using (scope.ServiceProvider.GetRequiredService<ITenantContext>().Impersonate(_companyId, _slug, CompanyMemberRole.Owner))
                {
                    var customer = new Customer
                    {
                        No = Guid.NewGuid().ToString("N"), Party = new Party { Name = "Real customer", PartyType = PartyTypes.Customer },
                        PrimaryContact = new Contact { FirstName = "Customer", LastName = "Contact", Party = new Party { Name = "Contact", PartyType = PartyTypes.Contact } },
                        AccountsReceivableAccount = db.Accounts.Single(a => a.AccountCode == "10120"),
                        SalesAccount = db.Accounts.Single(a => a.AccountCode == "40100")
                    };
                    var item = new Item { No = Guid.NewGuid().ToString("N"), Code = "CONSULTING", Description = "Consulting", Price = 100,
                        SmallestMeasurementId = db.Measurements.First().Id, SalesAccount = customer.SalesAccount };
                    db.Customers.Add(customer);
                    db.Items.Add(item);
                    await db.SaveChangesAsync();
                    return new Dto.Sales.SalesInvoice
                    {
                        CustomerId = customer.Id, InvoiceDate = DateTime.UtcNow,
                        SalesInvoiceLines = new List<Dto.Sales.SalesInvoiceLine> { new Dto.Sales.SalesInvoiceLine
                        {
                            ItemId = item.Id, Quantity = 1, Amount = 100, MeasurementId = item.SmallestMeasurementId.Value
                        } }
                    };
                }
            }
        }

        private async Task<HttpResponseMessage> Send(HttpMethod method, string path, object body = null, string token = null, bool scoped = true)
        {
            using (var request = new HttpRequestMessage(method, path))
            {
                var bearer = token ?? _ownerToken;
                if (!string.IsNullOrEmpty(bearer)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
                if (scoped) request.Headers.Add("X-AccountGo-Org", _slug);
                if (body != null) request.Content = JsonContent.Create(body);
                return await _client.SendAsync(request);
            }
        }

        private static async Task<JsonElement> Json(HttpResponseMessage response)
        {
            var text = await response.Content.ReadAsStringAsync();
            return JsonDocument.Parse(text).RootElement.Clone();
        }

        private static int TotalResponseHeaderBytes(HttpResponseMessage response)
        {
            var statusLineBytes = Encoding.UTF8.GetByteCount(
                $"HTTP/{response.Version} {(int)response.StatusCode} {response.ReasonPhrase}\r\n");
            var headersBytes = response.Headers.Concat(response.Content.Headers)
                .Sum(h => Encoding.UTF8.GetByteCount(h.Key + ": " + string.Join(", ", h.Value) + "\r\n"));
            return statusLineBytes + headersBytes + 2;
        }

        private static string SyncSql(HttpResponseMessage response)
        {
            return JsonSerializer.Serialize(AllSyncStatements(response));
        }

        private static List<string> AllSyncStatements(HttpResponseMessage response)
        {
            var chunks = new List<string>();
            var count = response.Headers.Contains("x-d1-sync-sql-count")
                ? int.Parse(response.Headers.GetValues("x-d1-sync-sql-count").Single()) : 1;
            for (var i = 0; i < count; i++)
            {
                var key = response.Headers.Contains("x-d1-sync-sql") ? "x-d1-sync-sql" : "x-d1-sync-sql-" + i;
                chunks.Add(response.Headers.GetValues(key).Single());
            }
            Assert.Equal("gzip", response.Headers.GetValues("x-d1-sync-encoding").Single());
            using (var input = new MemoryStream(Convert.FromBase64String(string.Concat(chunks))))
            using (var gzip = new GZipStream(input, CompressionMode.Decompress))
            using (var reader = new StreamReader(gzip, Encoding.UTF8))
                return JsonSerializer.Deserialize<List<string>>(reader.ReadToEnd());
        }

        private class FakeEmailSender : IEmailSender
        {
            public bool Fail { get; set; }
            public List<Message> Messages { get; } = new List<Message>();
            public Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default(CancellationToken))
            {
                if (Fail) throw new HttpRequestException("Simulated email delivery failure");
                Messages.Add(new Message { To = to, Subject = subject, Html = htmlBody });
                return Task.CompletedTask;
            }
        }

        private class Message
        {
            public string To { get; set; }
            public string Subject { get; set; }
            public string Html { get; set; }
        }
    }
}
