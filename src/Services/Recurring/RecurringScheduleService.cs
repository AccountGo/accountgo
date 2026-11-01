// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Core.Data;
using Core.Domain;
using Core.Domain.Auditing;
using Core.Domain.Items;
using Core.Domain.Recurring;
using Core.Domain.Recurring.Calendar;
using Core.Domain.Sales;
using Core.Tenancy;
using Microsoft.Extensions.Logging;

namespace Services.Recurring
{
    /// <summary>
    /// #484: management (CRUD + lifecycle) for sales-invoice recurring schedules. See
    /// <see cref="IRecurringScheduleService"/>. Writes go through an <see cref="IUnitOfWork"/> serializable
    /// transaction so a schedule and its lines (and the audit row) commit together; lifecycle changes are audited.
    /// </summary>
    public sealed class RecurringScheduleService : IRecurringScheduleService
    {
        private const int DefaultPreviewCount = 3;
        private const string AuditTable = "RecurringSchedule";

        private readonly ITenantContext _tenant;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IRepository<RecurringSchedule> _scheduleRepo;
        private readonly IRepository<RecurringScheduleLine> _lineRepo;
        private readonly IRepository<RecurringOccurrence> _occurrenceRepo;
        private readonly IRepository<Customer> _customerRepo;
        private readonly IRepository<Party> _partyRepo;
        private readonly IRepository<Item> _itemRepo;
        private readonly IRepository<AuditLog> _auditRepo;
        private readonly ILogger<RecurringScheduleService> _logger;

        public RecurringScheduleService(
            ITenantContext tenant,
            IUnitOfWork unitOfWork,
            IRepository<RecurringSchedule> scheduleRepo,
            IRepository<RecurringScheduleLine> lineRepo,
            IRepository<RecurringOccurrence> occurrenceRepo,
            IRepository<Customer> customerRepo,
            IRepository<Party> partyRepo,
            IRepository<Item> itemRepo,
            IRepository<AuditLog> auditRepo,
            ILogger<RecurringScheduleService> logger)
        {
            _tenant = tenant;
            _unitOfWork = unitOfWork;
            _scheduleRepo = scheduleRepo;
            _lineRepo = lineRepo;
            _occurrenceRepo = occurrenceRepo;
            _customerRepo = customerRepo;
            _partyRepo = partyRepo;
            _itemRepo = itemRepo;
            _auditRepo = auditRepo;
            _logger = logger;
        }

        // ----- reads -----

        public IReadOnlyList<RecurringScheduleSummary> ListSchedules(RecurringScheduleStatus? status, bool needsAttentionOnly)
        {
            _tenant.RequireCompanyId();
            var today = Today();

            var query = _scheduleRepo.Table.Where(s => s.Kind == RecurringScheduleKind.SalesInvoice);
            if (status.HasValue)
                query = query.Where(s => s.Status == status.Value);
            if (needsAttentionOnly)
                query = query.Where(s => s.NeedsAttention);

            var schedules = query.ToList();
            var names = CustomerNames(schedules.Select(s => s.PartyId));
            var archived = CustomerArchivedFlags(schedules.Select(s => s.PartyId));

            return schedules
                .OrderByDescending(s => s.Id)
                .Select(s => new RecurringScheduleSummary
                {
                    Id = s.Id,
                    Name = s.Name,
                    CustomerId = s.PartyId,
                    CustomerName = names.GetValueOrDefault(s.PartyId),
                    CustomerArchived = archived.GetValueOrDefault(s.PartyId),
                    Status = s.Status,
                    Cadence = s.Cadence,
                    Interval = s.Interval,
                    Action = s.Action,
                    NextRunDate = s.NextRunDate,
                    StartDate = s.StartDate,
                    EndDate = s.EndDate,
                    MaxOccurrences = s.MaxOccurrences,
                    OccurrenceCount = s.OccurrenceCount,
                    NeedsAttention = s.NeedsAttention,
                    ConsecutiveFailures = s.ConsecutiveFailures,
                    IsOverdue = IsOverdue(s, today),
                })
                .ToList();
        }

