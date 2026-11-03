// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Core.Data;
using Core.Domain;
using Core.Domain.Items;
using Core.Domain.Recurring;
using Core.Domain.Recurring.Calendar;
using Core.Domain.Sales;
using Core.Domain.Security;
using Core.Domain.TaxSystem;
using Core.Tenancy;
using Microsoft.Extensions.Logging;
using Services.Inventory;
using Services.Sales;

namespace Services.Recurring
{
    /// <summary>
    /// #482: processes a claimed sales-invoice occurrence into a draft invoice, and issues it when the schedule
    /// asks for auto-issue and every guard in the ticket passes. See <see cref="IRecurringInvoiceGenerationService"/>.
    /// </summary>
    public sealed class RecurringInvoiceGenerationService : IRecurringInvoiceGenerationService
    {
        private readonly ITenantContext _tenant;
        private readonly IUnitOfWork _unitOfWork;
        private readonly ISalesService _salesService;
        private readonly IInventoryService _inventoryService;
        private readonly IRepository<RecurringOccurrence> _occurrenceRepo;
        private readonly IRepository<RecurringSchedule> _scheduleRepo;
        private readonly IRepository<RecurringScheduleLine> _lineRepo;
        private readonly IRepository<Customer> _customerRepo;
        private readonly IRepository<CompanySetting> _companySettingRepo;
        private readonly IRepository<Tax> _taxRepo;
        private readonly IRepository<CompanyMember> _memberRepo;
        private readonly IRepository<SecurityRolePermission> _rolePermissionRepo;
        private readonly ILogger<RecurringInvoiceGenerationService> _logger;
        private readonly Core.Authorization.IWorkspacePolicyTransaction? _policyTransaction;

        public RecurringInvoiceGenerationService(
            ITenantContext tenant,
            IUnitOfWork unitOfWork,
            ISalesService salesService,
            IInventoryService inventoryService,
            IRepository<RecurringOccurrence> occurrenceRepo,
            IRepository<RecurringSchedule> scheduleRepo,
            IRepository<RecurringScheduleLine> lineRepo,
            IRepository<Customer> customerRepo,
            IRepository<CompanySetting> companySettingRepo,
            IRepository<Tax> taxRepo,
            IRepository<CompanyMember> memberRepo,
            IRepository<SecurityRolePermission> rolePermissionRepo,
            ILogger<RecurringInvoiceGenerationService> logger,
            Core.Authorization.IWorkspacePolicyTransaction? policyTransaction = null)
        {
            _tenant = tenant;
            _unitOfWork = unitOfWork;
            _salesService = salesService;
            _inventoryService = inventoryService;
            _occurrenceRepo = occurrenceRepo;
            _scheduleRepo = scheduleRepo;
            _lineRepo = lineRepo;
            _customerRepo = customerRepo;
            _companySettingRepo = companySettingRepo;
            _taxRepo = taxRepo;
            _memberRepo = memberRepo;
            _rolePermissionRepo = rolePermissionRepo;
            _logger = logger;
            _policyTransaction = policyTransaction;
        }

        public int EnsureOccurrence(int scheduleId, DateTime scheduledRunDate, string leaseOwner, DateTime leaseUntil)
        {
            var companyId = _tenant.RequireCompanyId();
            var runDate = scheduledRunDate.Date;
            using var tx = _unitOfWork.BeginSerializableTransaction();
            _unitOfWork.DiscardPendingChanges();

            var schedule = _scheduleRepo.Table.FirstOrDefault(s => s.Id == scheduleId)
                ?? throw new InvalidOperationException($"Recurring schedule {scheduleId} not found.");

            // A schedule that is not actively generating must neither create a new occurrence nor re-claim an
            // existing one for processing. Checked before any claim so a paused schedule can't be run — directly
            // via run-now or indirectly by picking up an occurrence left behind before it was paused.
            if (schedule.Status != RecurringScheduleStatus.Active)
                throw new InvalidRunDateException(
                    schedule.Status == RecurringScheduleStatus.Paused
                        ? "The schedule is paused. Resume it before generating a run."
                        : "The schedule has ended.");

            var existing = _occurrenceRepo.Table
                .FirstOrDefault(o => o.RecurringScheduleId == scheduleId && o.ScheduledRunDate == runDate);
            if (existing != null)
            {
                // Claim an occurrence that is free to run: still Scheduled, or Running with an expired lease
                // (a crashed prior attempt). A live lease held by someone else, or an already-finished occurrence,
                // is returned untouched for ProcessOccurrence to treat as abort/replay.
                var leaseExpired = !existing.LeasedUntil.HasValue || existing.LeasedUntil.Value < DateTime.UtcNow;
                if (existing.Status == RecurringOccurrenceStatus.Scheduled
                    || (existing.Status == RecurringOccurrenceStatus.Running && leaseExpired)
                    || (existing.Status == RecurringOccurrenceStatus.Failed && !existing.SalesInvoiceId.HasValue))
                {
                    existing.Status = RecurringOccurrenceStatus.Running;
                    existing.LeasedBy = leaseOwner;
                    existing.LeasedUntil = leaseUntil;
                    existing.ErrorCode = null;
                    existing.ErrorMessage = null;
                    existing.GuardFailureReason = null;
                    existing.UpdatedAt = DateTime.UtcNow;
                    _occurrenceRepo.Update(existing);
                }
                tx.Commit();
                return existing.Id;
            }

            // A new occurrence may only be created on a date the schedule's calendar actually produces, within its
            // active window. This stops a caller-supplied date from manufacturing a second, duplicate-period
            // document in a month that already has a legitimate run (each distinct date is a distinct idempotency key).
            ValidateRunDateOnCalendar(schedule, runDate);

            var documentDate = runDate;
            var (periodStart, periodEnd) = NextDateCalculator.ComputeServicePeriod(
                documentDate, schedule.ServicePeriodMode, schedule.Cadence, schedule.Interval);

            var occurrence = new RecurringOccurrence
            {
                CompanyId = companyId,
                RecurringScheduleId = scheduleId,
                ScheduledRunDate = runDate,
                PeriodStart = periodStart,
                PeriodEnd = periodEnd,
                DocumentDate = documentDate,
                Status = RecurringOccurrenceStatus.Running,
                IdempotencyKey = BuildIdempotencyKey(companyId, scheduleId, runDate),
                LeasedBy = leaseOwner,
                LeasedUntil = leaseUntil,
                AttemptCount = 0,
                CreatedAt = DateTime.UtcNow,
            };

            // The unique (CompanyId, ScheduleId, ScheduledRunDate) index makes this safe under a race: a
            // concurrent insert of the same run date throws on SaveChanges, and we fall back to the existing row.
            try
            {
                _occurrenceRepo.Insert(occurrence);
                tx.Commit();
                return occurrence.Id;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Occurrence for schedule {ScheduleId} on {RunDate} already exists; reusing.", scheduleId, runDate);
                tx.Dispose();
                _unitOfWork.DiscardPendingChanges();
                var raced = _occurrenceRepo.Table
                    .FirstOrDefault(o => o.RecurringScheduleId == scheduleId && o.ScheduledRunDate == runDate);
                if (raced != null)
                    return raced.Id;
                throw;
            }
        }

