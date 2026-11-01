// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Api.Data;
using Api.Tenancy;
using Core.Data;
using Core.Domain;
using Core.Domain.Items;
using Core.Domain.Recurring;
using Core.Domain.Sales;
using Core.Domain.Security;
using Core.Domain.TaxSystem;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Services.Inventory;
using Services.Recurring;
using Services.Sales;
using Xunit;

namespace AccountGo.BackendTests.Recurring
{
    /// <summary>
    /// #482: the generation service turns a claimed occurrence into a draft and, for auto-issue schedules,
    /// applies the guard list. These tests pin the decision for each guard (draft vs downgrade vs fail-closed)
    /// using a real occurrence/schedule graph and a mocked sales service so we assert the decision, not posting.
    /// </summary>
    public sealed class RecurringInvoiceGenerationServiceTests : IDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"recurring-{Guid.NewGuid():N}.db");
        private readonly ApiDbContext _db;
        private readonly TenantContext _tenant = new();
        private readonly Company _company;
        private readonly Mock<ISalesService> _sales = new(MockBehavior.Strict);
        private readonly Mock<IInventoryService> _inventory = new(MockBehavior.Loose);
        private readonly RecurringInvoiceGenerationService _service;

        /// <summary>Tax amount the mocked SaveSalesInvoice stamps on each line (what the real service would resolve).</summary>
        private decimal _taxAmountOnSave = 12m;

        public RecurringInvoiceGenerationServiceTests()
        {
            _connection = new SqliteConnection($"Data Source={_databasePath};Pooling=False");
            _connection.Open();
            _db = new ApiDbContext(new DbContextOptionsBuilder<ApiDbContext>().UseSqlite(_connection).Options, tenantContext: _tenant);
            _db.Database.EnsureCreated();

            _company = new Company { Name = "Gen Co", Slug = "gen-co" };
            _db.Companies.Add(_company);
            _db.SaveChanges();
            _tenant.SetTenant(_company.Id, _company.Slug!, CompanyMemberRole.Owner, Array.Empty<string>());

            // CreateSalesInvoice: persist a draft header (as the real service does via the sales-order path),
            // stamp resolved tax, and return the id. GetSalesInvoiceById reloads it for guards/issue.
            _sales.Setup(s => s.CreateSalesInvoice(It.IsAny<Dto.Sales.SalesInvoice>()))
                .Returns<Dto.Sales.SalesInvoice>(dto => Core.Domain.Error.Result<Dto.Sales.SalesInvoice>.Success(PersistDraftFromDto(dto)));
            _sales.Setup(s => s.GetSalesInvoiceById(It.IsAny<int>()))
                .Returns<int>(id => _db.SalesInvoiceHeaders.Include(h => h.SalesInvoiceLines).First(h => h.Id == id));
            // The service stamps the recurring Origin on the saved header via the void UpdateSalesInvoice overload.
            _sales.Setup(s => s.UpdateSalesInvoice(It.IsAny<SalesInvoiceHeader>()))
                .Callback<SalesInvoiceHeader>(_ => _db.SaveChanges());

            _service = CreateService(_db, _sales.Object);
        }

        private RecurringInvoiceGenerationService CreateService(
            ApiDbContext db, ISalesService sales, IUnitOfWork? unitOfWork = null,
            IRepository<RecurringOccurrence>? occurrences = null) =>
            new RecurringInvoiceGenerationService(
                _tenant,
                unitOfWork ?? new EfUnitOfWork(db),
                sales,
                _inventory.Object,
                occurrences ?? new EfRepository<RecurringOccurrence>(NullLogger<RecurringOccurrence>.Instance, db),
                new EfRepository<RecurringSchedule>(NullLogger<RecurringSchedule>.Instance, db),
                new EfRepository<RecurringScheduleLine>(NullLogger<RecurringScheduleLine>.Instance, db),
                new EfRepository<Customer>(NullLogger<Customer>.Instance, db),
                new EfRepository<CompanySetting>(NullLogger<CompanySetting>.Instance, db),
                new EfRepository<Tax>(NullLogger<Tax>.Instance, db),
                new EfRepository<CompanyMember>(NullLogger<CompanyMember>.Instance, db),
                new EfRepository<SecurityRolePermission>(NullLogger<SecurityRolePermission>.Instance, db),
                NullLogger<RecurringInvoiceGenerationService>.Instance);

        private ApiDbContext CreateSiblingContext() =>
            new ApiDbContext(
                new DbContextOptionsBuilder<ApiDbContext>().UseSqlite(_connection.ConnectionString).Options,
                tenantContext: _tenant);

        private Mock<ISalesService> CreateSiblingSales(ApiDbContext db)
        {
            var sales = new Mock<ISalesService>(MockBehavior.Strict);
            sales.Setup(s => s.CreateSalesInvoice(It.IsAny<Dto.Sales.SalesInvoice>()))
                .Returns<Dto.Sales.SalesInvoice>(dto =>
                    Core.Domain.Error.Result<Dto.Sales.SalesInvoice>.Success(PersistDraftFromDto(dto, db)));
            sales.Setup(s => s.GetSalesInvoiceById(It.IsAny<int>()))
                .Returns<int>(id => db.SalesInvoiceHeaders.Include(h => h.SalesInvoiceLines).First(h => h.Id == id));
            sales.Setup(s => s.UpdateSalesInvoice(It.IsAny<SalesInvoiceHeader>()))
                .Callback<SalesInvoiceHeader>(_ => db.SaveChanges());
            return sales;
        }

        public void Dispose()
        {
            _db.Dispose();
            _connection.Dispose();
            File.Delete(_databasePath);
        }

        // ---- fixture builders ----

        private Customer AddCustomer(DateTime? archivedAt = null)
        {
            var c = new Customer { CompanyId = _company.Id, No = Guid.NewGuid().ToString("N"), ArchivedAt = archivedAt };
            _db.Customers.Add(c);
            _db.SaveChanges();
            return c;
        }

        private CompanySetting AddCompanySetting(DateTime booksStart, DateTime? lockedThrough = null)
        {
            var s = new CompanySetting
            {
                CompanyId = _company.Id,
                BooksStartDate = booksStart,
                BooksLockedThrough = lockedThrough,
            };
            _db.CompanySettings.Add(s);
            _db.SaveChanges();
            return s;
        }

        private Tax AddTax(decimal rate, bool active = true)
        {
            var t = new Tax { CompanyId = _company.Id, TaxName = "VAT", TaxCode = "VAT", Rate = rate, IsActive = active };
            _db.Taxes.Add(t);
            _db.SaveChanges();
            return t;
        }

        private RecurringSchedule AddSchedule(
            int customerId,
            RecurringAction action,
            string? automationEnabledBy = null,
            RecurringServicePeriodMode periodMode = RecurringServicePeriodMode.CurrentMonth,
            RecurringCadence cadence = RecurringCadence.Monthly,
            RecurringScheduleStatus status = RecurringScheduleStatus.Active)
        {
            var schedule = new RecurringSchedule
            {
                CompanyId = _company.Id,
                Kind = RecurringScheduleKind.SalesInvoice,
                PartyId = customerId,
                Name = "Retainer",
                Cadence = cadence,
                Interval = 1,
                DayOfMonth = 1,
                StartDate = new DateTime(2026, 1, 1),
                Action = action,
                Status = status,
                ServicePeriodMode = periodMode,
                AutomationEnabledBy = automationEnabledBy,
            };
            _db.RecurringSchedules.Add(schedule);
            _db.SaveChanges();
            return schedule;
        }

        private RecurringScheduleLine AddLine(int scheduleId, int itemId, int? taxId = null, decimal amount = 100m, decimal qty = 1m)
        {
            var line = new RecurringScheduleLine
            {
                CompanyId = _company.Id,
                RecurringScheduleId = scheduleId,
                ItemId = itemId,
                Quantity = qty,
                Amount = amount,
                TaxId = taxId,
            };
            _db.RecurringScheduleLines.Add(line);
            _db.SaveChanges();
            return line;
        }

        /// <summary>A service item with a sell measurement; not stock-tracked. Persisted so the #481 consistency guard passes.</summary>
        private Item ServiceItem() => PersistItem("SVC", "Consulting", ItemTypes.Service);

        private Item StockItem() => PersistItem("WIDGET", "Widget", ItemTypes.Purchased);

        private Item PersistItem(string no, string description, ItemTypes itemType)
        {
            var category = new ItemCategory { CompanyId = _company.Id, Name = description + " Cat", ItemType = itemType };
            _db.ItemCategories.Add(category);
            var measurement = new Measurement { CompanyId = _company.Id, Description = "Each" };
            _db.Measurements.Add(measurement);
            _db.SaveChanges();

            var item = new Item
            {
                CompanyId = _company.Id,
                No = no + "-" + Guid.NewGuid().ToString("N").Substring(0, 6),
                Description = description,
                ItemCategoryId = category.Id,
                SellMeasurementId = measurement.Id,
                SmallestMeasurementId = measurement.Id,
                SalesAccountId = null,
            };
            _db.Items.Add(item);
            _db.SaveChanges();

            // GetItemById must return the item with its category loaded so stock-tracking detection works.
            item.ItemCategory = category;
            return item;
        }

        private void SetupItem(Item item) =>
            _inventory.Setup(i => i.GetItemById(item.Id)).Returns(item);

        private RecurringOccurrence AddClaimedOccurrence(
            int scheduleId,
            DateTime documentDate,
            string leaseOwner,
            RecurringServicePeriodMode periodMode = RecurringServicePeriodMode.CurrentMonth,
            RecurringCadence cadence = RecurringCadence.Monthly,
            int interval = 1)
        {
            var (periodStart, periodEnd) = Core.Domain.Recurring.Calendar.NextDateCalculator
                .ComputeServicePeriod(documentDate, periodMode, cadence, interval);
            var occ = new RecurringOccurrence
            {
                CompanyId = _company.Id,
                RecurringScheduleId = scheduleId,
                ScheduledRunDate = documentDate.Date,
                DocumentDate = documentDate.Date,
                PeriodStart = periodStart,
                PeriodEnd = periodEnd,
                Status = RecurringOccurrenceStatus.Running,
                IdempotencyKey = $"{_company.Id}-{scheduleId}-{documentDate:yyyy-MM-dd}",
                LeasedBy = leaseOwner,
                LeasedUntil = DateTime.UtcNow.AddMinutes(5),
                CreatedAt = DateTime.UtcNow,
            };
            _db.RecurringOccurrences.Add(occ);
            _db.SaveChanges();
            return occ;
        }

        private CompanyMember AddMember(string userId, CompanyMemberRole role)
        {
            var m = new CompanyMember { CompanyId = _company.Id, UserId = userId, Role = role, Status = CompanyMemberStatus.Active, JoinedAt = DateTime.UtcNow };
            _db.CompanyMembers.Add(m);
            _db.SaveChanges();
            return m;
        }

        private RecurringOccurrence Reload(int occurrenceId) =>
            _db.RecurringOccurrences.AsNoTracking().First(o => o.Id == occurrenceId);

        /// <summary>Persists a draft header from the DTO as the real CreateSalesInvoice would, stamping resolved tax.</summary>
        private Dto.Sales.SalesInvoice PersistDraftFromDto(Dto.Sales.SalesInvoice dto, ApiDbContext? db = null)
        {
            db ??= _db;
            var header = new SalesInvoiceHeader
            {
                CompanyId = _company.Id,
                CustomerId = dto.CustomerId,
                Date = dto.InvoiceDate,
                Status = SalesInvoiceStatus.Draft,
                PaymentTermId = dto.PaymentTermId,
                Notes = dto.Notes,
                PaymentInstructions = dto.PaymentInstructions,
            };
            foreach (var l in dto.SalesInvoiceLines!)
            {
                header.SalesInvoiceLines.Add(new SalesInvoiceLine
                {
                    CompanyId = _company.Id,
                    ItemId = l.ItemId!.Value,
                    MeasurementId = l.MeasurementId ?? 1,
                    Quantity = l.Quantity ?? 1,
                    Amount = l.Amount ?? 0,
                    Discount = l.Discount ?? 0,
                    TaxId = l.TaxId,
                    TaxAmount = l.TaxId.HasValue ? _taxAmountOnSave : 0m,
                    SalesAccountId = l.SalesAccountId,
                    Description = l.ItemDescription,
                });
            }
            db.SalesInvoiceHeaders.Add(header);
            db.SaveChanges();
            dto.Id = header.Id;
            return dto;
        }

        // ---- tests ----

        [Fact]
        public void ProcessOccurrence_CreateDraftAction_CreatesDraftAndLinks()
        {
            AddCompanySetting(new DateTime(2026, 1, 1));
            var customer = AddCustomer();
            var item = ServiceItem();
            SetupItem(item);
            var schedule = AddSchedule(customer.Id, RecurringAction.CreateDraft);
            AddLine(schedule.Id, item.Id);
            var occ = AddClaimedOccurrence(schedule.Id, new DateTime(2026, 6, 1), "lease-1");

            var result = _service.ProcessOccurrence(occ.Id, "lease-1");

            Assert.Equal(RecurringGenerationOutcome.DraftCreated, result.Outcome);
            Assert.NotNull(result.SalesInvoiceId);
            var reloaded = Reload(occ.Id);
            Assert.Equal(RecurringOccurrenceStatus.DraftCreated, reloaded.Status);
            Assert.Equal(result.SalesInvoiceId, reloaded.SalesInvoiceId);
            _sales.Verify(s => s.PostSalesInvoice(It.IsAny<int>()), Times.Never);
        }

        [Fact]
        public void ProcessOccurrence_AutoIssue_AllGuardsPass_Issues()
        {
            AddCompanySetting(new DateTime(2026, 1, 1));
            var owner = AddMember("auto-user", CompanyMemberRole.Owner);
            var customer = AddCustomer();
            var item = ServiceItem();
            SetupItem(item);
            var tax = AddTax(12m);
            var schedule = AddSchedule(customer.Id, RecurringAction.AutoIssue, automationEnabledBy: owner.UserId);
            AddLine(schedule.Id, item.Id, taxId: tax.Id);
            var occ = AddClaimedOccurrence(schedule.Id, new DateTime(2026, 6, 1), "lease-1");

            _sales.Setup(s => s.PostSalesInvoice(It.IsAny<int>()));

            var result = _service.ProcessOccurrence(occ.Id, "lease-1");

            Assert.Equal(RecurringGenerationOutcome.Issued, result.Outcome);
            var reloaded = Reload(occ.Id);
            Assert.Equal(RecurringOccurrenceStatus.Issued, reloaded.Status);
            _sales.Verify(s => s.PostSalesInvoice(result.SalesInvoiceId!.Value), Times.Once);

            // #5: the generated invoice is stamped with the recurring origin.
            var saved = _db.SalesInvoiceHeaders.AsNoTracking().First(i => i.Id == result.SalesInvoiceId!.Value);
            Assert.Equal(Core.Domain.SalesInvoiceOrigins.RecurringSchedule, saved.Origin);
        }

        [Fact]
        public void ProcessOccurrence_AutoIssue_ActorNotAuthorized_DowngradesToDraft()
        {
            AddCompanySetting(new DateTime(2026, 1, 1));
            // Member exists but is a plain Member with no roles -> not authorized to post.
            var member = AddMember("weak-user", CompanyMemberRole.Member);
            var customer = AddCustomer();
            var item = ServiceItem();
            SetupItem(item);
            var tax = AddTax(12m);
            var schedule = AddSchedule(customer.Id, RecurringAction.AutoIssue, automationEnabledBy: member.UserId);
            AddLine(schedule.Id, item.Id, taxId: tax.Id);
            var occ = AddClaimedOccurrence(schedule.Id, new DateTime(2026, 6, 1), "lease-1");

            var result = _service.ProcessOccurrence(occ.Id, "lease-1");

            Assert.Equal(RecurringGenerationOutcome.DraftCreatedGuardFailed, result.Outcome);
            var reloaded = Reload(occ.Id);
            Assert.Equal(RecurringOccurrenceStatus.DraftCreated, reloaded.Status);
            Assert.Equal(RecurringGuardReasons.AutomationActorNotAuthorized, reloaded.GuardFailureReason);
            _sales.Verify(s => s.PostSalesInvoice(It.IsAny<int>()), Times.Never);
        }

        [Fact]
        public void ProcessOccurrence_AutoIssue_StockTrackedLine_DowngradesToDraft()
        {
            AddCompanySetting(new DateTime(2026, 1, 1));
            var owner = AddMember("auto-user", CompanyMemberRole.Owner);
            var customer = AddCustomer();
            var item = StockItem();
            SetupItem(item);
            var tax = AddTax(12m);
            var schedule = AddSchedule(customer.Id, RecurringAction.AutoIssue, automationEnabledBy: owner.UserId);
            AddLine(schedule.Id, item.Id, taxId: tax.Id);
            var occ = AddClaimedOccurrence(schedule.Id, new DateTime(2026, 6, 1), "lease-1");

            var result = _service.ProcessOccurrence(occ.Id, "lease-1");

            Assert.Equal(RecurringGenerationOutcome.DraftCreatedGuardFailed, result.Outcome);
            Assert.Equal(RecurringGuardReasons.StockTrackedLine, Reload(occ.Id).GuardFailureReason);
            _sales.Verify(s => s.PostSalesInvoice(It.IsAny<int>()), Times.Never);
        }

        [Fact]
        public void ProcessOccurrence_AutoIssue_ArchivedClient_DowngradesToDraft()
        {
            AddCompanySetting(new DateTime(2026, 1, 1));
            var owner = AddMember("auto-user", CompanyMemberRole.Owner);
            var customer = AddCustomer(archivedAt: new DateTime(2026, 5, 1));
            var item = ServiceItem();
            SetupItem(item);
            var tax = AddTax(12m);
            var schedule = AddSchedule(customer.Id, RecurringAction.AutoIssue, automationEnabledBy: owner.UserId);
            AddLine(schedule.Id, item.Id, taxId: tax.Id);
            var occ = AddClaimedOccurrence(schedule.Id, new DateTime(2026, 6, 1), "lease-1");

            var result = _service.ProcessOccurrence(occ.Id, "lease-1");

            Assert.Equal(RecurringGenerationOutcome.DraftCreatedGuardFailed, result.Outcome);
            Assert.Equal(RecurringGuardReasons.ClientNotActive, Reload(occ.Id).GuardFailureReason);
        }

        [Fact]
        public void ProcessOccurrence_AutoIssue_CrossMonthServicePeriod_DowngradesToDraft()
        {
            AddCompanySetting(new DateTime(2026, 1, 1));
            var owner = AddMember("auto-user", CompanyMemberRole.Owner);
            var customer = AddCustomer();
            var item = ServiceItem();
            SetupItem(item);
            var tax = AddTax(12m);
            // PreviousMonth mode: a June 1 document covers May -> service period is in a different month.
            var schedule = AddSchedule(customer.Id, RecurringAction.AutoIssue, automationEnabledBy: owner.UserId,
                periodMode: RecurringServicePeriodMode.PreviousMonth);
            AddLine(schedule.Id, item.Id, taxId: tax.Id);
            var occ = AddClaimedOccurrence(schedule.Id, new DateTime(2026, 6, 1), "lease-1",
                periodMode: RecurringServicePeriodMode.PreviousMonth);

            var result = _service.ProcessOccurrence(occ.Id, "lease-1");

            Assert.Equal(RecurringGenerationOutcome.DraftCreatedGuardFailed, result.Outcome);
            Assert.Equal(RecurringGuardReasons.ServicePeriodCrossesMonth, Reload(occ.Id).GuardFailureReason);
        }

        [Fact]
        public void ProcessOccurrence_AutoIssue_LockedPostingDate_FailsClosedWithoutPosting()
        {
            // Books locked through June 30; a June 1 document date is in the locked period.
            AddCompanySetting(new DateTime(2026, 1, 1), lockedThrough: new DateTime(2026, 6, 30));
            var owner = AddMember("auto-user", CompanyMemberRole.Owner);
            var customer = AddCustomer();
            var item = ServiceItem();
            SetupItem(item);
            var tax = AddTax(12m);
            var schedule = AddSchedule(customer.Id, RecurringAction.AutoIssue, automationEnabledBy: owner.UserId);
            AddLine(schedule.Id, item.Id, taxId: tax.Id);
            var occ = AddClaimedOccurrence(schedule.Id, new DateTime(2026, 6, 1), "lease-1");

            var result = _service.ProcessOccurrence(occ.Id, "lease-1");

            Assert.Equal(RecurringGenerationOutcome.Failed, result.Outcome);
            Assert.Equal(RecurringErrorCodes.BooksLocked, result.ErrorCode);
            var reloaded = Reload(occ.Id);
            Assert.Equal(RecurringOccurrenceStatus.Failed, reloaded.Status);
            // Fail-closed: no draft left behind, nothing posted.
            Assert.Null(reloaded.SalesInvoiceId);
            _sales.Verify(s => s.PostSalesInvoice(It.IsAny<int>()), Times.Never);
        }

        [Fact]
        public void ProcessOccurrence_AutoIssue_TaxResolvesToZeroOnTaxableLine_FailsClosed()
        {
            AddCompanySetting(new DateTime(2026, 1, 1));
            var owner = AddMember("auto-user", CompanyMemberRole.Owner);
            var customer = AddCustomer();
            var item = ServiceItem();
            SetupItem(item);
            var tax = AddTax(12m); // non-zero rate...
            var schedule = AddSchedule(customer.Id, RecurringAction.AutoIssue, automationEnabledBy: owner.UserId);
            AddLine(schedule.Id, item.Id, taxId: tax.Id);
            var occ = AddClaimedOccurrence(schedule.Id, new DateTime(2026, 6, 1), "lease-1");

            // ...but the saved draft comes back with a zero tax amount (resolution failed): must not issue.
            _taxAmountOnSave = 0m;

            var result = _service.ProcessOccurrence(occ.Id, "lease-1");

            Assert.Equal(RecurringGenerationOutcome.Failed, result.Outcome);
            Assert.Equal(RecurringErrorCodes.TaxNotResolved, result.ErrorCode);
            Assert.Equal(RecurringOccurrenceStatus.Failed, Reload(occ.Id).Status);
            _sales.Verify(s => s.PostSalesInvoice(It.IsAny<int>()), Times.Never);
        }

        [Fact]
        public void ProcessOccurrence_LeaseNotHeld_Aborts()
        {
            AddCompanySetting(new DateTime(2026, 1, 1));
            var customer = AddCustomer();
            var item = ServiceItem();
            SetupItem(item);
            var schedule = AddSchedule(customer.Id, RecurringAction.CreateDraft);
            AddLine(schedule.Id, item.Id);
            var occ = AddClaimedOccurrence(schedule.Id, new DateTime(2026, 6, 1), "other-worker");

            var result = _service.ProcessOccurrence(occ.Id, "lease-1");

            Assert.Equal(RecurringGenerationOutcome.Aborted, result.Outcome);
            Assert.Equal(RecurringOccurrenceStatus.Running, Reload(occ.Id).Status);
            _sales.Verify(s => s.CreateSalesInvoice(It.IsAny<Dto.Sales.SalesInvoice>()), Times.Never);
        }

        [Fact]
        public void ProcessOccurrence_AlreadyIssued_IsIdempotentNoOp()
        {
            AddCompanySetting(new DateTime(2026, 1, 1));
            var customer = AddCustomer();
            var schedule = AddSchedule(customer.Id, RecurringAction.AutoIssue);
            var occ = AddClaimedOccurrence(schedule.Id, new DateTime(2026, 6, 1), "lease-1");

            // An already-issued occurrence pointing at a real invoice row.
            var existingInvoice = new SalesInvoiceHeader { CompanyId = _company.Id, CustomerId = customer.Id, Status = SalesInvoiceStatus.Open };
            _db.SalesInvoiceHeaders.Add(existingInvoice);
            _db.SaveChanges();
            occ.Status = RecurringOccurrenceStatus.Issued;
            occ.SalesInvoiceId = existingInvoice.Id;
            _db.SaveChanges();

            var result = _service.ProcessOccurrence(occ.Id, "lease-1");

            Assert.Equal(RecurringGenerationOutcome.AlreadyProcessed, result.Outcome);
            Assert.Equal(existingInvoice.Id, result.SalesInvoiceId);
            _sales.Verify(s => s.CreateSalesInvoice(It.IsAny<Dto.Sales.SalesInvoice>()), Times.Never);
        }

        [Fact]
        public void EnsureOccurrence_IsIdempotentOnRunDate()
        {
            AddCompanySetting(new DateTime(2026, 1, 1));
            var customer = AddCustomer();
            var schedule = AddSchedule(customer.Id, RecurringAction.CreateDraft);

            var first = _service.EnsureOccurrence(schedule.Id, new DateTime(2026, 6, 1), "lease-1", DateTime.UtcNow.AddMinutes(5));
            var second = _service.EnsureOccurrence(schedule.Id, new DateTime(2026, 6, 1), "lease-1", DateTime.UtcNow.AddMinutes(5));

            Assert.Equal(first, second);
            Assert.Single(_db.RecurringOccurrences.Where(o => o.RecurringScheduleId == schedule.Id));
        }

        // ---- #2: quarterly/yearly must stay drafts even when all other guards pass ----

        [Theory]
        [InlineData(RecurringCadence.Quarterly)]
        [InlineData(RecurringCadence.Yearly)]
        public void ProcessOccurrence_AutoIssue_MultiMonthCadence_DowngradesToDraft(RecurringCadence cadence)
        {
            AddCompanySetting(new DateTime(2026, 1, 1));
            var owner = AddMember("auto-user", CompanyMemberRole.Owner);
            var customer = AddCustomer();
            var item = ServiceItem();
            SetupItem(item);
            var tax = AddTax(12m);
            var schedule = AddSchedule(customer.Id, RecurringAction.AutoIssue, automationEnabledBy: owner.UserId, cadence: cadence);
            AddLine(schedule.Id, item.Id, taxId: tax.Id);
            var occ = AddClaimedOccurrence(schedule.Id, new DateTime(2026, 6, 1), "lease-1");

            var result = _service.ProcessOccurrence(occ.Id, "lease-1");

            Assert.Equal(RecurringGenerationOutcome.DraftCreatedGuardFailed, result.Outcome);
            Assert.Equal(RecurringGuardReasons.MultiMonthCadence, Reload(occ.Id).GuardFailureReason);
            _sales.Verify(s => s.PostSalesInvoice(It.IsAny<int>()), Times.Never);
        }

        // ---- #1 (follow-up): weekly runs bill their own week, not the whole month ----

        [Fact]
        public void ProcessOccurrence_AutoIssue_WeeklyWithinMonth_Issues()
        {
            AddCompanySetting(new DateTime(2026, 1, 1));
            var owner = AddMember("auto-user", CompanyMemberRole.Owner);
            var customer = AddCustomer();
            var item = ServiceItem();
            SetupItem(item);
            var tax = AddTax(12m);
            var schedule = AddSchedule(customer.Id, RecurringAction.AutoIssue, automationEnabledBy: owner.UserId,
                cadence: RecurringCadence.Weekly);
            AddLine(schedule.Id, item.Id, taxId: tax.Id);
            // A week starting mid-month sits inside June.
            var occ = AddClaimedOccurrence(schedule.Id, new DateTime(2026, 6, 8), "lease-1", cadence: RecurringCadence.Weekly);

            _sales.Setup(s => s.PostSalesInvoice(It.IsAny<int>()));

            var result = _service.ProcessOccurrence(occ.Id, "lease-1");

            Assert.Equal(RecurringGenerationOutcome.Issued, result.Outcome);
            // The recorded period is the week, not the month, so distinct weekly runs don't share a period.
            var reloaded = Reload(occ.Id);
            Assert.Equal(new DateTime(2026, 6, 8), reloaded.PeriodStart);
            Assert.Equal(new DateTime(2026, 6, 14), reloaded.PeriodEnd);
        }

        [Fact]
        public void ProcessOccurrence_AutoIssue_WeeklyStraddlingMonthEnd_DowngradesToDraft()
        {
            AddCompanySetting(new DateTime(2026, 1, 1));
            var owner = AddMember("auto-user", CompanyMemberRole.Owner);
            var customer = AddCustomer();
            var item = ServiceItem();
            SetupItem(item);
            var tax = AddTax(12m);
            var schedule = AddSchedule(customer.Id, RecurringAction.AutoIssue, automationEnabledBy: owner.UserId,
                cadence: RecurringCadence.Weekly);
            AddLine(schedule.Id, item.Id, taxId: tax.Id);
            // A week starting June 29 runs into July -> crosses the month boundary -> must stay a draft.
            var occ = AddClaimedOccurrence(schedule.Id, new DateTime(2026, 6, 29), "lease-1", cadence: RecurringCadence.Weekly);

            var result = _service.ProcessOccurrence(occ.Id, "lease-1");

            Assert.Equal(RecurringGenerationOutcome.DraftCreatedGuardFailed, result.Outcome);
            Assert.Equal(RecurringGuardReasons.ServicePeriodCrossesMonth, Reload(occ.Id).GuardFailureReason);
            _sales.Verify(s => s.PostSalesInvoice(It.IsAny<int>()), Times.Never);
        }

        // ---- #6: a tax that no longer resolves makes CreateSalesInvoice throw; fail closed, never left Running ----

        [Fact]
        public void ProcessOccurrence_CreateThrowsForArchivedTax_FailsClosed_NotLeftRunning()
        {
            AddCompanySetting(new DateTime(2026, 1, 1));
            var customer = AddCustomer();
            var item = ServiceItem();
            SetupItem(item);
            var tax = AddTax(12m, active: false); // archived
            var schedule = AddSchedule(customer.Id, RecurringAction.AutoIssue);
            AddLine(schedule.Id, item.Id, taxId: tax.Id);
            var occ = AddClaimedOccurrence(schedule.Id, new DateTime(2026, 6, 1), "lease-1");

            // The real CreateSalesInvoice throws for an archived tax. Simulate that for this occurrence's draft.
            _sales.Setup(s => s.CreateSalesInvoice(It.IsAny<Dto.Sales.SalesInvoice>()))
                .Throws(new InvalidOperationException($"{tax.TaxName} is archived. Pick another tax for this line."));

            var result = _service.ProcessOccurrence(occ.Id, "lease-1");

            Assert.Equal(RecurringGenerationOutcome.Failed, result.Outcome);
            Assert.Equal(RecurringErrorCodes.TaxNotResolved, result.ErrorCode);
            var reloaded = Reload(occ.Id);
            Assert.Equal(RecurringOccurrenceStatus.Failed, reloaded.Status);
            Assert.NotEqual(RecurringOccurrenceStatus.Running, reloaded.Status);
            Assert.Null(reloaded.SalesInvoiceId);
        }

        // ---- #1: a second process of the same occurrence creates no second document ----

        [Fact]
        public void ProcessOccurrence_SecondCallSameOccurrence_DoesNotCreateSecondInvoice()
        {
            AddCompanySetting(new DateTime(2026, 1, 1));
            var customer = AddCustomer();
            var item = ServiceItem();
            SetupItem(item);
            var schedule = AddSchedule(customer.Id, RecurringAction.CreateDraft);
            AddLine(schedule.Id, item.Id);
            var occ = AddClaimedOccurrence(schedule.Id, new DateTime(2026, 6, 1), "lease-1");

            var first = _service.ProcessOccurrence(occ.Id, "lease-1");
            Assert.Equal(RecurringGenerationOutcome.DraftCreated, first.Outcome);

            // A second call (same lease, e.g. a duplicate run-now) must be an idempotent no-op, not a new invoice.
            var second = _service.ProcessOccurrence(occ.Id, "lease-1");
            Assert.Equal(RecurringGenerationOutcome.AlreadyProcessed, second.Outcome);
            Assert.Equal(first.SalesInvoiceId, second.SalesInvoiceId);
            Assert.Single(_db.SalesInvoiceHeaders.Where(i => i.CompanyId == _company.Id));
            _sales.Verify(s => s.CreateSalesInvoice(It.IsAny<Dto.Sales.SalesInvoice>()), Times.Once);
        }

        // ---- #480: exactly-once series advancement (concurrency / crash safety) ----

        [Fact]
        public void RunSchedule_AdvancesSeriesOnce_AndMarksOccurrenceAdvanced()
        {
            AddCompanySetting(new DateTime(2026, 1, 1));
            var customer = AddCustomer();
            var item = ServiceItem();
            SetupItem(item);
            var schedule = AddSchedule(customer.Id, RecurringAction.CreateDraft);
            schedule.NextRunDate = new DateTime(2026, 6, 1);
            _db.SaveChanges();
            AddLine(schedule.Id, item.Id);

            var result = _service.RunSchedule(schedule.Id, new DateTime(2026, 6, 1), "run-1", DateTime.UtcNow.AddMinutes(5));
            Assert.Equal(RecurringGenerationOutcome.DraftCreated, result.Outcome);

            var reloaded = _db.RecurringSchedules.AsNoTracking().First(s => s.Id == schedule.Id);
            Assert.Equal(new DateTime(2026, 7, 1), reloaded.NextRunDate);
            Assert.Equal(1, reloaded.OccurrenceCount);
            var occurrence = _db.RecurringOccurrences.AsNoTracking().Single(o => o.RecurringScheduleId == schedule.Id);
            Assert.True(occurrence.SeriesAdvanced);
        }

        [Fact]
        public void RunSchedule_ReplayOfCompletedOccurrence_DoesNotAdvanceOrCountTwice()
        {
            AddCompanySetting(new DateTime(2026, 1, 1));
            var customer = AddCustomer();
            var item = ServiceItem();
            SetupItem(item);
            var schedule = AddSchedule(customer.Id, RecurringAction.CreateDraft);
            schedule.NextRunDate = new DateTime(2026, 6, 1);
            _db.SaveChanges();
            AddLine(schedule.Id, item.Id);

            var first = _service.RunSchedule(schedule.Id, new DateTime(2026, 6, 1), "run-1", DateTime.UtcNow.AddMinutes(5));
            Assert.Equal(RecurringGenerationOutcome.DraftCreated, first.Outcome);

            // A slow/duplicate caller still holding the original date. The serializable ProcessOccurrence sees the
            // committed document and replays; because the occurrence already advanced the series, the schedule must
            // be untouched (no second advance, no second count, no second invoice).
            var replay = _db.RecurringSchedules.First(s => s.Id == schedule.Id);
            replay.NextRunDate = new DateTime(2026, 6, 1);
            _db.SaveChanges();

            var second = _service.RunSchedule(schedule.Id, new DateTime(2026, 6, 1), "run-2", DateTime.UtcNow.AddMinutes(5));
            Assert.Equal(RecurringGenerationOutcome.AlreadyProcessed, second.Outcome);
            Assert.Equal(first.SalesInvoiceId, second.SalesInvoiceId);

            var reloaded = _db.RecurringSchedules.AsNoTracking().First(s => s.Id == schedule.Id);
            Assert.Equal(new DateTime(2026, 6, 1), reloaded.NextRunDate); // untouched by the replay
            Assert.Equal(1, reloaded.OccurrenceCount);
            Assert.Single(_db.SalesInvoiceHeaders.Where(i => i.CompanyId == _company.Id));
            _sales.Verify(s => s.CreateSalesInvoice(It.IsAny<Dto.Sales.SalesInvoice>()), Times.Once);
        }

        [Fact]
        public void RunSchedule_RecoversCompletedButUnadvancedOccurrence_ExactlyOnce()
        {
            // Crash safety: the invoice committed but the series advance did not (SeriesAdvanced=false, NextRunDate
            // still on the completed occurrence). The next run must recover by advancing exactly once, creating no
            // second document.
            AddCompanySetting(new DateTime(2026, 1, 1));
            var customer = AddCustomer();
            var item = ServiceItem();
            SetupItem(item);
            var schedule = AddSchedule(customer.Id, RecurringAction.CreateDraft);
            schedule.NextRunDate = new DateTime(2026, 6, 1);
            _db.SaveChanges();
            AddLine(schedule.Id, item.Id);

            var first = _service.RunSchedule(schedule.Id, new DateTime(2026, 6, 1), "run-1", DateTime.UtcNow.AddMinutes(5));
            Assert.Equal(RecurringGenerationOutcome.DraftCreated, first.Outcome);

            // Reproduce the stranded post-crash state.
            var stranded = _db.RecurringSchedules.First(s => s.Id == schedule.Id);
            stranded.NextRunDate = new DateTime(2026, 6, 1);
            stranded.OccurrenceCount = 0;
            var occ = _db.RecurringOccurrences.First(o => o.RecurringScheduleId == schedule.Id);
            occ.SeriesAdvanced = false;
            _db.SaveChanges();

            var recover = _service.RunSchedule(schedule.Id, new DateTime(2026, 6, 1), "run-2", DateTime.UtcNow.AddMinutes(5));
            Assert.Equal(RecurringGenerationOutcome.AlreadyProcessed, recover.Outcome);
            Assert.Equal(first.SalesInvoiceId, recover.SalesInvoiceId);

            var reloaded = _db.RecurringSchedules.AsNoTracking().First(s => s.Id == schedule.Id);
            Assert.Equal(new DateTime(2026, 7, 1), reloaded.NextRunDate); // advanced by recovery
            Assert.Equal(1, reloaded.OccurrenceCount);                    // counted exactly once
            Assert.True(_db.RecurringOccurrences.AsNoTracking().First(o => o.Id == occ.Id).SeriesAdvanced);
            Assert.Single(_db.SalesInvoiceHeaders.Where(i => i.CompanyId == _company.Id));
            _sales.Verify(s => s.CreateSalesInvoice(It.IsAny<Dto.Sales.SalesInvoice>()), Times.Once);
        }

        [Fact]
        public async Task ProcessOccurrence_ConcurrentStaleContexts_CreateOnlyOneInvoice()
        {
            var customer = AddCustomer();
            var item = ServiceItem();
            SetupItem(item);
            var schedule = AddSchedule(customer.Id, RecurringAction.CreateDraft);
            AddLine(schedule.Id, item.Id);
            var occurrence = AddClaimedOccurrence(schedule.Id, new DateTime(2026, 6, 1), "same-owner");
            using var secondDb = CreateSiblingContext();
            _ = secondDb.RecurringOccurrences.Single(o => o.Id == occurrence.Id);
            _ = secondDb.RecurringSchedules.Single(s => s.Id == schedule.Id);
            var secondSales = CreateSiblingSales(secondDb);
            using var firstCreating = new ManualResetEventSlim();
            using var secondStarting = new ManualResetEventSlim();
            var secondService = CreateService(secondDb, secondSales.Object,
                new BeforeTransactionUnitOfWork(new EfUnitOfWork(secondDb), _ => secondStarting.Set()));

            _sales.Setup(s => s.CreateSalesInvoice(It.IsAny<Dto.Sales.SalesInvoice>()))
                .Returns<Dto.Sales.SalesInvoice>(dto =>
                {
                    firstCreating.Set();
                    Assert.True(secondStarting.Wait(TimeSpan.FromSeconds(10)));
                    return Core.Domain.Error.Result<Dto.Sales.SalesInvoice>.Success(PersistDraftFromDto(dto));
                });

            var first = Task.Run(() => _service.ProcessOccurrence(occurrence.Id, "same-owner"));
            var second = Task.Run(() =>
            {
                Assert.True(firstCreating.Wait(TimeSpan.FromSeconds(10)));
                return secondService.ProcessOccurrence(occurrence.Id, "same-owner");
            });
            var results = await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(60));

            Assert.Equal(RecurringGenerationOutcome.DraftCreated, results[0].Outcome);
            Assert.Equal(RecurringGenerationOutcome.AlreadyProcessed, results[1].Outcome);
            Assert.Equal(results[0].SalesInvoiceId, results[1].SalesInvoiceId);
            Assert.Single(_db.SalesInvoiceHeaders.AsNoTracking());
            Assert.Equal(1, _db.RecurringSchedules.AsNoTracking().Single(s => s.Id == schedule.Id).OccurrenceCount);
            secondSales.Verify(s => s.CreateSalesInvoice(It.IsAny<Dto.Sales.SalesInvoice>()), Times.Never);
        }

        [Fact]
        public void ProcessOccurrence_AnotherAttemptCommitsAfterRollback_DoesNotOverwriteInvoice()
        {
            var customer = AddCustomer();
            var item = ServiceItem();
            SetupItem(item);
            var schedule = AddSchedule(customer.Id, RecurringAction.CreateDraft);
            AddLine(schedule.Id, item.Id);
            var occurrence = AddClaimedOccurrence(schedule.Id, new DateTime(2026, 6, 1), "same-owner");
            using var secondDb = CreateSiblingContext();
            var secondSales = CreateSiblingSales(secondDb);
            var secondService = CreateService(secondDb, secondSales.Object);
            RecurringGenerationResult? completed = null;
            var unitOfWork = new BeforeTransactionUnitOfWork(new EfUnitOfWork(_db), count =>
            {
                if (count == 2)
                    completed = secondService.ProcessOccurrence(occurrence.Id, "same-owner");
            });
            var service = CreateService(_db, _sales.Object, unitOfWork);
            _sales.Setup(s => s.CreateSalesInvoice(It.IsAny<Dto.Sales.SalesInvoice>()))
                .Throws(new InvalidOperationException("Transient draft failure"));

            var result = service.ProcessOccurrence(occurrence.Id, "same-owner");

            Assert.NotNull(completed);
            Assert.Equal(RecurringGenerationOutcome.DraftCreated, completed.Outcome);
            Assert.Equal(RecurringGenerationOutcome.AlreadyProcessed, result.Outcome);
            Assert.Equal(completed.SalesInvoiceId, result.SalesInvoiceId);
            var persisted = Reload(occurrence.Id);
            Assert.Equal(RecurringOccurrenceStatus.DraftCreated, persisted.Status);
            Assert.Equal(completed.SalesInvoiceId, persisted.SalesInvoiceId);
            Assert.True(persisted.SeriesAdvanced);
            Assert.Single(_db.SalesInvoiceHeaders.AsNoTracking());
        }

        [Fact]
        public void ProcessOccurrence_ClaimReplacedAfterRollback_DoesNotMarkNewAttemptFailed()
        {
            var customer = AddCustomer();
            var item = ServiceItem();
            SetupItem(item);
            var schedule = AddSchedule(customer.Id, RecurringAction.CreateDraft);
            AddLine(schedule.Id, item.Id);
            var occurrence = AddClaimedOccurrence(schedule.Id, new DateTime(2026, 6, 1), "original-owner");
            using var secondDb = CreateSiblingContext();
            var unitOfWork = new BeforeTransactionUnitOfWork(new EfUnitOfWork(_db), count =>
            {
                if (count == 2)
                {
                    var reclaimed = secondDb.RecurringOccurrences.Single(o => o.Id == occurrence.Id);
                    reclaimed.LeasedBy = "new-owner";
                    reclaimed.LeasedUntil = DateTime.UtcNow.AddMinutes(10);
                    secondDb.SaveChanges();
                }
            });
            var service = CreateService(_db, _sales.Object, unitOfWork);
            _sales.Setup(s => s.CreateSalesInvoice(It.IsAny<Dto.Sales.SalesInvoice>()))
                .Throws(new InvalidOperationException("Transient draft failure"));

            var result = service.ProcessOccurrence(occurrence.Id, "original-owner");

            Assert.Equal(RecurringGenerationOutcome.Aborted, result.Outcome);
            var persisted = Reload(occurrence.Id);
            Assert.Equal(RecurringOccurrenceStatus.Running, persisted.Status);
            Assert.Equal("new-owner", persisted.LeasedBy);
            Assert.False(persisted.SeriesAdvanced);
            Assert.Equal(0, persisted.AttemptCount);
        }

        [Fact]
        public void ProcessOccurrence_FailureSaveThrows_RollsBackFailureAndSeriesTogether()
        {
            var customer = AddCustomer();
            var item = ServiceItem();
            SetupItem(item);
            var schedule = AddSchedule(customer.Id, RecurringAction.CreateDraft);
            AddLine(schedule.Id, item.Id);
            schedule.NextRunDate = new DateTime(2026, 6, 1);
            _db.SaveChanges();
            var occurrence = AddClaimedOccurrence(schedule.Id, schedule.NextRunDate.Value, "owner");
            var realRepo = new EfRepository<RecurringOccurrence>(NullLogger<RecurringOccurrence>.Instance, _db);
            var occurrences = new Mock<IRepository<RecurringOccurrence>>();
            occurrences.SetupGet(r => r.Table).Returns(() => realRepo.Table);
            occurrences.Setup(r => r.Update(It.IsAny<RecurringOccurrence>()))
                .Callback<RecurringOccurrence>(o =>
                {
                    if (o.Status == RecurringOccurrenceStatus.Failed && o.SeriesAdvanced)
                        throw new InvalidOperationException("Interrupted failure save");
                    realRepo.Update(o);
                });
            _sales.Setup(s => s.CreateSalesInvoice(It.IsAny<Dto.Sales.SalesInvoice>()))
                .Throws(new InvalidOperationException("Transient draft failure"));
            var service = CreateService(_db, _sales.Object, occurrences: occurrences.Object);

            Assert.Throws<InvalidOperationException>(() => service.ProcessOccurrence(occurrence.Id, "owner"));

            var persisted = Reload(occurrence.Id);
            Assert.Equal(RecurringOccurrenceStatus.Running, persisted.Status);
            Assert.False(persisted.SeriesAdvanced);
            Assert.Equal(0, persisted.AttemptCount);
            var persistedSchedule = _db.RecurringSchedules.AsNoTracking().Single(s => s.Id == schedule.Id);
            Assert.Equal(new DateTime(2026, 6, 1), persistedSchedule.NextRunDate);
            Assert.Equal(0, persistedSchedule.OccurrenceCount);
            Assert.Empty(_db.SalesInvoiceHeaders.AsNoTracking());
        }

        private sealed class BeforeTransactionUnitOfWork(IUnitOfWork inner, Action<int> beforeBegin) : IUnitOfWork
        {
            private int _transactionCount;

            public ITransactionScope BeginSerializableTransaction()
            {
                beforeBegin(++_transactionCount);
                return inner.BeginSerializableTransaction();
            }

            public void DiscardPendingChanges() => inner.DiscardPendingChanges();
        }

        [Fact]
        public void EnsureOccurrence_NonScheduledDate_Throws()
        {
            AddCompanySetting(new DateTime(2026, 1, 1));
            var customer = AddCustomer();
            // Monthly on day 1 from 2026-01-01; the 15th is never a run date.
            var schedule = AddSchedule(customer.Id, RecurringAction.CreateDraft);

            Assert.Throws<InvalidRunDateException>(() =>
                _service.EnsureOccurrence(schedule.Id, new DateTime(2026, 6, 15), "lease-1", DateTime.UtcNow.AddMinutes(5)));
        }

        [Fact]
        public void EnsureOccurrence_BeforeStartDate_Throws()
        {
            AddCompanySetting(new DateTime(2025, 1, 1));
            var customer = AddCustomer();
            var schedule = AddSchedule(customer.Id, RecurringAction.CreateDraft); // starts 2026-01-01

            Assert.Throws<InvalidRunDateException>(() =>
                _service.EnsureOccurrence(schedule.Id, new DateTime(2025, 12, 1), "lease-1", DateTime.UtcNow.AddMinutes(5)));
        }

        [Fact]
        public void EnsureOccurrence_AfterEndDate_Throws()
        {
            AddCompanySetting(new DateTime(2026, 1, 1));
            var customer = AddCustomer();
            var schedule = AddSchedule(customer.Id, RecurringAction.CreateDraft);
            schedule.EndDate = new DateTime(2026, 3, 31);
            _db.SaveChanges();

            Assert.Throws<InvalidRunDateException>(() =>
                _service.EnsureOccurrence(schedule.Id, new DateTime(2026, 6, 1), "lease-1", DateTime.UtcNow.AddMinutes(5)));
        }

        [Fact]
        public void EnsureOccurrence_PastMaxOccurrences_Throws()
        {
            AddCompanySetting(new DateTime(2026, 1, 1));
            var customer = AddCustomer();
            var schedule = AddSchedule(customer.Id, RecurringAction.CreateDraft);
            schedule.MaxOccurrences = 2; // only 2026-01-01 and 2026-02-01
            _db.SaveChanges();

            // The 3rd run date (2026-03-01) is past the limit.
            Assert.Throws<InvalidRunDateException>(() =>
                _service.EnsureOccurrence(schedule.Id, new DateTime(2026, 3, 1), "lease-1", DateTime.UtcNow.AddMinutes(5)));
        }

        [Fact]
        public void EnsureOccurrence_ValidSeriesDate_Creates()
        {
            AddCompanySetting(new DateTime(2026, 1, 1));
            var customer = AddCustomer();
            var schedule = AddSchedule(customer.Id, RecurringAction.CreateDraft);

            var id = _service.EnsureOccurrence(schedule.Id, new DateTime(2026, 4, 1), "lease-1", DateTime.UtcNow.AddMinutes(5));

            Assert.True(id > 0);
            var occ = _db.RecurringOccurrences.AsNoTracking().Single(o => o.Id == id);
            Assert.Equal(new DateTime(2026, 4, 1), occ.ScheduledRunDate);
        }

        // ---- paused/ended schedules must not generate ----

        [Fact]
        public void EnsureOccurrence_PausedSchedule_NewDate_Throws()
        {
            AddCompanySetting(new DateTime(2026, 1, 1));
            var customer = AddCustomer();
            var schedule = AddSchedule(customer.Id, RecurringAction.CreateDraft, status: RecurringScheduleStatus.Paused);

            var ex = Assert.Throws<InvalidRunDateException>(() =>
                _service.EnsureOccurrence(schedule.Id, new DateTime(2026, 4, 1), "lease-1", DateTime.UtcNow.AddMinutes(5)));
            Assert.Contains("paused", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(_db.RecurringOccurrences.Where(o => o.RecurringScheduleId == schedule.Id));
        }

        [Fact]
        public void EnsureOccurrence_PausedSchedule_DoesNotReclaimExistingScheduledOccurrence()
        {
            AddCompanySetting(new DateTime(2026, 1, 1));
            var customer = AddCustomer();
            var schedule = AddSchedule(customer.Id, RecurringAction.CreateDraft, status: RecurringScheduleStatus.Paused);

            // An occurrence that already exists (e.g. created before the schedule was paused), still Scheduled.
            var occ = new RecurringOccurrence
            {
                CompanyId = _company.Id,
                RecurringScheduleId = schedule.Id,
                ScheduledRunDate = new DateTime(2026, 4, 1),
                DocumentDate = new DateTime(2026, 4, 1),
                PeriodStart = new DateTime(2026, 4, 1),
                PeriodEnd = new DateTime(2026, 4, 30),
                Status = RecurringOccurrenceStatus.Scheduled,
                IdempotencyKey = $"{_company.Id}-{schedule.Id}-2026-04-01",
                CreatedAt = DateTime.UtcNow,
            };
            _db.RecurringOccurrences.Add(occ);
            _db.SaveChanges();

            Assert.Throws<InvalidRunDateException>(() =>
                _service.EnsureOccurrence(schedule.Id, new DateTime(2026, 4, 1), "lease-1", DateTime.UtcNow.AddMinutes(5)));

            // The occurrence was not claimed (still Scheduled, no lease).
            var reloaded = _db.RecurringOccurrences.AsNoTracking().Single(o => o.Id == occ.Id);
            Assert.Equal(RecurringOccurrenceStatus.Scheduled, reloaded.Status);
            Assert.Null(reloaded.LeasedBy);
        }

        [Fact]
        public void ProcessOccurrence_SchedulePausedAfterClaim_Aborts_NoDocument()
        {
            AddCompanySetting(new DateTime(2026, 1, 1));
            var customer = AddCustomer();
            var item = ServiceItem();
            SetupItem(item);
            // Active at claim time, so the occurrence is legitimately Running...
            var schedule = AddSchedule(customer.Id, RecurringAction.CreateDraft);
            AddLine(schedule.Id, item.Id);
            var occ = AddClaimedOccurrence(schedule.Id, new DateTime(2026, 6, 1), "lease-1");

            // ...then the schedule is paused before the run is processed.
            schedule.Status = RecurringScheduleStatus.Paused;
            _db.SaveChanges();

            var result = _service.ProcessOccurrence(occ.Id, "lease-1");

            Assert.Equal(RecurringGenerationOutcome.Aborted, result.Outcome);
            var reloaded = Reload(occ.Id);
            Assert.Null(reloaded.SalesInvoiceId);
            Assert.NotEqual(RecurringOccurrenceStatus.DraftCreated, reloaded.Status);
            _sales.Verify(s => s.CreateSalesInvoice(It.IsAny<Dto.Sales.SalesInvoice>()), Times.Never);
        }

        [Fact]
        public void EnsureOccurrence_EndedSchedule_Throws()
        {
            AddCompanySetting(new DateTime(2026, 1, 1));
            var customer = AddCustomer();
            var schedule = AddSchedule(customer.Id, RecurringAction.CreateDraft, status: RecurringScheduleStatus.Ended);

            var ex = Assert.Throws<InvalidRunDateException>(() =>
                _service.EnsureOccurrence(schedule.Id, new DateTime(2026, 4, 1), "lease-1", DateTime.UtcNow.AddMinutes(5)));
            Assert.Contains("ended", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
    }
}