        public RecurringScheduleDetail? GetSchedule(int scheduleId)
        {
            _tenant.RequireCompanyId();
            var schedule = _scheduleRepo.Table.FirstOrDefault(s =>
                s.Id == scheduleId && s.Kind == RecurringScheduleKind.SalesInvoice);
            if (schedule == null)
                return null;

            var lines = _lineRepo.Table
                .Where(l => l.RecurringScheduleId == scheduleId)
                .OrderBy(l => l.SortOrder).ThenBy(l => l.Id)
                .ToList();
            var itemNos = ItemNumbers(lines.Where(l => l.ItemId.HasValue).Select(l => l.ItemId!.Value));

            var detail = new RecurringScheduleDetail
            {
                Id = schedule.Id,
                Name = schedule.Name,
                CustomerId = schedule.PartyId,
                CustomerName = CustomerNames(new[] { schedule.PartyId }).GetValueOrDefault(schedule.PartyId),
                CustomerArchived = CustomerArchivedFlags(new[] { schedule.PartyId }).GetValueOrDefault(schedule.PartyId),
                Status = schedule.Status,
                Cadence = schedule.Cadence,
                Interval = schedule.Interval,
                DayOfMonth = schedule.DayOfMonth,
                StartDate = schedule.StartDate,
                EndDate = schedule.EndDate,
                MaxOccurrences = schedule.MaxOccurrences,
                NextRunDate = schedule.NextRunDate,
                OccurrenceCount = schedule.OccurrenceCount,
                Action = schedule.Action,
                ServicePeriodMode = schedule.ServicePeriodMode,
                PaymentTermId = schedule.PaymentTermId,
                Notes = schedule.Notes,
                PaymentInstructions = schedule.PaymentInstructions,
                NeedsAttention = schedule.NeedsAttention,
                ConsecutiveFailures = schedule.ConsecutiveFailures,
                IsOverdue = IsOverdue(schedule, Today()),
                Lines = lines.Select(l => new RecurringScheduleLineView
                {
                    Id = l.Id,
                    ItemId = l.ItemId,
                    ItemNo = l.ItemId.HasValue ? itemNos.GetValueOrDefault(l.ItemId.Value) : null,
                    Description = l.Description,
                    Quantity = l.Quantity,
                    Amount = l.Amount,
                    Discount = l.Discount,
                    DiscountType = l.DiscountType,
                    TaxId = l.TaxId,
                    AccountId = l.AccountId,
                    SortOrder = l.SortOrder,
                }).ToList(),
            };

            detail.UpcomingDates = SafePreview(
                schedule.Cadence, schedule.Interval, schedule.StartDate, schedule.DayOfMonth,
                schedule.EndDate, schedule.MaxOccurrences, DefaultPreviewCount, schedule.NextRunDate);

            return detail;
        }

        public IReadOnlyList<RecurringOccurrenceView> GetOccurrences(int scheduleId)
        {
            _tenant.RequireCompanyId();
            // Scope through the schedule so a cross-tenant id cannot read another company's occurrences.
            var exists = _scheduleRepo.Table.Any(s =>
                s.Id == scheduleId && s.Kind == RecurringScheduleKind.SalesInvoice);
            if (!exists)
                return Array.Empty<RecurringOccurrenceView>();

            return _occurrenceRepo.Table
                .Where(o => o.RecurringScheduleId == scheduleId)
                .OrderByDescending(o => o.ScheduledRunDate).ThenByDescending(o => o.Id)
                .ToList()
                .Select(o => new RecurringOccurrenceView
                {
                    Id = o.Id,
                    RecurringScheduleId = o.RecurringScheduleId,
                    ScheduledRunDate = o.ScheduledRunDate,
                    PeriodStart = o.PeriodStart,
                    PeriodEnd = o.PeriodEnd,
                    DocumentDate = o.DocumentDate,
                    Status = o.Status,
                    SalesInvoiceId = o.SalesInvoiceId,
                    ErrorCode = o.ErrorCode,
                    GuardFailureReason = o.GuardFailureReason,
                    SkipReason = o.SkipReason,
                    ActionBy = o.ActionBy,
                    AttemptCount = o.AttemptCount,
                    UpdatedAt = o.UpdatedAt,
                    CreatedAt = o.CreatedAt,
                })
                .ToList();
        }