        public RecurringGenerationResult ProcessOccurrence(int occurrenceId, string leaseOwner)
        {
            _tenant.RequireCompanyId();
            using var tx = _unitOfWork.BeginSerializableTransaction();
            var policyScope = _policyTransaction?.BeginExternalScope();
            // Claiming and controller lookups may have tracked older versions. Read authoritative state only
            // after acquiring the transaction; a tracking query alone would reuse those stale property values.
            _unitOfWork.DiscardPendingChanges();

            var occurrence = _occurrenceRepo.Table.FirstOrDefault(o => o.Id == occurrenceId);
            if (occurrence == null)
            {
                tx.Dispose();
                return RecurringGenerationResult.Aborted(occurrenceId, "Occurrence not found.");
            }

            // Idempotent replay: the occurrence already produced a document.
            if (occurrence.SalesInvoiceId.HasValue &&
                (occurrence.Status == RecurringOccurrenceStatus.DraftCreated ||
                 occurrence.Status == RecurringOccurrenceStatus.Issued))
            {
                var invoiceId0 = occurrence.SalesInvoiceId;
                // Crash recovery: a prior attempt committed the document but the process may have died before the
                // series advanced. If this terminal occurrence has not advanced the series, do it now — in this
                // transaction, exactly once (the flag makes it idempotent) — so the schedule is never stranded on a
                // completed occurrence. No new document is created.
                if (!occurrence.SeriesAdvanced)
                {
                    var sched = _scheduleRepo.Table.FirstOrDefault(s => s.Id == occurrence.RecurringScheduleId);
                    if (sched != null)
                    {
                        // Modifying the schedule triggers the #481 cross-tenant consistency guard in SaveChanges,
                        // which resolves the schedule's Customer. Pre-load it into the tracker so that guard
                        // resolves from the change tracker (Local) rather than issuing a DB read mid-transaction.
                        _ = _customerRepo.Table.FirstOrDefault(c => c.Id == sched.PartyId);
                        AdvanceSeriesForOccurrence(sched, occurrence, attemptSucceeded: true);
                        tx.Commit();
                        return RecurringGenerationResult.AlreadyProcessed(occurrenceId, invoiceId0);
                    }
                }
                tx.Dispose();
                return RecurringGenerationResult.AlreadyProcessed(occurrenceId, invoiceId0);
            }

            // Only a leased, in-progress occurrence may be processed, and only by its lease owner.
            if (occurrence.Status != RecurringOccurrenceStatus.Running)
            {
                tx.Dispose();
                return RecurringGenerationResult.Aborted(occurrenceId, $"Occurrence is {occurrence.Status}, not Running.");
            }
            if (!HoldsLease(occurrence, leaseOwner))
            {
                tx.Dispose();
                return RecurringGenerationResult.Aborted(occurrenceId, RecurringGuardReasons.LeaseNotHeld);
            }
            // Belt and braces for same-lease concurrency: never create a second document for an occurrence that
            // already carries one, even if its status was left mid-flight.
            if (occurrence.SalesInvoiceId.HasValue)
            {
                var invoiceId0 = occurrence.SalesInvoiceId;
                tx.Dispose();
                return RecurringGenerationResult.AlreadyProcessed(occurrenceId, invoiceId0);
            }

            var schedule = _scheduleRepo.Table.FirstOrDefault(s => s.Id == occurrence.RecurringScheduleId);
            if (schedule == null)
            {
                tx.Dispose();
                _unitOfWork.DiscardPendingChanges();
                return Fail(occurrence, null, RecurringErrorCodes.InvalidSchedule, "Parent schedule not found.");
            }
            if (schedule.Kind != RecurringScheduleKind.SalesInvoice)
            {
                tx.Dispose();
                return RecurringGenerationResult.Aborted(occurrenceId, "Occurrence is not a sales-invoice schedule.");
            }
            // A schedule paused (or ended) after this occurrence was claimed must not produce a document. Abort
            // without failing — pausing is a temporary, reversible state, so the occurrence waits for a resume.
            if (schedule.Status != RecurringScheduleStatus.Active)
            {
                tx.Dispose();
                return RecurringGenerationResult.Aborted(occurrenceId, $"Schedule is {schedule.Status}, not Active.");
            }

            var lines = _lineRepo.Table
                .Where(l => l.RecurringScheduleId == schedule.Id)
                .OrderBy(l => l.SortOrder)
                .ToList();

            var customer = _customerRepo.Table.FirstOrDefault(c => c.Id == schedule.PartyId);
            if (customer == null)
            {
                tx.Dispose();
                _unitOfWork.DiscardPendingChanges();
                return Fail(occurrence, null, RecurringErrorCodes.InvalidSchedule, "Customer not found for schedule.");
            }

            Dto.Sales.SalesInvoice draftDto;
            try
            {
                draftDto = BuildDraftDto(schedule, lines, customer, occurrence);
            }
            catch (RecurringGenerationException ex)
            {
                tx.Dispose();
                _unitOfWork.DiscardPendingChanges();
                return Fail(occurrence, null, ex.ErrorCode, ex.Message);
            }

            // CreateSalesInvoice goes through the same path the sales UI uses: it builds the backing sales order,
            // saves a Draft, and resolves per-line tax amounts. It can throw for a missing/archived tax or a
            // mis-set item; a throw must leave the occurrence Failed (and attention-visible), not Running.
            SalesInvoiceHeader invoice;
            try
            {
                var createResult = _salesService.CreateSalesInvoice(draftDto);
                if (createResult.IsFailure || createResult.Value?.Id is not int createdId || createdId == 0)
                {
                    var reason = createResult.IsFailure ? createResult.Error.Message : "Could not create the draft invoice.";
                    tx.Dispose();
                    _unitOfWork.DiscardPendingChanges();
                    return Fail(occurrence, null, RecurringErrorCodes.InvalidSchedule, reason);
                }

                invoice = _salesService.GetSalesInvoiceById(createdId);
                // Stamp the recurring origin so the document is identifiable for audit, reporting, and later
                // lifecycle handling. The DTO path does not carry Origin, so set it on the saved header.
                invoice.Origin = SalesInvoiceOrigins.RecurringSchedule;
                _salesService.UpdateSalesInvoice(invoice);
            }
            catch (RecurringGenerationException ex)
            {
                tx.Dispose();
                _unitOfWork.DiscardPendingChanges();
                return Fail(occurrence, null, ex.ErrorCode, ex.Message);
            }
            catch (Exception ex)
            {
                // A tax that no longer resolves (missing/archived) is a fail-closed tax error; anything else is an
                // invalid-schedule setup error. Either way the occurrence must be marked failed, never left Running.
                tx.Dispose();
                _unitOfWork.DiscardPendingChanges();
                var code = ex.Message.Contains("tax", StringComparison.OrdinalIgnoreCase)
                    ? RecurringErrorCodes.TaxNotResolved
                    : RecurringErrorCodes.InvalidSchedule;
                return Fail(occurrence, null, code, ex.Message);
            }

            occurrence.SalesInvoiceId = invoice.Id;
            occurrence.UpdatedAt = DateTime.UtcNow;

            var autoIssue = schedule.Action == RecurringAction.AutoIssue;
            if (!autoIssue)
            {
                occurrence.Status = RecurringOccurrenceStatus.DraftCreated;
                _occurrenceRepo.Update(occurrence);
                // Advance the series in the same transaction that commits the draft, so the occurrence's terminal
                // state and the schedule's new position are persisted atomically.
                AdvanceSeriesForOccurrence(schedule, occurrence, attemptSucceeded: true);
                tx.Commit();
                return RecurringGenerationResult.Draft(occurrenceId, invoice.Id);
            }

            // Auto-issue path: evaluate the #482 guards.
            var guard = EvaluateAutoIssueGuards(schedule, customer, invoice, occurrence);

            if (guard.Decision == GuardDecision.DowngradeToDraft)
            {
                occurrence.Status = RecurringOccurrenceStatus.DraftCreated;
                occurrence.GuardFailureReason = guard.Reason;
                occurrence.UpdatedAt = DateTime.UtcNow;
                _occurrenceRepo.Update(occurrence);
                // A downgrade still produced a committed draft: it is a terminal success for the series, which
                // advances atomically with the commit (a guard downgrade on a final occurrence keeps NeedsAttention).
                AdvanceSeriesForOccurrence(schedule, occurrence, attemptSucceeded: true);
                tx.Commit();
                return RecurringGenerationResult.DraftGuardFailed(occurrenceId, invoice.Id, guard.Reason!);
            }

            if (guard.Decision == GuardDecision.FailClosed)
            {
                // Do not post. Roll back the whole attempt (including the draft) and mark the occurrence failed.
                tx.Dispose();
                _unitOfWork.DiscardPendingChanges();
                policyScope?.RolledBack();
                return Fail(occurrence, null, guard.ErrorCode!, guard.Reason!);
            }

            // All guards passed: issue (post GL) and complete the occurrence in the same transaction.
            try
            {
                _salesService.PostSalesInvoice(invoice.Id);
            }
            catch (Core.Domain.BooksLockedException ex)
            {
                tx.Dispose();
                _unitOfWork.DiscardPendingChanges();
                policyScope?.RolledBack();
                return Fail(occurrence, null, RecurringErrorCodes.BooksLocked, ex.Message);
            }
            catch (Core.Domain.BooksStartViolationException ex)
            {
                tx.Dispose();
                _unitOfWork.DiscardPendingChanges();
                policyScope?.RolledBack();
                return Fail(occurrence, null, RecurringErrorCodes.BooksLocked, ex.Message);
            }
            catch (Exception ex)
            {
                tx.Dispose();
                _unitOfWork.DiscardPendingChanges();
                policyScope?.RolledBack();
                return Fail(occurrence, null, RecurringErrorCodes.PostFailed, ex.Message);
            }

            occurrence.Status = RecurringOccurrenceStatus.Issued;
            occurrence.GuardFailureReason = null;
            occurrence.UpdatedAt = DateTime.UtcNow;
            _occurrenceRepo.Update(occurrence);
            // Advance the series atomically with issuing the invoice.
            AdvanceSeriesForOccurrence(schedule, occurrence, attemptSucceeded: true);
            tx.Commit();
            // The invoice is durably posted: drain the deferred first-useful-action telemetry enqueued by
            // PostSalesInvoice for this committed action. Dispose first so the drain opens its own
            // transaction (the service owns this one). Never throws into generation.
            tx.Dispose();
            policyScope?.Committed();
            return RecurringGenerationResult.Issued(occurrenceId, invoice.Id);
        }

