#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Api.Data;
using Core.Domain;
using Core.Domain.Financials;
using Core.Domain.Sales;
using Xunit;

namespace AccountGo.BackendTests
{
    public partial class MultiTenancyPhase3Tests
    {
        [Fact]
        public async Task Catalog_SalesAccounts_ReturnsOnlyPostableRevenueAccounts()
        {
            var res = await Send(HttpMethod.Get, "/api/Catalog/SalesAccounts");
            Assert.True(res.IsSuccessStatusCode, await res.Content.ReadAsStringAsync());

            var accounts = await res.Content.ReadFromJsonAsync<List<SalesAccountOptionDto>>();
            Assert.NotNull(accounts);
            Assert.Contains(accounts, a => a.Code == "40100");
            Assert.Contains(accounts, a => a.Code == "40600");

            // Must NOT contain parent account 40000
            Assert.DoesNotContain(accounts, a => a.Code == "40000");
            // Must NOT contain assets (10800) or expenses (50200)
            Assert.DoesNotContain(accounts, a => a.Code == "10800");
            Assert.DoesNotContain(accounts, a => a.Code == "50200");
        }

        [Fact]
        public async Task Catalog_SaveItem_PersistsAndDefaultsSalesAccount()
        {
            // 1. Service item without specified sales account defaults to 40100
            var defaultItem = await SaveItem(new Dto.Inventory.CatalogItem
            {
                Name = "Default Sales Item",
                Price = 50m
            });
            Assert.Equal(AccountId("40100"), defaultItem.SalesAccountId);
            Assert.Equal("40100", defaultItem.SalesAccountCode);

            // Fetch single item
            var fetched = await (await Send(HttpMethod.Get, $"/api/Catalog/Item?id={defaultItem.Id}"))
                .Content.ReadFromJsonAsync<Dto.Inventory.CatalogItem>();
            Assert.Equal(AccountId("40100"), fetched.SalesAccountId);
            Assert.Equal("40100", fetched.SalesAccountCode);

            // 2. Service item with custom sales account 40600 (Other Income)
            var otherIncomeId = AccountId("40600");
            var customItem = await SaveItem(new Dto.Inventory.CatalogItem
            {
                Name = "Custom Sales Item",
                Price = 120m,
                SalesAccountId = otherIncomeId
            });
            Assert.Equal(otherIncomeId, customItem.SalesAccountId);
            Assert.Equal("40600", customItem.SalesAccountCode);

            var fetchedCustom = await (await Send(HttpMethod.Get, $"/api/Catalog/Item?id={customItem.Id}"))
                .Content.ReadFromJsonAsync<Dto.Inventory.CatalogItem>();
            Assert.Equal(otherIncomeId, fetchedCustom.SalesAccountId);
            Assert.Equal("40600", fetchedCustom.SalesAccountCode);

            // 3. Update item: changing sales account updates it
            fetchedCustom.SalesAccountId = AccountId("40100");
            var updated = await SaveItem(fetchedCustom);
            Assert.Equal(AccountId("40100"), updated.SalesAccountId);
            Assert.Equal("40100", updated.SalesAccountCode);
        }

        [Fact]
        public async Task Catalog_SaveItem_StockItem_CustomSalesAccount_DoesNotResetOnSubsequentSaves()
        {
            SetInventoryModuleEnabled(true);
            var otherIncomeId = AccountId("40600");
            var stockItem = await SaveItem(new Dto.Inventory.CatalogItem
            {
                Name = "Custom Sales Stock Item",
                Price = 75m,
                Cost = 25m,
                TrackStock = true,
                SalesAccountId = otherIncomeId
            });

            Assert.True(stockItem.TrackStock);
            Assert.Equal(AccountId("10800"), stockItem.InventoryAccountId);
            Assert.Equal(otherIncomeId, stockItem.SalesAccountId);
            Assert.Equal("40600", stockItem.SalesAccountCode);

            // Subsequent update without salesAccountId in DTO retains custom sales account
            var updateDto = new Dto.Inventory.CatalogItem
            {
                Id = stockItem.Id,
                Name = "Custom Sales Stock Item Updated",
                Price = 80m,
                Cost = 30m,
                TrackStock = true
            };
            var savedAgain = await SaveItem(updateDto);
            Assert.Equal(otherIncomeId, savedAgain.SalesAccountId);
            Assert.Equal("40600", savedAgain.SalesAccountCode);
            Assert.Equal(AccountId("10800"), savedAgain.InventoryAccountId);

            // Reload to verify DB state
            var reloaded = await (await Send(HttpMethod.Get, $"/api/Catalog/Item?id={stockItem.Id}"))
                .Content.ReadFromJsonAsync<Dto.Inventory.CatalogItem>();
            Assert.Equal(otherIncomeId, reloaded.SalesAccountId);
            Assert.Equal("40600", reloaded.SalesAccountCode);
        }