        public IReadOnlyList<DateTime> PreviewDates(
            RecurringCadence cadence, int interval, DateTime startDate, int? dayOfMonth,
            DateTime? endDate, int? maxOccurrences, int count)
        {
            if (count < 1) count = 1;
            if (count > 24) count = 24;
            try
            {
                return NextDateCalculator.ComputeNextDates(
                    cadence, NormalizeInterval(interval), startDate.Date,
                    NormalizeDayOfMonth(cadence, dayOfMonth, startDate),
                    count, endDate?.Date, maxOccurrences);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                throw new RecurringScheduleValidationException(ex.Message);
            }
        }

        // ----- create / update -----

        public int CreateSchedule(RecurringScheduleInput input, string actingUser)
        {
            var companyId = _tenant.RequireCompanyId();
            ValidateInput(input, companyId);

            using var tx = _unitOfWork.BeginSerializableTransaction();
            _unitOfWork.DiscardPendingChanges();

            var dayOfMonth = NormalizeDayOfMonth(input.Cadence, input.DayOfMonth, input.StartDate);
            var firstDate = NextDateCalculator.ComputeFirstDate(
                input.Cadence, NormalizeInterval(input.Interval), input.StartDate.Date, dayOfMonth);

            var schedule = new RecurringSchedule
            {
                CompanyId = companyId,
                Kind = RecurringScheduleKind.SalesInvoice,
                PartyId = input.CustomerId,
                Name = (input.Name ?? "").Trim(),
                Status = RecurringScheduleStatus.Active,
                Cadence = input.Cadence,
                Interval = NormalizeInterval(input.Interval),
                DayOfMonth = dayOfMonth,
                StartDate = input.StartDate.Date,
                EndDate = input.EndDate?.Date,
                MaxOccurrences = input.MaxOccurrences,
                NextRunDate = firstDate,
                Action = input.Action,
                ServicePeriodMode = input.ServicePeriodMode,
                PaymentTermId = input.PaymentTermId,
                Notes = input.Notes,
                PaymentInstructions = input.PaymentInstructions,
                AutomationEnabledBy = input.Action == RecurringAction.CreateDraft ? null : actingUser,
                AutomationEnabledAt = input.Action == RecurringAction.CreateDraft ? null : DateTime.UtcNow,
            };
            _scheduleRepo.Insert(schedule);

            SaveLines(schedule.Id, companyId, input.Lines);
            WriteAudit(companyId, actingUser, AuditEventTypes.Added, schedule.Id, "Status", "", "Active");
            tx.Commit();

            _logger.LogInformation("Recurring schedule {ScheduleId} created by {User}.", schedule.Id, actingUser);
            return schedule.Id;
        }

