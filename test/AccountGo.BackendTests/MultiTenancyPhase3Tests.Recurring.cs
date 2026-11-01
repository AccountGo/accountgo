// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable disable
using System;
using System.Linq;
using Api.Data;
using Core.Domain;
using Core.Domain.Items;
using Core.Domain.Recurring;
using Core.Domain.Sales;
using Core.Tenancy;
using Microsoft.Extensions.DependencyInjection;
using Services.Recurring;
using Xunit;

namespace AccountGo.BackendTests
{
    // #482: processing a claimed occurrence through the real generation service + real SalesService against a
    // provisioned workspace, so the whole draft-and-issue path (including GL posting) is exercised end to end.
    public partial class MultiTenancyPhase3Tests
    {
        [SqliteFact]
        public void Recurring_AutoIssue_AllGuardsPass_PostsInvoiceToLedger()
        {
            // The seeded fiscal year covers the current UTC year; keep the document inside it and mid-month so the
            // service period (the whole calendar month) and the document date share a month.
            var documentDate = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 15);

            int occurrenceId;
            int scheduleId;
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
                var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
                using (tenant.Impersonate(_companyId, _slug, CompanyMemberRole.Owner))
                {
                    var salesAccountId = db.Accounts.Single(a => a.AccountCode == "40100").Id;
                    var measurementId = db.Measurements.First().Id;

                    var customer = new Customer { CompanyId = _companyId, No = "RECUR-CUST-1" };
                    db.Customers.Add(customer);

                    var category = new ItemCategory { CompanyId = _companyId, Name = "Services", ItemType = ItemTypes.Service };
                    db.ItemCategories.Add(category);
                    db.SaveChanges();

                    var item = new Item
                    {
                        CompanyId = _companyId,
                        No = "RETAINER",
                        Description = "Monthly retainer",
                        ItemCategoryId = category.Id,
                        SalesAccountId = salesAccountId,
                        SellMeasurementId = measurementId,
                        SmallestMeasurementId = measurementId,
                        Price = 1000m,
                    };
                    db.Items.Add(item);
                    db.SaveChanges();

                    var schedule = new RecurringSchedule
                    {
                        CompanyId = _companyId,
                        Kind = RecurringScheduleKind.SalesInvoice,
                        PartyId = customer.Id,
                        Name = "Retainer",
                        Cadence = RecurringCadence.Monthly,
                        Interval = 1,
                        DayOfMonth = 15,
                        StartDate = documentDate,
                        Action = RecurringAction.AutoIssue,
                        ServicePeriodMode = RecurringServicePeriodMode.CurrentMonth,
                        AutomationEnabledBy = _ownerId,   // the workspace owner holds every permission
                    };
                    db.RecurringSchedules.Add(schedule);
                    db.SaveChanges();
                    scheduleId = schedule.Id;

                    db.RecurringScheduleLines.Add(new RecurringScheduleLine
                    {
                        CompanyId = _companyId,
                        RecurringScheduleId = schedule.Id,
                        ItemId = item.Id,
                        Quantity = 1,
                        Amount = 1000m,
                    });
                    db.SaveChanges();

                    var generation = scope.ServiceProvider.GetRequiredService<IRecurringInvoiceGenerationService>();
                    occurrenceId = generation.EnsureOccurrence(scheduleId, documentDate, "test-run", DateTime.UtcNow.AddMinutes(5));

                    var result = generation.ProcessOccurrence(occurrenceId, "test-run");

                    Assert.Equal(RecurringGenerationOutcome.Issued, result.Outcome);
                    Assert.NotNull(result.SalesInvoiceId);
                }
            }