        // ----- #480: manual run and retry -----

        public RecurringGenerationResult RunSchedule(int scheduleId, DateTime? runDate, string leaseOwner, DateTime leaseUntil)
        {
            _tenant.RequireCompanyId();

            var schedule = _scheduleRepo.Table.FirstOrDefault(s => s.Id == scheduleId)
                ?? throw new InvalidRunDateException($"Recurring schedule {scheduleId} not found.");

            if (schedule.Kind != RecurringScheduleKind.SalesInvoice)
                throw new InvalidRunDateException("This schedule does not generate sales invoices.");

            // A paused or ended schedule must not generate. Surface it the same way EnsureOccurrence would, before
            // any occurrence work, so the caller gets a clear 400 and nothing is created.
            if (schedule.Status != RecurringScheduleStatus.Active)
                throw new InvalidRunDateException(
                    schedule.Status == RecurringScheduleStatus.Paused
                        ? "The schedule is paused. Resume it before generating a run."
                        : "The schedule has ended.");

            // Manual execution runs only the schedule's next due occurrence. There must be a next run date to run —
            // an exhausted or never-scheduled schedule has nothing to generate here, and a failed past occurrence is
            // recovered through the retry endpoint, not by naming its date in run-now.
            if (!schedule.NextRunDate.HasValue)
                throw new InvalidRunDateException(
                    "This schedule has no next run date. There is nothing to run; retry a failed occurrence from its history instead.");

            var nextRunDate = schedule.NextRunDate.Value.Date;
            var effectiveRunDate = (runDate?.Date) ?? nextRunDate;

            // If the caller names a date it must be the current next run date; an arbitrary later date is rejected so
            // the intervening periods are never silently skipped and left unrecorded.
            if (effectiveRunDate != nextRunDate)
                throw new InvalidRunDateException(
                    $"This schedule's next run is {nextRunDate:yyyy-MM-dd}. " +
                    "A manual run can only generate the next occurrence.");

            // #480: archiving the client pauses the schedule. Do this before creating an occurrence so an archived
            // client produces no new document; pausing is reversible and the schedule is flagged for attention so a
            // person can see why it stopped. Existing open invoices are untouched.
            if (CustomerArchived(schedule.PartyId))
            {
                PauseForArchivedCustomer(schedule);
                return RecurringGenerationResult.Aborted(0, RecurringGuardReasons.CustomerArchivedSchedulePaused);
            }

            var occurrenceId = EnsureOccurrence(scheduleId, effectiveRunDate, leaseOwner, leaseUntil);
            // ProcessOccurrence advances the series atomically as part of committing the occurrence's terminal
            // state (see AdvanceSeriesForOccurrence), so there is no separate, non-atomic advancement step here.
            return ProcessOccurrence(occurrenceId, leaseOwner);
        }

