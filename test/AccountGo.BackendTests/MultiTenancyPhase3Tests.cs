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
    public partial class MultiTenancyPhase3Tests : IAsyncLifetime
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
                builder.UseSetting("PUBLIC_API_URL", "https://api.example");
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
        public async Task SaveSalesInvoice_FreeTextLines_UseTheGeneralItem_KeepTheirText_AndPost()
        {
            var invoice = await NewInvoice();
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

            foreach (var text in new[] { "Website design", "Logo design" })
            {
                var line = invoice.SalesInvoiceLines.Single();
                line.ItemId = null;
                line.MeasurementId = null;
                line.ItemDescription = text;
                var response = await Send(HttpMethod.Post, "/api/Sales/SaveSalesInvoice", invoice);
                Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            }

            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
                using (scope.ServiceProvider.GetRequiredService<ITenantContext>().Impersonate(_companyId, _slug, CompanyMemberRole.Owner))
                {
                    // Typing doesn't add to the catalog: both lines are booked to the one general item.
                    var item = db.Items.Single();
                    Assert.Equal(Api.Service.CatalogDefaults.GeneralItemName, item.Description);
                    var lines = db.SalesInvoiceLines.Include(l => l.SalesInvoiceHeader).Where(l => l.ItemId == item.Id).ToList();
                    Assert.Equal(new[] { "Logo design", "Website design" }, lines.Select(l => l.Description).OrderBy(d => d));
                    Assert.All(lines, l => Assert.NotNull(l.SalesInvoiceHeader.GeneralLedgerHeaderId));
                }
            }
        }

        [Fact]
        public async Task SaveSalesInvoice_FreeTextLine_NamedLikeAnActiveItem_UsesThatItem()
        {
            var hosting = await SaveItem(new Dto.Inventory.CatalogItem { Name = "Hosting", Price = 15 });
            var archived = await SaveItem(new Dto.Inventory.CatalogItem { Name = "Old plan", Price = 9 });
            Assert.True((await Send(HttpMethod.Post, $"/api/Catalog/ArchiveItem?id={archived.Id}")).IsSuccessStatusCode);

            var invoice = await NewInvoice();
            invoice.Status = "Draft";
            invoice.SalesInvoiceLines = new List<Dto.Sales.SalesInvoiceLine>
            {
                new() { ItemDescription = "hosting", Quantity = 1, Amount = 15 },
                new() { ItemDescription = "Old plan", Quantity = 1, Amount = 9 },
            };
            var saved = await Send(HttpMethod.Post, "/api/Sales/SaveSalesInvoice", invoice);
            Assert.True(saved.IsSuccessStatusCode, await saved.Content.ReadAsStringAsync());
            var lines = (await GetInvoice((await Json(saved)).GetProperty("id").GetInt32())).SalesInvoiceLines;
            Assert.Equal(hosting.Id, lines.Single(l => l.ItemDescription == "hosting").ItemId);
            // An archived item isn't matched; the line goes to the general item.
            Assert.NotEqual(archived.Id, lines.Single(l => l.ItemDescription == "Old plan").ItemId);
        }

        [Fact]
        public async Task Catalog_CreateEditArchiveAndDelete()
        {
            var created = await SaveItem(new Dto.Inventory.CatalogItem { Name = "  Annual support ", Code = "SUP-1", Price = 1200, Cost = 300 });
            Assert.Equal("Annual support", created.Name);
            Assert.True(created.CanDelete);
            var listed = await (await Send(HttpMethod.Get, "/api/Catalog/Items")).Content.ReadFromJsonAsync<List<Dto.Inventory.CatalogItem>>();
            Assert.Contains(listed, i => i.Id == created.Id && i.Code == "SUP-1" && i.Price == 1200 && i.Cost == 300);

            Assert.Equal(HttpStatusCode.Conflict, (await Send(HttpMethod.Post, "/api/Catalog/SaveItem", new Dto.Inventory.CatalogItem { Name = "Other", Code = "sup-1" })).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await Send(HttpMethod.Post, "/api/Catalog/SaveItem", new Dto.Inventory.CatalogItem { Name = " " })).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await Send(HttpMethod.Post, "/api/Catalog/SaveItem", new Dto.Inventory.CatalogItem { Name = "Refund", Price = -1 })).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await Send(HttpMethod.Get, "/api/Catalog/Item?id=999999")).StatusCode);

            // Used on an invoice, then renamed: the invoice line keeps its text, and the item can only be archived.
            var invoice = await NewInvoice();
            invoice.SalesInvoiceLines.Single().ItemId = created.Id;
            invoice.SalesInvoiceLines.Single().ItemDescription = "Annual support";
            var saved = await Send(HttpMethod.Post, "/api/Sales/SaveSalesInvoice", invoice);
            Assert.True(saved.IsSuccessStatusCode, await saved.Content.ReadAsStringAsync());
            var invoiceId = (await Json(saved)).GetProperty("id").GetInt32();

            created.Name = "Premium support";
            created.Price = 1500;
            var edited = await SaveItem(created);
            Assert.Equal("Premium support", edited.Name);
            Assert.False(edited.CanDelete);
            Assert.Equal("Annual support", (await GetInvoice(invoiceId)).SalesInvoiceLines.Single().ItemDescription);

            var refused = await Send(HttpMethod.Delete, $"/api/Catalog/DeleteItem?id={created.Id}");
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
            Assert.Contains("Archive it instead", await refused.Content.ReadAsStringAsync());

            Assert.True((await Send(HttpMethod.Post, $"/api/Catalog/ArchiveItem?id={created.Id}")).IsSuccessStatusCode);
            var picker = await Json(await Send(HttpMethod.Get, "/api/Common/Items"));
            Assert.True(picker.EnumerateArray().Single(i => i.GetProperty("id").GetInt32() == created.Id).GetProperty("archived").GetBoolean());
            Assert.True((await Send(HttpMethod.Post, $"/api/Catalog/ArchiveItem?id={created.Id}&archived=false")).IsSuccessStatusCode);

            var unused = await SaveItem(new Dto.Inventory.CatalogItem { Name = "Typo item" });
            Assert.Equal(HttpStatusCode.NoContent, (await Send(HttpMethod.Delete, $"/api/Catalog/DeleteItem?id={unused.Id}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await Send(HttpMethod.Get, $"/api/Catalog/Item?id={unused.Id}")).StatusCode);

            // Members without "Manage Items" can't change the catalog.
            await JoinOther();
            Assert.Equal(HttpStatusCode.Forbidden, (await Send(HttpMethod.Post, "/api/Catalog/SaveItem", created, _otherToken)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await Send(HttpMethod.Delete, $"/api/Catalog/DeleteItem?id={created.Id}", token: _otherToken)).StatusCode);
        }

        [Fact]
        public async Task RecordPayment_OnlyOnIssuedInvoices()
        {
            var invoice = await NewInvoice();
            invoice.Status = "Draft";
            var saved = await Send(HttpMethod.Post, "/api/Sales/SaveSalesInvoice", invoice);
            Assert.True(saved.IsSuccessStatusCode, await saved.Content.ReadAsStringAsync());
            var invoiceId = (await saved.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
            var payment = new Dto.Sales.RecordInvoicePaymentDto { InvoiceId = invoiceId, Amount = 100, PaymentDate = DateTime.UtcNow };

            var onDraft = await Send(HttpMethod.Post, "/api/Sales/RecordPayment", payment);
            Assert.Equal(HttpStatusCode.BadRequest, onDraft.StatusCode);
            Assert.Equal("Draft", (await GetInvoice(invoiceId)).Status);
            Assert.Equal(0m, await DashboardKpi("moneyInBank"));

            Assert.True((await Send(HttpMethod.Post, $"/api/Sales/IssueSalesInvoice?id={invoiceId}")).IsSuccessStatusCode);
            var onIssued = await Send(HttpMethod.Post, "/api/Sales/RecordPayment", payment);
            Assert.True(onIssued.IsSuccessStatusCode, await onIssued.Content.ReadAsStringAsync());
            Assert.Equal("Paid", (await GetInvoice(invoiceId)).Status);

            var voided = await NewInvoice();
            voided.Status = "Draft";
            var voidedId = (await (await Send(HttpMethod.Post, "/api/Sales/SaveSalesInvoice", voided)).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
            Assert.True((await Send(HttpMethod.Post, $"/api/Sales/VoidSalesInvoice?id={voidedId}")).IsSuccessStatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await Send(HttpMethod.Post, "/api/Sales/RecordPayment",
                new Dto.Sales.RecordInvoicePaymentDto { InvoiceId = voidedId, Amount = 100, PaymentDate = DateTime.UtcNow })).StatusCode);
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

        [Fact]
        public async Task SaveSalesInvoice_DueDate_FollowsTermInBusinessDays_OrIsKeptAsCustom()
        {
            int termId, termDays;
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
                using (scope.ServiceProvider.GetRequiredService<ITenantContext>().Impersonate(_companyId, _slug, CompanyMemberRole.Owner))
                {
                    var term = db.PaymentTerms.Where(t => t.DueAfterDays > 0).OrderBy(t => t.Id).First();
                    termId = term.Id;
                    termDays = term.DueAfterDays.GetValueOrDefault();
                }
            }
            var friday = new DateTime(2026, 10, 23);

            // A term and no due date: the due date is the term's business days after the invoice date.
            var withTerm = await NewInvoice();
            withTerm.Status = "Draft";
            withTerm.InvoiceDate = friday;
            withTerm.PaymentTermId = termId;
            var saved = await Send(HttpMethod.Post, "/api/Sales/SaveSalesInvoice", withTerm);
            Assert.True(saved.IsSuccessStatusCode, await saved.Content.ReadAsStringAsync());
            var created = await GetInvoice((await saved.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32());
            Assert.Equal(BusinessDays.Add(friday, termDays), created.DueDate!.Value.Date);
            Assert.Equal(termId, created.PaymentTermId);

            // No term and a due date: custom terms, kept as sent, also on later edits.
            created.Status = "Draft";
            created.PaymentTermId = null;
            created.DueDate = friday.AddDays(3);
            var custom = await Send(HttpMethod.Post, "/api/Sales/SaveSalesInvoice", created);
            Assert.True(custom.IsSuccessStatusCode, await custom.Content.ReadAsStringAsync());
            var afterCustom = await GetInvoice(created.Id);
            Assert.Null(afterCustom.PaymentTermId);
            Assert.Equal(friday.AddDays(3), afterCustom.DueDate!.Value.Date);

            // A due date before the invoice date is refused.
            afterCustom.Status = "Draft";
            afterCustom.DueDate = friday.AddDays(-1);
            var early = await Send(HttpMethod.Post, "/api/Sales/SaveSalesInvoice", afterCustom);
            Assert.Equal(HttpStatusCode.BadRequest, early.StatusCode);
            Assert.Contains("due date", await early.Content.ReadAsStringAsync());
        }

        [Theory]
        [InlineData("2026-10-23", 0, "2026-10-23")] // Friday, due on receipt
        [InlineData("2026-10-23", 1, "2026-10-26")] // Friday + 1 is Monday
        [InlineData("2026-10-21", 10, "2026-11-04")] // Wednesday + 10 is two weeks later
        [InlineData("2026-10-24", 1, "2026-10-26")] // Saturday + 1 is Monday
        [InlineData("2026-10-23", 30, "2026-12-04")]
        public void BusinessDays_SkipWeekends(string start, int days, string expected) =>
            Assert.Equal(DateTime.Parse(expected, System.Globalization.CultureInfo.InvariantCulture),
                BusinessDays.Add(DateTime.Parse(start, System.Globalization.CultureInfo.InvariantCulture), days));

        [Fact]
        public async Task InvoiceNumber_DraftsHaveNone_IssuingTakesTheNextInOrder()
        {
            var first = await SaveDraft();
            var second = await SaveDraft();
            Assert.Null((await GetInvoice(first)).No);
            Assert.Null((await GetInvoice(second)).No);

            var next = await NextNumber();
            // Issue in the opposite order to saving: the number follows the issue, not the draft.
            Assert.Equal(next, await Issue(second));
            Assert.Equal((int.Parse(next) + 1).ToString(), await Issue(first));
            Assert.Equal((int.Parse(next) + 2).ToString(), await NextNumber());

            // Issuing straight from the form numbers the invoice too.
            var invoice = await NewInvoice();
            invoice.Status = "Open";
            var saved = await Send(HttpMethod.Post, "/api/Sales/SaveSalesInvoice", invoice);
            Assert.True(saved.IsSuccessStatusCode, await saved.Content.ReadAsStringAsync());
            Assert.Equal((int.Parse(next) + 2).ToString(), (await Json(saved)).GetProperty("no").GetString());
        }

        [Fact]
        public async Task InvoiceNumber_Typed_IsKept_Unique_AndMovesTheSequencePastIt()
        {
            var typed = await SaveDraft(" #INV-A ");
            Assert.Equal("INV-A", (await GetInvoice(typed)).No);

            // Unique per workspace, ignoring case.
            var clash = await NewInvoice();
            clash.Status = "Draft";
            clash.No = "inv-a";
            var refused = await Send(HttpMethod.Post, "/api/Sales/SaveSalesInvoice", clash);
            Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
            Assert.Contains("already exists", await refused.Content.ReadAsStringAsync());

            var invalid = await NewInvoice();
            invalid.Status = "Draft";
            invalid.No = "no spaces!";
            Assert.Equal(HttpStatusCode.BadRequest, (await Send(HttpMethod.Post, "/api/Sales/SaveSalesInvoice", invalid)).StatusCode);

            // A draft holding a typed plain number is skipped by automatic numbering.
            var next = int.Parse(await NextNumber());
            var holder = await SaveDraft((next).ToString());
            Assert.Equal((next + 1).ToString(), await NextNumber());
            Assert.Equal((next + 1).ToString(), await Issue(await SaveDraft()));

            // Issuing a typed number ahead of the sequence moves the sequence past it.
            var ahead = await SaveDraft("5000");
            Assert.Equal("5000", await Issue(ahead));
            Assert.Equal("5001", await Issue(await SaveDraft()));
            Assert.Equal(next.ToString(), await Issue(holder));
        }

        [Fact]
        public async Task InvoiceNumber_ConcurrentIssues_NeverShareANumber()
        {
            var drafts = new List<int>();
            for (var i = 0; i < 12; i++)
                drafts.Add(await SaveDraft());
            var next = int.Parse(await NextNumber());

            var numbers = await Task.WhenAll(drafts.Select(Issue));

            Assert.Equal(drafts.Count, numbers.Distinct().Count());
            Assert.Equal(Enumerable.Range(next, drafts.Count).Select(n => n.ToString()).OrderBy(n => n), numbers.OrderBy(n => n));
        }

        [Fact]
        public async Task InvoiceNumber_IssueRefusesWhenTheConfirmedNumberWasTaken()
        {
            var first = await SaveDraft();
            var second = await SaveDraft();
            var shown = await NextNumber();

            var ok = await Send(HttpMethod.Post, $"/api/Sales/IssueSalesInvoice?id={second}&expectedNo={shown}");
            Assert.True(ok.IsSuccessStatusCode, await ok.Content.ReadAsStringAsync());

            // The first was confirmed against the same number; it is refused and stays an unnumbered draft.
            var stale = await Send(HttpMethod.Post, $"/api/Sales/IssueSalesInvoice?id={first}&expectedNo={shown}");
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
            var problem = await Json(stale);
            Assert.Equal("number_changed", problem.GetProperty("code").GetString());
            Assert.Equal((int.Parse(shown) + 1).ToString(), problem.GetProperty("next").GetString());
            var unchanged = await GetInvoice(first);
            Assert.Null(unchanged.No);
            Assert.Equal("Draft", unchanged.Status);
        }

        [Fact]
        public async Task InvoiceNumber_IssuedCantBeDeleted_DraftsCan()
        {
            var issued = await SaveDraft();
            var number = await Issue(issued);
            var refused = await Send(HttpMethod.Delete, $"/api/Sales/DeleteSalesInvoice?id={issued}");
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
            Assert.Equal(number, (await GetInvoice(issued)).No);

            var draft = await SaveDraft();
            Assert.Equal(HttpStatusCode.NoContent, (await Send(HttpMethod.Delete, $"/api/Sales/DeleteSalesInvoice?id={draft}")).StatusCode);
        }

        [Fact]
        public async Task InvoiceNumber_UniqueIndex_RejectsADuplicate_LocallyAndOnD1Replay()
        {
            var draft = await SaveDraft();
            var elsewhere = await SaveDraft();
            var next = await NextNumber();

            using (var source = new SqliteConnection("Data Source=" + _path))
            using (var replica = new SqliteConnection("Data Source=:memory:;Foreign Keys=True"))
            {
                source.Open();
                replica.Open();
                source.BackupDatabase(replica);
                using (var command = replica.CreateCommand())
                {
                    // The D1 migration applies cleanly on top of the existing schema.
                    command.CommandText = File.ReadAllText(Path.Combine(RepoRoot(), "src/Api/d1/migrations/0008_invoice_number_unique.sql"));
                    command.ExecuteNonQuery();
                    // Another container already issued the same number into D1.
                    command.CommandText = $"UPDATE SalesInvoiceHeader SET No = '{next}', Status = 1 WHERE Id = {elsewhere}";
                    Assert.Equal(1, command.ExecuteNonQuery());
                }

                var issued = await Send(HttpMethod.Post, $"/api/Sales/IssueSalesInvoice?id={draft}");
                Assert.True(issued.IsSuccessStatusCode, await issued.Content.ReadAsStringAsync());
                var statements = AllSyncStatements(issued);
                Assert.Contains(statements, s => s.StartsWith("INSERT INTO \"SalesInvoiceHeader\"") && s.Contains($"'{next}'"));

                // The worker applies the batch in one transaction; the clash fails it instead of keeping two #next.
                using (var tx = replica.BeginTransaction())
                using (var command = replica.CreateCommand())
                {
                    command.Transaction = tx;
                    command.CommandText = "PRAGMA defer_foreign_keys=ON";
                    command.ExecuteNonQuery();
                    var error = Assert.Throws<SqliteException>(() =>
                    {
                        foreach (var statement in statements)
                        {
                            command.CommandText = statement;
                            command.ExecuteNonQuery();
                        }
                    });
                    Assert.Contains("UNIQUE constraint failed: SalesInvoiceHeader.CompanyId, SalesInvoiceHeader.No", error.Message);
                }
            }

            // And locally, a second invoice with an existing number can't be stored.
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
                using (scope.ServiceProvider.GetRequiredService<ITenantContext>().Impersonate(_companyId, _slug, CompanyMemberRole.Owner))
                {
                    var copy = db.SalesInvoiceHeaders.AsNoTracking().Single(i => i.Id == draft);
                    copy.Id = 0;
                    db.SalesInvoiceHeaders.Add(copy);
                    Assert.Throws<DbUpdateException>(() => db.SaveChanges());
                }
            }
        }

        [Theory]
        [InlineData(null, "Draft", false, " #A-1 ", "A-1")] // new: typed, trimmed, '#' dropped
        [InlineData(null, "Draft", false, "  ", null)] // new: blank means numbered at issue
        [InlineData("7", "Draft", true, "", null)] // draft: clearing the number hands it back to the sequence
        [InlineData("7", "Open", true, null, "7")] // issued: an omitted number keeps the issued one
        [InlineData("7", "Open", true, "7", "7")]
        public void InvoiceNumber_ForSave(string current, string status, bool exists, string requested, string expected) =>
            Assert.Equal(expected, InvoiceNumber.ForSave(current, Enum.Parse<SalesInvoiceStatus>(status), exists, requested));

        [Fact]
        public void InvoiceNumber_ForSave_RefusesChangingAnIssuedNumber_AndBadInput()
        {
            Assert.Equal(InvoiceNumberError.Locked,
                Assert.Throws<InvoiceNumberException>(() => InvoiceNumber.ForSave("7", SalesInvoiceStatus.Open, true, "8")).Error);
            Assert.Equal(InvoiceNumberError.Invalid,
                Assert.Throws<InvoiceNumberException>(() => InvoiceNumber.ForSave(null, null, false, new string('1', 31))).Error);
            Assert.Equal(InvoiceNumberError.Invalid,
                Assert.Throws<InvoiceNumberException>(() => InvoiceNumber.ForSave(null, null, false, "-1")).Error);
        }

        private async Task<int> SaveDraft(string number = null)
        {
            var invoice = await NewInvoice();
            invoice.Status = "Draft";
            invoice.No = number;
            var saved = await Send(HttpMethod.Post, "/api/Sales/SaveSalesInvoice", invoice);
            Assert.True(saved.IsSuccessStatusCode, await saved.Content.ReadAsStringAsync());
            return (await Json(saved)).GetProperty("id").GetInt32();
        }

        private async Task<string> Issue(int id)
        {
            var issued = await Send(HttpMethod.Post, $"/api/Sales/IssueSalesInvoice?id={id}");
            Assert.True(issued.IsSuccessStatusCode, await issued.Content.ReadAsStringAsync());
            return (await Json(issued)).GetProperty("no").GetString();
        }

        private async Task<string> NextNumber()
        {
            var response = await Send(HttpMethod.Get, "/api/Sales/NextSalesInvoiceNumber");
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            return (await Json(response)).GetProperty("next").GetString();
        }

        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Api", "d1")))
                dir = dir.Parent;
            return dir?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
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
        public async Task SendSalesInvoiceEmail_LinksPdf_BlindCopiesSender_OnlyForIssuedInvoices()
        {
            var invoice = await NewInvoice("billing@customer.example");
            invoice.Status = "Draft";
            var saved = await Send(HttpMethod.Post, "/api/Sales/SaveSalesInvoice", invoice);
            Assert.True(saved.IsSuccessStatusCode, await saved.Content.ReadAsStringAsync());
            var invoiceId = (await saved.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
            var path = $"/api/Sales/SendSalesInvoiceEmail?id={invoiceId}";

            Assert.Equal(HttpStatusCode.BadRequest, (await Send(HttpMethod.Post, path)).StatusCode);
            Assert.True((await Send(HttpMethod.Post, $"/api/Sales/IssueSalesInvoice?id={invoiceId}")).IsSuccessStatusCode);

            var sent = await Send(HttpMethod.Post, path);
            Assert.True(sent.IsSuccessStatusCode, await sent.Content.ReadAsStringAsync());
            var message = _emails.Messages.Last().Raw;
            Assert.Equal("billing@customer.example", message.To);
            Assert.Equal(new[] { "phase3-owner@example.com" }, message.Bcc);
            Assert.Equal("phase3-owner@example.com", message.ReplyTo);
            Assert.EndsWith("via AccountGo", message.FromName);
            Assert.StartsWith("Invoice ", message.Subject);
            Assert.Empty(message.Attachments);
            Assert.Contains("View invoice", message.Html);
            Assert.Contains("/api/public/invoices/pdf?", message.Html);
            Assert.Contains("billing%40customer.example", message.Html);
            Assert.Contains("Real customer", message.Html);
            Assert.False(string.IsNullOrWhiteSpace(message.Text));

            // Not a member of the workspace: no email goes out.
            var count = _emails.Messages.Count;
            Assert.False((await Send(HttpMethod.Post, path, token: _otherToken)).IsSuccessStatusCode);
            Assert.Equal(count, _emails.Messages.Count);

            // A provider failure is reported, and the invoice stays issued.
            _emails.Fail = true;
            Assert.Equal(HttpStatusCode.BadGateway, (await Send(HttpMethod.Post, path)).StatusCode);
            _emails.Fail = false;
            var reloaded = await (await Send(HttpMethod.Get, $"/api/Sales/SalesInvoice?id={invoiceId}")).Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("Open", reloaded.GetProperty("status").GetString());

            // Only the send the provider accepted is on record, and the invoice shows it.
            var emails = reloaded.GetProperty("emails");
            Assert.Equal(1, emails.GetArrayLength());
            Assert.Equal("billing@customer.example", emails[0].GetProperty("to").GetString());
            Assert.Equal("phase3-owner@example.com", emails[0].GetProperty("sentBy").GetString());
            Assert.Equal("Invoice", emails[0].GetProperty("kind").GetString());
            Assert.EndsWith("Z", emails[0].GetProperty("sentAt").GetString());
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
                using (scope.ServiceProvider.GetRequiredService<ITenantContext>().Impersonate(_companyId, _slug, CompanyMemberRole.Owner))
                {
                    var record = db.SalesInvoiceEmails.Single(e => e.SalesInvoiceHeaderId == invoiceId);
                    Assert.Equal(_companyId, record.CompanyId);
                    Assert.Equal(_ownerId, record.SentByUserId);
                    Assert.Equal("phase3-owner@example.com", record.BccEmail);
                    Assert.Equal(message.Subject, record.Subject);
                    Assert.StartsWith("fake-", record.ProviderMessageId);
                }
            }

            // Advanced opt-in still attaches the PDF when asked.
            Assert.True((await Send(HttpMethod.Post, path + "&attachPdf=true")).IsSuccessStatusCode);
            var withAttach = _emails.Messages.Last().Raw;
            var attachment = Assert.Single(withAttach.Attachments);
            Assert.Matches(@"^Invoice-[A-Za-z0-9_-]+\.pdf$", attachment.FileName);
            Assert.Equal("%PDF", Encoding.ASCII.GetString(attachment.Content, 0, 4));
            Assert.Contains("View invoice", withAttach.Html);
        }

        [Fact]
        public async Task SendSalesInvoiceEmail_CapsSendsPerWorkspaceHour()
        {
            var invoice = await NewInvoice("billing@customer.example");
            invoice.Status = "Open";
            var saved = await Send(HttpMethod.Post, "/api/Sales/SaveSalesInvoice", invoice);
            Assert.True(saved.IsSuccessStatusCode, await saved.Content.ReadAsStringAsync());
            var invoiceId = (await saved.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();

            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
                using (scope.ServiceProvider.GetRequiredService<ITenantContext>().Impersonate(_companyId, _slug, CompanyMemberRole.Owner))
                {
                    // An old send no longer counts; 29 in the last hour leave room for one more.
                    db.SalesInvoiceEmails.Add(new SalesInvoiceEmail { SalesInvoiceHeaderId = invoiceId, ToEmail = "x@example.com", Subject = "old", SentAt = DateTime.UtcNow.AddHours(-2) });
                    for (var i = 0; i < 29; i++)
                        db.SalesInvoiceEmails.Add(new SalesInvoiceEmail { SalesInvoiceHeaderId = invoiceId, ToEmail = "x@example.com", Subject = "recent", SentAt = DateTime.UtcNow.AddMinutes(-5) });
                    db.SaveChanges();
                }
            }

            var path = $"/api/Sales/SendSalesInvoiceEmail?id={invoiceId}";
            Assert.True((await Send(HttpMethod.Post, path)).IsSuccessStatusCode);
            var count = _emails.Messages.Count;
            Assert.Equal((HttpStatusCode)429, (await Send(HttpMethod.Post, path)).StatusCode);
            Assert.Equal(count, _emails.Messages.Count);
        }

        [Fact]
        public async Task SendSalesInvoiceEmail_RequiresCustomerEmail()
        {
            var invoice = await NewInvoice();
            invoice.Status = "Open";
            var saved = await Send(HttpMethod.Post, "/api/Sales/SaveSalesInvoice", invoice);
            Assert.True(saved.IsSuccessStatusCode, await saved.Content.ReadAsStringAsync());
            var invoiceId = (await saved.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();

            var response = await Send(HttpMethod.Post, $"/api/Sales/SendSalesInvoiceEmail?id={invoiceId}");
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("no valid email", await response.Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task SendSalesInvoiceFollowUpEmail_LinksPdf_BlindCopiesSender_OnlyForUnpaidIssued()
        {
            var invoice = await NewInvoice("billing@customer.example");
            invoice.Status = "Draft";
            var saved = await Send(HttpMethod.Post, "/api/Sales/SaveSalesInvoice", invoice);
            Assert.True(saved.IsSuccessStatusCode, await saved.Content.ReadAsStringAsync());
            var invoiceId = (await saved.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
            var path = $"/api/Sales/SendSalesInvoiceFollowUpEmail?id={invoiceId}";

            Assert.Equal(HttpStatusCode.BadRequest, (await Send(HttpMethod.Post, path)).StatusCode);
            Assert.True((await Send(HttpMethod.Post, $"/api/Sales/IssueSalesInvoice?id={invoiceId}")).IsSuccessStatusCode);

            var sent = await Send(HttpMethod.Post, path);
            Assert.True(sent.IsSuccessStatusCode, await sent.Content.ReadAsStringAsync());
            var message = _emails.Messages.Last().Raw;
            Assert.Equal("billing@customer.example", message.To);
            Assert.Equal(new[] { "phase3-owner@example.com" }, message.Bcc);
            Assert.StartsWith("Reminder: Invoice ", message.Subject);
            Assert.Empty(message.Attachments);
            Assert.Contains("View invoice", message.Html);
            Assert.Contains("/api/public/invoices/pdf?", message.Html);

            var reloaded = await (await Send(HttpMethod.Get, $"/api/Sales/SalesInvoice?id={invoiceId}")).Content.ReadFromJsonAsync<JsonElement>();
            var emails = reloaded.GetProperty("emails");
            Assert.Equal(1, emails.GetArrayLength());
            Assert.Equal("FollowUp", emails[0].GetProperty("kind").GetString());
            Assert.Equal("billing@customer.example", emails[0].GetProperty("to").GetString());
        }

        [Fact]
        public async Task SendSalesInvoiceEmail_SupportsUpToTwoAdditionalRecipients_CappedAtThreeTotal()
        {
            var invoice = await NewInvoice("billing@customer.example");
            invoice.Status = "Open";
            var saved = await Send(HttpMethod.Post, "/api/Sales/SaveSalesInvoice", invoice);
            Assert.True(saved.IsSuccessStatusCode, await saved.Content.ReadAsStringAsync());
            var invoiceId = (await saved.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();

            // > 2 additional recipients (total > 3) is rejected
            var tooMany = await Send(HttpMethod.Post, $"/api/Sales/SendSalesInvoiceEmail?id={invoiceId}&additionalRecipients=one@example.com&additionalRecipients=two@example.com&additionalRecipients=three@example.com");
            Assert.Equal(HttpStatusCode.BadRequest, tooMany.StatusCode);
            Assert.Contains("maximum of 3 recipients", await tooMany.Content.ReadAsStringAsync());

            // Invalid email is rejected
            var invalid = await Send(HttpMethod.Post, $"/api/Sales/SendSalesInvoiceEmail?id={invoiceId}&additionalRecipients=not-an-email");
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            Assert.Contains("not a valid email", await invalid.Content.ReadAsStringAsync());

            // Valid 2 additional recipients (total 3) succeeds and is dispatched on demand
            var path = $"/api/Sales/SendSalesInvoiceEmail?id={invoiceId}&additionalRecipients=ap@customer.example&additionalRecipients=cfo@customer.example";
            var sent = await Send(HttpMethod.Post, path);
            Assert.True(sent.IsSuccessStatusCode, await sent.Content.ReadAsStringAsync());

            var message = _emails.Messages.Last().Raw;
            Assert.Equal("billing@customer.example", message.To);
            Assert.Equal(new[] { "ap@customer.example", "cfo@customer.example" }, message.AdditionalTo);

            // Resend payload builder includes all 3
            var payload = ResendEmailSender.BuildPayload("billing@accountgo.dev", message);
            Assert.Contains("billing@customer.example", payload);
            Assert.Contains("ap@customer.example", payload);
            Assert.Contains("cfo@customer.example", payload);

            // Database invoice recipient email is NOT changed (remains on-demand only)
            var reloaded = await (await Send(HttpMethod.Get, $"/api/Sales/SalesInvoice?id={invoiceId}")).Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("billing@customer.example", reloaded.GetProperty("recipientEmail").GetString());
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
        public async Task BusinessProfileAndInvoiceSettings_SyncToD1_ReplaysToTheSameRow()
        {
            // A copy of the database stands in for D1; every response's sync SQL is replayed on it like the worker does.
            var mirror = _path + ".mirror";
            using (var source = new SqliteConnection("Data Source=" + _path))
            {
                source.Open();
                using var vacuum = source.CreateCommand();
                vacuum.CommandText = $"VACUUM INTO '{mirror}'";
                vacuum.ExecuteNonQuery();
            }

            try
            {
                var saves = new (string Path, object Body)[]
                {
                    (Current + "/business-profile", new { legalName = "Marvin Perez", address = new { line1 = "1 First St", city = "Makati" } }),
                    // Replacing the address: EF deletes the old owned instance and adds a new one.
                    (Current + "/business-profile", new { legalName = "Marvin Perez", businessEmail = "m@example.com",
                        address = new { line1 = "2 Second St", city = "Makati", postalCode = "1200", countryCode = "Philippines" } }),
                    (Current + "/sales-invoice-settings", new { sellerName = "LegalName", showNotes = true, showPaymentInstructions = true }),
                };
                foreach (var (path, body) in saves)
                {
                    var response = await Send(HttpMethod.Put, path, body);
                    Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
                    ReplayOnMirror(mirror, AllSyncStatements(response));
                }
                Assert.Equal(CompanySettingRow(_path), CompanySettingRow(mirror));
                Assert.Contains("2 Second St", CompanySettingRow(mirror));

                var cleared = await Send(HttpMethod.Put, Current + "/business-profile", new { legalName = "Marvin Perez" });
                Assert.True(cleared.IsSuccessStatusCode);
                ReplayOnMirror(mirror, AllSyncStatements(cleared));
                Assert.Equal(CompanySettingRow(_path), CompanySettingRow(mirror));
                Assert.DoesNotContain("2 Second St", CompanySettingRow(mirror));
                Assert.Contains("USD", CompanySettingRow(mirror));
            }
            finally
            {
                SqliteConnection.ClearAllPools();
                if (File.Exists(mirror)) File.Delete(mirror);
            }
        }

        private static void ReplayOnMirror(string mirror, List<string> statements)
        {
            using var connection = new SqliteConnection("Data Source=" + mirror);
            connection.Open();
            using var tx = connection.BeginTransaction();
            foreach (var sql in new[] { "PRAGMA defer_foreign_keys = on" }.Concat(statements))
            {
                using var command = connection.CreateCommand();
                command.Transaction = tx;
                command.CommandText = sql;
                command.ExecuteNonQuery();
            }
            tx.Commit();
        }

        private string CompanySettingRow(string path)
        {
            using var connection = new SqliteConnection("Data Source=" + path);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT * FROM \"CompanySetting\" WHERE \"CompanyId\" = " + _companyId;
            using var reader = command.ExecuteReader();
            Assert.True(reader.Read(), "CompanySetting row missing in " + path);
            return string.Join("|", Enumerable.Range(0, reader.FieldCount).Select(i => reader.GetName(i) + "=" + (reader.IsDBNull(i) ? "NULL" : reader.GetValue(i))));
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
            // A draft follows the current business details; issued invoices are covered below.
            invoice.Status = "Draft";
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
        public async Task IssuedInvoice_KeepsTheSellerItWasIssuedWith()
        {
            var profile = (string address) => new { legalName = "Juan Dela Cruz", businessEmail = "juan@example.com", address = new { line1 = address, city = "Makati" } };
            Assert.True((await Send(HttpMethod.Put, Current + "/business-profile", profile("1 Old St"))).IsSuccessStatusCode);
            Assert.True((await Send(HttpMethod.Put, Current + "/sales-invoice-settings", new { sellerName = "LegalName" })).IsSuccessStatusCode);

            var issuedDto = await NewInvoice();
            var issuedResponse = await Send(HttpMethod.Post, "/api/Sales/SaveSalesInvoice", issuedDto);
            Assert.True(issuedResponse.IsSuccessStatusCode, await issuedResponse.Content.ReadAsStringAsync());
            Assert.Contains(AllSyncStatements(issuedResponse), sql => sql.Contains("SellerJson") && sql.Contains("1 Old St"));
            var issuedId = (await issuedResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();

            var draftDto = await NewInvoice();
            draftDto.Status = "Draft";
            var draftResponse = await Send(HttpMethod.Post, "/api/Sales/SaveSalesInvoice", draftDto);
            Assert.True(draftResponse.IsSuccessStatusCode, await draftResponse.Content.ReadAsStringAsync());
            var draftId = (await draftResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();

            // The business moves and switches back to its trading name.
            Assert.True((await Send(HttpMethod.Put, Current + "/business-profile", profile("9 New St"))).IsSuccessStatusCode);
            Assert.True((await Send(HttpMethod.Put, Current + "/sales-invoice-settings", new { sellerName = "TradingName" })).IsSuccessStatusCode);

            var issued = await GetInvoice(issuedId);
            Assert.Equal("Juan Dela Cruz", issued.Seller.Name);
            Assert.Equal(new[] { "1 Old St", "Makati" }, issued.Seller.AddressLines);
            var draft = await GetInvoice(draftId);
            Assert.Equal("Phase Three", draft.Seller.Name);
            Assert.Equal(new[] { "9 New St", "Makati" }, draft.Seller.AddressLines);

            // Editing an issued invoice is rejected.
            var edit = await GetInvoice(issuedId);
            edit.ReferenceNo = "PO-1";
            var edited = await Send(HttpMethod.Post, "/api/Sales/SaveSalesInvoice", edit);
            Assert.Equal(HttpStatusCode.BadRequest, edited.StatusCode);
            Assert.Equal(new[] { "1 Old St", "Makati" }, (await GetInvoice(issuedId)).Seller.AddressLines);

            // A draft records the seller when it is issued, not when it was drafted.
            Assert.True((await Send(HttpMethod.Post, $"/api/Sales/IssueSalesInvoice?id={draftId}")).IsSuccessStatusCode);
            Assert.True((await Send(HttpMethod.Put, Current + "/business-profile", profile("5 Later St"))).IsSuccessStatusCode);
            Assert.Equal(new[] { "9 New St", "Makati" }, (await GetInvoice(draftId)).Seller.AddressLines);

            // Show/hide options stay workspace-wide, including on issued invoices.
            Assert.True((await Send(HttpMethod.Put, Current + "/sales-invoice-settings", new { showSellerAddress = false })).IsSuccessStatusCode);
            Assert.Empty((await GetInvoice(issuedId)).Seller.AddressLines);

            var pdf = await Send(HttpMethod.Get, $"/api/Sales/SalesInvoicePdf?id={issuedId}");
            Assert.Equal(HttpStatusCode.OK, pdf.StatusCode);
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

        private int RoleId(string name)
        {
            using (var scope = _factory.Services.CreateScope())
                return scope.ServiceProvider.GetRequiredService<ApiDbContext>().SecurityRoles.Single(r => r.Name == name).Id;
        }

        private int MemberId(string userId)
        {
            using (var scope = _factory.Services.CreateScope())
                return scope.ServiceProvider.GetRequiredService<ApiDbContext>().CompanyMembers.Single(m => m.UserId == userId && m.CompanyId == _companyId).Id;
        }

        [Fact]
        public async Task IssuedInvoice_KeepsTheCustomerAndLineTextItWasIssuedWith()
        {
            var issuedDto = await NewInvoice("old@example.com");
            await EditCustomer(issuedDto.CustomerId, "Old Name Ltd", "old@example.com", "1 Old St");
            // A catalog line keeps the text typed over it, not just the item's description.
            issuedDto.SalesInvoiceLines[0].ItemDescription = "Consulting, March";
            var issuedResponse = await Send(HttpMethod.Post, "/api/Sales/SaveSalesInvoice", issuedDto);
            Assert.True(issuedResponse.IsSuccessStatusCode, await issuedResponse.Content.ReadAsStringAsync());
            Assert.Contains(AllSyncStatements(issuedResponse), sql => sql.Contains("BillToJson") && sql.Contains("Old Name Ltd"));
            var issuedId = (await Json(issuedResponse)).GetProperty("id").GetInt32();

            // A draft for the same customer, its line left blank: it takes the item's description as it is now.
            var draftDto = new Dto.Sales.SalesInvoice
            {
                CustomerId = issuedDto.CustomerId, InvoiceDate = DateTime.UtcNow, Status = "Draft",
                SalesInvoiceLines = new List<Dto.Sales.SalesInvoiceLine> { new Dto.Sales.SalesInvoiceLine
                {
                    ItemId = issuedDto.SalesInvoiceLines[0].ItemId, Quantity = 1, Amount = 100, MeasurementId = issuedDto.SalesInvoiceLines[0].MeasurementId
                } }
            };
            var draftResponse = await Send(HttpMethod.Post, "/api/Sales/SaveSalesInvoice", draftDto);
            Assert.True(draftResponse.IsSuccessStatusCode, await draftResponse.Content.ReadAsStringAsync());
            var draftId = (await Json(draftResponse)).GetProperty("id").GetInt32();

            await EditCustomer(issuedDto.CustomerId, "New Name Inc", "new@example.com", "9 New St");
            await EditItem(issuedDto.SalesInvoiceLines[0].ItemId.Value, "Advisory");

            var issued = await GetInvoice(issuedId);
            Assert.Equal("Old Name Ltd", issued.CustomerName);
            Assert.Equal("old@example.com", issued.CustomerEmail);
            Assert.Equal("1 Old St, Makati", issued.CustomerAddress);
            Assert.Equal("Consulting, March", issued.SalesInvoiceLines.Single().ItemDescription);
            // Emails go to the address on file now.
            Assert.Equal("new@example.com", issued.RecipientEmail);
            var listed = (await (await Send(HttpMethod.Get, "/api/Sales/SalesInvoices")).Content.ReadFromJsonAsync<List<Dto.Sales.SalesInvoice>>())
                .Single(i => i.Id == issuedId);
            Assert.Equal("Consulting, March", listed.SalesInvoiceLines.Single().ItemDescription);

            // A draft follows the customer until it is issued; its line text is what was saved.
            var draft = await GetInvoice(draftId);
            Assert.Equal("New Name Inc", draft.CustomerName);
            Assert.Equal("9 New St, Makati", draft.CustomerAddress);
            Assert.Equal("Consulting", draft.SalesInvoiceLines.Single().ItemDescription);

            Assert.True((await Send(HttpMethod.Post, $"/api/Sales/IssueSalesInvoice?id={draftId}")).IsSuccessStatusCode);
            await EditCustomer(issuedDto.CustomerId, "Newest Name Co", "newest@example.com", "5 Later St");
            draft = await GetInvoice(draftId);
            Assert.Equal("New Name Inc", draft.CustomerName);
            Assert.Equal("new@example.com", draft.CustomerEmail);

            // Editing an issued invoice is rejected.
            var edit = await GetInvoice(issuedId);
            edit.ReferenceNo = "PO-1";
            var editRes = await Send(HttpMethod.Post, "/api/Sales/SaveSalesInvoice", edit);
            Assert.Equal(HttpStatusCode.BadRequest, editRes.StatusCode);
            Assert.Equal("Old Name Ltd", (await GetInvoice(issuedId)).CustomerName);
        }

        [Fact]
        public async Task InvoiceHistoryMigration_BackfillsIssuedInvoices_AndLineText()
        {
            var issuedDto = await NewInvoice("billing@example.com");
            await EditCustomer(issuedDto.CustomerId, "Backfilled Co", "billing@example.com", null);
            var issuedResponse = await Send(HttpMethod.Post, "/api/Sales/SaveSalesInvoice", issuedDto);
            Assert.True(issuedResponse.IsSuccessStatusCode, await issuedResponse.Content.ReadAsStringAsync());
            var issuedId = (await Json(issuedResponse)).GetProperty("id").GetInt32();
            var draftId = await SaveDraft();

            using (var source = new SqliteConnection("Data Source=" + _path))
            using (var replica = new SqliteConnection("Data Source=:memory:"))
            {
                source.Open();
                replica.Open();
                source.BackupDatabase(replica);
                using (var command = replica.CreateCommand())
                {
                    // D1 as it is before the migration.
                    command.CommandText = @"ALTER TABLE ""SalesInvoiceLine"" DROP COLUMN ""Description"";
                                            ALTER TABLE ""SalesInvoiceHeader"" DROP COLUMN ""BillToJson"";";
                    command.ExecuteNonQuery();
                    command.CommandText = File.ReadAllText(Path.Combine(RepoRoot(), "src/Api/d1/migrations/0009_invoice_history.sql"));
                    command.ExecuteNonQuery();

                    command.CommandText = $@"SELECT ""BillToJson"" FROM ""SalesInvoiceHeader"" WHERE ""Id"" = {issuedId}";
                    var billTo = InvoiceBillTo.FromJson(command.ExecuteScalar() as string);
                    Assert.NotNull(billTo);
                    Assert.Equal("Backfilled Co", billTo.Name);
                    Assert.Equal("billing@example.com", billTo.Email);
                    Assert.Empty(billTo.AddressLines);

                    command.CommandText = $@"SELECT ""BillToJson"" FROM ""SalesInvoiceHeader"" WHERE ""Id"" = {draftId}";
                    Assert.IsType<DBNull>(command.ExecuteScalar());

                    command.CommandText = $@"SELECT ""Description"" FROM ""SalesInvoiceLine"" WHERE ""SalesInvoiceHeaderId"" = {issuedId}";
                    Assert.Equal("Consulting", command.ExecuteScalar());
                }
            }
        }

        [Fact]
        public void InvoiceBillTo_ReadsTheBackfillFormat_AndIsCapturedOnlyWhenIssued()
        {
            var backfilled = InvoiceBillTo.FromJson(@"{""name"":""Acme"",""email"":null,""phone"":null,""addressLines"":[""1 Main St"",null,"" "",""PH""]}");
            Assert.Equal(new[] { "1 Main St", "PH" }, backfilled.AddressLines);
            Assert.Equal("1 Main St, PH", backfilled.AddressText);
            Assert.DoesNotContain("addressText", backfilled.ToJson());
            Assert.Null(InvoiceBillTo.FromJson("{not json"));

            var party = new Party { Name = "Acme", CompanyStreet = "1 Main St" };
            foreach (var (status, captured) in new (SalesInvoiceStatus?, bool)[]
                { (SalesInvoiceStatus.Draft, false), (SalesInvoiceStatus.Void, false), (null, false), (SalesInvoiceStatus.Open, true) })
            {
                var invoice = new SalesInvoiceHeader { Status = status };
                invoice.CaptureBillToIfIssued(party);
                Assert.Equal(captured, invoice.BillToJson != null);
            }

            var once = new SalesInvoiceHeader { Status = SalesInvoiceStatus.Open };
            once.CaptureBillToIfIssued(party);
            once.CaptureBillToIfIssued(new Party { Name = "Renamed" });
            Assert.Equal("Acme", InvoiceBillTo.FromJson(once.BillToJson).Name);
        }

        [Fact]
        public void InvoiceBillTo_FromParty_UsesBillToContactAttentionAddressAndFallbacks()
        {
            var company = new Party
            {
                Name = "Acme Corp",
                Email = "billing@acme.test",
                Phone = "+1 555 0100",
                CompanyStreet = "1 Main St",
                CompanyProvince = "Makati",
                CompanyPostalCode = "1226",
                CompanyCountry = "PH",
            };
            var contact = new Contact
            {
                Id = 42,
                FirstName = "Jane",
                LastName = "Doe",
                Party = new Party
                {
                    Email = "jane@acme.test",
                    Phone = null,
                    CompanyStreet = "9 Contact Ave",
                    CompanyProvince = "Quezon City",
                    CompanyPostalCode = "1100",
                    CompanyCountry = "PH",
                },
            };

            var withContact = InvoiceBillTo.FromParty(company, contact);
            Assert.Equal("Acme Corp", withContact.Name);
            Assert.Equal("Attn: Jane Doe", withContact.Attention);
            Assert.Equal(42, withContact.ContactId);
            Assert.Equal("jane@acme.test", withContact.Email);
            Assert.Equal("+1 555 0100", withContact.Phone);
            Assert.Equal(new[] { "9 Contact Ave", "Quezon City", "1100", "PH" }, withContact.AddressLines);

            var contactWithoutAddress = new Contact
            {
                Id = 7,
                FirstName = "Bob",
                LastName = "Smith",
                Party = new Party { Email = null, Phone = "+1 555 0199" },
            };
            var fallback = InvoiceBillTo.FromParty(company, contactWithoutAddress);
            Assert.Equal("Attn: Bob Smith", fallback.Attention);
            Assert.Equal("billing@acme.test", fallback.Email);
            Assert.Equal("+1 555 0199", fallback.Phone);
            Assert.Equal(new[] { "1 Main St", "Makati", "1226", "PH" }, fallback.AddressLines);

            var companyOnly = InvoiceBillTo.FromParty(company);
            Assert.Null(companyOnly.Attention);
            Assert.Null(companyOnly.ContactId);
            Assert.Equal("billing@acme.test", companyOnly.Email);

            var roundTrip = InvoiceBillTo.FromJson(withContact.ToJson());
            Assert.Equal("Attn: Jane Doe", roundTrip!.Attention);
            Assert.Equal(42, roundTrip.ContactId);

            var issued = new SalesInvoiceHeader { Status = SalesInvoiceStatus.Open };
            issued.CaptureBillToIfIssued(company, contact);
            Assert.Equal("Attn: Jane Doe", InvoiceBillTo.FromJson(issued.BillToJson)!.Attention);
        }

        [Fact]
        public async Task ClientContacts_BillToRole_AndIssuedInvoiceSnapshot()
        {
            var client = await SaveClient(new Dto.Sales.Client
            {
                Name = "Acme Corp",
                Email = "billing@acme.test",
                Street = "1 Main St",
                City = "Makati",
                Country = "PH",
            });
            var loaded = await GetClient(client.Id);
            Assert.Null(loaded.BillToContactId);
            Assert.Contains(loaded.Contacts, c => c.IsPrimary);

            var saved = await (await Send(HttpMethod.Post, $"/api/Sales/ClientContact?clientId={client.Id}", new Dto.Common.ContactPersonDto
            {
                FirstName = "Jane",
                LastName = "Doe",
                Email = "jane@acme.test",
                Street = "9 Contact Ave",
                City = "Quezon City",
                Country = "PH",
                IsBillTo = true,
            })).Content.ReadFromJsonAsync<Dto.Sales.Client>();
            Assert.NotNull(saved.BillToContactId);
            Assert.Contains(saved.Contacts, c => c.IsBillTo && c.FirstName == "Jane" && c.Email == "jane@acme.test");

            var invoice = await NewInvoice();
            invoice.CustomerId = client.Id;
            var issued = await Send(HttpMethod.Post, "/api/Sales/SaveSalesInvoice", invoice);
            Assert.True(issued.IsSuccessStatusCode, await issued.Content.ReadAsStringAsync());
            var invoiceId = (await Json(issued)).GetProperty("id").GetInt32();
            var printed = await GetInvoice(invoiceId);
            Assert.Equal("Acme Corp", printed.CustomerName);
            Assert.Equal("Attn: Jane Doe", printed.CustomerAttention);
            Assert.Equal("jane@acme.test", printed.CustomerEmail);
            Assert.Contains("9 Contact Ave", printed.CustomerAddress);
            Assert.Equal("jane@acme.test", printed.RecipientEmail);

            var other = await (await Send(HttpMethod.Post, $"/api/Sales/ClientContact?clientId={client.Id}", new Dto.Common.ContactPersonDto
            {
                FirstName = "Bob",
                LastName = "Smith",
                Email = "bob@acme.test",
            })).Content.ReadFromJsonAsync<Dto.Sales.Client>();
            var bobId = other.Contacts.Single(c => c.FirstName == "Bob").Id;
            var switched = await (await Send(HttpMethod.Post, $"/api/Sales/SetClientBillToContact?clientId={client.Id}&contactId={bobId}"))
                .Content.ReadFromJsonAsync<Dto.Sales.Client>();
            Assert.Equal(bobId, switched.BillToContactId);
            Assert.Equal("jane@acme.test", (await GetInvoice(invoiceId)).CustomerEmail);

            var janeId = switched.Contacts.Single(c => c.FirstName == "Jane").Id;
            var afterDelete = await (await Send(HttpMethod.Delete, $"/api/Sales/DeleteClientContact?clientId={client.Id}&contactId={janeId}"))
                .Content.ReadFromJsonAsync<Dto.Sales.Client>();
            Assert.DoesNotContain(afterDelete.Contacts, c => c.Id == janeId);
            Assert.Equal(bobId, afterDelete.BillToContactId);

            var bob = afterDelete.Contacts.Single(c => c.Id == bobId);
            var cleared = await (await Send(HttpMethod.Post, $"/api/Sales/ClientContact?clientId={client.Id}", new Dto.Common.ContactPersonDto
            {
                Id = bob.Id,
                FirstName = bob.FirstName,
                LastName = bob.LastName,
                Email = bob.Email,
                IsBillTo = false,
                IsPrimary = bob.IsPrimary,
            })).Content.ReadFromJsonAsync<Dto.Sales.Client>();
            Assert.Null(cleared.BillToContactId);
            Assert.DoesNotContain(cleared.Contacts, c => c.IsBillTo);

            // Issued invoice stays sendable from frozen bill-to email after company email is cleared.
            await EditCustomer(client.Id, "Acme Corp", email: null, street: "1 Main St");
            Assert.Equal("jane@acme.test", (await GetInvoice(invoiceId)).RecipientEmail);
        }

        [Fact]
        public async Task Clients_CreateEditAndArchive_KeepIssuedInvoices()
        {
            var created = await SaveClient(new Dto.Sales.Client
            {
                Name = "  Acme Corp ", Email = "billing@acme.test", Phone = "+63 2 8000 0000",
                Street = "1 Ayala Ave", City = "Makati", PostalCode = "1226", Country = "Philippines",
                PaymentTermId = await FirstPaymentTermId()
            });
            Assert.True(created.Id > 0);
            Assert.Equal("Acme Corp", created.Name);
            Assert.True(created.CanDelete);
            Assert.Contains(await Clients(), c => c.Id == created.Id && c.City == "Makati" && !c.Archived);

            // The invoice picker gets the client's default terms.
            var picker = await (await Send(HttpMethod.Get, "/api/Sales/Customers")).Content.ReadFromJsonAsync<List<Dto.Sales.Customer>>();
            Assert.Equal(created.PaymentTermId, picker.Single(c => c.Id == created.Id).PaymentTermId);

            // Issue an invoice to it, then edit the client: the invoice keeps its bill-to, and the client can't be deleted.
            var invoice = await NewInvoice();
            invoice.CustomerId = created.Id;
            var issued = await Send(HttpMethod.Post, "/api/Sales/SaveSalesInvoice", invoice);
            Assert.True(issued.IsSuccessStatusCode, await issued.Content.ReadAsStringAsync());
            var invoiceId = (await Json(issued)).GetProperty("id").GetInt32();

            created.Name = "Acme Corporation";
            created.Email = "";
            created.Street = null;
            created.PaymentTermId = null;
            var edited = await SaveClient(created);
            Assert.Equal("Acme Corporation", edited.Name);
            Assert.Null(edited.Email);
            Assert.Null(edited.Street);
            Assert.Null(edited.PaymentTermId);
            Assert.False(edited.CanDelete);
            Assert.Equal("Acme Corporation", (await GetClient(created.Id)).Name);
            var printed = await GetInvoice(invoiceId);
            Assert.Equal("Acme Corp", printed.CustomerName);
            Assert.Equal("billing@acme.test", printed.CustomerEmail);
            Assert.Null(printed.RecipientEmail);

            // Archived: still listed and still on its invoices, flagged for the picker; restoring clears it.
            var archived = await Send(HttpMethod.Post, $"/api/Sales/ArchiveClient?id={created.Id}");
            Assert.True(archived.IsSuccessStatusCode, await archived.Content.ReadAsStringAsync());
            Assert.True((await GetClient(created.Id)).Archived);
            picker = await (await Send(HttpMethod.Get, "/api/Sales/Customers")).Content.ReadFromJsonAsync<List<Dto.Sales.Customer>>();
            Assert.True(picker.Single(c => c.Id == created.Id).Archived);
            Assert.Equal("Acme Corp", (await GetInvoice(invoiceId)).CustomerName);
            Assert.True((await Send(HttpMethod.Post, $"/api/Sales/ArchiveClient?id={created.Id}&archived=false")).IsSuccessStatusCode);
            Assert.False((await GetClient(created.Id)).Archived);

            var refused = await Send(HttpMethod.Delete, $"/api/Sales/DeleteClient?id={created.Id}");
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
            Assert.Contains("Archive it instead", await refused.Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task Clients_ValidateInput_AndNeedManagePermission()
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await Send(HttpMethod.Post, "/api/Sales/SaveClient", new Dto.Sales.Client { Name = "  " })).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await Send(HttpMethod.Post, "/api/Sales/SaveClient", new Dto.Sales.Client { Name = "Acme", Email = "not-an-email" })).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await Send(HttpMethod.Post, "/api/Sales/SaveClient", new Dto.Sales.Client { Name = new string('x', 201) })).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await Send(HttpMethod.Get, "/api/Sales/Client?id=999999")).StatusCode);

            var client = await SaveClient(new Dto.Sales.Client { Name = "Members can't edit" });
            await JoinOther();
            client.Name = "Hijacked";
            Assert.Equal(HttpStatusCode.Forbidden, (await Send(HttpMethod.Post, "/api/Sales/SaveClient", client, _otherToken)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await Send(HttpMethod.Post, $"/api/Sales/ArchiveClient?id={client.Id}", token: _otherToken)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await Send(HttpMethod.Delete, $"/api/Sales/DeleteClient?id={client.Id}", token: _otherToken)).StatusCode);
            Assert.Equal("Members can't edit", (await GetClient(client.Id)).Name);
        }

        [Fact]
        public async Task DeleteClient_RemovesAnUnusedClient_InForeignKeyOrder()
        {
            var client = await SaveClient(new Dto.Sales.Client { Name = "Added by mistake", Email = "oops@example.com" });
            var deleted = await Send(HttpMethod.Delete, $"/api/Sales/DeleteClient?id={client.Id}");
            Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await Send(HttpMethod.Get, $"/api/Sales/Client?id={client.Id}")).StatusCode);

            // D1 replays the statements in order and enforces foreign keys: the customer goes before its parties.
            var statements = AllSyncStatements(deleted);
            var customer = statements.FindIndex(sql => sql.StartsWith("DELETE FROM \"Customer\""));
            var contact = statements.FindIndex(sql => sql.StartsWith("DELETE FROM \"Contact\""));
            var party = statements.FindIndex(sql => sql.StartsWith("DELETE FROM \"Party\""));
            Assert.True(customer >= 0 && contact > customer && party > contact, string.Join("\n", statements));
            Assert.Equal(2, statements.Count(sql => sql.StartsWith("DELETE FROM \"Party\"")));

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            using (scope.ServiceProvider.GetRequiredService<ITenantContext>().Impersonate(_companyId, _slug, CompanyMemberRole.Owner))
                Assert.DoesNotContain(db.Parties, p => p.Name == "Added by mistake");
        }

        [Fact]
        public async Task IssuingAndVoiding_NeedThePostPermission_DraftingDoesNot()
        {
            // A sales representative drafts invoices and records receipts, but doesn't issue or void them.
            await JoinOther();
            Assert.Equal(HttpStatusCode.NoContent, (await Send(HttpMethod.Patch, Current + "/members/" + MemberId(_otherId),
                new { securityRoleIds = new[] { RoleId("Sales Representative") } })).StatusCode);

            var invoice = await NewInvoice();
            invoice.Status = "Open";
            var issuedOnSave = await Send(HttpMethod.Post, "/api/Sales/SaveSalesInvoice", invoice, _otherToken);
            Assert.Equal(HttpStatusCode.Forbidden, issuedOnSave.StatusCode);
            Assert.Contains("sales.invoices.post", await issuedOnSave.Content.ReadAsStringAsync());

            invoice.Status = "Draft";
            var drafted = await Send(HttpMethod.Post, "/api/Sales/SaveSalesInvoice", invoice, _otherToken);
            Assert.True(drafted.IsSuccessStatusCode, await drafted.Content.ReadAsStringAsync());
            var draftId = (await Json(drafted)).GetProperty("id").GetInt32();

            Assert.Equal(HttpStatusCode.Forbidden, (await Send(HttpMethod.Post, $"/api/Sales/IssueSalesInvoice?id={draftId}", token: _otherToken)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await Send(HttpMethod.Post, "/api/Sales/PostSalesInvoice", await GetInvoice(draftId), _otherToken)).StatusCode);
            Assert.Equal("Draft", (await GetInvoice(draftId)).Status);

            // The owner issues it; the representative records a payment but can't void.
            await Issue(draftId);
            Assert.Equal(HttpStatusCode.Forbidden, (await Send(HttpMethod.Post, $"/api/Sales/VoidSalesInvoice?id={draftId}", token: _otherToken)).StatusCode);
            var paid = await Send(HttpMethod.Post, "/api/Sales/RecordPayment",
                new Dto.Sales.RecordInvoicePaymentDto { InvoiceId = draftId, Amount = 10, PaymentDate = DateTime.UtcNow }, _otherToken);
            Assert.True(paid.IsSuccessStatusCode, await paid.Content.ReadAsStringAsync());
            Assert.Equal("Partially Paid", (await GetInvoice(draftId)).Status);

            // Without any sales role, a member can't record payments either.
            Assert.Equal(HttpStatusCode.NoContent, (await Send(HttpMethod.Patch, Current + "/members/" + MemberId(_otherId),
                new { securityRoleIds = new[] { ViewerRoleId() } })).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await Send(HttpMethod.Post, "/api/Sales/RecordPayment",
                new Dto.Sales.RecordInvoicePaymentDto { InvoiceId = draftId, Amount = 10, PaymentDate = DateTime.UtcNow }, _otherToken)).StatusCode);
        }

        [Fact]
        public async Task DeletingADraft_RemovesItsSalesOrder_SoTheClientCanBeDeleted()
        {
            var client = await SaveClient(new Dto.Sales.Client { Name = "Drafted once" });
            var invoice = await NewInvoice();
            invoice.CustomerId = client.Id;
            invoice.Status = "Draft";
            var saved = await Send(HttpMethod.Post, "/api/Sales/SaveSalesInvoice", invoice);
            Assert.True(saved.IsSuccessStatusCode, await saved.Content.ReadAsStringAsync());
            var draftId = (await Json(saved)).GetProperty("id").GetInt32();
            Assert.False((await GetClient(client.Id)).CanDelete);

            var deleted = await Send(HttpMethod.Delete, $"/api/Sales/DeleteSalesInvoice?id={draftId}");
            Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
            // D1 replays in order and enforces foreign keys: the invoice lines go before the order lines they point to,
            // and those before their order.
            var statements = AllSyncStatements(deleted);
            var invoiceLine = statements.FindIndex(sql => sql.StartsWith("DELETE FROM \"SalesInvoiceLine\""));
            var orderLine = statements.FindIndex(sql => sql.StartsWith("DELETE FROM \"SalesOrderLine\""));
            var order = statements.FindIndex(sql => sql.StartsWith("DELETE FROM \"SalesOrderHeader\""));
            Assert.True(invoiceLine >= 0 && orderLine > invoiceLine && order > orderLine, string.Join("\n", statements));

            Assert.True((await GetClient(client.Id)).CanDelete);
            Assert.Equal(HttpStatusCode.NoContent, (await Send(HttpMethod.Delete, $"/api/Sales/DeleteClient?id={client.Id}")).StatusCode);
        }

        [Fact]
        public async Task ClientBalances_MatchTheInvoiceTotals()
        {
            var client = await SaveClient(new Dto.Sales.Client { Name = "Balances" });
            async Task<int> Invoice(string status, decimal amount, DateTime date, DateTime? due = null)
            {
                var dto = await NewInvoice();
                dto.CustomerId = client.Id;
                dto.Status = status;
                dto.InvoiceDate = date;
                dto.DueDate = due;
                dto.SalesInvoiceLines.Single().Amount = amount;
                var response = await Send(HttpMethod.Post, "/api/Sales/SaveSalesInvoice", dto);
                Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
                return (await Json(response)).GetProperty("id").GetInt32();
            }

            var today = DateTime.UtcNow.Date;
            var open = await Invoice("Open", 100, today);
            var overdue = await Invoice("Open", 40, today.AddDays(-40), today.AddDays(-10));
            await Invoice("Draft", 999, today.AddDays(1));
            var voided = await Invoice("Open", 500, today.AddDays(-5));
            Assert.True((await Send(HttpMethod.Post, $"/api/Sales/VoidSalesInvoice?id={voided}")).IsSuccessStatusCode);
            Assert.True((await Send(HttpMethod.Post, "/api/Sales/RecordPayment",
                new Dto.Sales.RecordInvoicePaymentDto { InvoiceId = open, Amount = 30, PaymentDate = DateTime.UtcNow })).IsSuccessStatusCode);

            var invoices = await (await Send(HttpMethod.Get, $"/api/Sales/SalesInvoices?customerId={client.Id}")).Content.ReadFromJsonAsync<List<Dto.Sales.SalesInvoice>>();
            Assert.Equal(4, invoices.Count);
            Assert.All(invoices, i => Assert.Equal(client.Id, i.CustomerId));
            var expectedOutstanding = invoices.Where(i => i.Status is not ("Draft" or "Void")).Sum(i => i.BalanceDue);

            var fromList = (await Clients()).Single(c => c.Id == client.Id);
            var single = await GetClient(client.Id);
            foreach (var c in new[] { fromList, single })
            {
                Assert.Equal(expectedOutstanding, c.Outstanding);
                Assert.Equal(invoices.Single(i => i.Id == overdue).BalanceDue, c.Overdue);
                Assert.Equal(30, c.PaidToDate);
                Assert.Equal(today, c.LastInvoiceDate?.Date);
            }
            Assert.Equal(70 + 40, single.Outstanding);
        }

        private async Task<Dto.Inventory.CatalogItem> SaveItem(Dto.Inventory.CatalogItem item)
        {
            var response = await Send(HttpMethod.Post, "/api/Catalog/SaveItem", item);
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            return await response.Content.ReadFromJsonAsync<Dto.Inventory.CatalogItem>();
        }

        private async Task<Dto.Sales.Client> SaveClient(Dto.Sales.Client client)
        {
            var response = await Send(HttpMethod.Post, "/api/Sales/SaveClient", client);
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            return await response.Content.ReadFromJsonAsync<Dto.Sales.Client>();
        }

        private async Task<Dto.Sales.Client> GetClient(int id)
        {
            var response = await Send(HttpMethod.Get, $"/api/Sales/Client?id={id}");
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            return await response.Content.ReadFromJsonAsync<Dto.Sales.Client>();
        }

        private async Task<List<Dto.Sales.Client>> Clients() =>
            await (await Send(HttpMethod.Get, "/api/Sales/Clients")).Content.ReadFromJsonAsync<List<Dto.Sales.Client>>();

        private async Task<int> FirstPaymentTermId()
        {
            var terms = await Json(await Send(HttpMethod.Get, "/api/Common/PaymentTerms"));
            return terms[0].GetProperty("id").GetInt32();
        }

        private async Task EditCustomer(int customerId, string name, string email, string street)
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            using (scope.ServiceProvider.GetRequiredService<ITenantContext>().Impersonate(_companyId, _slug, CompanyMemberRole.Owner))
            {
                var party = db.Customers.Where(c => c.Id == customerId).Select(c => c.Party).Single();
                party.Name = name;
                party.Email = email;
                party.CompanyStreet = street;
                party.CompanyProvince = street == null ? null : "Makati";
                await db.SaveChangesAsync();
            }
        }

        private async Task EditItem(int itemId, string description)
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            using (scope.ServiceProvider.GetRequiredService<ITenantContext>().Impersonate(_companyId, _slug, CompanyMemberRole.Owner))
            {
                db.Items.Single(i => i.Id == itemId).Description = description;
                await db.SaveChangesAsync();
            }
        }

        private async Task<Dto.Sales.SalesInvoice> NewInvoice(string customerEmail = null)
        {
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
                using (scope.ServiceProvider.GetRequiredService<ITenantContext>().Impersonate(_companyId, _slug, CompanyMemberRole.Owner))
                {
                    var advancesAccount = db.Accounts.FirstOrDefault(a => a.AccountCode == "20120") ?? db.Accounts.First(a => a.AccountClassId == 2);
                    var customer = new Customer
                    {
                        No = Guid.NewGuid().ToString("N"), Party = new Party { Name = "Real customer", PartyType = PartyTypes.Customer, Email = customerEmail },
                        PrimaryContact = new Contact { FirstName = "Customer", LastName = "Contact", Party = new Party { Name = "Contact", PartyType = PartyTypes.Contact } },
                        AccountsReceivableAccount = db.Accounts.Single(a => a.AccountCode == "10120"),
                        SalesAccount = db.Accounts.Single(a => a.AccountCode == "40100"),
                        CustomerAdvancesAccount = advancesAccount,
                        CustomerAdvancesAccountId = advancesAccount.Id
                    };
                    var glSetting = db.GeneralLedgerSettings.FirstOrDefault();
                    if (glSetting != null && !glSetting.ShippingChargeAccountId.HasValue)
                    {
                        glSetting.ShippingChargeAccountId = customer.SalesAccount.Id;
                    }
                    var serviceCat = db.ItemCategories.FirstOrDefault(c => c.ItemType == ItemTypes.Service);
                    if (serviceCat == null)
                    {
                        serviceCat = new ItemCategory
                        {
                            Name = "Services",
                            ItemType = ItemTypes.Service,
                            MeasurementId = db.Measurements.First().Id,
                            SalesAccountId = customer.SalesAccount.Id
                        };
                        db.ItemCategories.Add(serviceCat);
                    }
                    var item = new Item { No = Guid.NewGuid().ToString("N"), Code = "CONSULTING", Description = "Consulting", Price = 100,
                        ItemCategory = serviceCat,
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
            public Task<string> SendAsync(EmailMessage message, CancellationToken ct = default(CancellationToken))
            {
                if (Fail) throw new HttpRequestException("Simulated email delivery failure");
                Messages.Add(new Message { To = message.To, Subject = message.Subject, Html = message.Html, Raw = message });
                return Task.FromResult("fake-" + Messages.Count);
            }
        }

        private class Message
        {
            public string To { get; set; }
            public string Subject { get; set; }
            public string Html { get; set; }
            public EmailMessage Raw { get; set; }
        }
    }
}
