using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Api.Filters;
using Core.Billing;
using Core.Domain;
using Core.Domain.Security;
using Core.Tenancy;
using Dto.Subscription;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Services.Administration;
using Services.Billing;
using Services.Financial;

namespace Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class SubscriptionController : BaseController
    {
        private readonly IAdministrationService _adminService;
        private readonly IPlanCatalog _plans;
        private readonly IBillingProvider _billing;
        private readonly ITenantContext _tenant;
        private readonly IBooksReadinessService _books;

        public SubscriptionController(
            IAdministrationService adminService,
            IPlanCatalog plans,
            IBillingProvider billing,
            ITenantContext tenant,
            IBooksReadinessService books)
        {
            _adminService = adminService;
            _plans = plans;
            _billing = billing;
            _tenant = tenant;
            _books = books;
        }

        internal const string BooksNotReadyCode = "BOOKS_NOT_READY";

        // GET /api/Subscription/companies was removed in Phase 2: it listed every workspace.
        // Use GET /api/organizations (caller's own memberships only).

        [TenantScoped]
        [Authorize]
        [HttpGet("current")]
        public IActionResult GetCurrentSubscription()
        {
            // Always the resolved workspace (X-AccountGo-Org); any ?slug= query value is ignored.
            var subscription = _adminService.GetCurrentSubscription();
            return Ok(subscription);
        }

        [TenantScoped]
        [Authorize]
        [RequireWorkspaceRole(CompanyMemberRole.Owner)]
        [HttpPut("update")]
        public async Task<IActionResult> UpdateSubscription([FromBody] UpdateSubscriptionDto dto, CancellationToken ct)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            try
            {
                var current = _adminService.GetCurrentSubscription();

                // Turning on Financials shows the general ledger. Every issued invoice and bill must already
                // have its ledger entry, or the reports would disagree with receivables and payables.
                if (GainsFinancials(current, dto))
                {
                    var readiness = _books.Check();
                    if (!readiness.IsReady)
                        return BooksNotReady(readiness);
                }

                if (!string.IsNullOrWhiteSpace(dto.Plan) && _billing.RequiresProviderCheckout)
                {
                    var targetPlan = dto.Plan.Trim().ToLowerInvariant();
                    if (!string.Equals(current.Plan, targetPlan, StringComparison.OrdinalIgnoreCase))
                    {
                        var returnUrl = ResolveCheckoutReturnUrl(dto.ReturnUrl, Request.Headers["Referer"].ToString());
                        if (!string.IsNullOrWhiteSpace(dto.BillingInterval)
                            && !returnUrl.Contains("interval=", StringComparison.OrdinalIgnoreCase))
                        {
                            var sep = returnUrl.Contains('?', StringComparison.Ordinal) ? "&" : "?";
                            returnUrl = $"{returnUrl}{sep}interval={Uri.EscapeDataString(dto.BillingInterval.Trim())}";
                        }

                        if (!dto.Confirmed)
                        {
                            PlanChangePreview? preview;
                            try
                            {
                                preview = await _billing.PreviewPlanChangeAsync(
                                    _tenant.RequireCompanyId(), targetPlan, returnUrl, ct);
                            }
                            catch (Exception ex) when (ex is not OperationCanceledException)
                            {
                                // Never fall through to charging when the price could not be shown.
                                return BadRequest(new { message = "We couldn't get the price for this change. Nothing was charged. Try again." });
                            }
                            if (preview != null)
                                return Ok(new { confirmationRequired = true, preview });
                        }

                        var result = await _billing.RequestPlanChangeAsync(
                            _tenant.RequireCompanyId(),
                            targetPlan,
                            returnUrl,
                            ct);
                        if (!string.IsNullOrEmpty(result.ErrorReason))
                            return BadRequest(new { message = result.ErrorReason });
                        if (!result.AppliedImmediately)
                            return Ok(new { redirectUrl = result.RedirectUrl });
                    }
                }

                var updated = _adminService.UpdateSubscription(dto);
                return Ok(updated);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        private bool GainsFinancials(TenantSubscriptionDto current, UpdateSubscriptionDto dto)
        {
            if (current.ActiveModules.Contains(SystemModules.Financials, StringComparer.OrdinalIgnoreCase))
                return false;

            List<string>? target = dto.ActiveModules;
            if (target == null)
            {
                if (string.IsNullOrWhiteSpace(dto.Plan) || !_plans.TryResolvePlan(dto.Plan, out var plan))
                    return false;
                target = plan.IncludedModules;
            }
            return target.Contains(SystemModules.Financials, StringComparer.OrdinalIgnoreCase);
        }

        private ObjectResult BooksNotReady(BooksReadiness readiness)
        {
            var count = readiness.UnpostedInvoices.Count + readiness.UnpostedBills.Count;
            var problem = new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Books need posting first",
                Detail = count == 1
                    ? "1 issued document has no ledger entry yet. Post it before turning on accounting."
                    : $"{count} issued documents have no ledger entry yet. Post them before turning on accounting.",
            };
            problem.Extensions["code"] = BooksNotReadyCode;
            problem.Extensions["unpostedInvoices"] = readiness.UnpostedInvoices;
            problem.Extensions["unpostedBills"] = readiness.UnpostedBills;
            return new ObjectResult(problem) { StatusCode = StatusCodes.Status409Conflict };
        }

        [TenantFree]
        [AllowAnonymous]
        [HttpGet("plans")]
        public IActionResult GetPlans()
        {
            return Ok(_plans.GetPlans());
        }

        [TenantFree]
        [AllowAnonymous]
        [HttpGet("modules")]
        public IActionResult GetModules()
        {
            var modules = _adminService.GetModuleDefinitions();
            return Ok(modules);
        }

        /// <summary>
        /// Stripe Checkout requires absolute https success/cancel URLs. Prefer the client-supplied
        /// return URL; fall back to Referer only when it is already an absolute http(s) URL.
        /// </summary>
        internal static string ResolveCheckoutReturnUrl(string? dtoReturnUrl, string? referer)
        {
            if (IsAbsoluteHttpUrl(dtoReturnUrl))
                return dtoReturnUrl!.Trim();
            if (IsAbsoluteHttpUrl(referer))
                return referer!.Trim();
            throw new ArgumentException(
                "A full return URL is required to start checkout. Refresh the page and try again.");
        }

        private static bool IsAbsoluteHttpUrl(string? value) =>
            !string.IsNullOrWhiteSpace(value)
            && Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);
    }
}
