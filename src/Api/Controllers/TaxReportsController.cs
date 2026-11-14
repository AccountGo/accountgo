// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Globalization;
using Api.Filters;
using Core.Authorization;
using Core.Domain;
using Core.Domain.Security;
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

        public TaxReportsController(ITaxPeriodReportService taxReports)
        {
            _taxReports = taxReports;
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
                return Ok(_taxReports.GetRecentPeriods(count));
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
                return Ok(_taxReports.Build(fromDate, toDate, limit));
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

        public sealed class UpdateTaxPeriodSettingsRequest
        {
            [JsonConverter(typeof(StringEnumConverter))]
            public TaxPeriodFrequency? Frequency { get; set; }
            public int? QuarterCycleStartMonth { get; set; }
        }
    }
}
