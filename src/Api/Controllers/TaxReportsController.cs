// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Claims;
using Api.Filters;
using Core.Authorization;
using Core.Domain;
using Core.Domain.Security;
using Core.Entitlements;
using Core.Tenancy;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Services.TaxSystem;

namespace Api.Controllers
{
    [Core.Tenancy.TenantScoped]
    [Route("api/tax")]
    [RequireModule(SystemModules.Sales, SystemModules.Purchases, SystemModules.Financials, SystemModules.Reports)]
    public class TaxReportsController : BaseController
    {
        private const string InvalidSettingsMessage = "Choose how often you file and which months your quarters start in.";
        private readonly ITaxPeriodReportService _taxReports;
        private readonly ITaxFilingService _taxFilings;
        private readonly ITenantContext _tenant;
        private readonly IEntitlementService _entitlements;

        public TaxReportsController(
            ITaxPeriodReportService taxReports,
            ITaxFilingService taxFilings,
            ITenantContext tenant,
            IEntitlementService entitlements)
        {
            _taxReports = taxReports;
            _taxFilings = taxFilings;
            _tenant = tenant;
            _entitlements = entitlements;
        }

        [HttpGet("settings")]
        [RequireOperation(AccessOperationCatalog.TaxReportsView)]
        public IActionResult GetSettings()
        {
            return Ok(_taxReports.GetSettings());
        }

        [HttpPut("settings")]
        [RequireOperation(AccessOperationCatalog.TaxPeriodsManage)]
        public IActionResult UpdateSettings([FromBody] UpdateTaxPeriodSettingsRequest? request)
        {
            if (!ModelState.IsValid
                || request?.Frequency is not { } frequency
                || !Enum.IsDefined(typeof(TaxPeriodFrequency), frequency))
            {
                return BadRequest(new { code = "invalid_settings", message = InvalidSettingsMessage });
            }

            if (request.QuarterCycleStartMonth is int month && (month < 1 || month > 3))
                return BadRequest(new { code = "invalid_settings", message = InvalidSettingsMessage });

            try
            {
                return Ok(_taxReports.UpdateSettings(frequency, request.QuarterCycleStartMonth));
            }
            catch (TaxPeriodReportValidationException ex)
            {
                return BadRequest(new { code = ex.Code, message = ex.Message });
            }
        }

        [HttpGet("periods")]
        [RequireOperation(AccessOperationCatalog.TaxReportsView)]
        public IActionResult GetPeriods([FromQuery] int count = 8)
        {
            try
            {
                var periods = _taxReports.GetRecentPeriods(count);
                var ranges = periods.Select(p => (
                    DateTime.ParseExact(p.Start, "yyyy-MM-dd", CultureInfo.InvariantCulture),
                    DateTime.ParseExact(p.End, "yyyy-MM-dd", CultureInfo.InvariantCulture)));
                var summaries = _taxFilings.GetPeriodSummaries(ranges);

                var enriched = periods
                    .Select(p =>
                    {
                        summaries.TryGetValue((p.Start, p.End), out var summary);
                        return p with { Filing = summary };
                    })
                    .ToList();

                return Ok(enriched);
            }
            catch (TaxPeriodReportValidationException ex)
            {
                return BadRequest(new { code = ex.Code, message = ex.Message });
            }
        }