        public RecurringGenerationResult SkipNextOccurrence(
            int scheduleId, DateTime? runDate, string actingUser, string reason, string leaseOwner, DateTime leaseUntil)
        {
            _tenant.RequireCompanyId();

            var schedule = _scheduleRepo.Table.FirstOrDefault(s => s.Id == scheduleId)
                ?? throw new InvalidRunDateException($"Recurring schedule {scheduleId} not found.");

            if (schedule.Kind != RecurringScheduleKind.SalesInvoice)
                throw new InvalidRunDateException("This schedule does not generate sales invoices.");

            if (schedule.Status != RecurringScheduleStatus.Active)
                throw new InvalidRunDateException(
                    schedule.Status == RecurringScheduleStatus.Paused
                        ? "The schedule is paused. Resume it before skipping a run."
                        : "The schedule has ended.");

            if (!schedule.NextRunDate.HasValue)
                throw new InvalidRunDateException(
                    "This schedule has no next run date. There is nothing to skip.");

            var nextRunDate = schedule.NextRunDate.Value.Date;
            var effectiveRunDate = (runDate?.Date) ?? nextRunDate;

            // Skip, like run, targets only the current next occurrence. A mismatched date is rejected so a caller
            // cannot skip past intervening periods and leave them unrecorded.
            if (effectiveRunDate != nextRunDate)
                throw new InvalidRunDateException(
                    $"This schedule's next run is {nextRunDate:yyyy-MM-dd}. A skip can only skip the next occurrence.");

            // Same archived-client policy as a run: pause and flag instead of recording a skip for an archived client.
            if (CustomerArchived(schedule.PartyId))
            {
                PauseForArchivedCustomer(schedule);
                return RecurringGenerationResult.Aborted(0, RecurringGuardReasons.CustomerArchivedSchedulePaused);
            }

            var occurrenceId = EnsureOccurrence(scheduleId, effectiveRunDate, leaseOwner, leaseUntil);

            // Mark the occurrence Skipped and advance the series atomically in one serializable transaction, keyed on
            // the occurrence so a replay cannot skip-and-advance twice. The calendar anchor is untouched: advancement
            // computes the next date from the schedule's StartDate, so later dates do not shift.
            using var tx = _unitOfWork.BeginSerializableTransaction();
            _unitOfWork.DiscardPendingChanges();

            // Re-read the schedule inside the transaction (DiscardPendingChanges detached the copy read above), so the
            // advance mutates a tracked row that will actually persist on commit.
            var trackedSchedule = _scheduleRepo.Table.FirstOrDefault(s => s.Id == scheduleId);
            if (trackedSchedule == null)
            {
                tx.Dispose();
                return RecurringGenerationResult.Aborted(0, "Schedule not found.");
            }

            var occurrence = _occurrenceRepo.Table.FirstOrDefault(o => o.Id == occurrenceId);
            if (occurrence == null)
            {
                tx.Dispose();
                return RecurringGenerationResult.Aborted(occurrenceId, "Occurrence not found.");
            }

            // Idempotent: an occurrence that already produced a document (a race with a run) is not skipped.
            if (occurrence.SalesInvoiceId.HasValue &&
                (occurrence.Status == RecurringOccurrenceStatus.DraftCreated ||
                 occurrence.Status == RecurringOccurrenceStatus.Issued))
            {
                var invoiceId0 = occurrence.SalesInvoiceId;
                tx.Dispose();
                return RecurringGenerationResult.AlreadyProcessed(occurrenceId, invoiceId0);
            }

            // Only the lease owner may skip the claimed occurrence; a live lease held elsewhere is left alone.
            if (occurrence.Status != RecurringOccurrenceStatus.Running || !HoldsLease(occurrence, leaseOwner))
            {
                tx.Dispose();
                return RecurringGenerationResult.Aborted(occurrenceId, RecurringGuardReasons.LeaseNotHeld);
            }

            // Pre-load the Customer so the #481 consistency guard resolves from the change tracker, not a DB read
            // inside the serializable transaction.
            _ = _customerRepo.Table.FirstOrDefault(c => c.Id == trackedSchedule.PartyId);

            occurrence.Status = RecurringOccurrenceStatus.Skipped;
            occurrence.ActionBy = actingUser;
            occurrence.SkipReason = Truncate(reason, 500);
            occurrence.LeasedBy = null;
            occurrence.LeasedUntil = null;
            occurrence.UpdatedAt = DateTime.UtcNow;
            _occurrenceRepo.Update(occurrence);

            // A skip is a terminal, successful disposition of the period: advance the series by one (attemptSucceeded
            // so an exhausting skip ends the schedule), exactly once, without changing the anchor.
            AdvanceSeriesForOccurrence(trackedSchedule, occurrence, attemptSucceeded: true);
            tx.Commit();

            return RecurringGenerationResult.Skipped(occurrenceId, Truncate(reason, 500));
        }