        public void UpdateSchedule(RecurringScheduleInput input, string actingUser)
        {
            var companyId = _tenant.RequireCompanyId();
            ValidateInput(input, companyId);

            using var tx = _unitOfWork.BeginSerializableTransaction();
            _unitOfWork.DiscardPendingChanges();

            var schedule = _scheduleRepo.Table.FirstOrDefault(s =>
                s.Id == input.Id && s.Kind == RecurringScheduleKind.SalesInvoice);
            if (schedule == null)
                throw new RecurringScheduleValidationException($"Recurring schedule {input.Id} not found.");
            if (schedule.Status == RecurringScheduleStatus.Ended)
                throw new RecurringScheduleValidationException("An ended schedule cannot be edited.");

            var dayOfMonth = NormalizeDayOfMonth(input.Cadence, input.DayOfMonth, input.StartDate);

            schedule.PartyId = input.CustomerId;
            schedule.Name = (input.Name ?? "").Trim();
            schedule.Cadence = input.Cadence;
            schedule.Interval = NormalizeInterval(input.Interval);
            schedule.DayOfMonth = dayOfMonth;
            schedule.StartDate = input.StartDate.Date;
            schedule.EndDate = input.EndDate?.Date;
            schedule.MaxOccurrences = input.MaxOccurrences;
            schedule.Action = input.Action;
            schedule.ServicePeriodMode = input.ServicePeriodMode;
            schedule.PaymentTermId = input.PaymentTermId;
            schedule.Notes = input.Notes;
            schedule.PaymentInstructions = input.PaymentInstructions;
            if (input.Action == RecurringAction.CreateDraft)
            {
                schedule.AutomationEnabledBy = null;
                schedule.AutomationEnabledAt = null;
            }
            else if (schedule.AutomationEnabledBy == null)
            {
                schedule.AutomationEnabledBy = actingUser;
                schedule.AutomationEnabledAt = DateTime.UtcNow;
            }

            // Recompute the next run from the new calendar rules for an Active schedule, choosing the next date on or
            // after today so an edit never back-dates a run. A paused/ended schedule keeps its stored next date.
            if (schedule.Status == RecurringScheduleStatus.Active)
                schedule.NextRunDate = NextDateOnOrAfter(schedule, Today());

            _scheduleRepo.Update(schedule);
            ReplaceLines(schedule.Id, companyId, input.Lines);
            WriteAudit(companyId, actingUser, AuditEventTypes.Modified, schedule.Id, "Schedule", "", "Edited");
            tx.Commit();

            _logger.LogInformation("Recurring schedule {ScheduleId} edited by {User}.", schedule.Id, actingUser);
        }

        // ----- lifecycle -----

        public void PauseSchedule(int scheduleId, string actingUser, string? reason)
        {
            var companyId = _tenant.RequireCompanyId();
            using var tx = _unitOfWork.BeginSerializableTransaction();
            _unitOfWork.DiscardPendingChanges();

            var schedule = RequireSales(scheduleId);
            if (schedule.Status == RecurringScheduleStatus.Ended)
                throw new RecurringScheduleValidationException("An ended schedule cannot be paused.");
            if (schedule.Status == RecurringScheduleStatus.Paused)
            {
                tx.Dispose();
                return; // idempotent
            }

            schedule.Status = RecurringScheduleStatus.Paused;
            _scheduleRepo.Update(schedule);
            WriteAudit(companyId, actingUser, AuditEventTypes.Modified, schedule.Id, "Status",
                "Active", reason is { Length: > 0 } ? $"Paused: {Trim(reason, 400)}" : "Paused");
            tx.Commit();

            _logger.LogInformation("Recurring schedule {ScheduleId} paused by {User}.", schedule.Id, actingUser);
        }