        [Fact]
        public async Task Catalog_SaveItem_RejectsNonIncomeAccounts_AndCrossTenant()
        {
            // Reject expense account (50200)
            var expenseRes = await Send(HttpMethod.Post, "/api/Catalog/SaveItem", new Dto.Inventory.CatalogItem
            {
                Name = "Expense Sales Item",
                Price = 10m,
                SalesAccountId = AccountId("50200")
            });
            Assert.Equal(HttpStatusCode.BadRequest, expenseRes.StatusCode);

            // Reject asset account (10120)
            var assetRes = await Send(HttpMethod.Post, "/api/Catalog/SaveItem", new Dto.Inventory.CatalogItem
            {
                Name = "Asset Sales Item",
                Price = 10m,
                SalesAccountId = AccountId("10120")
            });
            Assert.Equal(HttpStatusCode.BadRequest, assetRes.StatusCode);

            // Reject summary parent account (40000)
            var parentRes = await Send(HttpMethod.Post, "/api/Catalog/SaveItem", new Dto.Inventory.CatalogItem
            {
                Name = "Parent Revenue Item",
                Price = 10m,
                SalesAccountId = AccountId("40000")
            });
            Assert.Equal(HttpStatusCode.BadRequest, parentRes.StatusCode);

            // Cross tenant account reference rejection
            int otherTenantAccountId = 0;
            WithDb(db =>
            {
                var otherAcc = db.Accounts.FirstOrDefault(a => a.CompanyId != 1 && (a.AccountClassId == (int)AccountClasses.Revenue || a.AccountCode.StartsWith("4")));
                if (otherAcc != null)
                    otherTenantAccountId = otherAcc.Id;
            });

            if (otherTenantAccountId > 0)
            {
                var crossTenantRes = await Send(HttpMethod.Post, "/api/Catalog/SaveItem", new Dto.Inventory.CatalogItem
                {
                    Name = "Cross Tenant Item",
                    Price = 10m,
                    SalesAccountId = otherTenantAccountId
                });
                Assert.Equal(HttpStatusCode.BadRequest, crossTenantRes.StatusCode);
            }
        }

        [Fact]
        public async Task SalesInvoice_PostsToItemSalesAccount_WhenConfigured()
        {
            var otherIncomeId = AccountId("40600");
            var item = await SaveItem(new Dto.Inventory.CatalogItem
            {
                Name = "Consulting Other Income",
                Price = 200m,
                SalesAccountId = otherIncomeId
            });

            var invoice = await NewInvoice();
            invoice.Status = "Open";
            var line = invoice.SalesInvoiceLines.Single();
            line.ItemId = item.Id;
            line.ItemDescription = item.Name;
            line.Amount = 200m;
            line.Quantity = 1;
            line.Discount = 0;
            line.TaxId = null;

            var saveRes = await Send(HttpMethod.Post, "/api/Sales/SaveSalesInvoice", invoice);
            Assert.True(saveRes.IsSuccessStatusCode, await saveRes.Content.ReadAsStringAsync());
            var invoiceId = (await Json(saveRes)).GetProperty("id").GetInt32();

            int glHeaderId = 0;
            WithDb(db =>
            {
                var posted = db.SalesInvoiceHeaders.Single(i => i.Id == invoiceId);
                Assert.NotNull(posted.GeneralLedgerHeaderId);
                glHeaderId = posted.GeneralLedgerHeaderId.Value;
            });

            var entries = Ledger(glHeaderId);
            // Credit should hit 40600, NOT 40100
            Assert.Contains(entries, l => l.AccountId == otherIncomeId && l.DrCr == DrOrCrSide.Cr && l.Amount == 200m);
            Assert.DoesNotContain(entries, l => l.AccountId == AccountId("40100"));
        }