        public RecurringGenerationResult RetryOccurrence(int occurrenceId, string leaseOwner, DateTime leaseUntil)
        {
            _tenant.RequireCompanyId();

            var occurrence = _occurrenceRepo.Table.FirstOrDefault(o => o.Id == occurrenceId);
            if (occurrence == null)
                return RecurringGenerationResult.Aborted(occurrenceId, "Occurrence not found.");


            // Idempotent: an occurrence that already produced a document is not retried — return the existing link.
            if (occurrence.SalesInvoiceId.HasValue
                && (occurrence.Status == RecurringOccurrenceStatus.DraftCreated
                    || occurrence.Status == RecurringOccurrenceStatus.Issued))
                return RecurringGenerationResult.AlreadyProcessed(occurrenceId, occurrence.SalesInvoiceId);

            // Only a failed (or crashed-mid-run with an expired lease) occurrence with no document is retryable.
            // A still-scheduled occurrence, a live lease held by someone else, or a terminal state is left alone.
            var leaseExpired = !occurrence.LeasedUntil.HasValue || occurrence.LeasedUntil.Value < DateTime.UtcNow;
            var retryable =
                (occurrence.Status == RecurringOccurrenceStatus.Failed && !occurrence.SalesInvoiceId.HasValue)
                || (occurrence.Status == RecurringOccurrenceStatus.Running && leaseExpired && !occurrence.SalesInvoiceId.HasValue);
            if (!retryable)
                return RecurringGenerationResult.Aborted(occurrenceId,
                    $"Occurrence is {occurrence.Status} and cannot be retried.");

            var schedule = _scheduleRepo.Table.FirstOrDefault(s => s.Id == occurrence.RecurringScheduleId);
            if (schedule == null)
                return RecurringGenerationResult.Aborted(occurrenceId, "Parent schedule not found.");
            // A retry must not resurrect a paused/ended schedule's generation.
            if (schedule.Status != RecurringScheduleStatus.Active)
                return RecurringGenerationResult.Aborted(occurrenceId, $"Schedule is {schedule.Status}, not Active.");

            // Same archived-client policy as a fresh run: if the customer was archived after the failed attempt, a
            // retry must not quietly produce a draft for an archived client. Pause the schedule and flag it instead.
            if (CustomerArchived(schedule.PartyId))
            {
                PauseForArchivedCustomer(schedule);
                return RecurringGenerationResult.Aborted(occurrenceId, RecurringGuardReasons.CustomerArchivedSchedulePaused);
            }

            // Re-claim the occurrence under this owner, then process. EnsureOccurrence re-claims a failed row for
            // the same (schedule, run date), so retrying by occurrence id goes through the identical claim path and
            // keeps the same idempotency key. ProcessOccurrence does not advance the series again for a retry (the
            // occurrence's SeriesAdvanced flag is already set from its original attempt); if this retry completes the
            // schedule's final occurrence, it finalizes the exhausted schedule as Ended — all atomically.
            var claimedId = EnsureOccurrence(schedule.Id, occurrence.ScheduledRunDate, leaseOwner, leaseUntil);
            return ProcessOccurrence(claimedId, leaseOwner);
        }

        /// <summary>
        /// The single place the schedule's series position is advanced in response to an occurrence reaching a
        /// terminal state. Exactly-once per occurrence: it is a no-op once the occurrence's
        /// <see cref="RecurringOccurrence.SeriesAdvanced"/> flag is set. The caller is responsible for persisting —
        /// this only mutates the tracked <paramref name="schedule"/> and <paramref name="occurrence"/>, so when it
        /// runs inside <see cref="ProcessOccurrence"/>'s serializable transaction the schedule advance, the
        /// occurrence status, and the flag all commit atomically. A crash before commit leaves the occurrence
        /// non-terminal or with the flag unset, so a later run/replay re-advances exactly once.
        ///
        /// Forward runs (first attempt) advance the series by one occurrence, honouring the end date and occurrence
        /// limit: when the series is exhausted the next date is cleared. The schedule is marked
        /// <see cref="RecurringScheduleStatus.Ended"/> only when the exhausting run actually
        /// <paramref name="attemptSucceeded"/>; a failed final run clears the next date but leaves the schedule
        /// Active (flagged for attention) so its failed occurrence stays retryable.
        ///
        /// When the occurrence has already advanced the series (a retry of a previously-failed occurrence, whose
        /// original attempt advanced), the series is not advanced again. A now-successful retry that completes the
        /// schedule's last occurrence instead finalizes the exhausted-but-active schedule as Ended.
        /// </summary>
        private void AdvanceSeriesForOccurrence(RecurringSchedule schedule, RecurringOccurrence occurrence, bool attemptSucceeded)
        {
            var trackedSchedule = schedule;
            var trackedOccurrence = occurrence;

            if (trackedOccurrence.SeriesAdvanced)
            {
                // The series already moved for this occurrence (its first attempt advanced it). A retry must not
                // advance again. If this is the now-successful completion of the schedule's final occurrence — the
                // series is exhausted (no next date) but a failed final run left the schedule Active — finalize it.
                FinalizeExhaustedScheduleIfComplete(trackedSchedule, trackedOccurrence, attemptSucceeded);
                return;
            }

            DateTime next;
            try
            {
                next = NextDateCalculator.ComputeNextDate(
                    trackedSchedule.Cadence, trackedSchedule.Interval, trackedOccurrence.ScheduledRunDate,
                    trackedSchedule.DayOfMonth, trackedSchedule.StartDate);
            }
            catch (Exception ex)
            {
                // A calendar rule that can't produce a next date (e.g. bad day-of-month) must not strand the run.
                // Leave NextRunDate as-is for a person to correct the schedule, but still mark the occurrence
                // advanced so a replay does not retry this computation forever.
                _logger.LogWarning(ex, "Could not advance NextRunDate for schedule {ScheduleId} after {RunDate}.",
                    trackedSchedule.Id, trackedOccurrence.ScheduledRunDate);
                trackedOccurrence.SeriesAdvanced = true;
                _occurrenceRepo.Update(trackedOccurrence);
                return;
            }

            if (SeriesExhaustedAt(trackedSchedule, next))
            {
                // No more forward runs either way. End the schedule only when the exhausting run succeeded; a failed
                // final run leaves it Active (NextRunDate cleared) so the failed occurrence can still be retried.
                trackedSchedule.NextRunDate = null;
                if (attemptSucceeded)
                    trackedSchedule.Status = RecurringScheduleStatus.Ended;
                else
                    trackedSchedule.NeedsAttention = true;
            }
            else
            {
                trackedSchedule.NextRunDate = next;
            }

            trackedSchedule.OccurrenceCount += 1;
            trackedOccurrence.SeriesAdvanced = true;

            _scheduleRepo.Update(trackedSchedule);
            _occurrenceRepo.Update(trackedOccurrence);
        }

