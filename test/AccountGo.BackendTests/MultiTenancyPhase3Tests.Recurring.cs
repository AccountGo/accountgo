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
                using (tenant.ImpersonateUser(_companyId, _ownerId, _slug, CompanyMemberRole.Owner))
                {
                    var salesAccountId = db.Accounts.Single(a => a.AccountCode == "40100").Id;
                    var arAccountId = db.Accounts.Single(a => a.AccountCode == "10120").Id;
                    var measurementId = db.Measurements.First().Id;

                    var customer = new Customer { CompanyId = _companyId, No = "RECUR-CUST-1", AccountsReceivableAccountId = arAccountId };
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
                using (scope.ServiceProvider.GetRequiredService<ITenantContext>().ImpersonateUser(_companyId, _ownerId, _slug, CompanyMemberRole.Owner))
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
                using (scope.ServiceProvider.GetRequiredService<ITenantContext>().ImpersonateUser(_companyId, _ownerId, _slug, CompanyMemberRole.Owner))
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
                using (scope.ServiceProvider.GetRequiredService<ITenantContext>().ImpersonateUser(_companyId, _ownerId, _slug, CompanyMemberRole.Owner))
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
                using (scope.ServiceProvider.GetRequiredService<ITenantContext>().ImpersonateUser(_companyId, _ownerId, _slug, CompanyMemberRole.Owner))
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
                using (scope.ServiceProvider.GetRequiredService<ITenantContext>().ImpersonateUser(_companyId, _ownerId, _slug, CompanyMemberRole.Owner))
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
                using (scope.ServiceProvider.GetRequiredService<ITenantContext>().ImpersonateUser(_companyId, _ownerId, _slug, CompanyMemberRole.Owner))
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
                using (scope.ServiceProvider.GetRequiredService<ITenantContext>().ImpersonateUser(_companyId, _ownerId, _slug, CompanyMemberRole.Owner))
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
                using (scope.ServiceProvider.GetRequiredService<ITenantContext>().ImpersonateUser(_companyId, _ownerId, _slug, CompanyMemberRole.Owner))
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
                using (scope.ServiceProvider.GetRequiredService<ITenantContext>().ImpersonateUser(_companyId, _ownerId, _slug, CompanyMemberRole.Owner))
                {
                    var setting = db.CompanySettings.Single();
                    setting.BooksLockedThrough = null;
                    db.SaveChanges();

                    var generation = scope.ServiceProvider.GetRequiredService<IRecurringInvoiceGenerationService>();
                    var retry = generation.RetryOccurrence(occurrenceId, "retry-final", DateTime.UtcNow.AddMinutes(5));
                    Assert.NotEqual(RecurringGenerationOutcome.Aborted, retry.Outcome);
                    Assert.NotNull(retry.SalesInvoiceId);

                    // Finding 2: the successful retry of the final occurrence now finalizes the exhausted schedule.
                    var reloaded = db.RecurringSchedules.Single(s => s.Id == scheduleId);
                    Assert.Equal(RecurringScheduleStatus.Ended, reloaded.Status);
                    Assert.Null(reloaded.NextRunDate);
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
                using (scope.ServiceProvider.GetRequiredService<ITenantContext>().ImpersonateUser(_companyId, _ownerId, _slug, CompanyMemberRole.Owner))
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
                using (scope.ServiceProvider.GetRequiredService<ITenantContext>().ImpersonateUser(_companyId, _ownerId, _slug, CompanyMemberRole.Owner))
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

        [SqliteFact]
        public void Recurring_RunSchedule_NoNextRunDate_IsRejected_EvenWithCallerDate()
        {
            // Finding 1: with no NextRunDate (exhausted or never-scheduled), a manual run must be refused even when
            // the caller names a date — otherwise an exhausted schedule could be run outside the next-occurrence flow.
            var seedDate = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 2);
            int scheduleId = SeedSalesSchedule("NO-NEXT", RecurringAction.CreateDraft, seedDate, _ownerId);

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            using (scope.ServiceProvider.GetRequiredService<ITenantContext>().Impersonate(_companyId, _slug, CompanyMemberRole.Owner))
            {
                // Put the schedule in the exhausted-but-active state: no next run date.
                var schedule = db.RecurringSchedules.Single(s => s.Id == scheduleId);
                schedule.NextRunDate = null;
                db.SaveChanges();

                var generation = scope.ServiceProvider.GetRequiredService<IRecurringInvoiceGenerationService>();

                // Caller-supplied date must not be accepted.
                var ex1 = Assert.Throws<InvalidRunDateException>(() =>
                    generation.RunSchedule(scheduleId, seedDate, "run-x", DateTime.UtcNow.AddMinutes(5)));
                Assert.Contains("no next run date", ex1.Message, StringComparison.OrdinalIgnoreCase);

                // And the default (no date) path is equally refused.
                var ex2 = Assert.Throws<InvalidRunDateException>(() =>
                    generation.RunSchedule(scheduleId, null, "run-x", DateTime.UtcNow.AddMinutes(5)));
                Assert.Contains("no next run date", ex2.Message, StringComparison.OrdinalIgnoreCase);

                Assert.Empty(db.RecurringOccurrences.Where(o => o.RecurringScheduleId == scheduleId));
            }
        }

        [SqliteFact]
        public void Recurring_RetryOccurrence_MidSeries_DoesNotEndSchedule()
        {
            // A retry of a non-final failed occurrence must not end the schedule: the series still has runs left, so
            // NextRunDate stays set and the schedule stays Active after a successful retry.
            var runMonth = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
            var runDate = runMonth.AddDays(19); // 20th
            int scheduleId = SeedSalesSchedule("MID-RETRY", RecurringAction.AutoIssue, runDate, _ownerId);

            int occurrenceId;
            DateTime advancedNextRunDate;
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
                using (scope.ServiceProvider.GetRequiredService<ITenantContext>().ImpersonateUser(_companyId, _ownerId, _slug, CompanyMemberRole.Owner))
                {
                    // No end/max: the series continues indefinitely, so this occurrence is not the final one.
                    var setting = db.CompanySettings.Single();
                    setting.BooksLockedThrough = runMonth.AddMonths(1).AddDays(-1);
                    db.SaveChanges();

                    var generation = scope.ServiceProvider.GetRequiredService<IRecurringInvoiceGenerationService>();
                    var failed = generation.RunSchedule(scheduleId, null, "run-1", DateTime.UtcNow.AddMinutes(5));
                    Assert.Equal(RecurringGenerationOutcome.Failed, failed.Outcome);

                    var schedule = db.RecurringSchedules.Single(s => s.Id == scheduleId);
                    advancedNextRunDate = schedule.NextRunDate!.Value; // advanced to next month (non-null)
                    occurrenceId = db.RecurringOccurrences.Single(o => o.RecurringScheduleId == scheduleId).Id;
                }
            }

            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
                using (scope.ServiceProvider.GetRequiredService<ITenantContext>().ImpersonateUser(_companyId, _ownerId, _slug, CompanyMemberRole.Owner))
                {
                    var setting = db.CompanySettings.Single();
                    setting.BooksLockedThrough = null;
                    db.SaveChanges();

                    var generation = scope.ServiceProvider.GetRequiredService<IRecurringInvoiceGenerationService>();
                    var retry = generation.RetryOccurrence(occurrenceId, "retry-1", DateTime.UtcNow.AddMinutes(5));
                    Assert.NotEqual(RecurringGenerationOutcome.Aborted, retry.Outcome);

                    var schedule = db.RecurringSchedules.Single(s => s.Id == scheduleId);
                    Assert.Equal(RecurringScheduleStatus.Active, schedule.Status); // not ended
                    Assert.Equal(advancedNextRunDate, schedule.NextRunDate);       // series unchanged by retry
                }
            }
        }

        [SqliteFact]
        public void Recurring_RunSchedule_IdempotentReplay_DoesNotAdvanceTwice()
        {
            // HIGH finding: advancement is exactly-once per occurrence. The first run advances the series and marks
            // the occurrence SeriesAdvanced. A replay (the occurrence already produced its document) must neither
            // advance again nor double-count OccurrenceCount, even if the caller still points at the original date.
            var runDate = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 6);
            int scheduleId = SeedSalesSchedule("ADV-REPLAY", RecurringAction.CreateDraft, runDate, _ownerId);

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            using (scope.ServiceProvider.GetRequiredService<ITenantContext>().Impersonate(_companyId, _slug, CompanyMemberRole.Owner))
            {
                var generation = scope.ServiceProvider.GetRequiredService<IRecurringInvoiceGenerationService>();

                var first = generation.RunSchedule(scheduleId, runDate, "run-a", DateTime.UtcNow.AddMinutes(5));
                Assert.Equal(RecurringGenerationOutcome.DraftCreated, first.Outcome);

                var afterFirst = db.RecurringSchedules.Single(s => s.Id == scheduleId);
                Assert.Equal(runDate.AddMonths(1), afterFirst.NextRunDate);
                Assert.Equal(1, afterFirst.OccurrenceCount);
                Assert.True(db.RecurringOccurrences.Single(o => o.RecurringScheduleId == scheduleId).SeriesAdvanced);

                // A replay with the original date: the occurrence already advanced the series, so this must be a
                // no-op for the schedule. (Reset the stored date back as a stale second caller would see it.)
                afterFirst.NextRunDate = runDate;
                db.SaveChanges();

                var second = generation.RunSchedule(scheduleId, runDate, "run-b", DateTime.UtcNow.AddMinutes(5));
                Assert.Equal(RecurringGenerationOutcome.AlreadyProcessed, second.Outcome);
                Assert.Equal(first.SalesInvoiceId, second.SalesInvoiceId);

                var afterSecond = db.RecurringSchedules.Single(s => s.Id == scheduleId);
                Assert.Equal(1, afterSecond.OccurrenceCount);                  // not double-counted
                Assert.Equal(runDate, afterSecond.NextRunDate);                // not advanced a second time
                Assert.Single(db.RecurringOccurrences.Where(o => o.RecurringScheduleId == scheduleId));
            }
        }

        [SqliteFact]
        public void Recurring_RunSchedule_RecoversUnadvancedCompletedOccurrence_ExactlyOnce()
        {
            // HIGH finding: a crash can commit the invoice but not the series advance, stranding NextRunDate on the
            // completed occurrence. The next run (an idempotent replay) must recover by advancing the series exactly
            // once, without creating another invoice. Simulate the crash in one scope (run, then roll the schedule
            // back to the completed occurrence's date AND clear its SeriesAdvanced flag), then recover in a fresh
            // scope with its own DbContext — as a later request would.
            var runDate = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 7);
            int scheduleId = SeedSalesSchedule("ADV-RECOVER", RecurringAction.CreateDraft, runDate, _ownerId);

            int? firstInvoiceId;
            using (var scope = _factory.Services.CreateScope())
            using (scope.ServiceProvider.GetRequiredService<ITenantContext>().Impersonate(_companyId, _slug, CompanyMemberRole.Owner))
            {
                var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
                var generation = scope.ServiceProvider.GetRequiredService<IRecurringInvoiceGenerationService>();

                var first = generation.RunSchedule(scheduleId, runDate, "run-a", DateTime.UtcNow.AddMinutes(5));
                Assert.Equal(RecurringGenerationOutcome.DraftCreated, first.Outcome);
                firstInvoiceId = first.SalesInvoiceId;

                // Reproduce the stranded post-crash state: document committed, but series never advanced.
                var schedule = db.RecurringSchedules.Single(s => s.Id == scheduleId);
                schedule.NextRunDate = runDate;
                schedule.OccurrenceCount = 0;
                var occurrence = db.RecurringOccurrences.Single(o => o.RecurringScheduleId == scheduleId);
                occurrence.SeriesAdvanced = false;
                db.SaveChanges();
            }

            using (var scope = _factory.Services.CreateScope())
            using (scope.ServiceProvider.GetRequiredService<ITenantContext>().Impersonate(_companyId, _slug, CompanyMemberRole.Owner))
            {
                var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
                var generation = scope.ServiceProvider.GetRequiredService<IRecurringInvoiceGenerationService>();

                // The next run finds the completed-but-unadvanced occurrence and recovers it.
                var recover = generation.RunSchedule(scheduleId, runDate, "run-b", DateTime.UtcNow.AddMinutes(5));
                Assert.Equal(RecurringGenerationOutcome.AlreadyProcessed, recover.Outcome);
                Assert.Equal(firstInvoiceId, recover.SalesInvoiceId); // no new document

                var recovered = db.RecurringSchedules.Single(s => s.Id == scheduleId);
                Assert.Equal(runDate.AddMonths(1), recovered.NextRunDate);  // series advanced by recovery
                Assert.Equal(1, recovered.OccurrenceCount);                 // counted exactly once
                Assert.True(db.RecurringOccurrences.Single(o => o.RecurringScheduleId == scheduleId).SeriesAdvanced);
                Assert.Single(db.RecurringOccurrences.Where(o => o.RecurringScheduleId == scheduleId));
            }
        }

        [SqliteFact]
        public void Recurring_RetryOccurrence_FinalGuardDowngrade_EndsSchedule_ButKeepsAttention()
        {
            // MEDIUM finding: a successful retry whose outcome is a guard downgrade (draft, not a posted invoice)
            // still finalizes the exhausted schedule, but must NOT clear NeedsAttention — the recorded guard-failure
            // reason still needs review. Reproduce: a one-occurrence auto-issue schedule whose final run fails
            // (books locked), then retry with the books unlocked but a quarterly cadence that downgrades to a draft.
            var runMonth = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
            var runDate = runMonth.AddDays(21); // 22nd
            int scheduleId = SeedSalesSchedule("FINAL-DOWNGRADE", RecurringAction.AutoIssue, runDate, _ownerId);

            int occurrenceId;
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
                using (scope.ServiceProvider.GetRequiredService<ITenantContext>().ImpersonateUser(_companyId, _ownerId, _slug, CompanyMemberRole.Owner))
                {
                    var schedule = db.RecurringSchedules.Single(s => s.Id == scheduleId);
                    schedule.MaxOccurrences = 1;                       // this run is the last allowed one
                    schedule.Cadence = RecurringCadence.Quarterly;     // quarterly => auto-issue downgrades to draft
                    db.SaveChanges();

                    var setting = db.CompanySettings.Single();
                    setting.BooksLockedThrough = runMonth.AddMonths(4).AddDays(-1); // lock through the quarter
                    db.SaveChanges();

                    var generation = scope.ServiceProvider.GetRequiredService<IRecurringInvoiceGenerationService>();
                    var failed = generation.RunSchedule(scheduleId, null, "run-final", DateTime.UtcNow.AddMinutes(5));
                    Assert.Equal(RecurringGenerationOutcome.Failed, failed.Outcome); // fail-closed books guard first

                    var reloaded = db.RecurringSchedules.Single(s => s.Id == scheduleId);
                    Assert.Null(reloaded.NextRunDate);                 // exhausted
                    Assert.Equal(RecurringScheduleStatus.Active, reloaded.Status); // not yet ended
                    occurrenceId = db.RecurringOccurrences.Single(o => o.RecurringScheduleId == scheduleId).Id;
                }
            }

            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
                using (scope.ServiceProvider.GetRequiredService<ITenantContext>().ImpersonateUser(_companyId, _ownerId, _slug, CompanyMemberRole.Owner))
                {
                    var setting = db.CompanySettings.Single();
                    setting.BooksLockedThrough = null; // unlock: the only remaining blocker is the cadence downgrade
                    db.SaveChanges();

                    var generation = scope.ServiceProvider.GetRequiredService<IRecurringInvoiceGenerationService>();
                    var retry = generation.RetryOccurrence(occurrenceId, "retry-dg", DateTime.UtcNow.AddMinutes(5));
                    Assert.Equal(RecurringGenerationOutcome.DraftCreatedGuardFailed, retry.Outcome);

                    var reloaded = db.RecurringSchedules.Single(s => s.Id == scheduleId);
                    // The exhausted schedule is finalized...
                    Assert.Equal(RecurringScheduleStatus.Ended, reloaded.Status);
                    Assert.Null(reloaded.NextRunDate);
                    // ...but attention is preserved because the final occurrence recorded a guard failure to review.
                    Assert.True(reloaded.NeedsAttention);

                    var occurrence = db.RecurringOccurrences.Single(o => o.Id == occurrenceId);
                    Assert.Equal(RecurringOccurrenceStatus.DraftCreated, occurrence.Status);
                    Assert.NotNull(occurrence.GuardFailureReason);
                }
            }
        }

        [SqliteFact]
        public async Task Recurring_RetryEndpoint_PermissionSplit_PostRequiredForAutoIssue()
        {
            // MEDIUM finding (coverage): the retry endpoint's auto-issue post gate now goes through the shared
            // authorization service. A member with create-but-not-post must be denied retrying an auto-issue
            // occurrence. Seed a failed auto-issue occurrence (books locked), then retry as a Sales Representative.
            var runMonth = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
            var runDate = runMonth.AddDays(23); // 24th
            int scheduleId = SeedSalesSchedule("RETRY-PERM", RecurringAction.AutoIssue, runDate, _ownerId);

            int occurrenceId;
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
                using (scope.ServiceProvider.GetRequiredService<ITenantContext>().ImpersonateUser(_companyId, _ownerId, _slug, CompanyMemberRole.Owner))
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

            // _otherToken joins as a Sales Representative: has sales.invoices.create, not sales.invoices.post.
            await JoinOther();
            Assert.Equal(HttpStatusCode.NoContent, (await Send(HttpMethod.Patch, Current + "/members/" + MemberId(_otherId),
                new { securityRoleIds = new[] { RoleId("Sales Representative") } })).StatusCode);

            var res = await Send(HttpMethod.Post, $"/api/RecurringSchedules/occurrences/{occurrenceId}/retry",
                body: null, token: _otherToken);
            Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
            Assert.Contains("sales.invoices.post", await res.Content.ReadAsStringAsync());
        }

        // ----- #484: management endpoints (skip / pause / resume / end / list / preview) -----

        [SqliteFact]
        public async Task Recurring_Skip_RecordsReason_NoDocument_AdvancesOnce_AnchorUnchanged()
        {
            var runDate = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 10);
            int scheduleId = SeedSalesSchedule("SKIP-OK", RecurringAction.CreateDraft, runDate, _ownerId);

            var res = await Send(HttpMethod.Post, $"/api/RecurringSchedules/{scheduleId}/skip",
                new { runDate = IsoDate(runDate), reason = "Client on holiday" });
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            Assert.Equal("Skipped", (await Json(res)).GetProperty("outcome").GetString());

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            using (scope.ServiceProvider.GetRequiredService<ITenantContext>().Impersonate(_companyId, _slug, CompanyMemberRole.Owner))
            {
                var occurrence = db.RecurringOccurrences.Single(o => o.RecurringScheduleId == scheduleId);
                Assert.Equal(RecurringOccurrenceStatus.Skipped, occurrence.Status);
                Assert.Equal("Client on holiday", occurrence.SkipReason);
                Assert.Null(occurrence.SalesInvoiceId); // no document
                Assert.False(db.SalesInvoiceHeaders.Any(i => i.CompanyId == _companyId));

                var schedule = db.RecurringSchedules.Single(s => s.Id == scheduleId);
                // Advanced exactly one step; the anchor (StartDate/DayOfMonth) is unchanged so later dates do not shift.
                Assert.Equal(runDate.AddMonths(1), schedule.NextRunDate);
                Assert.Equal(1, schedule.OccurrenceCount);
                Assert.Equal(runDate, schedule.StartDate);
                Assert.Equal(runDate.Day, schedule.DayOfMonth);
            }
        }

        [SqliteFact]
        public async Task Recurring_Skip_WithoutReason_IsRejected()
        {
            var runDate = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 11);
            int scheduleId = SeedSalesSchedule("SKIP-NOREASON", RecurringAction.CreateDraft, runDate, _ownerId);

            var res = await Send(HttpMethod.Post, $"/api/RecurringSchedules/{scheduleId}/skip",
                new { runDate = IsoDate(runDate), reason = "" });
            Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
            Assert.Contains("reason", await res.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            using (scope.ServiceProvider.GetRequiredService<ITenantContext>().Impersonate(_companyId, _slug, CompanyMemberRole.Owner))
            {
                Assert.Empty(db.RecurringOccurrences.Where(o => o.RecurringScheduleId == scheduleId));
            }
        }

        [SqliteFact]
        public async Task Recurring_PauseThenEnd_LeaveExistingInvoicesUntouched()
        {
            var runDate = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 12);
            int scheduleId = SeedSalesSchedule("PAUSE-END", RecurringAction.CreateDraft, runDate, _ownerId);

            // Generate one real draft invoice first.
            int invoiceId;
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
                using (scope.ServiceProvider.GetRequiredService<ITenantContext>().ImpersonateUser(_companyId, _ownerId, _slug, CompanyMemberRole.Owner))
                {
                    var generation = scope.ServiceProvider.GetRequiredService<IRecurringInvoiceGenerationService>();
                    var run = generation.RunSchedule(scheduleId, runDate, "run-1", DateTime.UtcNow.AddMinutes(5));
                    Assert.Equal(RecurringGenerationOutcome.DraftCreated, run.Outcome);
                    invoiceId = run.SalesInvoiceId!.Value;
                }
            }

            Assert.Equal(HttpStatusCode.NoContent,
                (await Send(HttpMethod.Post, $"/api/RecurringSchedules/{scheduleId}/pause", new { reason = "Holiday" })).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent,
                (await Send(HttpMethod.Post, $"/api/RecurringSchedules/{scheduleId}/end", new { reason = "Contract closed" })).StatusCode);

            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
                using (scope.ServiceProvider.GetRequiredService<ITenantContext>().ImpersonateUser(_companyId, _ownerId, _slug, CompanyMemberRole.Owner))
                {
                    var schedule = db.RecurringSchedules.Single(s => s.Id == scheduleId);
                    Assert.Equal(RecurringScheduleStatus.Ended, schedule.Status);
                    Assert.Null(schedule.NextRunDate);

                    // The already-generated invoice is completely untouched.
                    var invoice = db.SalesInvoiceHeaders.Single(i => i.Id == invoiceId);
                    Assert.NotNull(invoice);

                    // Pause and End each wrote an audit row for the schedule.
                    var audits = db.AuditLogs.Where(a =>
                        a.CompanyId == _companyId && a.TableName == "RecurringSchedule" &&
                        a.RecordId == scheduleId.ToString()).ToList();
                    Assert.Contains(audits, a => a.NewValue != null && a.NewValue.StartsWith("Paused"));
                    Assert.Contains(audits, a => a.NewValue != null && a.NewValue.StartsWith("Ended"));
                }
            }
        }

        [SqliteFact]
        public async Task Recurring_Resume_ArchivedCustomer_IsRejected()
        {
            var runDate = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 13);
            int scheduleId = SeedSalesSchedule("RESUME-ARCH", RecurringAction.CreateDraft, runDate, _ownerId);

            Assert.Equal(HttpStatusCode.NoContent,
                (await Send(HttpMethod.Post, $"/api/RecurringSchedules/{scheduleId}/pause", new { reason = "x" })).StatusCode);

            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
                using (scope.ServiceProvider.GetRequiredService<ITenantContext>().ImpersonateUser(_companyId, _ownerId, _slug, CompanyMemberRole.Owner))
                {
                    var schedule = db.RecurringSchedules.Single(s => s.Id == scheduleId);
                    var customer = db.Customers.Single(c => c.Id == schedule.PartyId);
                    customer.ArchivedAt = DateTime.UtcNow;
                    db.SaveChanges();
                }
            }

            var res = await Send(HttpMethod.Post, $"/api/RecurringSchedules/{scheduleId}/resume");
            Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
            Assert.Contains("archived", await res.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);

            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
                using (scope.ServiceProvider.GetRequiredService<ITenantContext>().ImpersonateUser(_companyId, _ownerId, _slug, CompanyMemberRole.Owner))
                {
                    Assert.Equal(RecurringScheduleStatus.Paused,
                        db.RecurringSchedules.Single(s => s.Id == scheduleId).Status);
                }
            }
        }

        [SqliteFact]
        public async Task Recurring_Resume_DoesNotCatchUpMissedPeriods()
        {
            // Pause a schedule whose next run is in the past, then resume: NextRunDate must jump to the next date on
            // or after today (no back-fill of the missed period).
            var pastRun = DateTime.UtcNow.Date.AddMonths(-2); // two periods behind
            int scheduleId = SeedSalesSchedule("RESUME-NOCATCHUP", RecurringAction.CreateDraft, pastRun, _ownerId);

            Assert.Equal(HttpStatusCode.NoContent,
                (await Send(HttpMethod.Post, $"/api/RecurringSchedules/{scheduleId}/pause", new { reason = "x" })).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent,
                (await Send(HttpMethod.Post, $"/api/RecurringSchedules/{scheduleId}/resume")).StatusCode);

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            using (scope.ServiceProvider.GetRequiredService<ITenantContext>().Impersonate(_companyId, _slug, CompanyMemberRole.Owner))
            {
                var schedule = db.RecurringSchedules.Single(s => s.Id == scheduleId);
                Assert.Equal(RecurringScheduleStatus.Active, schedule.Status);
                Assert.NotNull(schedule.NextRunDate);
                Assert.True(schedule.NextRunDate!.Value.Date >= DateTime.UtcNow.Date,
                    "Resume must not back-fill a past run date.");
                // No occurrences were generated by the resume (no catch-up documents).
                Assert.Empty(db.RecurringOccurrences.Where(o => o.RecurringScheduleId == scheduleId));
            }
        }

        [SqliteFact]
        public async Task Recurring_List_And_Preview_Endpoints_Work()
        {
            var runDate = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 14);
            int scheduleId = SeedSalesSchedule("LIST-ME", RecurringAction.CreateDraft, runDate, _ownerId);

            var listRes = await Send(HttpMethod.Get, "/api/RecurringSchedules?status=Active");
            Assert.Equal(HttpStatusCode.OK, listRes.StatusCode);
            var list = await Json(listRes);
            Assert.True(list.GetArrayLength() >= 1);
            Assert.Contains(list.EnumerateArray(), e => e.GetProperty("id").GetInt32() == scheduleId);

            var detailRes = await Send(HttpMethod.Get, $"/api/RecurringSchedules/{scheduleId}");
            Assert.Equal(HttpStatusCode.OK, detailRes.StatusCode);
            var detail = await Json(detailRes);
            Assert.Equal(scheduleId, detail.GetProperty("id").GetInt32());
            Assert.True(detail.GetProperty("upcomingDates").GetArrayLength() >= 1);

            var previewRes = await Send(HttpMethod.Post, "/api/RecurringSchedules/preview", new
            {
                cadence = "Monthly",
                interval = 1,
                startDate = IsoDate(new DateTime(2026, 1, 31)),
                dayOfMonth = 31,
                count = 3,
            });
            Assert.Equal(HttpStatusCode.OK, previewRes.StatusCode);
            var preview = await Json(previewRes);
            var dates = preview.GetProperty("dates");
            Assert.Equal(3, dates.GetArrayLength());
            // Day 31 on a February occurrence clamps to the last day of February (28/29), per the calendar rule.
            Assert.Contains(dates.EnumerateArray(), d =>
            {
                var parsed = DateTime.Parse(d.GetString()!);
                return parsed.Month == 2 && parsed.Day == DateTime.DaysInMonth(parsed.Year, 2);
            });
        }

        [SqliteFact]
        public async Task Recurring_CrossTenant_SecondOrgCannotSeeOrActOnAnothersSchedule()
        {
            // A schedule in the first company.
            var runDate = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 15);
            int scheduleId = SeedSalesSchedule("XTEN", RecurringAction.CreateDraft, runDate, _ownerId);

            // _otherToken owns its own, separate company.
            var otherOrg = await Json(await Send(HttpMethod.Post, "/api/organizations",
                new { name = "Second Co", slug = "second-co" }, token: _otherToken, scoped: false));
            var secondSlug = "second-co";

            // Helper: a request as the second tenant (its own org header, its own token).
            Task<HttpResponseMessage> AsSecond(HttpMethod m, string path, object body = null) =>
                Send(m, path, body, token: _otherToken, scoped: false,
                    extraHeaders: new System.Collections.Generic.Dictionary<string, string> { ["X-AccountGo-Org"] = secondSlug });

            // The list, if reachable at all for the second company, must never contain the first company's schedule.
            // (A fresh org may lack the Sales module, in which case the module gate returns 403 — also "no access".)
            var listRes = await AsSecond(HttpMethod.Get, "/api/RecurringSchedules");
            Assert.NotEqual(HttpStatusCode.InternalServerError, listRes.StatusCode);
            if (listRes.StatusCode == HttpStatusCode.OK)
            {
                var list = await Json(listRes);
                Assert.DoesNotContain(list.EnumerateArray(), e => e.GetProperty("id").GetInt32() == scheduleId);
            }
            else
            {
                Assert.Contains(listRes.StatusCode,
                    new[] { HttpStatusCode.Forbidden, HttpStatusCode.NotFound });
            }

            // Direct detail / occurrences by the other company's id must never return the real data: a 404 (tenant
            // scope) or 403 (module gate) or an empty occurrence list are all acceptable; 200-with-the-schedule is not.
            var detailRes = await AsSecond(HttpMethod.Get, $"/api/RecurringSchedules/{scheduleId}");
            Assert.NotEqual(HttpStatusCode.OK, detailRes.StatusCode);
            var occRes = await AsSecond(HttpMethod.Get, $"/api/RecurringSchedules/{scheduleId}/occurrences");
            if (occRes.StatusCode == HttpStatusCode.OK)
                Assert.Equal(0, (await Json(occRes)).GetArrayLength());

            // Lifecycle actions on a foreign id must never succeed (no 2xx).
            foreach (var action in new[] { "pause", "resume", "end" })
            {
                var res = await AsSecond(HttpMethod.Post, $"/api/RecurringSchedules/{scheduleId}/{action}", new { reason = "x" });
                Assert.False(res.IsSuccessStatusCode, $"{action} on a foreign schedule must not succeed.");
            }
            var skipRes = await AsSecond(HttpMethod.Post, $"/api/RecurringSchedules/{scheduleId}/skip",
                new { runDate = IsoDate(runDate), reason = "x" });
            Assert.False(skipRes.IsSuccessStatusCode, "skip on a foreign schedule must not succeed.");

            // The first company's schedule is completely unchanged.
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            using (scope.ServiceProvider.GetRequiredService<ITenantContext>().Impersonate(_companyId, _slug, CompanyMemberRole.Owner))
            {
                var schedule = db.RecurringSchedules.Single(s => s.Id == scheduleId);
                Assert.Equal(RecurringScheduleStatus.Active, schedule.Status);
                Assert.Equal(runDate, schedule.NextRunDate);
            }
        }

        [SqliteFact]
        public async Task Recurring_Create_AutoApprove_IsRejected()
        {
            // AutoApprove (a vendor-bill disposition) must not persist on a sales schedule, and must not slip past
            // the auto-issue post gate.
            var body = new
            {
                customerId = SeedCustomerId("AUTOAPPROVE"),
                name = "Bad action",
                cadence = "Monthly",
                interval = 1,
                dayOfMonth = 1,
                startDate = IsoDate(new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1)),
                action = "AutoApprove",
                servicePeriodMode = "CurrentMonth",
                lines = new[] { new { description = "Svc", quantity = 1, amount = 100, discount = 0, discountType = "Percentage" } },
            };
            var res = await Send(HttpMethod.Post, "/api/RecurringSchedules", body);
            Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
            Assert.Contains("draft or auto-issue", await res.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Seeds just a customer and returns its id (for create-endpoint tests).</summary>
        private int SeedCustomerId(string tag)
        {
            var id = 0;
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            using (scope.ServiceProvider.GetRequiredService<ITenantContext>().Impersonate(_companyId, _slug, CompanyMemberRole.Owner))
            {
                var customer = new Core.Domain.Sales.Customer { CompanyId = _companyId, No = tag + "-CUST" };
                db.Customers.Add(customer);
                db.SaveChanges();
                id = customer.Id;
            }
            return id;
        }

        /// <summary>Seeds a one-line service-item sales schedule whose NextRunDate is the given run date.</summary>
        private int SeedSalesSchedule(string tag, RecurringAction action, DateTime runDate, string automationEnabledBy,
            RecurringScheduleStatus status = RecurringScheduleStatus.Active)
        {
            var scheduleId = 0;
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            using (scope.ServiceProvider.GetRequiredService<ITenantContext>().ImpersonateUser(_companyId, _ownerId, _slug, CompanyMemberRole.Owner))
            {
                var salesAccountId = db.Accounts.Single(a => a.AccountCode == "40100").Id;
                var arAccountId = db.Accounts.Single(a => a.AccountCode == "10120").Id;
                var measurementId = db.Measurements.First().Id;

                var customer = new Core.Domain.Sales.Customer { CompanyId = _companyId, No = tag + "-CUST", AccountsReceivableAccountId = arAccountId };
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
