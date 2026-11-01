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

        [SqliteFact]
        public async Task Recurring_RunNow_PausedSchedule_IsRejected_NoDocument()
        {
            var runDate = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 9);
            int scheduleId = SeedSalesSchedule("RUN-PAUSED", RecurringAction.AutoIssue, runDate, _ownerId,
                status: RecurringScheduleStatus.Paused);

            var response = await Send(HttpMethod.Post, $"/api/RecurringSchedules/{scheduleId}/run", new { runDate = IsoDate(runDate) });

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("paused", (await response.Content.ReadAsStringAsync()), StringComparison.OrdinalIgnoreCase);

            // No occurrence and no invoice were created.
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            using (scope.ServiceProvider.GetRequiredService<ITenantContext>().Impersonate(_companyId, _slug, CompanyMemberRole.Owner))
            {
                Assert.Empty(db.RecurringOccurrences.Where(o => o.RecurringScheduleId == scheduleId));
            }
        }

        // ---- #480: next-date advancement, retry, and archived-customer pause ----

        [SqliteFact]
        public void Recurring_RunSchedule_ForwardRun_AdvancesNextRunDate()
        {
            var runDate = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 5);
            int scheduleId = SeedSalesSchedule("ADV-FWD", RecurringAction.CreateDraft, runDate, _ownerId);

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            using (scope.ServiceProvider.GetRequiredService<ITenantContext>().Impersonate(_companyId, _slug, CompanyMemberRole.Owner))
            {
                var generation = scope.ServiceProvider.GetRequiredService<IRecurringInvoiceGenerationService>();
                var result = generation.RunSchedule(scheduleId, null, "run-adv", DateTime.UtcNow.AddMinutes(5));
                Assert.Equal(RecurringGenerationOutcome.DraftCreated, result.Outcome);

                var schedule = db.RecurringSchedules.Single(s => s.Id == scheduleId);
                // Monthly, day 5: the next run is the 5th of next month, computed from the calendar, not "now".
                Assert.Equal(runDate.AddMonths(1), schedule.NextRunDate);
                Assert.Equal(1, schedule.OccurrenceCount);
                Assert.Equal(RecurringScheduleStatus.Active, schedule.Status);
            }
        }

        [SqliteFact]
        public void Recurring_RunSchedule_FailedForwardRun_StillAdvances_AndOccurrenceIsRetryable()
        {
            // An auto-issue run whose posting date is locked fails closed, but the series still moves forward: the
            // failed occurrence is retried against its own key, not by rewinding NextRunDate (#480 / #479 AC).
            var runMonth = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
            var runDate = runMonth.AddDays(7); // 8th
            int scheduleId = SeedSalesSchedule("ADV-FAIL", RecurringAction.AutoIssue, runDate, _ownerId);

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            using (scope.ServiceProvider.GetRequiredService<ITenantContext>().Impersonate(_companyId, _slug, CompanyMemberRole.Owner))
            {
                var setting = db.CompanySettings.Single();
                setting.BooksLockedThrough = runMonth.AddMonths(1).AddDays(-1); // lock the whole run month
                db.SaveChanges();

                var generation = scope.ServiceProvider.GetRequiredService<IRecurringInvoiceGenerationService>();
                var result = generation.RunSchedule(scheduleId, null, "run-fail", DateTime.UtcNow.AddMinutes(5));
                Assert.Equal(RecurringGenerationOutcome.Failed, result.Outcome);
                Assert.Equal("BOOKS_LOCKED", result.ErrorCode);

                var schedule = db.RecurringSchedules.Single(s => s.Id == scheduleId);
                Assert.Equal(runDate.AddMonths(1), schedule.NextRunDate); // advanced despite the failure

                var occurrence = db.RecurringOccurrences.Single(o => o.RecurringScheduleId == scheduleId);
                Assert.Equal(RecurringOccurrenceStatus.Failed, occurrence.Status);
                Assert.Null(occurrence.SalesInvoiceId);
            }
        }

        [SqliteFact]
        public void Recurring_RetryOccurrence_DoesNotAdvanceNextRunDate_AndProducesDocumentOnceFixed()
        {
            var runMonth = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
            var runDate = runMonth.AddDays(10); // 11th
            int scheduleId = SeedSalesSchedule("RETRY-NOADV", RecurringAction.AutoIssue, runDate, _ownerId);

            int occurrenceId;
            DateTime advancedNextRunDate;
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
                using (scope.ServiceProvider.GetRequiredService<ITenantContext>().Impersonate(_companyId, _slug, CompanyMemberRole.Owner))
                {
                    var setting = db.CompanySettings.Single();
                    setting.BooksLockedThrough = runMonth.AddMonths(1).AddDays(-1);
                    db.SaveChanges();

                    var generation = scope.ServiceProvider.GetRequiredService<IRecurringInvoiceGenerationService>();
                    var failed = generation.RunSchedule(scheduleId, null, "run-1", DateTime.UtcNow.AddMinutes(5));
                    Assert.Equal(RecurringGenerationOutcome.Failed, failed.Outcome);

                    var schedule = db.RecurringSchedules.Single(s => s.Id == scheduleId);
                    advancedNextRunDate = schedule.NextRunDate!.Value; // already advanced by the failed forward run
                    occurrenceId = db.RecurringOccurrences.Single(o => o.RecurringScheduleId == scheduleId).Id;
                }
            }

            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
                using (scope.ServiceProvider.GetRequiredService<ITenantContext>().Impersonate(_companyId, _slug, CompanyMemberRole.Owner))
                {
                    // Fix the cause: unlock the books, then retry the same occurrence (downgrade to draft on an
                    // in-month auto-issue is fine — the point is the series does not move on retry).
                    var setting = db.CompanySettings.Single();
                    setting.BooksLockedThrough = null;
                    db.SaveChanges();

                    var generation = scope.ServiceProvider.GetRequiredService<IRecurringInvoiceGenerationService>();
                    var retry = generation.RetryOccurrence(occurrenceId, "retry-1", DateTime.UtcNow.AddMinutes(5));
                    Assert.True(retry.Outcome is RecurringGenerationOutcome.Issued or RecurringGenerationOutcome.DraftCreated
                        or RecurringGenerationOutcome.DraftCreatedGuardFailed);
                    Assert.NotNull(retry.SalesInvoiceId);

                    var schedule = db.RecurringSchedules.Single(s => s.Id == scheduleId);
                    // The retry must NOT move the series forward again.
                    Assert.Equal(advancedNextRunDate, schedule.NextRunDate);

                    // Still a single occurrence for the run date; it now carries the document.
                    var occurrence = db.RecurringOccurrences.Single(o => o.RecurringScheduleId == scheduleId);
                    Assert.Equal(occurrenceId, occurrence.Id);
                    Assert.NotNull(occurrence.SalesInvoiceId);
                }
            }
        }

        [SqliteFact]
        public void Recurring_RunSchedule_ArchivedCustomer_PausesSchedule_FlagsAttention_NoDocument()
        {
            var runDate = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 6);
            int scheduleId = SeedSalesSchedule("ARCH-PAUSE", RecurringAction.CreateDraft, runDate, _ownerId);

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            using (scope.ServiceProvider.GetRequiredService<ITenantContext>().Impersonate(_companyId, _slug, CompanyMemberRole.Owner))
            {
                var schedule = db.RecurringSchedules.Single(s => s.Id == scheduleId);
                var customer = db.Customers.Single(c => c.Id == schedule.PartyId);
                customer.ArchivedAt = DateTime.UtcNow;
                db.SaveChanges();

                var generation = scope.ServiceProvider.GetRequiredService<IRecurringInvoiceGenerationService>();
                var result = generation.RunSchedule(scheduleId, null, "run-arch", DateTime.UtcNow.AddMinutes(5));
                Assert.Equal(RecurringGenerationOutcome.Aborted, result.Outcome);

                var reloaded = db.RecurringSchedules.Single(s => s.Id == scheduleId);
                Assert.Equal(RecurringScheduleStatus.Paused, reloaded.Status);
                Assert.True(reloaded.NeedsAttention);

                // No occurrence and no invoice were created.
                Assert.Empty(db.RecurringOccurrences.Where(o => o.RecurringScheduleId == scheduleId));
            }
        }

        [SqliteFact]
        public void Recurring_RunSchedule_LastOccurrence_EndsSchedule()
        {
            var runDate = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 3);
            int scheduleId = SeedSalesSchedule("MAXOCC-END", RecurringAction.CreateDraft, runDate, _ownerId);

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            using (scope.ServiceProvider.GetRequiredService<ITenantContext>().Impersonate(_companyId, _slug, CompanyMemberRole.Owner))
            {
                // Cap the series at a single occurrence: after running the first (position 0), the next (position 1)
                // is past the limit, so the schedule ends and offers no further run.
                var schedule = db.RecurringSchedules.Single(s => s.Id == scheduleId);
                schedule.MaxOccurrences = 1;
                db.SaveChanges();

                var generation = scope.ServiceProvider.GetRequiredService<IRecurringInvoiceGenerationService>();
                var result = generation.RunSchedule(scheduleId, null, "run-max", DateTime.UtcNow.AddMinutes(5));
                Assert.Equal(RecurringGenerationOutcome.DraftCreated, result.Outcome);

                var reloaded = db.RecurringSchedules.Single(s => s.Id == scheduleId);
                Assert.Null(reloaded.NextRunDate);
                Assert.Equal(RecurringScheduleStatus.Ended, reloaded.Status);
            }
        }

        [SqliteFact]
        public async Task Recurring_RetryEndpoint_RetriesFailedOccurrence_HttpContract()
        {
            // Fail the first run with a locked posting date on an auto-issue schedule, then unlock and retry through
            // the endpoint. Uses the owner token, which holds sales.invoices.post.
            var runMonth = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
            var runDate = runMonth.AddDays(13); // 14th
            int scheduleId = SeedSalesSchedule("RETRY-HTTP", RecurringAction.AutoIssue, runDate, _ownerId);

            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
                using (scope.ServiceProvider.GetRequiredService<ITenantContext>().Impersonate(_companyId, _slug, CompanyMemberRole.Owner))
                {
                    var setting = db.CompanySettings.Single();
                    setting.BooksLockedThrough = runMonth.AddMonths(1).AddDays(-1); // lock the whole run month
                    db.SaveChanges();
                }
            }

            var failRes = await Send(HttpMethod.Post, $"/api/RecurringSchedules/{scheduleId}/run", new { runDate = IsoDate(runDate) });
            Assert.Equal(HttpStatusCode.OK, failRes.StatusCode);
            var failBody = await Json(failRes);
            Assert.Equal("Failed", failBody.GetProperty("outcome").GetString());
            Assert.Equal("BOOKS_LOCKED", failBody.GetProperty("errorCode").GetString());
            int occurrenceId = failBody.GetProperty("occurrenceId").GetInt32();

            // Unlock the books, then retry the occurrence via the endpoint.
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
                using (scope.ServiceProvider.GetRequiredService<ITenantContext>().Impersonate(_companyId, _slug, CompanyMemberRole.Owner))
                {
                    var setting = db.CompanySettings.Single();
                    setting.BooksLockedThrough = null;
                    db.SaveChanges();
                }
            }

            var retryRes = await Send(HttpMethod.Post, $"/api/RecurringSchedules/occurrences/{occurrenceId}/retry");
            Assert.Equal(HttpStatusCode.OK, retryRes.StatusCode);
            var retryBody = await Json(retryRes);
            // In-month auto-issue with all guards passing issues; the key contract is a document now exists for the
            // same occurrence and the outcome is a success, not a failure.
            var outcome = retryBody.GetProperty("outcome").GetString();
            Assert.True(outcome is "Issued" or "DraftCreated" or "DraftCreatedGuardFailed", $"unexpected outcome {outcome}");
            Assert.Equal(occurrenceId, retryBody.GetProperty("occurrenceId").GetInt32());
            Assert.True(retryBody.GetProperty("salesInvoiceId").GetInt32() > 0);
        }

        [SqliteFact]
        public void Recurring_RunSchedule_LaterThanNextRunDate_IsRejected_NoDocument()
        {
            // Finding 3: a manual run may only generate the schedule's next due occurrence. A later date must be
            // rejected rather than generated-and-skipped, so intervening periods are never silently un-recorded.
            var nextDate = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 4);
            var laterDate = nextDate.AddMonths(2); // two periods ahead
            int scheduleId = SeedSalesSchedule("LATER-REJECT", RecurringAction.CreateDraft, nextDate, _ownerId);

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            using (scope.ServiceProvider.GetRequiredService<ITenantContext>().Impersonate(_companyId, _slug, CompanyMemberRole.Owner))
            {
                var generation = scope.ServiceProvider.GetRequiredService<IRecurringInvoiceGenerationService>();
                var ex = Assert.Throws<InvalidRunDateException>(() =>
                    generation.RunSchedule(scheduleId, laterDate, "run-later", DateTime.UtcNow.AddMinutes(5)));
                Assert.Contains("next run", ex.Message, StringComparison.OrdinalIgnoreCase);

                // Nothing generated and the series did not move.
                Assert.Empty(db.RecurringOccurrences.Where(o => o.RecurringScheduleId == scheduleId));
                var schedule = db.RecurringSchedules.Single(s => s.Id == scheduleId);
                Assert.Equal(nextDate, schedule.NextRunDate);
            }
        }

        [SqliteFact]
        public void Recurring_RunSchedule_FailedFinalRun_DoesNotEndSchedule_OccurrenceStaysRetryable()
        {
            // Finding 2: a failed run on the last allowed occurrence clears NextRunDate but must NOT end the
            // schedule, or the failed occurrence could never be retried. Lock the books so the single allowed
            // auto-issue run fails closed.
            var runMonth = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
            var runDate = runMonth.AddDays(15); // 16th
            int scheduleId = SeedSalesSchedule("FINAL-FAIL", RecurringAction.AutoIssue, runDate, _ownerId);

            int occurrenceId;
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
                using (scope.ServiceProvider.GetRequiredService<ITenantContext>().Impersonate(_companyId, _slug, CompanyMemberRole.Owner))
                {
                    var schedule = db.RecurringSchedules.Single(s => s.Id == scheduleId);
                    schedule.MaxOccurrences = 1; // this run is the last allowed one
                    db.SaveChanges();

                    var setting = db.CompanySettings.Single();
                    setting.BooksLockedThrough = runMonth.AddMonths(1).AddDays(-1);
                    db.SaveChanges();

                    var generation = scope.ServiceProvider.GetRequiredService<IRecurringInvoiceGenerationService>();
                    var result = generation.RunSchedule(scheduleId, null, "run-final", DateTime.UtcNow.AddMinutes(5));
                    Assert.Equal(RecurringGenerationOutcome.Failed, result.Outcome);

                    var reloaded = db.RecurringSchedules.Single(s => s.Id == scheduleId);
                    Assert.Null(reloaded.NextRunDate);                 // no more forward runs
                    Assert.Equal(RecurringScheduleStatus.Active, reloaded.Status); // but NOT ended
                    Assert.True(reloaded.NeedsAttention);

                    occurrenceId = db.RecurringOccurrences.Single(o => o.RecurringScheduleId == scheduleId).Id;
                }
            }

            // The failed final occurrence can still be retried (unlock first).
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
                using (scope.ServiceProvider.GetRequiredService<ITenantContext>().Impersonate(_companyId, _slug, CompanyMemberRole.Owner))
                {
                    var setting = db.CompanySettings.Single();
                    setting.BooksLockedThrough = null;
                    db.SaveChanges();

                    var generation = scope.ServiceProvider.GetRequiredService<IRecurringInvoiceGenerationService>();
                    var retry = generation.RetryOccurrence(occurrenceId, "retry-final", DateTime.UtcNow.AddMinutes(5));
                    Assert.NotEqual(RecurringGenerationOutcome.Aborted, retry.Outcome);
                    Assert.NotNull(retry.SalesInvoiceId);
                }
            }
        }

        [SqliteFact]
        public void Recurring_RetryOccurrence_ArchivedCustomer_PausesSchedule_NoDocument()
        {
            // Finding 1: a retry must honor the archived-client policy — pause and flag, not quietly draft.
            var runMonth = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
            var runDate = runMonth.AddDays(17); // 18th
            int scheduleId = SeedSalesSchedule("RETRY-ARCH", RecurringAction.AutoIssue, runDate, _ownerId);

            int occurrenceId;
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
                using (scope.ServiceProvider.GetRequiredService<ITenantContext>().Impersonate(_companyId, _slug, CompanyMemberRole.Owner))
                {
                    var setting = db.CompanySettings.Single();
                    setting.BooksLockedThrough = runMonth.AddMonths(1).AddDays(-1);
                    db.SaveChanges();

                    var generation = scope.ServiceProvider.GetRequiredService<IRecurringInvoiceGenerationService>();
                    var failed = generation.RunSchedule(scheduleId, null, "run-1", DateTime.UtcNow.AddMinutes(5));
                    Assert.Equal(RecurringGenerationOutcome.Failed, failed.Outcome);
                    occurrenceId = db.RecurringOccurrences.Single(o => o.RecurringScheduleId == scheduleId).Id;
                }
            }

            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
                using (scope.ServiceProvider.GetRequiredService<ITenantContext>().Impersonate(_companyId, _slug, CompanyMemberRole.Owner))
                {
                    // Unlock the books (so it would otherwise succeed) but archive the customer before retry.
                    var setting = db.CompanySettings.Single();
                    setting.BooksLockedThrough = null;
                    var schedule = db.RecurringSchedules.Single(s => s.Id == scheduleId);
                    var customer = db.Customers.Single(c => c.Id == schedule.PartyId);
                    customer.ArchivedAt = DateTime.UtcNow;
                    db.SaveChanges();

                    var generation = scope.ServiceProvider.GetRequiredService<IRecurringInvoiceGenerationService>();
                    var retry = generation.RetryOccurrence(occurrenceId, "retry-arch", DateTime.UtcNow.AddMinutes(5));
                    Assert.Equal(RecurringGenerationOutcome.Aborted, retry.Outcome);

                    var reloaded = db.RecurringSchedules.Single(s => s.Id == scheduleId);
                    Assert.Equal(RecurringScheduleStatus.Paused, reloaded.Status);
                    Assert.True(reloaded.NeedsAttention);

                    // The occurrence is still the failed one; no document was produced on the archived retry.
                    var occurrence = db.RecurringOccurrences.Single(o => o.Id == occurrenceId);
                    Assert.Null(occurrence.SalesInvoiceId);
                }
            }
        }

        /// <summary>Seeds a one-line service-item sales schedule whose NextRunDate is the given run date.</summary>
        private int SeedSalesSchedule(string tag, RecurringAction action, DateTime runDate, string automationEnabledBy,
            RecurringScheduleStatus status = RecurringScheduleStatus.Active)
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
                    StartDate = runDate, NextRunDate = runDate, Action = action, Status = status,
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