            // Reload in a fresh scope: the invoice is issued (Open) and posted to the ledger, the occurrence Issued.
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
                using (scope.ServiceProvider.GetRequiredService<ITenantContext>().Impersonate(_companyId, _slug, CompanyMemberRole.Owner))
                {
                    var occurrence = db.RecurringOccurrences.Single(o => o.Id == occurrenceId);
                    Assert.Equal(RecurringOccurrenceStatus.Issued, occurrence.Status);
                    Assert.NotNull(occurrence.SalesInvoiceId);

                    var invoice = db.SalesInvoiceHeaders.Single(i => i.Id == occurrence.SalesInvoiceId.Value);
                    Assert.Equal(SalesInvoiceStatus.Open, invoice.Status);
                    Assert.True(invoice.GeneralLedgerHeaderId.HasValue, "Issued invoice must be posted to the ledger.");
                    Assert.False(string.IsNullOrEmpty(invoice.No), "Issued invoice must carry a number.");
                }
            }
        }

        [SqliteFact]
        public void Recurring_RunNow_IsIdempotent_SecondRunReturnsSameInvoice()
        {
            var documentDate = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 10);

            int scheduleId;
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
                using (scope.ServiceProvider.GetRequiredService<ITenantContext>().Impersonate(_companyId, _slug, CompanyMemberRole.Owner))
                {
                    var measurementId = db.Measurements.First().Id;
                    var salesAccountId = db.Accounts.Single(a => a.AccountCode == "40100").Id;

                    var customer = new Customer { CompanyId = _companyId, No = "RECUR-CUST-2" };
                    db.Customers.Add(customer);
                    var category = new ItemCategory { CompanyId = _companyId, Name = "Services2", ItemType = ItemTypes.Service };
                    db.ItemCategories.Add(category);
                    db.SaveChanges();
                    var item = new Item
                    {
                        CompanyId = _companyId, No = "RETAINER-2", Description = "Retainer 2",
                        ItemCategoryId = category.Id, SalesAccountId = salesAccountId,
                        SellMeasurementId = measurementId, SmallestMeasurementId = measurementId, Price = 500m,
                    };
                    db.Items.Add(item);
                    db.SaveChanges();

                    var schedule = new RecurringSchedule
                    {
                        CompanyId = _companyId, Kind = RecurringScheduleKind.SalesInvoice, PartyId = customer.Id,
                        Name = "Retainer 2", Cadence = RecurringCadence.Monthly, Interval = 1, DayOfMonth = 10,
                        StartDate = documentDate, Action = RecurringAction.CreateDraft,
                        ServicePeriodMode = RecurringServicePeriodMode.CurrentMonth,
                    };
                    db.RecurringSchedules.Add(schedule);
                    db.SaveChanges();
                    scheduleId = schedule.Id;
                    db.RecurringScheduleLines.Add(new RecurringScheduleLine
                    {
                        CompanyId = _companyId, RecurringScheduleId = schedule.Id, ItemId = item.Id, Quantity = 1, Amount = 500m,
                    });
                    db.SaveChanges();

                    var generation = scope.ServiceProvider.GetRequiredService<IRecurringInvoiceGenerationService>();
                    var occ1 = generation.EnsureOccurrence(scheduleId, documentDate, "run-1", DateTime.UtcNow.AddMinutes(5));
                    var first = generation.ProcessOccurrence(occ1, "run-1");
                    Assert.Equal(RecurringGenerationOutcome.DraftCreated, first.Outcome);

                    // A second run for the same date resolves the same occurrence and does not create a new document.
                    var occ2 = generation.EnsureOccurrence(scheduleId, documentDate, "run-2", DateTime.UtcNow.AddMinutes(5));
                    Assert.Equal(occ1, occ2);
                    var second = generation.ProcessOccurrence(occ2, "run-2");
                    Assert.Equal(RecurringGenerationOutcome.AlreadyProcessed, second.Outcome);
                    Assert.Equal(first.SalesInvoiceId, second.SalesInvoiceId);

                    Assert.Single(db.RecurringOccurrences.Where(o => o.RecurringScheduleId == scheduleId));
                }
            }
        }
    }
}