        [Fact]
        public async Task SalesInvoice_FreeTextLine_SalesAccountId_DraftSaveAndRead_PreservesAccount()
        {
            var otherIncomeId = AccountId("40600");
            var invoice = await NewInvoice();
            invoice.Status = "Draft";
            var line = invoice.SalesInvoiceLines.Single();
            line.ItemId = null;
            line.ItemDescription = "One-off Advisory Fee";
            line.Amount = 350m;
            line.Quantity = 1;
            line.Discount = 0;
            line.TaxId = null;
            line.SalesAccountId = otherIncomeId;

            var saveRes = await Send(HttpMethod.Post, "/api/Sales/SaveSalesInvoice", invoice);
            Assert.True(saveRes.IsSuccessStatusCode, await saveRes.Content.ReadAsStringAsync());
            var savedInvoice = await saveRes.Content.ReadFromJsonAsync<Dto.Sales.SalesInvoice>();
            Assert.NotNull(savedInvoice);
            var savedLine = savedInvoice.SalesInvoiceLines.Single();
            Assert.Equal(otherIncomeId, savedLine.SalesAccountId);
            Assert.Equal("40600", savedLine.SalesAccountCode);

            // Read back via GET
            var getRes = await Send(HttpMethod.Get, $"/api/Sales/SalesInvoice?id={savedInvoice.Id}");
            Assert.True(getRes.IsSuccessStatusCode, await getRes.Content.ReadAsStringAsync());
            var reloaded = await getRes.Content.ReadFromJsonAsync<Dto.Sales.SalesInvoice>();
            var reloadedLine = reloaded.SalesInvoiceLines.Single();
            Assert.Equal(otherIncomeId, reloadedLine.SalesAccountId);
            Assert.Equal("40600", reloadedLine.SalesAccountCode);

            // Assert that reloaded item is the shared GeneralItemName ("Service / Product")
            WithDb(db =>
            {
                var generalItem = db.Items.FirstOrDefault(i => i.Description == "Service / Product");
                Assert.NotNull(generalItem);
                Assert.Equal(generalItem.Id, reloadedLine.ItemId);
            });

            // Update draft with a different sales account (40100) and verify it remains editable
            var defaultSalesId = AccountId("40100");
            reloadedLine.SalesAccountId = defaultSalesId;
            var updateRes = await Send(HttpMethod.Post, "/api/Sales/SaveSalesInvoice", reloaded);
            Assert.True(updateRes.IsSuccessStatusCode, await updateRes.Content.ReadAsStringAsync());

            var reloaded2 = await (await Send(HttpMethod.Get, $"/api/Sales/SalesInvoice?id={savedInvoice.Id}"))
                .Content.ReadFromJsonAsync<Dto.Sales.SalesInvoice>();
            Assert.Equal(defaultSalesId, reloaded2.SalesInvoiceLines.Single().SalesAccountId);
            Assert.Equal("40100", reloaded2.SalesInvoiceLines.Single().SalesAccountCode);
        }

