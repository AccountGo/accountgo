// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Linq;
using Api.Data;
using Api.Tenancy;
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
            _connection = new SqliteConnection("DataSource=:memory:");
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

            _service = new RecurringInvoiceGenerationService(
                _tenant,
                new EfUnitOfWork(_db),
                _sales.Object,
                _inventory.Object,
                new EfRepository<RecurringOccurrence>(NullLogger<RecurringOccurrence>.Instance, _db),
                new EfRepository<RecurringSchedule>(NullLogger<RecurringSchedule>.Instance, _db),
                new EfRepository<RecurringScheduleLine>(NullLogger<RecurringScheduleLine>.Instance, _db),
                new EfRepository<Customer>(NullLogger<Customer>.Instance, _db),
                new EfRepository<CompanySetting>(NullLogger<CompanySetting>.Instance, _db),
                new EfRepository<Tax>(NullLogger<Tax>.Instance, _db),
                new EfRepository<CompanyMember>(NullLogger<CompanyMember>.Instance, _db),
                new EfRepository<SecurityRolePermission>(NullLogger<SecurityRolePermission>.Instance, _db),
                NullLogger<RecurringInvoiceGenerationService>.Instance);
        }

        public void Dispose()
        {
            _db.Dispose();
            _connection.Dispose();
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
            RecurringCadence cadence = RecurringCadence.Monthly)
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
            RecurringServicePeriodMode periodMode = RecurringServicePeriodMode.CurrentMonth)
        {
            var (periodStart, periodEnd) = Core.Domain.Recurring.Calendar.NextDateCalculator
                .ComputeServicePeriod(documentDate, periodMode);
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
        private Dto.Sales.SalesInvoice PersistDraftFromDto(Dto.Sales.SalesInvoice dto)
        {
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
            _db.SalesInvoiceHeaders.Add(header);
            _db.SaveChanges();
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

        // ---- #3: run-date validation in EnsureOccurrence ----

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
    }
}