        /// <summary>
        /// Finalizes a schedule a failed final run left Active-with-no-next-date once a now-successful retry
        /// completes that final occurrence. A guard downgrade (<paramref name="attemptSucceeded"/> with the
        /// occurrence recording a guard-failure reason) ends the series but preserves <c>NeedsAttention</c> so the
        /// reason stays surfaced for review. Does nothing to any other schedule state.
        /// </summary>
        private void FinalizeExhaustedScheduleIfComplete(RecurringSchedule trackedSchedule, RecurringOccurrence trackedOccurrence, bool attemptSucceeded)
        {
            if (!attemptSucceeded)
                return;
            // Only a schedule a failed final run left Active-with-no-next-date is a candidate.
            if (trackedSchedule.Status != RecurringScheduleStatus.Active || trackedSchedule.NextRunDate.HasValue)
                return;

            DateTime next;
            try
            {
                next = NextDateCalculator.ComputeNextDate(
                    trackedSchedule.Cadence, trackedSchedule.Interval, trackedOccurrence.ScheduledRunDate,
                    trackedSchedule.DayOfMonth, trackedSchedule.StartDate);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not evaluate series end for schedule {ScheduleId} after retry of {RunDate}.",
                    trackedSchedule.Id, trackedOccurrence.ScheduledRunDate);
                return;
            }

            if (!SeriesExhaustedAt(trackedSchedule, next))
                return;

            trackedSchedule.Status = RecurringScheduleStatus.Ended;
            // A clean success clears attention; a guard downgrade ends the series but keeps NeedsAttention so the
            // recorded guard-failure reason is still surfaced for review.
            var guardDowngrade = !string.IsNullOrEmpty(trackedOccurrence.GuardFailureReason);
            if (!guardDowngrade)
                trackedSchedule.NeedsAttention = false;
            _scheduleRepo.Update(trackedSchedule);
        }

        /// <summary>
        /// True when <paramref name="next"/> (the date after a just-run occurrence) falls past the schedule's end
        /// date or its occurrence limit — i.e. the schedule has no further run.
        /// </summary>
        private static bool SeriesExhaustedAt(RecurringSchedule schedule, DateTime next)
        {
            var position = NextDateCalculator.TryGetSeriesPosition(
                schedule.Cadence, schedule.Interval, schedule.StartDate, schedule.DayOfMonth, next);
            var pastEnd = schedule.EndDate.HasValue && next > schedule.EndDate.Value.Date;
            var pastMax = schedule.MaxOccurrences.HasValue && position >= 0 && position >= schedule.MaxOccurrences.Value;
            return pastEnd || pastMax;
        }

        private void PauseForArchivedCustomer(RecurringSchedule schedule)
        {
            // Re-load the tracked row: a prior ProcessOccurrence may have cleared the change tracker.
            var tracked = _scheduleRepo.Table.FirstOrDefault(s => s.Id == schedule.Id) ?? schedule;
            tracked.Status = RecurringScheduleStatus.Paused;
            tracked.NeedsAttention = true;
            _scheduleRepo.Update(tracked);
            _logger.LogInformation(
                "Schedule {ScheduleId} paused: customer {CustomerId} is archived.", tracked.Id, tracked.PartyId);
        }

        /// <summary>True when the schedule's customer has been archived (and is therefore not billable).</summary>
        private bool CustomerArchived(int partyId)
        {
            var customer = _customerRepo.Table.FirstOrDefault(c => c.Id == partyId)
                ?? throw new InvalidRunDateException("The schedule's customer could not be found.");
            return customer.ArchivedAt != null;
        }

        // ----- run-date validation -----

        /// <summary>
        /// Confirms <paramref name="runDate"/> is a date the schedule's calendar actually produces and is within the
        /// schedule's active window (start, end, max occurrences). Throws <see cref="InvalidRunDateException"/> otherwise.
        /// </summary>
        private static void ValidateRunDateOnCalendar(RecurringSchedule schedule, DateTime runDate)
        {
            // Defense in depth: callers gate on Active before reaching here, but keep the method self-consistent.
            if (schedule.Status != RecurringScheduleStatus.Active)
                throw new InvalidRunDateException(
                    schedule.Status == RecurringScheduleStatus.Paused
                        ? "The schedule is paused."
                        : "The schedule has ended.");

            if (runDate < schedule.StartDate.Date)
                throw new InvalidRunDateException(
                    $"{runDate:yyyy-MM-dd} is before the schedule start {schedule.StartDate:yyyy-MM-dd}.");

            if (schedule.EndDate.HasValue && runDate > schedule.EndDate.Value.Date)
                throw new InvalidRunDateException(
                    $"{runDate:yyyy-MM-dd} is after the schedule end {schedule.EndDate.Value:yyyy-MM-dd}.");

            // The series is anchored to the start; confirm the requested date is actually produced by it (so a
            // run-now for an arbitrary mid-period date is rejected rather than creating a duplicate-period document).
            // Position is found by walking only as far as the date, so there is no fixed horizon that would reject
            // valid far-future run dates.
            var position = NextDateCalculator.TryGetSeriesPosition(
                schedule.Cadence,
                schedule.Interval,
                schedule.StartDate,
                schedule.DayOfMonth,
                runDate);

            if (position < 0)
                throw new InvalidRunDateException(
                    $"{runDate:yyyy-MM-dd} is not a scheduled run date for this schedule.");

            if (schedule.MaxOccurrences.HasValue && position >= schedule.MaxOccurrences.Value)
                throw new InvalidRunDateException(
                    $"{runDate:yyyy-MM-dd} is past the schedule's {schedule.MaxOccurrences.Value}-occurrence limit.");
        }

        // ----- draft construction -----