        public void ResumeSchedule(int scheduleId, string actingUser)
        {
            var companyId = _tenant.RequireCompanyId();
            using var tx = _unitOfWork.BeginSerializableTransaction();
            _unitOfWork.DiscardPendingChanges();

            var schedule = RequireSales(scheduleId);
            if (schedule.Status != RecurringScheduleStatus.Paused)
                throw new RecurringScheduleValidationException("Only a paused schedule can be resumed.");

            // Refuse to resume onto an archived customer: it would start generating for a client that is no longer
            // billable. The customer must be un-archived (or the schedule edited) first.
            var customer = _customerRepo.Table.FirstOrDefault(c => c.Id == schedule.PartyId);
            if (customer == null)
                throw new RecurringScheduleValidationException("The schedule's customer could not be found.");
            if (customer.ArchivedAt != null)
                throw new RecurringScheduleValidationException(
                    "The client is archived. Restore the client before resuming this schedule.");

            // No catch-up: set the next run to the next calendar date on or after today, so missed periods while
            // paused are not back-filled. If the series is exhausted there is nothing left to run.
            var next = NextDateOnOrAfter(schedule, Today());
            schedule.Status = RecurringScheduleStatus.Active;
            schedule.NextRunDate = next;
            schedule.NeedsAttention = false;
            _scheduleRepo.Update(schedule);
            WriteAudit(companyId, actingUser, AuditEventTypes.Modified, schedule.Id, "Status", "Paused", "Active");
            tx.Commit();

            _logger.LogInformation("Recurring schedule {ScheduleId} resumed by {User} (no catch-up).", schedule.Id, actingUser);
        }

        public void EndSchedule(int scheduleId, string actingUser, string? reason)
        {
            var companyId = _tenant.RequireCompanyId();
            using var tx = _unitOfWork.BeginSerializableTransaction();
            _unitOfWork.DiscardPendingChanges();

            var schedule = RequireSales(scheduleId);
            if (schedule.Status == RecurringScheduleStatus.Ended)
            {
                tx.Dispose();
                return; // idempotent
            }

            var previous = schedule.Status.ToString();
            schedule.Status = RecurringScheduleStatus.Ended;
            schedule.NextRunDate = null;
            schedule.NeedsAttention = false;
            _scheduleRepo.Update(schedule);
            WriteAudit(companyId, actingUser, AuditEventTypes.Modified, schedule.Id, "Status",
                previous, reason is { Length: > 0 } ? $"Ended: {Trim(reason, 400)}" : "Ended");
            tx.Commit();

            _logger.LogInformation("Recurring schedule {ScheduleId} ended by {User}.", schedule.Id, actingUser);
        }

        // ----- helpers -----

        private RecurringSchedule RequireSales(int scheduleId)
        {
            var schedule = _scheduleRepo.Table.FirstOrDefault(s =>
                s.Id == scheduleId && s.Kind == RecurringScheduleKind.SalesInvoice);
            if (schedule == null)
                throw new RecurringScheduleValidationException($"Recurring schedule {scheduleId} not found.");
            return schedule;
        }

        private void ValidateInput(RecurringScheduleInput input, int companyId)
        {
            if (string.IsNullOrWhiteSpace(input.Name))
                throw new RecurringScheduleValidationException("A schedule name is required.");
            // Sales schedules support only draft or auto-issue. AutoApprove is a vendor-bill disposition and must
            // not be persisted on a sales schedule (it would also slip past the controller's auto-issue post gate).
            if (input.Action != RecurringAction.CreateDraft && input.Action != RecurringAction.AutoIssue)
                throw new RecurringScheduleValidationException(
                    "A sales schedule can only create a draft or auto-issue.");
            if (input.StartDate == default)
                throw new RecurringScheduleValidationException("A start date is required.");
            if (input.EndDate.HasValue && input.EndDate.Value.Date < input.StartDate.Date)
                throw new RecurringScheduleValidationException("The end date cannot be before the start date.");
            if (input.MaxOccurrences.HasValue && input.MaxOccurrences.Value < 1)
                throw new RecurringScheduleValidationException("Max occurrences must be at least 1.");

            var customer = _customerRepo.Table.FirstOrDefault(c => c.Id == input.CustomerId);
            if (customer == null || customer.CompanyId != companyId)
                throw new RecurringScheduleValidationException("Select a client for this schedule.");
            if (customer.ArchivedAt != null)
                throw new RecurringScheduleValidationException("The selected client is archived. Choose an active client.");

            var lines = input.Lines ?? new List<RecurringScheduleLineInput>();
            var meaningful = lines.Where(l =>
                (l.ItemId.HasValue) || !string.IsNullOrWhiteSpace(l.Description) || l.Amount > 0).ToList();
            if (meaningful.Count == 0)
                throw new RecurringScheduleValidationException("Add at least one line to the schedule.");
            foreach (var l in meaningful)
            {
                if (l.ItemId.HasValue)
                {
                    var item = _itemRepo.Table.FirstOrDefault(i => i.Id == l.ItemId.Value);
                    if (item == null || item.CompanyId != companyId)
                        throw new RecurringScheduleValidationException("A line references an item that does not exist.");
                }
                if (l.Quantity <= 0)
                    throw new RecurringScheduleValidationException("Each line quantity must be greater than zero.");
            }

            // Validate the calendar rules up front so a bad cadence/day-of-month fails here, not at generation time.
            try
            {
                NextDateCalculator.ComputeFirstDate(
                    input.Cadence, NormalizeInterval(input.Interval), input.StartDate.Date,
                    NormalizeDayOfMonth(input.Cadence, input.DayOfMonth, input.StartDate));
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                throw new RecurringScheduleValidationException(ex.Message);
            }
        }

