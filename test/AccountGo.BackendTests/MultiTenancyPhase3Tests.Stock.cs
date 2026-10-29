#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Api.Data;
using Api.Service;
using Core.Domain;
using Core.Domain.Items;
using Core.Domain.Purchases;
using Core.Domain.Sales;
using Core.Tenancy;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Services.Inventory;
using Xunit;

namespace AccountGo.BackendTests
{
    public partial class MultiTenancyPhase3Tests
    {
        [Fact]
        public async Task StockLedger_UsesMovingAverage_ReversesVoidsAndBalancesInventory()
        {
            var itemId = CreateStockItem("Test stock");
            var vendor = await SaveVendor(new Dto.Purchasing.Supplier { Name = "Stock supplier" });
            Assert.Throws<ArgumentOutOfRangeException>(() => AdjustStock(itemId, 0.00001m, 1m, "Count", "Rounded to zero"));

            var first = NewBill(vendor.Id, ("Test stock", 10, 5m, AccountId("10800")));
            first.Status = "Open";
            var firstBill = await SaveBillOk(first);
            AssertStockState(itemId, 10m, 5m);
            Assert.Equal(firstBill.Date.Date, JournalForBill(firstBill.Id).Date.Date);
            AssertStockInvariant(itemId);

            var second = NewBill(vendor.Id, ("Test stock", 10, 7m, AccountId("10800")));
            second.Status = "Open";
            var secondBill = await SaveBillOk(second);
            AssertStockState(itemId, 20m, 6m);
            Assert.Equal(secondBill.Date.Date, JournalForBill(secondBill.Id).Date.Date);
            AssertStockInvariant(itemId);

            var invoice = await NewStockInvoice(itemId, 5m);
            var savedInvoice = await Send(HttpMethod.Post, "/api/Sales/SaveSalesInvoice", invoice);
            Assert.True(savedInvoice.IsSuccessStatusCode, await savedInvoice.Content.ReadAsStringAsync());
            var invoiceId = (await Json(savedInvoice)).GetProperty("id").GetInt32();
            int invoiceLedgerId = 0;
            WithDb(db =>
            {
                var posted = db.SalesInvoiceHeaders.Single(i => i.Id == invoiceId);
                Assert.NotNull(posted.GeneralLedgerHeaderId);
                invoiceLedgerId = posted.GeneralLedgerHeaderId.Value;
            });
            var cogs = Ledger(invoiceLedgerId);
            Assert.Contains(cogs, line => line.AccountId == AccountId("50300") && line.DrCr == DrOrCrSide.Dr && line.Amount == 30m);
            Assert.Contains(cogs, line => line.AccountId == AccountId("10800") && line.DrCr == DrOrCrSide.Cr && line.Amount == 30m);
            AssertStockState(itemId, 15m, 6m);
            AssertStockInvariant(itemId);

            Assert.True((await Send(HttpMethod.Post, $"/api/Sales/VoidSalesInvoice?id={invoiceId}&date={IsoDate(DateTime.UtcNow)}")).IsSuccessStatusCode);
            AssertStockState(itemId, 20m, 6m);
            var invoiceAndVoid = Ledger(invoiceLedgerId).Concat(Ledger(InvoiceVoidLedgerId(invoiceId))).ToList();
            Assert.All(invoiceAndVoid.Select(line => line.AccountId).Distinct(), accountId =>
                Assert.Equal(0m, invoiceAndVoid.Where(line => line.AccountId == accountId)
                    .Sum(line => line.DrCr == DrOrCrSide.Dr ? line.Amount : -line.Amount)));
            AssertStockInvariant(itemId);

            Assert.True((await Send(HttpMethod.Post, $"/api/Purchasing/VoidBill?id={secondBill.Id}&date={IsoDate(DateTime.UtcNow)}&reason=correction")).IsSuccessStatusCode);
            AssertStockState(itemId, 10m, 5m);
            AssertStockInvariant(itemId);

            AdjustStock(itemId, -2m, null, "Damaged", "Damaged stock");
            AssertStockState(itemId, 8m, 5m);
            AssertStockInvariant(itemId);
            AdjustStock(itemId, 3m, 4m, "OpeningStock", "Opening stock");
            WithDb(db =>
            {
                var adjustment = db.InventoryAdjustments.OrderByDescending(a => a.Id).First();
                Assert.Equal("OpeningStock", adjustment.Reason);
                Assert.Equal(AccountIdIn(db, "30700"), db.GeneralLedgerLines
                    .Where(line => line.GeneralLedgerHeaderId == adjustment.GeneralLedgerHeaderId)
                    .Single(line => line.DrCr == DrOrCrSide.Cr).AccountId);
            });
            AssertStockState(itemId, 11m, 4.7273m);
            AssertStockInvariant(itemId);

            var oversold = await NewStockInvoice(itemId, 12m);
            var oversoldResponse = await Send(HttpMethod.Post, "/api/Sales/SaveSalesInvoice", oversold);
            Assert.True(oversoldResponse.IsSuccessStatusCode, await oversoldResponse.Content.ReadAsStringAsync());
            var oversoldJson = await Json(oversoldResponse);
            Assert.Contains(oversoldJson.GetProperty("warnings").EnumerateArray(),
                warning => warning.GetString() == "Only 11 of Test stock in stock.");
            var oversoldId = oversoldJson.GetProperty("id").GetInt32();
            WithDb(db => Assert.NotNull(db.SalesInvoiceHeaders.Single(i => i.Id == oversoldId).GeneralLedgerHeaderId));
            // Selling never changes the average, even past zero.
            AssertStockState(itemId, -1m, 4.7273m);
            AssertStockInvariant(itemId);

            SetInventoryModuleEnabled(true);
            var stockItems = await Json(await Send(HttpMethod.Get, "/api/Stock/Items"));
            Assert.Equal(stockItems.GetProperty("totalValue").GetDecimal(),
                stockItems.GetProperty("inventoryAccountBalance").GetDecimal());

            var movementResponse = await Json(await Send(HttpMethod.Get, $"/api/Stock/Movements?itemId={itemId}"));
            var movements = movementResponse.EnumerateArray().ToArray();
            var movementTypes = movements.Select(movement => movement.GetProperty("type").GetString()).ToArray();
            Assert.Contains("Bill", movementTypes);
            Assert.Contains("Invoice", movementTypes);
            Assert.Contains("Void of Invoice", movementTypes);
            Assert.Contains("Void of Bill", movementTypes);
            Assert.Contains("Adjustment", movementTypes);
            Assert.Contains(movements, movement => movement.GetProperty("linkType").GetString() == "bill");
            Assert.Contains(movements, movement => movement.GetProperty("linkType").GetString() == "invoice");

            decimal runningQuantity = 0;
            foreach (var movement in movements.Reverse())
            {
                runningQuantity += movement.GetProperty("in").GetDecimal() - movement.GetProperty("out").GetDecimal();
                Assert.Equal(runningQuantity, movement.GetProperty("runningQuantity").GetDecimal());
            }
            Assert.Equal(-1m, movements[0].GetProperty("runningQuantity").GetDecimal());
        }