        [Fact]
        public async Task SalesInvoice_SalesAccount_FreeTextLine_ResolvingToCatalogItem_IgnoresLineOverride()
        {
            var defaultSalesId = AccountId("40100");
            var otherIncomeId = AccountId("40600");

            var catalogItem = await SaveItem(new Dto.Inventory.CatalogItem
            {
                Name = "Direct Name Matched Item",
                Price = 90m,
                SalesAccountId = defaultSalesId
            });

            var invoice = await NewInvoice();
            invoice.Status = "Open";
            var line = invoice.SalesInvoiceLines.Single();
            // Free-text style: ItemId is null, but Description matches the catalog item name
            line.ItemId = null;
            line.ItemDescription = "Direct Name Matched Item";
            line.Amount = 90m;
            line.Quantity = 1;
            line.SalesAccountId = otherIncomeId; // Attempt to override

            var saveRes = await Send(HttpMethod.Post, "/api/Sales/SaveSalesInvoice", invoice);
            Assert.True(saveRes.IsSuccessStatusCode, await saveRes.Content.ReadAsStringAsync());
            var invoiceId = (await Json(saveRes)).GetProperty("id").GetInt32();

            int glHeaderId = 0;
            WithDb(db =>
            {
                var posted = db.SalesInvoiceHeaders.Single(i => i.Id == invoiceId);
                Assert.NotNull(posted.GeneralLedgerHeaderId);
                glHeaderId = posted.GeneralLedgerHeaderId.Value;
            });

            var entries = Ledger(glHeaderId);
            // Because line resolved to a real catalog item, line override is ignored and credit hits item's 40100
            Assert.Contains(entries, l => l.AccountId == defaultSalesId && l.DrCr == DrOrCrSide.Cr && l.Amount == 90m);
            Assert.DoesNotContain(entries, l => l.AccountId == otherIncomeId);
        }

        [Fact]
        public async Task SalesInvoice_SalesAccount_PostInvoice_WithCatalogLineAndFreeTextOverride_SplitsRevenueCorrectly()
        {
            var otherIncomeId = AccountId("40600");
            var defaultSalesId = AccountId("40100");

            // Line 1: catalog item with default 40100
            var catalogItem = await SaveItem(new Dto.Inventory.CatalogItem
            {
                Name = "Standard Widget",
                Price = 100m,
                SalesAccountId = defaultSalesId
            });

            var invoice = await NewInvoice();
            invoice.Status = "Open";
            var line1 = invoice.SalesInvoiceLines.Single();
            line1.ItemId = catalogItem.Id;
            line1.ItemDescription = catalogItem.Name;
            line1.Amount = 100m;
            line1.Quantity = 1;
            line1.Discount = 0;
            line1.TaxId = null;
            // Attempt to pass line override on catalog item should be ignored
            line1.SalesAccountId = otherIncomeId;

            // Line 2: free-text line overridden to 40600
            var line2 = new Dto.Sales.SalesInvoiceLine
            {
                ItemId = null,
                ItemDescription = "Custom One-off Setup Fee",
                Amount = 150m,
                Quantity = 1,
                Discount = 0,
                TaxId = null,
                SalesAccountId = otherIncomeId
            };
            invoice.SalesInvoiceLines.Add(line2);

            var saveRes = await Send(HttpMethod.Post, "/api/Sales/SaveSalesInvoice", invoice);
            Assert.True(saveRes.IsSuccessStatusCode, await saveRes.Content.ReadAsStringAsync());
            var invoiceId = (await Json(saveRes)).GetProperty("id").GetInt32();

            int glHeaderId = 0;
            WithDb(db =>
            {
                var posted = db.SalesInvoiceHeaders.Single(i => i.Id == invoiceId);
                Assert.NotNull(posted.GeneralLedgerHeaderId);
                glHeaderId = posted.GeneralLedgerHeaderId.Value;
            });

            var entries = Ledger(glHeaderId);
            // Credit to 40100 for line 1 ($100)
            Assert.Contains(entries, l => l.AccountId == defaultSalesId && l.DrCr == DrOrCrSide.Cr && l.Amount == 100m);
            // Credit to 40600 for line 2 ($150)
            Assert.Contains(entries, l => l.AccountId == otherIncomeId && l.DrCr == DrOrCrSide.Cr && l.Amount == 150m);
            // Debit to AR for total $250
            Assert.Contains(entries, l => l.AccountId == AccountId("10120") && l.DrCr == DrOrCrSide.Dr && l.Amount == 250m);
        }

