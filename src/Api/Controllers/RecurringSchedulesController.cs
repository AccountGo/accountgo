// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Linq;
using System.Security.Claims;
using Api.Filters;
using Core.Domain.Recurring;
using Microsoft.AspNetCore.Mvc;
using Services.Recurring;

namespace Api.Controllers
{
    /// <summary>
    /// #482: manual control over recurring sales-invoice schedules. "Run now" does not bypass the occurrence
    /// model — it creates or retrieves the single idempotent occurrence for a run date and processes it through
    /// the same <see cref="IRecurringInvoiceGenerationService"/> the worker (#480) will use.
    /// </summary>
    [Core.Tenancy.TenantScoped]
    [Route("api/[controller]")]
    [RequireModule("sales")]
    public class RecurringSchedulesController : BaseController
    {
        private readonly IRecurringInvoiceGenerationService _generation;
        private readonly Core.Data.IRepository<RecurringSchedule> _scheduleRepo;
        private readonly Core.Tenancy.ITenantContext _tenant;
        private readonly ILogger<RecurringSchedulesController> _logger;

        public RecurringSchedulesController(
            IRecurringInvoiceGenerationService generation,
            Core.Data.IRepository<RecurringSchedule> scheduleRepo,
            Core.Tenancy.ITenantContext tenant,
            ILogger<RecurringSchedulesController> logger)
        {
            _generation = generation;
            _scheduleRepo = scheduleRepo;
            _tenant = tenant;
            _logger = logger;
        }

        public sealed class RunNowRequest
        {
            /// <summary>Which scheduled run to generate. Defaults to the schedule's NextRunDate.</summary>
            public DateTime? RunDate { get; set; }
        }

        /// <summary>
        /// Generates the document for one run of a schedule. Creating the draft needs the invoice-create
        /// permission; an auto-issue schedule also needs the post permission because it may post to the ledger.
        /// </summary>
        [HttpPost("{id}/run")]
        [RequirePermission(Core.Domain.Security.SystemPermissions.SalesInvoicesCreate)]
        public IActionResult RunNow(int id, [FromBody] RunNowRequest? request = null)
        {
            var actor = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
            if (string.IsNullOrWhiteSpace(actor))
                return Unauthorized();

            var schedule = _scheduleRepo.Table.FirstOrDefault(s => s.Id == id);
            if (schedule == null)
                return NotFound(new { message = $"Recurring schedule {id} not found." });
            if (schedule.Kind != RecurringScheduleKind.SalesInvoice)
                return BadRequest(new { message = "This schedule does not generate sales invoices." });

            // Auto-issue may post to the ledger, so a run-now on such a schedule also needs the post permission.
            if (schedule.Action == RecurringAction.AutoIssue
                && !_tenant.Permissions.Contains(Core.Domain.Security.SystemPermissions.SalesInvoicesPost))
                return RequirePermissionAttribute.Denied(Core.Domain.Security.SystemPermissions.SalesInvoicesPost);

            var runDate = (request?.RunDate ?? schedule.NextRunDate)?.Date;
            if (runDate == null)
                return BadRequest(new { message = "No run date to generate. Pass runDate or set the schedule's next run date." });

            try
            {
                // A short, immediate lease owned by this request; the worker/lease lifecycle proper is #480.
                var leaseOwner = $"run-now:{actor}";
                var occurrenceId = _generation.EnsureOccurrence(id, runDate.Value, leaseOwner, DateTime.UtcNow.AddMinutes(5));
                var result = _generation.ProcessOccurrence(occurrenceId, leaseOwner);

                return Ok(new
                {
                    occurrenceId = result.OccurrenceId,
                    outcome = result.Outcome.ToString(),
                    salesInvoiceId = result.SalesInvoiceId,
                    errorCode = result.ErrorCode,
                    message = result.Message,
                });
            }
            catch (InvalidRunDateException ex)
            {
                // Domain-safe, user-facing validation message.
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                // Don't leak internal/database detail to the caller; keep it in the logs.
                _logger.LogError(ex, "Run-now failed for schedule {ScheduleId} on {RunDate}.", id, runDate);
                return StatusCode(500, new { message = "Could not generate the invoice for this run. Please try again or contact support." });
            }
        }
    }
}