        [Fact]
        public async Task StockApi_RequiresInventoryModuleAndAdjustmentPermission()
        {
            SetInventoryModuleEnabled(false);
            Assert.Equal(HttpStatusCode.Forbidden, (await Send(HttpMethod.Get, "/api/Stock/Items")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await Send(HttpMethod.Get, "/api/Stock/Movements")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await Send(HttpMethod.Post, "/api/Stock/Adjust", new
            {
                itemId = 1,
                quantityChange = 1,
                unitCost = 1,
                reason = "Count",
                date = DateTime.UtcNow.Date
            })).StatusCode);

            SetInventoryModuleEnabled(true);
            var item = await SaveItem(new Dto.Inventory.CatalogItem
            {
                Name = "Adjustment permission stock",
                TrackStock = true
            });

            WithDb(db =>
            {
                var member = new CompanyMember
                {
                    CompanyId = _companyId,
                    UserId = _otherId,
                    Role = CompanyMemberRole.Member,
                    Status = CompanyMemberStatus.Active,
                    JoinedAt = DateTime.UtcNow
                };
                db.CompanyMembers.Add(member);
                db.SaveChanges();
                db.CompanyMemberSecurityRoles.Add(new CompanyMemberSecurityRole
                {
                    CompanyMemberId = member.Id,
                    SecurityRoleId = db.SecurityRoles.Single(role => role.Name == "Sales Representative").Id
                });
                db.SaveChanges();
            });

            Assert.Equal(HttpStatusCode.OK, (await Send(HttpMethod.Get, "/api/Stock/Items", token: _otherToken)).StatusCode);
            var adjustmentRequest = new
            {
                itemId = item.Id,
                quantityChange = 3m,
                unitCost = 2m,
                reason = "Count",
                date = DateTime.UtcNow.Date,
                memo = "Counted stock"
            };
            Assert.Equal(HttpStatusCode.Forbidden,
                (await Send(HttpMethod.Post, "/api/Stock/Adjust", adjustmentRequest, token: _otherToken)).StatusCode);

            WithDb(db =>
            {
                db.SecurityRolePermissions.Add(new Core.Domain.Security.SecurityRolePermission
                {
                    SecurityRoleId = db.SecurityRoles.Single(role => role.Name == "Sales Representative").Id,
                    SecurityPermissionId = db.SecurityPermissions.Single(permission => permission.Name == "inventory.adjustments.post").Id
                });
                db.SaveChanges();
            });
            var posted = await Send(HttpMethod.Post, "/api/Stock/Adjust", adjustmentRequest, token: _otherToken);
            Assert.Equal(HttpStatusCode.OK, posted.StatusCode);
            var movements = await Json(await Send(HttpMethod.Get, $"/api/Stock/Movements?itemId={item.Id}"));
            var adjustment = Assert.Single(movements.EnumerateArray().ToArray());
            Assert.Equal("Adjustment", adjustment.GetProperty("type").GetString());
            Assert.Equal(3m, adjustment.GetProperty("runningQuantity").GetDecimal());

            var stockItems = await Json(await Send(HttpMethod.Get, "/api/Stock/Items"));
            Assert.Equal(6m, stockItems.GetProperty("totalValue").GetDecimal());
            Assert.Equal(6m, stockItems.GetProperty("inventoryAccountBalance").GetDecimal());
        }