        [Fact]
        public async Task SalesInvoice_SalesAccount_FreeTextLine_WithoutOverride_CreditsDefault40100()
        {
            var defaultSalesId = AccountId("40100");
            var invoice = await NewInvoice();
            invoice.Status = "Open";
            var line = invoice.SalesInvoiceLines.Single();
            line.ItemId = null;
            line.ItemDescription = "Standard Free-Text Line";
            line.Amount = 80m;
            line.Quantity = 1;
            line.Discount = 0;
            line.TaxId = null;
            line.SalesAccountId = null;

            var saveRes = await Send(HttpMethod.Post, "/api/Sales/SaveSalesInvoice", invoice);
            Assert.True(saveRes.IsSuccessStatusCode, await saveRes.Content.ReadAsStringAsync());
            var invoiceId = (await Json(saveRes)).GetProperty("id").GetInt32();

            int glHeaderId = 0;
            WithDb(db =>
            {
                var posted = db.SalesInvoiceHeaders.Single(i => i.Id == invoiceId);
                Assert.NotNull(posted.GeneralLedgerHeaderId);
                glHeaderId = posted.GeneralLedgerHeaderId.Value;
            });

            var entries = Ledger(glHeaderId);
            // Credit should hit 40100 for $80
            Assert.Contains(entries, l => l.AccountId == defaultSalesId && l.DrCr == DrOrCrSide.Cr && l.Amount == 80m);
        }

        [Fact]
        public async Task SalesInvoice_SalesAccount_FreeTextOverride_DoesNotMutateSharedGeneralItem()
        {
            var defaultSalesId = AccountId("40100");
            var otherIncomeId = AccountId("40600");

            int generalItemId = 0;
            WithDb(db =>
            {
                var item = db.Items.FirstOrDefault(i => i.Description == "Service / Product");
                if (item != null)
                {
                    generalItemId = item.Id;
                    Assert.Equal(defaultSalesId, item.SalesAccountId);
                }
            });

            var invoice = await NewInvoice();
            invoice.Status = "Open";
            var line = invoice.SalesInvoiceLines.Single();
            line.ItemId = null;
            line.ItemDescription = "One-Off Service";
            line.Amount = 500m;
            line.Quantity = 1;
            line.Discount = 0;
            line.TaxId = null;
            line.SalesAccountId = otherIncomeId;

            var saveRes = await Send(HttpMethod.Post, "/api/Sales/SaveSalesInvoice", invoice);
            Assert.True(saveRes.IsSuccessStatusCode, await saveRes.Content.ReadAsStringAsync());

            // Verify general item's sales account in DB is STILL 40100
            WithDb(db =>
            {
                var item = db.Items.FirstOrDefault(i => i.Description == "Service / Product");
                Assert.NotNull(item);
                Assert.Equal(defaultSalesId, item.SalesAccountId);
            });
        }