        private void SaveLines(int scheduleId, int companyId, IEnumerable<RecurringScheduleLineInput> inputs)
        {
            var order = 0;
            foreach (var l in inputs)
            {
                if (!l.ItemId.HasValue && string.IsNullOrWhiteSpace(l.Description) && l.Amount <= 0)
                    continue;
                _lineRepo.Insert(new RecurringScheduleLine
                {
                    CompanyId = companyId,
                    RecurringScheduleId = scheduleId,
                    ItemId = l.ItemId,
                    Description = l.Description?.Trim(),
                    Quantity = l.Quantity <= 0 ? 1 : l.Quantity,
                    Amount = l.Amount,
                    Discount = l.Discount,
                    DiscountType = l.DiscountType,
                    TaxId = l.TaxId,
                    AccountId = l.AccountId,
                    SortOrder = order++,
                });
            }
        }

        private void ReplaceLines(int scheduleId, int companyId, IEnumerable<RecurringScheduleLineInput> inputs)
        {
            // Replace-all keeps the write simple and correct: a schedule has few lines, and generation reads the
            // current set by schedule id. Existing invoices already generated are untouched (they are separate rows).
            var existing = _lineRepo.Table.Where(l => l.RecurringScheduleId == scheduleId).ToList();
            foreach (var row in existing)
                _lineRepo.Delete(row);
            SaveLines(scheduleId, companyId, inputs);
        }

        private void WriteAudit(int companyId, string actingUser, AuditEventTypes type, int scheduleId,
            string field, string original, string updated)
        {
            _auditRepo.Insert(new AuditLog
            {
                CompanyId = companyId,
                UserName = actingUser,
                AuditEventDateUTC = DateTime.UtcNow,
                AuditEventType = (int)type,
                TableName = AuditTable,
                RecordId = scheduleId.ToString(CultureInfo.InvariantCulture),
                FieldName = field,
                OriginalValue = original,
                NewValue = updated,
            });
        }

        /// <summary>The next calendar date on or after <paramref name="from"/>, or null when the series is exhausted.</summary>
        private static DateTime? NextDateOnOrAfter(RecurringSchedule schedule, DateTime from)
        {
            var dayOfMonth = schedule.DayOfMonth;
            try
            {
                // Walk the series from the start and return the first date >= from that is within the window.
                var dates = NextDateCalculator.ComputeNextDates(
                    schedule.Cadence, schedule.Interval, schedule.StartDate.Date, dayOfMonth,
                    count: 1_000, schedule.EndDate?.Date, schedule.MaxOccurrences);
                foreach (var d in dates)
                    if (d.Date >= from.Date)
                        return d;
                return null;
            }
            catch
            {
                return schedule.NextRunDate;
            }
        }

