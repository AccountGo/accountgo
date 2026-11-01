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
        private readonly IRecurringScheduleService _schedules;
        private readonly Core.Data.IRepository<RecurringSchedule> _scheduleRepo;
        private readonly Core.Data.IRepository<RecurringOccurrence> _occurrenceRepo;
        private readonly Core.Authorization.IAccessAuthorizationService _authorization;
        private readonly ILogger<RecurringSchedulesController> _logger;

        public RecurringSchedulesController(
            IRecurringInvoiceGenerationService generation,
            IRecurringScheduleService schedules,
            Core.Data.IRepository<RecurringSchedule> scheduleRepo,
            Core.Data.IRepository<RecurringOccurrence> occurrenceRepo,
            Core.Authorization.IAccessAuthorizationService authorization,
            ILogger<RecurringSchedulesController> logger)
        {
            _generation = generation;
            _schedules = schedules;
            _scheduleRepo = scheduleRepo;
            _occurrenceRepo = occurrenceRepo;
            _authorization = authorization;
            _logger = logger;
        }

        public sealed class RunNowRequest
        {
            /// <summary>
            /// Optional confirmation of which run to generate. A manual run always generates the schedule's current
            /// next run date; when supplied, this must equal that date or the request is rejected.
            /// </summary>
            public DateTime? RunDate { get; set; }
        }

        /// <summary>Skip the next occurrence: a reason is required; it records a skip, not a document.</summary>
        public sealed class SkipRequest
        {
            public DateTime? RunDate { get; set; }
            public string? Reason { get; set; }
        }

        /// <summary>An optional reason for a pause or end.</summary>
        public sealed class ReasonRequest
        {
            public string? Reason { get; set; }
        }

        /// <summary>Editor payload for the next-dates preview (no persistence).</summary>
        public sealed class PreviewRequest
        {
            public RecurringCadence Cadence { get; set; } = RecurringCadence.Monthly;
            public int Interval { get; set; } = 1;
            public DateTime StartDate { get; set; }
            public int? DayOfMonth { get; set; }
            public DateTime? EndDate { get; set; }
            public int? MaxOccurrences { get; set; }
            public int Count { get; set; } = 3;
        }

        // ----- #484: list / detail / occurrences / preview -----

        /// <summary>Lists sales-invoice schedules, optionally filtered by status or needs-attention.</summary>
        [HttpGet]
        [RequirePermission(Core.Domain.Security.SystemPermissions.SalesInvoicesView)]
        public IActionResult List([FromQuery] string? status = null, [FromQuery] bool needsAttention = false)
        {
            RecurringScheduleStatus? parsed = null;
            if (!string.IsNullOrWhiteSpace(status))
            {
                if (!Enum.TryParse<RecurringScheduleStatus>(status, ignoreCase: true, out var s))
                    return BadRequest(new { message = "Unknown status filter." });
                parsed = s;
            }
            return Ok(_schedules.ListSchedules(parsed, needsAttention));
        }

        /// <summary>Full detail for one schedule (lines + upcoming dates).</summary>
        [HttpGet("{id}")]
        [RequirePermission(Core.Domain.Security.SystemPermissions.SalesInvoicesView)]
        public IActionResult Get(int id)
        {
            var detail = _schedules.GetSchedule(id);
            if (detail == null)
                return NotFound(new { message = $"Recurring schedule {id} not found." });
            return Ok(detail);
        }

        /// <summary>Occurrence history for one schedule, newest first.</summary>
        [HttpGet("{id}/occurrences")]
        [RequirePermission(Core.Domain.Security.SystemPermissions.SalesInvoicesView)]
        public IActionResult Occurrences(int id)
        {
            // A missing/other-tenant schedule yields an empty history rather than leaking existence.
            return Ok(_schedules.GetOccurrences(id));
        }

        /// <summary>Preview the next run dates for editor input (no persistence).</summary>
        [HttpPost("preview")]
        [RequirePermission(Core.Domain.Security.SystemPermissions.SalesInvoicesView)]
        public IActionResult Preview([FromBody] PreviewRequest request)
        {
            try
            {
                var dates = _schedules.PreviewDates(
                    request.Cadence, request.Interval, request.StartDate, request.DayOfMonth,
                    request.EndDate, request.MaxOccurrences, request.Count);
                return Ok(new { dates });
            }
            catch (RecurringScheduleValidationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        // ----- #484: create / update -----

        /// <summary>Creates a new sales-invoice schedule. Needs invoice-create; auto-issue also needs post.</summary>
        [HttpPost]
        [RequirePermission(Core.Domain.Security.SystemPermissions.SalesInvoicesCreate)]
        public IActionResult Create([FromBody] RecurringScheduleInput input)
        {
            var actor = Actor();
            if (actor == null) return Unauthorized();
            if (input.Action == RecurringAction.AutoIssue)
            {
                var denied = DenyIfMissingPostPermission();
                if (denied != null) return denied;
            }
            try
            {
                var id = _schedules.CreateSchedule(input, actor);
                return Ok(new { id });
            }
            catch (RecurringScheduleValidationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        /// <summary>Updates an existing schedule. Needs invoice-create; auto-issue also needs post.</summary>
        [HttpPut("{id}")]
        [RequirePermission(Core.Domain.Security.SystemPermissions.SalesInvoicesCreate)]
        public IActionResult Update(int id, [FromBody] RecurringScheduleInput input)
        {
            var actor = Actor();
            if (actor == null) return Unauthorized();
            input.Id = id;
            if (input.Action == RecurringAction.AutoIssue)
            {
                var denied = DenyIfMissingPostPermission();
                if (denied != null) return denied;
            }
            try
            {
                _schedules.UpdateSchedule(input, actor);
                return NoContent();
            }
            catch (RecurringScheduleValidationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        // ----- #484: lifecycle actions -----

        /// <summary>Skips the schedule's next occurrence; records a reason, creates no document, advances by one.</summary>
        [HttpPost("{id}/skip")]
        [RequirePermission(Core.Domain.Security.SystemPermissions.SalesInvoicesCreate)]
        public IActionResult Skip(int id, [FromBody] SkipRequest request)
        {
            var actor = Actor();
            if (actor == null) return Unauthorized();
            if (string.IsNullOrWhiteSpace(request.Reason))
                return BadRequest(new { message = "A reason is required to skip a run." });

            var schedule = _scheduleRepo.Table.FirstOrDefault(s => s.Id == id);
            if (schedule == null)
                return NotFound(new { message = $"Recurring schedule {id} not found." });
            if (schedule.Kind != RecurringScheduleKind.SalesInvoice)
                return BadRequest(new { message = "This schedule does not generate sales invoices." });

            try
            {
                var leaseOwner = $"skip:{actor}:{Guid.NewGuid():N}";
                var result = _generation.SkipNextOccurrence(
                    id, request.RunDate, actor, request.Reason!.Trim(), leaseOwner, DateTime.UtcNow.AddMinutes(5));
                return Ok(ResponseFor(result));
            }
            catch (InvalidRunDateException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Skip failed for schedule {ScheduleId}.", id);
                return StatusCode(500, new { message = "Could not skip this run. Please try again or contact support." });
            }
        }

        /// <summary>Pauses an active schedule. Existing invoices are untouched.</summary>
        [HttpPost("{id}/pause")]
        [RequirePermission(Core.Domain.Security.SystemPermissions.SalesInvoicesCreate)]
        public IActionResult Pause(int id, [FromBody] ReasonRequest? request = null)
        {
            var actor = Actor();
            if (actor == null) return Unauthorized();
            try
            {
                _schedules.PauseSchedule(id, actor, request?.Reason?.Trim());
                return NoContent();
            }
            catch (RecurringScheduleValidationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        /// <summary>Resumes a paused schedule without catch-up; refuses an archived customer.</summary>
        [HttpPost("{id}/resume")]
        [RequirePermission(Core.Domain.Security.SystemPermissions.SalesInvoicesCreate)]
        public IActionResult Resume(int id)
        {
            var actor = Actor();
            if (actor == null) return Unauthorized();
            try
            {
                _schedules.ResumeSchedule(id, actor);
                return NoContent();
            }
            catch (RecurringScheduleValidationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        /// <summary>Ends a schedule permanently. Existing invoices are untouched.</summary>
        [HttpPost("{id}/end")]
        [RequirePermission(Core.Domain.Security.SystemPermissions.SalesInvoicesCreate)]
        public IActionResult End(int id, [FromBody] ReasonRequest? request = null)
        {
            var actor = Actor();
            if (actor == null) return Unauthorized();
            try
            {
                _schedules.EndSchedule(id, actor, request?.Reason?.Trim());
                return NoContent();
            }
            catch (RecurringScheduleValidationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        private string? Actor()
        {
            var actor = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
            return string.IsNullOrWhiteSpace(actor) ? null : actor;
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
            // Resolve it through the shared authorization service (live grants) rather than a cached snapshot.
            if (schedule.Action == RecurringAction.AutoIssue)
            {
                var denied = DenyIfMissingPostPermission();
                if (denied != null)
                    return denied;
            }

            try
            {
                // A short, immediate lease owned by this request. The service resolves the run date (defaulting to
                // the schedule's NextRunDate), runs the single idempotent occurrence, and advances the series on a
                // forward run. Archiving the client pauses the schedule instead of generating.
                var leaseOwner = $"run-now:{actor}:{Guid.NewGuid():N}";
                var result = _generation.RunSchedule(id, request?.RunDate, leaseOwner, DateTime.UtcNow.AddMinutes(5));
                return Ok(ResponseFor(result));
            }
            catch (InvalidRunDateException ex)
            {
                // Domain-safe, user-facing validation message.
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                // Don't leak internal/database detail to the caller; keep it in the logs.
                _logger.LogError(ex, "Run-now failed for schedule {ScheduleId}.", id);
                return StatusCode(500, new { message = "Could not generate the invoice for this run. Please try again or contact support." });
            }
        }

        /// <summary>
        /// Retries one specific occurrence by id (a failed or crashed run). Re-processes it against its own
        /// idempotency key and never advances the schedule's next run date. Needs the invoice-create permission; an
        /// auto-issue schedule also needs the post permission because the retry may post to the ledger.
        /// </summary>
        [HttpPost("occurrences/{occurrenceId}/retry")]
        [RequirePermission(Core.Domain.Security.SystemPermissions.SalesInvoicesCreate)]
        public IActionResult RetryOccurrence(int occurrenceId)
        {
            var actor = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
            if (string.IsNullOrWhiteSpace(actor))
                return Unauthorized();

            var occurrence = _occurrenceRepo.Table.FirstOrDefault(o => o.Id == occurrenceId);
            if (occurrence == null)
                return NotFound(new { message = $"Occurrence {occurrenceId} not found." });

            var schedule = _scheduleRepo.Table.FirstOrDefault(s => s.Id == occurrence.RecurringScheduleId);
            if (schedule == null)
                return NotFound(new { message = "The occurrence's schedule could not be found." });
            if (schedule.Kind != RecurringScheduleKind.SalesInvoice)
                return BadRequest(new { message = "This occurrence does not belong to a sales-invoice schedule." });

            // Same permission split as run-now: retrying an auto-issue schedule may post to the ledger. Resolve the
            // post permission through the shared authorization service (live grants), not a cached snapshot.
            if (schedule.Action == RecurringAction.AutoIssue)
            {
                var denied = DenyIfMissingPostPermission();
                if (denied != null)
                    return denied;
            }

            try
            {
                var leaseOwner = $"retry:{actor}:{Guid.NewGuid():N}";
                var result = _generation.RetryOccurrence(occurrenceId, leaseOwner, DateTime.UtcNow.AddMinutes(5));
                return Ok(ResponseFor(result));
            }
            catch (InvalidRunDateException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Retry failed for occurrence {OccurrenceId}.", occurrenceId);
                return StatusCode(500, new { message = "Could not retry this run. Please try again or contact support." });
            }
        }

        /// <summary>
        /// Checks the invoice-post permission through the shared <see cref="Core.Authorization.IAccessAuthorizationService"/>
        /// (which resolves live grants) and returns a 403 result if it is missing, or null if allowed. Used for the
        /// extra in-body auto-issue gate so it stays consistent with the attribute-based authorization path rather
        /// than reading a potentially stale tenant permission snapshot.
        /// </summary>
        private IActionResult? DenyIfMissingPostPermission()
        {
            var decision = _authorization.AuthorizePermission(
                Core.Domain.Security.SystemPermissions.SalesInvoicesPost);
            if (decision.Allowed)
                return null;

            if (decision.ReasonCode == Core.Authorization.AccessReasonCode.PermissionDenied)
                return RequirePermissionAttribute.Denied(
                    decision.MissingPermission ?? Core.Domain.Security.SystemPermissions.SalesInvoicesPost,
                    decision.PolicyId,
                    decision.PolicyVersion);

            var problem = new ProblemDetails
            {
                Title = decision.SafeTitle,
                Detail = decision.SafeMessage,
                Status = decision.HttpStatus,
            };
            problem.Extensions["code"] = decision.ReasonCodeString;
            return new ObjectResult(problem) { StatusCode = decision.HttpStatus };
        }

        /// <summary>The curated, safe response body shared by run-now and retry.</summary>
        private static object ResponseFor(RecurringGenerationResult result) => new
        {
            occurrenceId = result.OccurrenceId,
            outcome = result.Outcome.ToString(),
            salesInvoiceId = result.SalesInvoiceId,
            errorCode = result.ErrorCode,
            // Only curated, safe text is returned. A failure's raw detail (which may carry database or
            // internal information) stays in the occurrence's ErrorMessage and the logs, never the response.
            message = SafeMessageFor(result),
        };

        /// <summary>
        /// Curated, safe response text for a generation result. Downgrade reasons are fixed, non-sensitive strings
        /// and are passed through; a failure is described by its error code only — its raw detail (possibly carrying
        /// database or internal information) is left in the occurrence's ErrorMessage and the logs.
        /// </summary>
        private static string? SafeMessageFor(RecurringGenerationResult result)
        {
            switch (result.Outcome)
            {
                case RecurringGenerationOutcome.DraftCreatedGuardFailed:
                    // These come from RecurringGuardReasons: fixed, reviewable, non-sensitive.
                    return result.Message;

                case RecurringGenerationOutcome.Skipped:
                    // The skip reason is user-entered text the caller just supplied; safe to echo back.
                    return result.Message;

                case RecurringGenerationOutcome.Aborted:
                    // Only surface the one abort reason that is a user-facing, fixed string the UI should explain:
                    // the schedule was paused because its client is archived. Other aborts (lease held elsewhere,
                    // occurrence not found) are internal race/plumbing detail and stay out of the response.
                    return result.Message == RecurringGuardReasons.CustomerArchivedSchedulePaused
                        ? result.Message
                        : null;

                case RecurringGenerationOutcome.Failed:
                    return result.ErrorCode switch
                    {
                        RecurringErrorCodes.TaxNotResolved =>
                            "The tax on a line could not be resolved. Fix the line's tax and try again.",
                        RecurringErrorCodes.BooksLocked =>
                            "The document date is in a locked period or outside the open books. Choose a different date or unlock the period.",
                        RecurringErrorCodes.InvalidSchedule =>
                            "The schedule could not be turned into a valid invoice. Review its lines and client.",
                        RecurringErrorCodes.PostFailed =>
                            "The invoice could not be posted to the ledger. Please try again or contact support.",
                        _ => "The run could not be completed.",
                    };

                default:
                    return null;
            }
        }
    }
}