        private Dto.Sales.SalesInvoice BuildDraftDto(
            RecurringSchedule schedule,
            IReadOnlyList<RecurringScheduleLine> lines,
            Customer customer,
            RecurringOccurrence occurrence)
        {
            if (lines.Count == 0)
                throw new RecurringGenerationException(RecurringErrorCodes.InvalidSchedule, "The schedule has no lines to invoice.");

            var dto = new Dto.Sales.SalesInvoice
            {
                CustomerId = customer.Id,
                InvoiceDate = occurrence.DocumentDate.Date,
                PaymentTermId = schedule.PaymentTermId,
                Notes = schedule.Notes,
                PaymentInstructions = schedule.PaymentInstructions,
                No = null,
                SalesInvoiceLines = new List<Dto.Sales.SalesInvoiceLine>(),
            };

            foreach (var line in lines)
            {
                if (!line.ItemId.HasValue)
                    throw new RecurringGenerationException(
                        RecurringErrorCodes.InvalidSchedule,
                        "A schedule line has no item. Free-text lines can't be generated into an invoice yet.");

                var item = _inventoryService.GetItemById(line.ItemId.Value)
                    ?? throw new RecurringGenerationException(
                        RecurringErrorCodes.InvalidSchedule, $"Item {line.ItemId} not found.");

                var measurementId = item.SellMeasurementId ?? item.SmallestMeasurementId
                    ?? throw new RecurringGenerationException(
                        RecurringErrorCodes.InvalidSchedule,
                        $"Item {item.No} has no unit of measure to copy onto the invoice.");

                dto.SalesInvoiceLines!.Add(new Dto.Sales.SalesInvoiceLine
                {
                    ItemId = item.Id,
                    MeasurementId = measurementId,
                    Quantity = line.Quantity,
                    Amount = line.Amount,
                    Discount = ToPercentDiscount(line),
                    TaxId = line.TaxId,
                    SalesAccountId = line.AccountId,
                    ItemDescription = SalesInvoiceLine.DescriptionFor(line.Description, item),
                });
            }

            return dto;
        }

        /// <summary>
        /// The sales invoice line stores discount as a percentage. Convert an absolute schedule discount into the
        /// equivalent percentage of the gross line so the posted figures match the schedule's intent.
        /// </summary>
        private static decimal ToPercentDiscount(RecurringScheduleLine line)
        {
            if (line.DiscountType == RecurringDiscountType.Percentage)
                return line.Discount;

            var gross = line.Quantity * line.Amount;
            if (gross <= 0)
                return 0;
            var percent = line.Discount / gross * 100m;
            return Math.Round(Math.Clamp(percent, 0m, 100m), 6, MidpointRounding.AwayFromZero);
        }

        // ----- guard evaluation -----

        private enum GuardDecision { Pass, DowngradeToDraft, FailClosed }

        private readonly struct GuardResult
        {
            public GuardDecision Decision { get; init; }
            public string? Reason { get; init; }
            public string? ErrorCode { get; init; }

            public static GuardResult Pass() => new() { Decision = GuardDecision.Pass };
            public static GuardResult Downgrade(string reason) => new() { Decision = GuardDecision.DowngradeToDraft, Reason = reason };
            public static GuardResult Fail(string code, string reason) => new() { Decision = GuardDecision.FailClosed, ErrorCode = code, Reason = reason };
        }

        private GuardResult EvaluateAutoIssueGuards(
            RecurringSchedule schedule,
            Customer customer,
            SalesInvoiceHeader invoice,
            RecurringOccurrence occurrence)
        {
            // Fail-closed guards first: a locked/out-of-range posting date or a tax that won't resolve must never
            // slip through to a draft-and-forget; per #482 they fail the occurrence without posting.

            // Tax resolution: a line that names a tax which is missing/archived cannot be issued; and a line that
            // resolves to zero tax because resolution failed must not be issued silently.
            var taxResult = CheckTaxResolves(invoice);
            if (taxResult.Decision == GuardDecision.FailClosed)
                return taxResult;

            // Posting date must be after BooksLockedThrough and on/after books start.
            var dateResult = CheckPostingDate(invoice.Date);
            if (dateResult.Decision == GuardDecision.FailClosed)
                return dateResult;

            // Downgradeable guards: a valid draft exists; a person can review and issue it.

            // Only monthly-period schedules may auto-issue. Quarterly and yearly schedules bill a period that
            // spans several calendar months, so their service period can never sit inside the document's month;
            // per the accounting rule they always stay drafts for a person to review.
            if (schedule.Cadence == RecurringCadence.Quarterly || schedule.Cadence == RecurringCadence.Yearly)
                return GuardResult.Downgrade(RecurringGuardReasons.MultiMonthCadence);

            // Service period and document date must fall in the same calendar month.
            if (NextDateCalculator.ServicePeriodCrossesMonthBoundary(occurrence.PeriodStart, occurrence.PeriodEnd)
                || occurrence.DocumentDate.Year != occurrence.PeriodStart.Year
                || occurrence.DocumentDate.Month != occurrence.PeriodStart.Month)
            {
                return GuardResult.Downgrade(RecurringGuardReasons.ServicePeriodCrossesMonth);
            }

            // Client must be active.
            if (customer.ArchivedAt != null)
                return GuardResult.Downgrade(RecurringGuardReasons.ClientNotActive);

            // No stock-tracked lines.
            if (HasStockTrackedLine(invoice))
                return GuardResult.Downgrade(RecurringGuardReasons.StockTrackedLine);

            // Auto-issue must have been enabled by a user still authorized to post invoices.
            if (!AutomationActorCanPost(schedule))
                return GuardResult.Downgrade(RecurringGuardReasons.AutomationActorNotAuthorized);

            return GuardResult.Pass();
        }

        private GuardResult CheckTaxResolves(SalesInvoiceHeader invoice)
        {
            var taxes = _taxRepo.Table.ToList();
            foreach (var line in invoice.SalesInvoiceLines)
            {
                if (!line.TaxId.HasValue)
                {
                    // A line with no tax is allowed only when it genuinely has no tax. We cannot distinguish a
                    // deliberate zero-tax line from a failed resolution here, so a null TaxId is treated as "no tax
                    // expected" — the schedule line is the source of truth for whether tax applies.
                    continue;
                }

                var tax = taxes.FirstOrDefault(t => t.Id == line.TaxId.Value);
                if (tax == null)
                    return GuardResult.Fail(RecurringErrorCodes.TaxNotResolved,
                        $"Tax {line.TaxId} on a line could not be found; refusing to issue.");
                if (!tax.IsActive)
                    return GuardResult.Fail(RecurringErrorCodes.TaxNotResolved,
                        $"Tax {tax.TaxName} is archived; refusing to issue at a wrong tax.");
                if (tax.Rate != 0 && line.TaxAmount == 0)
                    return GuardResult.Fail(RecurringErrorCodes.TaxNotResolved,
                        $"Tax {tax.TaxName} resolved to zero on a taxable line; refusing to issue.");
            }

            return GuardResult.Pass();
        }

