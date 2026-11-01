// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable disable
using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
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

        // ---- #1 (follow-up): run-now returns curated error text, never raw internal detail ----

        [SqliteFact]
        public async Task Recurring_RunNow_LockedDate_ReturnsCuratedMessage_NotRawDetail()
        {
            // Books locked through the end of the run month, so an auto-issue run fails closed on the posting date.
            var runMonth = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
            var runDate = runMonth.AddDays(14); // 15th
            int scheduleId = SeedSalesSchedule("RUN-LOCK", RecurringAction.AutoIssue, runDate, _ownerId);

            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
                using (scope.ServiceProvider.GetRequiredService<ITenantContext>().Impersonate(_companyId, _slug, CompanyMemberRole.Owner))
                {
                    var setting = db.CompanySettings.Single();
                    setting.BooksLockedThrough = runMonth.AddMonths(1).AddDays(-1); // end of the run month
                    db.SaveChanges();
                }
            }

            var response = await Send(HttpMethod.Post, $"/api/RecurringSchedules/{scheduleId}/run", new { runDate = IsoDate(runDate) });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await Json(response);

            Assert.Equal("Failed", body.GetProperty("outcome").GetString());
            Assert.Equal("BOOKS_LOCKED", body.GetProperty("errorCode").GetString());
            var message = body.GetProperty("message").GetString() ?? "";
            // Curated, safe text only; no raw exception/stack/SQL detail.
            Assert.Contains("locked period", message);
            Assert.DoesNotContain("Exception", message);
            Assert.DoesNotContain("SQLite", message);
            Assert.DoesNotContain(" at ", message);
        }

        // ---- permission split: create vs post on run-now ----

        [SqliteFact]
        public async Task Recurring_RunNow_PermissionSplit_CreateAllowsDraft_PostRequiredForAutoIssue()
        {
            var runDate = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 12);

            // A draft-only schedule and an auto-issue schedule, both owned by the workspace.
            int draftScheduleId = SeedSalesSchedule("RUN-PERM-DRAFT", RecurringAction.CreateDraft, runDate, _ownerId);
            int autoScheduleId = SeedSalesSchedule("RUN-PERM-AUTO", RecurringAction.AutoIssue, runDate, _ownerId);

            // _otherToken joins as a Sales Representative: has sales.invoices.create, not sales.invoices.post.
            await JoinOther();
            Assert.Equal(HttpStatusCode.NoContent, (await Send(HttpMethod.Patch, Current + "/members/" + MemberId(_otherId),
                new { securityRoleIds = new[] { RoleId("Sales Representative") } })).StatusCode);

            // Draft-only run-now needs only create -> allowed.
            var draftRes = await Send(HttpMethod.Post, $"/api/RecurringSchedules/{draftScheduleId}/run",
                new { runDate = IsoDate(runDate) }, token: _otherToken);
            Assert.Equal(HttpStatusCode.OK, draftRes.StatusCode);
            Assert.Equal("DraftCreated", (await Json(draftRes)).GetProperty("outcome").GetString());

            // Auto-issue run-now may post -> requires post, which the Sales Rep lacks -> 403.
            var autoRes = await Send(HttpMethod.Post, $"/api/RecurringSchedules/{autoScheduleId}/run",
                new { runDate = IsoDate(runDate) }, token: _otherToken);
            Assert.Equal(HttpStatusCode.Forbidden, autoRes.StatusCode);
            Assert.Contains("sales.invoices.post", await autoRes.Content.ReadAsStringAsync());
        }

        /// <summary>Seeds a one-line service-item sales schedule whose NextRunDate is the given run date.</summary>
        private int SeedSalesSchedule(string tag, RecurringAction action, DateTime runDate, string automationEnabledBy)
        {
            var scheduleId = 0;
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            using (scope.ServiceProvider.GetRequiredService<ITenantContext>().Impersonate(_companyId, _slug, CompanyMemberRole.Owner))
            {
                var salesAccountId = db.Accounts.Single(a => a.AccountCode == "40100").Id;
                var measurementId = db.Measurements.First().Id;

                var customer = new Core.Domain.Sales.Customer { CompanyId = _companyId, No = tag + "-CUST" };
                db.Customers.Add(customer);
                var category = new Core.Domain.Items.ItemCategory { CompanyId = _companyId, Name = tag + "-Cat", ItemType = Core.Domain.ItemTypes.Service };
                db.ItemCategories.Add(category);
                db.SaveChanges();

                var item = new Core.Domain.Items.Item
                {
                    CompanyId = _companyId, No = tag + "-ITEM", Description = "Service " + tag,
                    ItemCategoryId = category.Id, SalesAccountId = salesAccountId,
                    SellMeasurementId = measurementId, SmallestMeasurementId = measurementId, Price = 100m,
                };
                db.Items.Add(item);
                db.SaveChanges();

                var schedule = new RecurringSchedule
                {
                    CompanyId = _companyId, Kind = RecurringScheduleKind.SalesInvoice, PartyId = customer.Id,
                    Name = tag, Cadence = RecurringCadence.Monthly, Interval = 1, DayOfMonth = runDate.Day,
                    StartDate = runDate, NextRunDate = runDate, Action = action,
                    ServicePeriodMode = RecurringServicePeriodMode.CurrentMonth,
                    AutomationEnabledBy = automationEnabledBy,
                };
                db.RecurringSchedules.Add(schedule);
                db.SaveChanges();
                scheduleId = schedule.Id;

                db.RecurringScheduleLines.Add(new RecurringScheduleLine
                {
                    CompanyId = _companyId, RecurringScheduleId = schedule.Id, ItemId = item.Id, Quantity = 1, Amount = 100m,
                });
                db.SaveChanges();
            }
            return scheduleId;
        }
    }
}