        [Fact]
        public async Task StockApi_MissingInventoryAccount_ReturnsHelpfulBadRequest()
        {
            SetInventoryModuleEnabled(true);
            WithDb(db =>
            {
                db.Accounts.Single(account => account.AccountCode == "10800").AccountCode = "10801";
                db.SaveChanges();
            });
            var response = await Send(HttpMethod.Get, "/api/Stock/Items");
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("Inventory account (10800)", (await Json(response)).GetProperty("message").GetString());
        }

        [Fact]
        public async Task Catalog_StockCreationIsModuleGated_OpeningStockPosts_AndBillsUseInventory()
        {
            SetInventoryModuleEnabled(false);
            var stock = new Dto.Inventory.CatalogItem
            {
                Name = "Catalog stock",
                TrackStock = true,
                OpeningQuantity = 4m,
                OpeningUnitCost = 3.25m
            };
            Assert.Equal(HttpStatusCode.Forbidden,
                (await Send(HttpMethod.Post, "/api/Catalog/SaveItem", stock)).StatusCode);

            SetInventoryModuleEnabled(true);
            var created = await SaveItem(stock);
            Assert.True(created.TrackStock);
            Assert.True(created.HasStockMovements);
            Assert.Equal(4m, created.QuantityOnHand);
            Assert.Equal(3.25m, created.AverageCost);
            Assert.Equal(AccountId("10800"), created.InventoryAccountId);

            WithDb(db =>
            {
                var item = db.Items.Include(i => i.ItemCategory).Single(i => i.Id == created.Id);
                Assert.Equal("Stock items", item.ItemCategory.Name);
                Assert.Equal(ItemTypes.Purchased, item.ItemCategory.ItemType);
                Assert.Equal(AccountIdIn(db, "40100"), item.SalesAccountId);
                Assert.Equal(AccountIdIn(db, "10800"), item.InventoryAccountId);
                Assert.Equal(AccountIdIn(db, "50300"), item.CostOfGoodsSoldAccountId);
                Assert.Equal(AccountIdIn(db, "50310"), item.InventoryAdjustmentAccountId);
                var adjustment = db.InventoryAdjustments.Single(a => a.ItemId == item.Id);
                Assert.Equal("OpeningStock", adjustment.Reason);
                var openingEntry = db.GeneralLedgerLines.Where(l => l.GeneralLedgerHeaderId == adjustment.GeneralLedgerHeaderId).ToList();
                Assert.Contains(openingEntry, line => line.AccountId == AccountIdIn(db, "10800") && line.DrCr == DrOrCrSide.Dr && line.Amount == 13m);
                Assert.Contains(openingEntry, line => line.AccountId == AccountIdIn(db, "30700") && line.DrCr == DrOrCrSide.Cr && line.Amount == 13m);
            });

            var vendor = await SaveVendor(new Dto.Purchasing.Supplier { Name = "Catalog stock supplier" });
            var accounts = await (await Send(HttpMethod.Get, "/api/Purchasing/BillAccounts"))
                .Content.ReadFromJsonAsync<List<Dto.Purchasing.BillAccount>>();
            Assert.Contains(accounts, account => account.Code == "10800" && account.IsInventoryAccount);
            var bill = NewBill(vendor.Id, ("Catalog stock", 2m, 5m, AccountId("50200")));
            bill.Lines[0].ItemId = created.Id;
            bill.Status = "Open";
            var approved = await SaveBillOk(bill);
            Assert.Equal(AccountId("10800"), approved.Lines.Single().AccountId);
            Assert.True(approved.Lines.Single().IsStockItem);
            Assert.Contains(Ledger(BillLedgerId(approved.Id)),
                line => line.AccountId == AccountId("10800") && line.DrCr == DrOrCrSide.Dr && line.Amount == 10m);
            AssertStockState(created.Id, 6m, 3.8333m);
            AssertStockInvariant(created.Id);

            var invoice = await NewStockInvoice(created.Id, 2m);
            invoice.Status = "Open";
            var issuedResponse = await Send(HttpMethod.Post, "/api/Sales/SaveSalesInvoice", invoice);
            Assert.True(issuedResponse.IsSuccessStatusCode, await issuedResponse.Content.ReadAsStringAsync());
            var invoiceId = (await Json(issuedResponse)).GetProperty("id").GetInt32();
            WithDb(db => Assert.NotNull(db.SalesInvoiceHeaders.Single(i => i.Id == invoiceId).GeneralLedgerHeaderId));
            AssertStockState(created.Id, 4m, 3.8325m);
            AssertStockInvariant(created.Id);

            var switchedOff = await Send(HttpMethod.Post, "/api/Catalog/SaveItem",
                new Dto.Inventory.CatalogItem { Id = created.Id, Name = created.Name, TrackStock = false });
            Assert.Equal(HttpStatusCode.BadRequest, switchedOff.StatusCode);
            Assert.Contains("can't stop tracking stock", await switchedOff.Content.ReadAsStringAsync());
        }