        private GuardResult CheckPostingDate(DateTime documentDate)
        {
            var setting = _companySettingRepo.Table.FirstOrDefault();
            if (setting == null)
                return GuardResult.Pass();

            if (documentDate.Date < setting.BooksStartDate.Date)
                return GuardResult.Fail(RecurringErrorCodes.BooksLocked,
                    $"Document date {documentDate:yyyy-MM-dd} is before books start {setting.BooksStartDate:yyyy-MM-dd}.");

            if (setting.BooksLockedThrough.HasValue && documentDate.Date <= setting.BooksLockedThrough.Value.Date)
                return GuardResult.Fail(RecurringErrorCodes.BooksLocked,
                    $"Document date {documentDate:yyyy-MM-dd} is in a locked period (locked through {setting.BooksLockedThrough.Value:yyyy-MM-dd}).");

            return GuardResult.Pass();
        }

        private bool HasStockTrackedLine(SalesInvoiceHeader invoice)
        {
            foreach (var line in invoice.SalesInvoiceLines)
            {
                var item = _inventoryService.GetItemById(line.ItemId);
                if (item?.ItemCategory?.ItemType == ItemTypes.Purchased)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Resolves the permissions of the user who enabled auto-issue (stored on the schedule) in this company,
        /// mirroring how the request pipeline resolves a member's permissions, and confirms they may post invoices.
        /// </summary>
        private bool AutomationActorCanPost(RecurringSchedule schedule)
        {
            var userId = schedule.AutomationEnabledBy;
            if (string.IsNullOrWhiteSpace(userId))
                return false;

            var member = _memberRepo.Table
                .Where(m => m.CompanyId == schedule.CompanyId
                    && m.UserId == userId
                    && m.Status == CompanyMemberStatus.Active)
                .Select(m => new { m.Id, m.Role })
                .FirstOrDefault();
            if (member == null)
                return false;

            // Owners and admins hold every permission, exactly as the request pipeline grants them.
            if (member.Role == CompanyMemberRole.Owner || member.Role == CompanyMemberRole.Admin)
                return true;

            var roleIds = _memberRepo.Table
                .Where(m => m.Id == member.Id)
                .SelectMany(m => m.SecurityRoles.Select(r => r.SecurityRoleId))
                .ToList();
            if (roleIds.Count == 0)
                return false;

            return _rolePermissionRepo.Table
                .Any(srp => roleIds.Contains(srp.SecurityRoleId)
                    && srp.SecurityPermission.Name == SystemPermissions.SalesInvoicesPost);
        }

        // ----- helpers -----

        private static bool HoldsLease(RecurringOccurrence occurrence, string leaseOwner) =>
            !string.IsNullOrEmpty(leaseOwner)
            && string.Equals(occurrence.LeasedBy, leaseOwner, StringComparison.Ordinal)
            && occurrence.LeasedUntil.HasValue
            && occurrence.LeasedUntil.Value >= DateTime.UtcNow;

        private RecurringGenerationResult Fail(RecurringOccurrence occurrence, int? salesInvoiceId, string errorCode, string message)
        {
            // The document transaction has rolled back. Another attempt may have committed or reclaimed the
            // occurrence since then, so fence this failure against the original claim inside a fresh transaction.
            using var tx = _unitOfWork.BeginSerializableTransaction();
            _unitOfWork.DiscardPendingChanges();
            var tracked = _occurrenceRepo.Table.FirstOrDefault(o => o.Id == occurrence.Id);
            if (tracked == null)
                return RecurringGenerationResult.Aborted(occurrence.Id, "Occurrence not found.");
            if (tracked.SalesInvoiceId.HasValue)
                return RecurringGenerationResult.AlreadyProcessed(tracked.Id, tracked.SalesInvoiceId);
            if (tracked.Status != RecurringOccurrenceStatus.Running
                || !HoldsLease(tracked, occurrence.LeasedBy ?? string.Empty)
                || tracked.LeasedUntil != occurrence.LeasedUntil)
                return RecurringGenerationResult.Aborted(tracked.Id, RecurringGuardReasons.LeaseNotHeld);

            tracked.Status = RecurringOccurrenceStatus.Failed;
            tracked.ErrorCode = Truncate(errorCode, 50);
            tracked.ErrorMessage = Truncate(message, 2000);
            tracked.AttemptCount += 1;
            tracked.SalesInvoiceId = salesInvoiceId;
            tracked.GuardFailureReason = null;
            tracked.UpdatedAt = DateTime.UtcNow;

            // A failed attempt is still a terminal state for the series: the run was attempted and must not be
            // retried by rewinding the schedule. Advance the series here so the failed occurrence and the schedule's
            // new position persist together. The advance is exactly-once (keyed on the occurrence's SeriesAdvanced
            // flag), so a retry that fails again does not advance a second time. A schedule-not-found failure has no
            // schedule to advance and is left as a pure occurrence failure.
            var schedule = _scheduleRepo.Table.FirstOrDefault(s => s.Id == tracked.RecurringScheduleId);
            if (schedule != null)
            {
                // Pre-load the Customer so the #481 consistency guard in SaveChanges resolves from the change
                // tracker rather than issuing a DB read (the change tracker was cleared on the fail-closed path).
                _ = _customerRepo.Table.FirstOrDefault(c => c.Id == schedule.PartyId);
                AdvanceSeriesForOccurrence(schedule, tracked, attemptSucceeded: false);
            }

            _occurrenceRepo.Update(tracked);
            tx.Commit();
            return RecurringGenerationResult.Fail(tracked.Id, salesInvoiceId, errorCode, message);
        }

        private static string BuildIdempotencyKey(int companyId, int scheduleId, DateTime runDate) =>
            $"{companyId}-{scheduleId}-{runDate:yyyy-MM-dd}";

        private static string Truncate(string value, int max) =>
            value.Length <= max ? value : value.Substring(0, max);

        private sealed class RecurringGenerationException : Exception
        {
            public string ErrorCode { get; }
            public RecurringGenerationException(string errorCode, string message) : base(message) => ErrorCode = errorCode;
        }
    }
}