        private static IReadOnlyList<DateTime> SafePreview(
            RecurringCadence cadence, int interval, DateTime startDate, int? dayOfMonth,
            DateTime? endDate, int? maxOccurrences, int count, DateTime? fromNextRun)
        {
            try
            {
                var all = NextDateCalculator.ComputeNextDates(
                    cadence, interval < 1 ? 1 : interval, startDate.Date, dayOfMonth,
                    count + 24, endDate?.Date, maxOccurrences);
                // Prefer dates from the schedule's current next run forward, so the preview matches what will run.
                if (fromNextRun.HasValue)
                {
                    var upcoming = all.Where(d => d.Date >= fromNextRun.Value.Date).Take(count).ToList();
                    if (upcoming.Count > 0)
                        return upcoming;
                }
                return all.Take(count).ToList();
            }
            catch
            {
                return Array.Empty<DateTime>();
            }
        }

        private static int NormalizeInterval(int interval) => interval < 1 ? 1 : interval;

        /// <summary>
        /// Non-weekly cadences require a day-of-month; default it from the start date when the caller omits it.
        /// Weekly ignores it.
        /// </summary>
        private static int? NormalizeDayOfMonth(RecurringCadence cadence, int? dayOfMonth, DateTime startDate)
        {
            if (cadence == RecurringCadence.Weekly)
                return null;
            if (dayOfMonth.HasValue && dayOfMonth.Value >= 1 && dayOfMonth.Value <= 31)
                return dayOfMonth;
            return startDate.Day;
        }

        private static bool IsOverdue(RecurringSchedule s, DateTime today) =>
            s.Status == RecurringScheduleStatus.Active
            && s.NextRunDate.HasValue
            && s.NextRunDate.Value.Date <= today.Date;

        private Dictionary<int, string> CustomerNames(IEnumerable<int> customerIds)
        {
            var ids = customerIds.Distinct().ToList();
            if (ids.Count == 0) return new();
            var customers = _customerRepo.Table.Where(c => ids.Contains(c.Id))
                .Select(c => new { c.Id, c.PartyId }).ToList();
            var partyIds = customers.Where(c => c.PartyId.HasValue).Select(c => c.PartyId!.Value).Distinct().ToList();
            var partyNames = _partyRepo.Table.Where(p => partyIds.Contains(p.Id))
                .Select(p => new { p.Id, p.Name }).ToList()
                .ToDictionary(p => p.Id, p => p.Name);
            var result = new Dictionary<int, string>();
            foreach (var c in customers)
                if (c.PartyId.HasValue && partyNames.TryGetValue(c.PartyId.Value, out var name))
                    result[c.Id] = name;
            return result;
        }

        private Dictionary<int, bool> CustomerArchivedFlags(IEnumerable<int> customerIds)
        {
            var ids = customerIds.Distinct().ToList();
            if (ids.Count == 0) return new();
            return _customerRepo.Table.Where(c => ids.Contains(c.Id))
                .Select(c => new { c.Id, Archived = c.ArchivedAt != null }).ToList()
                .ToDictionary(c => c.Id, c => c.Archived);
        }

        private Dictionary<int, string> ItemNumbers(IEnumerable<int> itemIds)
        {
            var ids = itemIds.Distinct().ToList();
            if (ids.Count == 0) return new();
            return _itemRepo.Table.Where(i => ids.Contains(i.Id))
                .Select(i => new { i.Id, i.No }).ToList()
                .ToDictionary(i => i.Id, i => i.No);
        }

        /// <summary>
        /// The workspace's "today". Timezone handling across the app is UTC-date based (see BooksStartDate); use the
        /// same basis so overdue and no-catch-up resume line up with the rest of the system.
        /// </summary>
        private static DateTime Today() => DateTime.UtcNow.Date;

        private static string Trim(string value, int max) =>
            value.Length <= max ? value : value.Substring(0, max);
    }
}
