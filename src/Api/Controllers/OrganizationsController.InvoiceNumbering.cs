#nullable enable
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text.Json;
using Api.Auditing;
using Api.Filters;
using Core.Domain;
using Core.Domain.Auditing;
using Core.Domain.Financials;
using Core.Domain.Sales;
using Core.Domain.Security;
using Core.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Api.Controllers
{
    public partial class OrganizationsController
    {
        public sealed class SalesInvoiceNumberingModel
        {
            public string? Format { get; set; }
            public int NextNumber { get; set; }
            public int MinNextNumber { get; set; }
            public string CurrentFiscalYearCode { get; set; } = string.Empty;
            public string NextFiscalYearCode { get; set; } = string.Empty;
            public string SampleCurrentYear { get; set; } = string.Empty;
            public string SampleNextYear { get; set; } = string.Empty;
            public bool HasIssuedInCurrentFiscalYear { get; set; }
        }

        public sealed class UpdateSalesInvoiceNumberingRequest
        {
            public string? Format { get; set; }
            public int? NextNumber { get; set; }
        }

        private IActionResult GetSalesInvoiceNumberingCore()
        {
            var seq = _db.SequenceNumbers.FirstOrDefault(s => s.SequenceNumberType == SequenceNumberTypes.SalesInvoice);
            var fmt = DocumentNumberFormat.Parse(seq?.Format);
            var today = DateTime.UtcNow.Date;
            var (currentCode, nextCode, currentStart, currentEnd, currentFyId) = ResolvePreviewFiscalYears(today);

            int currentNext;
            if (fmt.UsesFiscalYear && currentFyId.HasValue)
            {
                var series = _db.DocumentNumberSeries.FirstOrDefault(s =>
                    s.SequenceNumberType == SequenceNumberTypes.SalesInvoice && s.FinancialYearId == currentFyId.Value);
                currentNext = Math.Max(series?.NextNumber ?? 1, 1);
            }
            else if (fmt.UsesFiscalYear)
            {
                currentNext = 1;
            }
            else
            {
                currentNext = Math.Max(seq?.NextNumber ?? 1, 1);
            }

            var maxIssued = ComputeMaxIssuedSequence(fmt, fmt.UsesFiscalYear ? currentCode : null,
                fmt.UsesFiscalYear ? currentStart : null, fmt.UsesFiscalYear ? currentEnd : null);
            var minNext = Math.Max(currentNext, maxIssued + 1);
            var hasIssued = _db.SalesInvoiceHeaders.Any(i =>
                i.Status != SalesInvoiceStatus.Draft && i.Date >= currentStart && i.Date <= currentEnd);

            return Ok(new SalesInvoiceNumberingModel
            {
                Format = fmt.RawFormat,
                NextNumber = currentNext,
                MinNextNumber = minNext,
                CurrentFiscalYearCode = currentCode,
                NextFiscalYearCode = nextCode,
                SampleCurrentYear = fmt.Render(currentNext, currentCode),
                SampleNextYear = fmt.Render(fmt.UsesFiscalYear ? 1 : currentNext, nextCode),
                HasIssuedInCurrentFiscalYear = hasIssued
            });
        }

        private IActionResult UpdateSalesInvoiceNumberingCore(UpdateSalesInvoiceNumberingRequest? request)
        {
            if (request == null)
                return Problem(statusCode: 400, title: "Sales invoice numbering settings required.");

            if (!DocumentNumberFormat.TryParse(request.Format, out var fmt, out var formatError) || fmt == null)
            {
                return Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Invalid invoice number format",
                    detail: formatError,
                    extensions: new Dictionary<string, object?> { ["code"] = "invalid_format" });
            }

            using var tx = _db.Database.BeginTransaction(IsolationLevel.Serializable);

            var companyId = _tenant.RequireCompanyId();
            var seq = _db.SequenceNumbers.FirstOrDefault(s => s.SequenceNumberType == SequenceNumberTypes.SalesInvoice);
            if (seq == null)
            {
                seq = new SequenceNumber
                {
                    CompanyId = companyId,
                    SequenceNumberType = SequenceNumberTypes.SalesInvoice,
                    Description = nameof(SequenceNumberTypes.SalesInvoice),
                    Prefix = string.Empty,
                    UsePrefix = false,
                    NextNumber = 1,
                };
                _db.SequenceNumbers.Add(seq);
                _db.SaveChanges();
            }

            var oldFormat = seq.Format;
            var today = DateTime.UtcNow.Date;
            FinancialYear? currentFy = null;
            DocumentNumberSeries? series = null;
            int oldNext;

            if (fmt.UsesFiscalYear)
            {
                _financial.EnsureFiscalYearsThrough(today);
                currentFy = _db.FinancialYears.FirstOrDefault(y => y.StartDate <= today && y.EndDate >= today);
                if (currentFy == null)
                    return Problem(statusCode: 400, title: "No fiscal year covers today's date.");

                series = _db.DocumentNumberSeries.FirstOrDefault(s =>
                    s.SequenceNumberType == SequenceNumberTypes.SalesInvoice && s.FinancialYearId == currentFy.Id);
                oldNext = Math.Max(series?.NextNumber ?? 1, 1);
            }
            else
            {
                oldNext = Math.Max(seq.NextNumber, 1);
            }

            var maxIssued = ComputeMaxIssuedSequence(
                fmt,
                fmt.UsesFiscalYear ? currentFy!.FiscalYearCode : null,
                fmt.UsesFiscalYear ? currentFy!.StartDate : null,
                fmt.UsesFiscalYear ? currentFy!.EndDate : null);
            var minAllowed = Math.Max(oldNext, maxIssued + 1);
            var newNext = request.NextNumber ?? oldNext;

            if (newNext < 1 || newNext < minAllowed)
            {
                return Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Invalid next number",
                    detail: $"Next number must be at least {minAllowed} (forward-only and above any already issued number in this series).",
                    extensions: new Dictionary<string, object?> { ["code"] = "invalid_next_number", ["minNextNumber"] = minAllowed });
            }

            seq.Format = fmt.RawFormat;
            if (fmt.UsesFiscalYear)
            {
                if (series == null)
                {
                    series = new DocumentNumberSeries
                    {
                        CompanyId = companyId,
                        SequenceNumberType = SequenceNumberTypes.SalesInvoice,
                        FinancialYearId = currentFy!.Id,
                        NextNumber = newNext
                    };
                    _db.DocumentNumberSeries.Add(series);
                }
                else
                {
                    series.NextNumber = newNext;
                }
            }
            else
            {
                seq.NextNumber = newNext;
            }

            if (!string.Equals(oldFormat, fmt.RawFormat, StringComparison.Ordinal) || oldNext != newNext)
            {
                var auditContext = HttpContext?.RequestServices.GetService<IRequestAuditContext>();
                var changes = new Dictionary<string, object?>
                {
                    ["format"] = new { old = oldFormat, @new = fmt.RawFormat },
                    ["nextNumber"] = new { old = oldNext, @new = newNext },
                    ["fiscalYearCode"] = currentFy?.FiscalYearCode
                };
                _db.RecordChanges.Add(new RecordChange
                {
                    CompanyId = companyId,
                    EntityType = "SalesInvoiceNumbering",
                    EntityId = seq.Id,
                    Action = "Modified",
                    ChangesJson = JsonSerializer.Serialize(changes),
                    ActorUserId = auditContext?.ActorUserId ?? UserId,
                    ActorKind = auditContext?.ActorKind ?? "user",
                    CorrelationId = auditContext?.CorrelationId ?? HttpContext?.TraceIdentifier ?? string.Empty,
                    IpAddress = auditContext?.IpAddress ?? HttpContext?.Connection.RemoteIpAddress?.ToString(),
                    OccurredAt = DateTime.UtcNow
                });
            }

            _db.SaveChanges();
            tx.Commit();

            var (currentCode, nextCode, currentStart, currentEnd, _) = ResolvePreviewFiscalYears(today);
            var hasIssued = _db.SalesInvoiceHeaders.Any(i =>
                i.Status != SalesInvoiceStatus.Draft && i.Date >= currentStart && i.Date <= currentEnd);

            return Ok(new SalesInvoiceNumberingModel
            {
                Format = fmt.RawFormat,
                NextNumber = newNext,
                MinNextNumber = Math.Max(newNext, maxIssued + 1),
                CurrentFiscalYearCode = currentCode,
                NextFiscalYearCode = nextCode,
                SampleCurrentYear = fmt.Render(newNext, currentCode),
                SampleNextYear = fmt.Render(fmt.UsesFiscalYear ? 1 : newNext, nextCode),
                HasIssuedInCurrentFiscalYear = hasIssued
            });
        }

        private (string CurrentCode, string NextCode, DateTime CurrentStart, DateTime CurrentEnd, int? CurrentFyId) ResolvePreviewFiscalYears(DateTime today)
        {
            var setting = _db.CompanySettings.FirstOrDefault();
            var pattern = setting?.FiscalYearPattern ?? FiscalYearPattern.Calendar;
            var startMonth = setting?.FiscalYearStartMonth;

            var currentFy = _db.FinancialYears.FirstOrDefault(y => y.StartDate <= today && y.EndDate >= today);
            string currentCode;
            DateTime currentStart;
            DateTime currentEnd;
            if (currentFy != null)
            {
                currentCode = currentFy.FiscalYearCode;
                currentStart = currentFy.StartDate;
                currentEnd = currentFy.EndDate;
            }
            else
            {
                var computed = FiscalYearCalculator.Containing(today, pattern, startMonth);
                currentCode = computed.Code;
                currentStart = computed.Start;
                currentEnd = computed.End;
            }

            var nextDate = currentEnd.Date.AddDays(1);
            var nextFy = _db.FinancialYears.FirstOrDefault(y => y.StartDate <= nextDate && y.EndDate >= nextDate);
            var nextCode = nextFy?.FiscalYearCode ?? FiscalYearCalculator.Containing(nextDate, pattern, startMonth).Code;

            return (currentCode, nextCode, currentStart, currentEnd, currentFy?.Id);
        }

        private int ComputeMaxIssuedSequence(DocumentNumberFormat fmt, string? fyCode, DateTime? start, DateTime? end)
        {
            var query = _db.SalesInvoiceHeaders.Where(i => i.No != null);
            if (start.HasValue && end.HasValue)
                query = query.Where(i => i.Date >= start.Value && i.Date <= end.Value);

            var max = 0;
            foreach (var no in query.Select(i => i.No))
            {
                if (fmt.TryParseSequence(no, fyCode, out var val) && val > max)
                    max = val;
            }
            return max;
        }
    }
}