        [HttpGet("report")]
        [RequireOperation(AccessOperationCatalog.TaxReportsView)]
        public IActionResult GetReport(
            [FromQuery] string? from,
            [FromQuery] string? to,
            [FromQuery] bool includeAllDocuments = false)
        {
            if (!DateTime.TryParseExact(from, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var fromDate) ||
                !DateTime.TryParseExact(to, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var toDate))
            {
                return BadRequest(new { code = "invalid_date", message = "Provide from and to dates in yyyy-MM-dd format." });
            }

            try
            {
                var limit = includeAllDocuments ? (int?)null : TaxPeriodReportService.DefaultDocumentLimit;
                var report = _taxReports.Build(fromDate, toDate, limit);
                var filings = _taxFilings.ForPeriod(fromDate, toDate);
                var changedSinceFiling = filings.Count > 0
                    ? _taxFilings.ChangedSinceFilingForPeriod(fromDate, toDate)
                    : null;

                var canRecordFiling = _tenant.IsResolved
                    && _tenant.Permissions.Contains(SystemPermissions.AdminTaxesManage);
                var canLockOnFiling = HasFinancialsModule()
                    && _tenant.IsResolved
                    && _tenant.Permissions.Contains(SystemPermissions.FinancialsPeriodsClose);

                return Ok(new
                {
                    report.From,
                    report.To,
                    report.Basis,
                    report.Currency,
                    report.IsWholePeriod,
                    report.PeriodLabel,
                    report.Output,
                    report.Input,
                    report.NetTax,
                    report.Documents,
                    report.DocumentsTruncated,
                    report.Warnings,
                    report.Ledger,
                    report.Lock,
                    filings,
                    changedSinceFiling,
                    canRecordFiling,
                    canLockOnFiling
                });
            }
            catch (TaxPeriodReportValidationException ex)
            {
                return BadRequest(new { code = ex.Code, message = ex.Message });
            }
            catch (TaxPeriodReportConflictException ex)
            {
                return Conflict(new { code = ex.Code, message = ex.Message });
            }
        }

        [HttpGet("filings")]
        [RequireOperation(AccessOperationCatalog.TaxReportsView)]
        public IActionResult ListFilings()
        {
            return Ok(_taxFilings.List());
        }

        [HttpPost("filings")]
        [RequireOperation(AccessOperationCatalog.TaxFilingsRecord)]
        public IActionResult RecordFiling([FromBody] RecordTaxFilingHttpRequest? request)
        {
            if (request == null
                || !DateTime.TryParseExact(request.PeriodStart, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var periodStart)
                || !DateTime.TryParseExact(request.PeriodEnd, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var periodEnd)
                || !DateTime.TryParseExact(request.FiledOn, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var filedOn))
            {
                return BadRequest(new { code = "invalid_date", message = "Provide periodStart, periodEnd, and filedOn in yyyy-MM-dd format." });
            }

            var actor = GetUserNameFromRequestHeader()
                ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? User.FindFirst("sub")?.Value
                ?? "unknown";

            try
            {
                var filing = _taxFilings.Record(new RecordTaxFilingRequest(
                    PeriodStart: periodStart,
                    PeriodEnd: periodEnd,
                    FiledOn: filedOn,
                    Reference: request.Reference,
                    Notes: request.Notes,
                    LockBooks: request.LockBooks,
                    ExpectedNetTax: request.ExpectedNetTax,
                    AmendsFilingId: request.AmendsFilingId,
                    AmendmentReason: request.AmendmentReason,
                    Actor: actor));

                return StatusCode(201, filing);
            }
            catch (TaxFilingValidationException ex)
            {
                return BadRequest(new { code = ex.Code, message = ex.Message });
            }
            catch (TaxFilingConflictException ex)
            {
                return Conflict(new { code = ex.Code, message = ex.Message });
            }
            catch (TaxFilingForbiddenException ex)
            {
                return StatusCode(403, new { code = ex.Code, message = ex.Message });
            }
        }

        private bool HasFinancialsModule() =>
            _tenant.IsResolved
            && _entitlements.Evaluate(
                _tenant.RequireCompanyId(),
                new EntitlementAction.Module(ModuleAccess.Read, SystemModules.Financials)).Allowed;

        public sealed class UpdateTaxPeriodSettingsRequest
        {
            [JsonConverter(typeof(StringEnumConverter))]
            public TaxPeriodFrequency? Frequency { get; set; }
            public int? QuarterCycleStartMonth { get; set; }
        }

        public sealed class RecordTaxFilingHttpRequest
        {
            public string? PeriodStart { get; set; }
            public string? PeriodEnd { get; set; }
            public string? FiledOn { get; set; }
            public string? Reference { get; set; }
            public string? Notes { get; set; }
            public bool LockBooks { get; set; }
            public decimal ExpectedNetTax { get; set; }
            public int? AmendsFilingId { get; set; }
            public string? AmendmentReason { get; set; }
        }
    }
}