        [Fact]
        public async Task SalesInvoice_SalesAccount_Save_RejectsNonIncomeAccounts_AndCrossTenantLineAccount()
        {
            // 1. Reject expense account (50200) on invoice line
            var invoice1 = await NewInvoice();
            invoice1.SalesInvoiceLines.Single().SalesAccountId = AccountId("50200");
            var expenseRes = await Send(HttpMethod.Post, "/api/Sales/SaveSalesInvoice", invoice1);
            Assert.Equal(HttpStatusCode.BadRequest, expenseRes.StatusCode);

            // 2. Reject asset account (10120) on invoice line
            var invoice2 = await NewInvoice();
            invoice2.SalesInvoiceLines.Single().SalesAccountId = AccountId("10120");
            var assetRes = await Send(HttpMethod.Post, "/api/Sales/SaveSalesInvoice", invoice2);
            Assert.Equal(HttpStatusCode.BadRequest, assetRes.StatusCode);

            // 3. Reject parent account (40000) on invoice line
            var invoice3 = await NewInvoice();
            invoice3.SalesInvoiceLines.Single().SalesAccountId = AccountId("40000");
            var parentRes = await Send(HttpMethod.Post, "/api/Sales/SaveSalesInvoice", invoice3);
            Assert.Equal(HttpStatusCode.BadRequest, parentRes.StatusCode);

            // 4. Reject cross-tenant account on invoice line
            int otherTenantAccountId = 0;
            WithDb(db =>
            {
                var otherAcc = db.Accounts.FirstOrDefault(a => a.CompanyId != 1 && (a.AccountClassId == (int)AccountClasses.Revenue || a.AccountCode.StartsWith("4")));
                if (otherAcc != null)
                    otherTenantAccountId = otherAcc.Id;
            });

            if (otherTenantAccountId > 0)
            {
                var invoice4 = await NewInvoice();
                invoice4.SalesInvoiceLines.Single().SalesAccountId = otherTenantAccountId;
                var crossTenantRes = await Send(HttpMethod.Post, "/api/Sales/SaveSalesInvoice", invoice4);
                Assert.Equal(HttpStatusCode.BadRequest, crossTenantRes.StatusCode);
            }
        }

        [Fact]
        public async Task SalesInvoice_SalesAccount_PdfGeneration_Succeeds_AndDoesNotLeakAccountInfo()
        {
            var invoice = await NewInvoice();
            invoice.Status = "Open";
            var line = invoice.SalesInvoiceLines.Single();
            line.ItemId = null;
            line.ItemDescription = "Customer Invoice PDF Test";
            line.Amount = 175m;
            line.SalesAccountId = AccountId("40600");

            var saveRes = await Send(HttpMethod.Post, "/api/Sales/SaveSalesInvoice", invoice);
            Assert.True(saveRes.IsSuccessStatusCode, await saveRes.Content.ReadAsStringAsync());
            var invoiceId = (await Json(saveRes)).GetProperty("id").GetInt32();

            var pdfRes = await Send(HttpMethod.Get, $"/api/Sales/SalesInvoicePdf?id={invoiceId}");
            Assert.True(pdfRes.IsSuccessStatusCode, await pdfRes.Content.ReadAsStringAsync());
            Assert.Equal("application/pdf", pdfRes.Content.Headers.ContentType?.MediaType);
            var bytes = await pdfRes.Content.ReadAsByteArrayAsync();
            Assert.NotEmpty(bytes);

            // Fetch the invoice DTO to test customer-facing email templates
            var getRes = await Send(HttpMethod.Get, $"/api/Sales/SalesInvoice?id={invoiceId}");
            var invoiceDto = await getRes.Content.ReadFromJsonAsync<Dto.Sales.SalesInvoice>();
            Assert.NotNull(invoiceDto);

            // Assert customer email templates do not leak internal revenue account code or name
            var email = Api.Service.InvoiceEmailTemplate.Build(invoiceDto, "USD", canReply: true, "https://example.com/pdf", attachPdf: true);
            Assert.DoesNotContain("40600", email.Html);
            Assert.DoesNotContain("Other Income", email.Html);
            Assert.DoesNotContain("40600", email.Text);
            Assert.DoesNotContain("Other Income", email.Text);

            var followUp = Api.Service.InvoiceEmailTemplate.BuildFollowUp(invoiceDto, "USD", canReply: true, "https://example.com/pdf", attachPdf: true);
            Assert.DoesNotContain("40600", followUp.Html);
            Assert.DoesNotContain("Other Income", followUp.Html);
            Assert.DoesNotContain("40600", followUp.Text);
            Assert.DoesNotContain("Other Income", followUp.Text);

            // Assert raw uncompressed text streams in PDF do not contain the account code or name
            var pdfAscii = System.Text.Encoding.Latin1.GetString(bytes);
            Assert.DoesNotContain("40600", pdfAscii);
            Assert.DoesNotContain("Other Income", pdfAscii);
        }

        private class SalesAccountOptionDto
        {
            public int Id { get; set; }
            public string Code { get; set; }
            public string Name { get; set; }
        }
    }
}