        private void SetInventoryModuleEnabled(bool enabled)
        {
            WithDb(db =>
            {
                var company = db.Companies.Single(c => c.Id == _companyId);
                company.Plan = "community";
                company.ActiveModulesJson = null;
                // Community catalog includes inventory; opt out via DisabledModulesJson when disabled.
                company.DisabledModulesJson = enabled ? "[]" : "[\"inventory\"]";
                db.SaveChanges();
            });
        }

        [Fact]
        public async Task StockMigration_BackfillsExistingWorkspacesAndCanBeReappliedSafely()
        {
            var itemId = CreateStockItem("Legacy stock", 7.5m);
            WithDb(db =>
            {
                var item = db.Items.Single(i => i.Id == itemId);
                var measurementId = item.PurchaseMeasurementId.Value;
                db.InventoryControlJournals.AddRange(
                    new InventoryControlJournal { ItemId = item.Id, MeasurementId = measurementId, DocumentType = DocumentTypes.PurchaseInvoice, Date = DateTime.UtcNow.Date, INQty = 10m },
                    new InventoryControlJournal { ItemId = item.Id, MeasurementId = measurementId, DocumentType = DocumentTypes.SalesInvoice, Date = DateTime.UtcNow.Date, OUTQty = 2m, IsReverse = true },
                    new InventoryControlJournal { ItemId = item.Id, MeasurementId = measurementId, DocumentType = DocumentTypes.PurchaseInvoice, Date = DateTime.UtcNow.Date, INQty = 3m, IsReverse = true },
                    new InventoryControlJournal { ItemId = item.Id, MeasurementId = measurementId, DocumentType = DocumentTypes.SalesInvoice, Date = DateTime.UtcNow.Date, OUTQty = 1m });
                db.SaveChanges();
            });

            var secondWorkspace = await Send(HttpMethod.Post, "/api/organizations",
                new { name = "Stock Migration Two", slug = "stock-migration-two" }, scoped: false);
            Assert.Equal(HttpStatusCode.Created, secondWorkspace.StatusCode);

            var secondCompanyId = 0;
            WithDb(db => secondCompanyId = db.Companies.Single(c => c.Slug == "stock-migration-two").Id);
            using (var scope = _factory.Services.CreateScope())
            {
                var inventory = scope.ServiceProvider.GetRequiredService<IInventoryService>();
                using (scope.ServiceProvider.GetRequiredService<ITenantContext>()
                    .Impersonate(secondCompanyId, "stock-migration-two", CompanyMemberRole.Owner))
                    Assert.Null(inventory.GetItemById(itemId));
            }

            using var source = new SqliteConnection("Data Source=" + _path);
            using var replica = new SqliteConnection("Data Source=:memory:");
            source.Open();
            replica.Open();
            source.BackupDatabase(replica);
            using var command = replica.CreateCommand();
            command.CommandText = @"
                UPDATE ""Item"" SET ""InventoryAdjustmentAccountId"" = NULL
                WHERE ""InventoryAdjustmentAccountId"" IN (SELECT ""Id"" FROM ""Account"" WHERE ""AccountCode"" IN ('30700', '50310'));
                UPDATE ""ItemCategory"" SET ""AdjustmentAccountId"" = NULL
                WHERE ""AdjustmentAccountId"" IN (SELECT ""Id"" FROM ""Account"" WHERE ""AccountCode"" IN ('30700', '50310'));
                DELETE FROM ""Account"" WHERE ""AccountCode"" IN ('30700', '50310');
                DELETE FROM ""SequenceNumber"" WHERE ""SequenceNumberType"" = 16;
                DROP TABLE ""InventoryAdjustment"";
                ALTER TABLE ""Item"" DROP COLUMN ""QuantityOnHand"";
                ALTER TABLE ""Item"" DROP COLUMN ""AverageCost"";
                ALTER TABLE ""Item"" DROP COLUMN ""StockValue"";
                ALTER TABLE ""InventoryControlJournal"" DROP COLUMN ""DocumentId"";
                ALTER TABLE ""InventoryControlJournal"" DROP COLUMN ""UnitCost"";
                ALTER TABLE ""InventoryControlJournal"" DROP COLUMN ""Memo"";";
            command.ExecuteNonQuery();

            SQLitePCL.raw.sqlite3_limit(replica.Handle, SQLitePCL.raw.SQLITE_LIMIT_COMPOUND_SELECT, 1);
            var migration = File.ReadAllText(Path.Combine(RepoRoot(), "src/Api/d1/migrations/0014_stock.sql"));
            ApplyStockMigration(command, migration, skipColumnAdds: false);
            ApplyStockMigration(command, migration, skipColumnAdds: true);
            var valueMigration = File.ReadAllText(Path.Combine(RepoRoot(), "src/Api/d1/migrations/0016_stock_value.sql"));
            ApplyStockMigration(command, valueMigration, skipColumnAdds: false);
            ApplyStockMigration(command, valueMigration, skipColumnAdds: true);

            Assert.True(ColumnExists(command, "Item", "QuantityOnHand"));
            Assert.True(ColumnExists(command, "Item", "AverageCost"));
            Assert.True(ColumnExists(command, "Item", "StockValue"));
            Assert.True(ColumnExists(command, "InventoryControlJournal", "DocumentId"));
            Assert.True(TableExists(command, "InventoryAdjustment"));
            command.CommandText = @"SELECT ""QuantityOnHand"", ""AverageCost"", ""StockValue"" FROM ""Item"" WHERE ""Id"" = " + itemId;
            using (var reader = command.ExecuteReader())
            {
                Assert.True(reader.Read());
                Assert.Equal(10m, reader.GetDecimal(0));
                Assert.Equal(7.5m, reader.GetDecimal(1));
                Assert.Equal(75m, reader.GetDecimal(2));
            }

            command.CommandText = @"SELECT ""Id"" FROM ""Company"" ORDER BY ""Id""";
            var companyIds = new List<int>();
            using (var reader = command.ExecuteReader())
                while (reader.Read()) companyIds.Add(reader.GetInt32(0));

            foreach (var companyId in companyIds)
            {
                foreach (var code in new[] { "30700", "50310" })
                {
                    command.CommandText = $@"SELECT COUNT(*) FROM ""Account"" WHERE ""CompanyId"" = {companyId} AND ""AccountCode"" = '{code}'";
                    Assert.Equal(1L, command.ExecuteScalar());
                }
                command.CommandText = $@"SELECT COUNT(*) FROM ""SequenceNumber"" WHERE ""CompanyId"" = {companyId} AND ""SequenceNumberType"" = 16";
                Assert.Equal(1L, command.ExecuteScalar());
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void StockValueMigration_BackfillsMovementCentsInsteadOfRoundedAverage(bool localUpgrade)
        {
            var itemId = CreateStockItem("Valued migration stock");
            AdjustStock(itemId, 10000m, 1m, "Count", "First receipt");
            AdjustStock(itemId, 20000m, 1.01m, "Count", "Second receipt");
            using var source = new SqliteConnection("Data Source=" + _path);
            using var replica = new SqliteConnection("Data Source=:memory:");
            source.Open();
            replica.Open();
            source.BackupDatabase(replica);
            SQLitePCL.raw.sqlite3_limit(replica.Handle, SQLitePCL.raw.SQLITE_LIMIT_COMPOUND_SELECT, 1);
            using var command = replica.CreateCommand();
            command.CommandText = @"ALTER TABLE ""Item"" DROP COLUMN ""StockValue"";";
            command.ExecuteNonQuery();
            if (localUpgrade)
            {
                Api.Data.Schema.SqliteSchemaUpgrader.Upgrade(replica);
                Api.Data.Schema.SqliteSchemaUpgrader.Upgrade(replica);
            }
            else
            {
                var migration = File.ReadAllText(Path.Combine(RepoRoot(), "src/Api/d1/migrations/0016_stock_value.sql"));
                ApplyStockMigration(command, migration, skipColumnAdds: false);
                ApplyStockMigration(command, migration, skipColumnAdds: true);
            }
            command.CommandText = @"SELECT ""StockValue"" FROM ""Item"" WHERE ""Id"" = " + itemId;
            Assert.Equal(30200m, Convert.ToDecimal(command.ExecuteScalar()));
        }

        private int CreateStockItem(string description, decimal cost = 0m)
        {
            var itemId = 0;
            WithDb(db =>
            {
                var sales = db.Accounts.Single(account => account.AccountCode == "40100");
                var inventory = db.Accounts.Single(account => account.AccountCode == "10800");
                var cogs = db.Accounts.Single(account => account.AccountCode == "50300");
                var adjustments = db.Accounts.Single(account => account.AccountCode == "50310");
                var measurement = db.Measurements.First();
                var category = new ItemCategory
                {
                    Name = "Stock items",
                    ItemType = ItemTypes.Purchased,
                    MeasurementId = measurement.Id,
                    SalesAccountId = sales.Id,
                    InventoryAccountId = inventory.Id,
                    CostOfGoodsSoldAccountId = cogs.Id,
                    AdjustmentAccountId = adjustments.Id
                };
                var item = new Item
                {
                    No = Guid.NewGuid().ToString("N"),
                    Code = "STOCK-" + Guid.NewGuid().ToString("N").Substring(0, 8),
                    Description = description,
                    Cost = cost,
                    Price = 20m,
                    ItemCategory = category,
                    SmallestMeasurementId = measurement.Id,
                    SellMeasurementId = measurement.Id,
                    PurchaseMeasurementId = measurement.Id,
                    SalesAccountId = sales.Id,
                    InventoryAccountId = inventory.Id,
                    CostOfGoodsSoldAccountId = cogs.Id,
                    InventoryAdjustmentAccountId = adjustments.Id
                };
                db.Items.Add(item);
                db.SaveChanges();
                itemId = item.Id;
            });
            return itemId;
        }

        private void AdjustStock(int itemId, decimal quantityChange, decimal? unitCost, string reason, string memo)
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            using (scope.ServiceProvider.GetRequiredService<ITenantContext>().Impersonate(_companyId, _slug, CompanyMemberRole.Owner))
            {
                var item = db.Items.Include(i => i.InventoryControlJournals).Single(i => i.Id == itemId);
                scope.ServiceProvider.GetRequiredService<IStockService>()
                    .Adjust(item, quantityChange, unitCost, reason, DateTime.UtcNow.Date, memo);
                db.SaveChanges();
            }
        }

        private async Task<Dto.Sales.SalesInvoice> NewStockInvoice(int itemId, decimal quantity)
        {
            var invoice = await NewInvoice();
            var item = new Item();
            var measurementId = 0;
            WithDb(db =>
            {
                item = db.Items.Single(i => i.Id == itemId);
                measurementId = item.SellMeasurementId.Value;
            });
            invoice.Status = "Open";
            var line = invoice.SalesInvoiceLines.Single();
            line.ItemId = itemId;
            line.ItemDescription = item.Description;
            line.Quantity = quantity;
            line.Amount = 20m;
            line.MeasurementId = measurementId;
            return invoice;
        }

        private void AssertStockState(int itemId, decimal quantity, decimal averageCost)
        {
            var state = (Quantity: 0m, AverageCost: 0m);
            WithDb(db =>
            {
                var item = db.Items.Single(i => i.Id == itemId);
                state = (item.QuantityOnHand, item.AverageCost);
            });
            Assert.Equal(quantity, state.Quantity);
            Assert.Equal(averageCost, state.AverageCost);
        }

        private void AssertStockInvariant(int itemId)
        {
            WithDb(db =>
            {
                var item = db.Items.Include(i => i.InventoryControlJournals).Single(i => i.Id == itemId);
                Assert.Equal(item.ComputeQuantityOnHand(), item.QuantityOnHand);

                var stockValue = db.Items.ToList().Sum(i => i.StockValue);
                var inventoryAccountId = AccountIdIn(db, "10800");
                var lines = db.GeneralLedgerLines.Where(line => line.AccountId == inventoryAccountId).ToList();
                var inventoryBalance = lines.Sum(line => line.DrCr == DrOrCrSide.Dr ? line.Amount : -line.Amount);
                Assert.Equal(inventoryBalance, stockValue);

                var allLines = db.GeneralLedgerLines.ToList();
                var debits = allLines.Where(line => line.DrCr == DrOrCrSide.Dr).Sum(line => line.Amount);
                var credits = allLines.Where(line => line.DrCr == DrOrCrSide.Cr).Sum(line => line.Amount);
                Assert.Equal(debits, credits);
            });
        }

        private InventoryControlJournal JournalForBill(int billId)
        {
            InventoryControlJournal journal = null;
            WithDb(db =>
            {
                var line = db.PurchaseInvoiceLines.Single(l => l.PurchaseInvoiceHeaderId == billId);
                journal = db.InventoryControlJournals.Single(j => j.Id == line.InventoryControlJournalId);
            });
            return journal;
        }

        private int InvoiceVoidLedgerId(int invoiceId)
        {
            var id = 0;
            WithDb(db => id = db.SalesInvoiceHeaders.Single(i => i.Id == invoiceId).VoidGeneralLedgerHeaderId.Value);
            return id;
        }

        private static bool ColumnExists(SqliteCommand command, string table, string column)
        {
            command.CommandText = $"PRAGMA table_info(\"{table}\")";
            using var reader = command.ExecuteReader();
            while (reader.Read())
                if (reader.GetString(1) == column)
                    return true;
            return false;
        }

        private static bool TableExists(SqliteCommand command, string table)
        {
            command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $name";
            command.Parameters.Clear();
            command.Parameters.AddWithValue("$name", table);
            return Convert.ToInt32(command.ExecuteScalar()) == 1;
        }

        private static void ApplyStockMigration(SqliteCommand command, string migration, bool skipColumnAdds)
        {
            var sqlStatements = string.Join("\n", migration.Split('\n')
                .Where(line => !line.TrimStart().StartsWith("--", StringComparison.Ordinal)))
                .Split(';');
            foreach (var statement in sqlStatements)
            {
                var sql = statement.Trim();
                if (sql.Length == 0 || (skipColumnAdds && sql.StartsWith("ALTER TABLE", StringComparison.OrdinalIgnoreCase)))
                    continue;
                command.Parameters.Clear();
                command.CommandText = sql;
                command.ExecuteNonQuery();
            }
        }
    }
}
