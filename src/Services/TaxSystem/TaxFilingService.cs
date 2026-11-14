// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using Core.Data;
using Core.Domain;
using Core.Domain.Security;
using Core.Domain.TaxSystem;
using Core.Entitlements;
using Core.Tenancy;
using Services.Financial;

namespace Services.TaxSystem
{
    public sealed class TaxFilingService : ITaxFilingService
    {
        private static readonly JsonSerializerOptions SnapshotJsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };

        private readonly IRepository<TaxFiling> _filings;
        private readonly IRepository<CompanySetting> _settings;
        private readonly ITaxPeriodReportService _taxReports;
        private readonly IBooksLockService _booksLock;
        private readonly IEntitlementService _entitlements;
        private readonly ITenantContext _tenant;
        private readonly IUnitOfWork _unitOfWork;

        public TaxFilingService(
            IRepository<TaxFiling> filings,
            IRepository<CompanySetting> settings,
            ITaxPeriodReportService taxReports,
            IBooksLockService booksLock,
            IEntitlementService entitlements,
            ITenantContext tenant,
            IUnitOfWork unitOfWork)
        {
            _filings = filings;
            _settings = settings;
            _taxReports = taxReports;
            _booksLock = booksLock;
            _entitlements = entitlements;
            _tenant = tenant;
            _unitOfWork = unitOfWork;
        }

        public TaxFilingDto Record(RecordTaxFilingRequest request)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            var start = request.PeriodStart.Date;
            var end = request.PeriodEnd.Date;
            var filedOn = request.FiledOn.Date;

            using var tx = _unitOfWork.BeginSerializableTransaction();

            // 1. Valid dates.
            if (start > end)
                throw new TaxFilingValidationException("invalid_range", "Choose an end date on or after the start date.");

            var setting = _settings.Table.FirstOrDefault() ?? new CompanySetting
            {
                CompanyId = _tenant.RequireCompanyId()
            };
            var cycleStart = TaxPeriodCalculator.EffectiveCycleStart(setting);
            var today = WorkspaceToday(setting.Timezone);

            // 2. Whole period on the current grid.
            if (!TaxPeriodCalculator.IsWholePeriod(start, end, setting.TaxPeriodFrequency, cycleStart))
                throw new TaxFilingValidationException("tax_period_not_whole", "Choose a whole tax period to record a filing.");

            // 3. Period ended (I1).
            if (end >= today)
                throw new TaxFilingValidationException("tax_period_not_ended", "You can record a filing once the period has ended.");

            if (filedOn > today || filedOn < start)
                throw new TaxFilingValidationException("invalid_filed_on", "Filed on date must be between the period start and today.");

            var period = TaxPeriodCalculator.Containing(start, setting.TaxPeriodFrequency, cycleStart);

            // 4. Overlap and amendment chain (I3).
            var existingFilings = _filings.Table.ToList();
            var identical = existingFilings
                .Where(f => f.PeriodStart.Date == start && f.PeriodEnd.Date == end)
                .OrderByDescending(f => f.Sequence)
                .ToList();

            var sequence = 0;
            if (identical.Count > 0)
            {
                if (!request.AmendsFilingId.HasValue)
                {
                    throw new TaxFilingConflictException(
                        "tax_filing_exists",
                        "This period already has a filing. Record an amended filing instead.");
                }

                var latest = identical[0];
                if (latest.Id != request.AmendsFilingId.Value)
                {
                    throw new TaxFilingConflictException(
                        "tax_amendment_not_latest",
                        "Amend the latest filing for this period.");
                }

                if (string.IsNullOrWhiteSpace(request.AmendmentReason))
                {
                    throw new TaxFilingValidationException(
                        "tax_amendment_reason_required",
                        "Give a reason for the amendment.");
                }

                sequence = latest.Sequence + 1;
            }
            else if (request.AmendsFilingId.HasValue)
            {
                throw new TaxFilingConflictException(
                    "tax_amendment_not_latest",
                    "Amend the latest filing for this period.");
            }

            var overlapping = existingFilings
                .Where(f => f.PeriodStart.Date <= end
                            && f.PeriodEnd.Date >= start
                            && !(f.PeriodStart.Date == start && f.PeriodEnd.Date == end))
                .OrderByDescending(f => f.PeriodEnd)
                .FirstOrDefault();

            if (overlapping != null)
            {
                var existingLabel = PeriodLabelFor(overlapping);
                var existingEndStr = FormatDate(overlapping.PeriodEnd);
                throw new TaxFilingConflictException(
                    "tax_period_overlaps_filing",
                    $"{period.Label} overlaps the {existingLabel} filing. Choose a period that starts after {existingEndStr}.");
            }

            // Lock books permission check before inserting anything.
            if (request.LockBooks && (!HasFinancialsModule() || !_tenant.Permissions.Contains(SystemPermissions.FinancialsPeriodsClose)))
            {
                throw new TaxFilingForbiddenException(
                    "books_lock_not_allowed",
                    "You do not have permission to lock the books when recording a tax filing.");
            }

            // 5. Recompute on the server (I4).
            var report = _taxReports.Build(start, end, documentLimit: 0);
            if (Round2(report.NetTax) != Round2(request.ExpectedNetTax))
            {
                throw new TaxFilingConflictException(
                    "tax_figures_changed",
                    "The figures changed while you were reviewing them. Refresh and check again.");
            }

            // 6. Snapshot JSON, camelCase.
            var snapshot = new FilingSnapshot(
                Basis: report.Basis,
                Output: new SnapshotSide(
                    Rows: report.Output.Rows.Select(ToSnapshotRow).ToList(),
                    Taxable: report.Output.Taxable,
                    Tax: report.Output.Tax),
                Input: new SnapshotSide(
                    Rows: report.Input.Rows.Select(ToSnapshotRow).ToList(),
                    Taxable: report.Input.Taxable,
                    Tax: report.Input.Tax),
                NetTax: report.NetTax);

            var snapshotJson = JsonSerializer.Serialize(snapshot, SnapshotJsonOptions);

            var basisEnum = string.Equals(report.Basis, nameof(AccountingBasis.Cash), StringComparison.OrdinalIgnoreCase)
                ? AccountingBasis.Cash
                : AccountingBasis.Accrual;

            var reference = string.IsNullOrWhiteSpace(request.Reference) ? null : request.Reference.Trim();
            var notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
            var amendmentReason = string.IsNullOrWhiteSpace(request.AmendmentReason) ? null : request.AmendmentReason.Trim();
            var actor = string.IsNullOrWhiteSpace(request.Actor) ? "unknown" : request.Actor;

            var filing = new TaxFiling
            {
                CompanyId = _tenant.RequireCompanyId(),
                PeriodStart = start,
                PeriodEnd = end,
                Frequency = setting.TaxPeriodFrequency,
                Basis = basisEnum,
                Sequence = sequence,
                AmendsFilingId = request.AmendsFilingId,
                AmendmentReason = amendmentReason,
                FiledOn = filedOn,
                Reference = reference,
                Notes = notes,
                OutputTax = Round2(report.Output.Tax),
                InputTax = Round2(report.Input.Tax),
                NetTax = Round2(report.NetTax),
                SnapshotJson = snapshotJson,
                CreatedBy = actor,
                CreatedAt = DateTime.UtcNow
            };

            // 7. Insert & 8. Lock books + commit (with unique index and serialization conflict backstop).
            try
            {
                _filings.Insert(filing);

                if (request.LockBooks)
                {
                    var refLabel = reference ?? "no reference";
                    try
                    {
                        _booksLock.Advance(end, $"Tax filing {period.Label} ({refLabel})", actor);
                    }
                    catch (BooksLockValidationException ex)
                    {
                        _unitOfWork.DiscardPendingChanges();
                        throw new TaxFilingValidationException("books_lock_invalid", ex.Message);
                    }
                }

                tx.Commit();
            }
            catch (Exception ex) when (IsConcurrencyOrUniqueConflict(ex))
            {
                _unitOfWork.DiscardPendingChanges();
                throw new TaxFilingConflictException(
                    "tax_filing_exists",
                    "This period already has a filing. Record an amended filing instead.");
            }

            return ToDto(filing);
        }

        public IReadOnlyList<TaxFilingDto> List()
        {
            return _filings.Table
                .OrderByDescending(f => f.PeriodStart)
                .ThenByDescending(f => f.PeriodEnd)
                .ThenByDescending(f => f.Sequence)
                .ToList()
                .Select(ToDto)
                .ToList();
        }

        public IReadOnlyList<TaxFilingDto> ForPeriod(DateTime periodStart, DateTime periodEnd)
        {
            var start = periodStart.Date;
            var end = periodEnd.Date;
            return _filings.Table
                .Where(f => f.PeriodStart == start && f.PeriodEnd == end)
                .OrderByDescending(f => f.Sequence)
                .ToList()
                .Select(ToDto)
                .ToList();
        }

        public TaxChangedSinceFilingDto ChangedSinceFiling(TaxFiling filing)
        {
            if (filing == null)
                throw new ArgumentNullException(nameof(filing));

            var current = _taxReports.Build(filing.PeriodStart.Date, filing.PeriodEnd.Date, documentLimit: 0, filing.Basis);
            return ComputeChangedSinceFiling(filing, current);
        }

        public TaxChangedSinceFilingDto? ChangedSinceFilingForPeriod(
            DateTime periodStart,
            DateTime periodEnd,
            TaxPeriodReport? precomputedCurrentReport = null)
        {
            var start = periodStart.Date;
            var end = periodEnd.Date;
            var latest = _filings.Table
                .Where(f => f.PeriodStart == start && f.PeriodEnd == end)
                .OrderByDescending(f => f.Sequence)
                .FirstOrDefault();

            if (latest == null)
                return null;

            if (precomputedCurrentReport != null
                && string.Equals(latest.Basis.ToString(), precomputedCurrentReport.Basis, StringComparison.OrdinalIgnoreCase))
            {
                return ComputeChangedSinceFiling(latest, precomputedCurrentReport);
            }

            return ChangedSinceFiling(latest);
        }

        private static TaxChangedSinceFilingDto ComputeChangedSinceFiling(TaxFiling filing, TaxPeriodReport current)
        {
            var snapshot = DeserializeSnapshot(filing.SnapshotJson);

            var diffRows = new List<TaxFilingDiffRowDto>();
            CompareSide("output", snapshot.Output.Rows, current.Output.Rows, diffRows);
            CompareSide("input", snapshot.Input.Rows, current.Input.Rows, diffRows);

            var netDifference = Round2(current.NetTax - filing.NetTax);
            var changed = diffRows.Count > 0 || netDifference != 0m;

            return new TaxChangedSinceFilingDto(
                Rows: diffRows,
                NetDifference: netDifference,
                Changed: changed);
        }

        public IReadOnlyDictionary<(string Start, string End), TaxPeriodFilingSummaryDto> GetPeriodSummaries(
            IEnumerable<(DateTime Start, DateTime End)> periods)
        {
            var periodList = periods.Select(p => (Start: p.Start.Date, End: p.End.Date)).Distinct().ToList();
            if (periodList.Count == 0)
                return new Dictionary<(string Start, string End), TaxPeriodFilingSummaryDto>();

            var minStart = periodList.Min(p => p.Start);
            var maxEnd = periodList.Max(p => p.End);

            var candidateFilings = _filings.Table
                .Where(f => f.PeriodStart >= minStart && f.PeriodEnd <= maxEnd)
                .ToList();

            var result = new Dictionary<(string Start, string End), TaxPeriodFilingSummaryDto>();
            foreach (var p in periodList)
            {
                var latest = candidateFilings
                    .Where(f => f.PeriodStart.Date == p.Start && f.PeriodEnd.Date == p.End)
                    .OrderByDescending(f => f.Sequence)
                    .FirstOrDefault();

                if (latest == null)
                    continue;

                var changedSinceFiling = ChangedSinceFiling(latest);
                result[(FormatDate(p.Start), FormatDate(p.End))] = new TaxPeriodFilingSummaryDto(
                    Id: latest.Id,
                    Sequence: latest.Sequence,
                    FiledOn: FormatDate(latest.FiledOn),
                    Reference: latest.Reference,
                    NetTax: latest.NetTax,
                    ChangedSinceFiling: changedSinceFiling);
            }

            return result;
        }

        private static void CompareSide(
            string side,
            IReadOnlyList<SnapshotRow> filedRows,
            IReadOnlyList<TaxPeriodTaxRow> currentRows,
            List<TaxFilingDiffRowDto> diffRows)
        {
            var filedByTaxId = new Dictionary<int, SnapshotRow>();
            foreach (var r in filedRows)
            {
                var key = r.TaxId ?? -1;
                filedByTaxId[key] = r;
            }

            var currentByTaxId = new Dictionary<int, TaxPeriodTaxRow>();
            foreach (var r in currentRows)
            {
                var key = r.TaxId ?? -1;
                currentByTaxId[key] = r;
            }

            var allKeys = filedByTaxId.Keys
                .Union(currentByTaxId.Keys)
                .OrderBy(k => k == -1 ? int.MaxValue : k);

            foreach (var key in allKeys)
            {
                filedByTaxId.TryGetValue(key, out var fRow);
                currentByTaxId.TryGetValue(key, out var cRow);

                var filedTax = Round2(fRow?.Tax ?? 0m);
                var currentTax = Round2(cRow?.Tax ?? 0m);
                var diff = Round2(currentTax - filedTax);
                if (diff == 0m)
                    continue;

                var taxId = key == -1 ? (int?)null : key;
                var taxCode = cRow?.TaxCode ?? fRow?.TaxCode ?? "NO-TAX";
                diffRows.Add(new TaxFilingDiffRowDto(
                    Side: side,
                    TaxId: taxId,
                    TaxCode: taxCode,
                    Filed: filedTax,
                    Current: currentTax,
                    Difference: diff));
            }
        }

        private static SnapshotRow ToSnapshotRow(TaxPeriodTaxRow row) => new(
            TaxId: row.TaxId,
            TaxCode: row.TaxCode,
            TaxName: row.TaxName,
            Rate: row.Rate,
            Taxable: row.Taxable,
            Tax: row.Tax);

        private static FilingSnapshot DeserializeSnapshot(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return new FilingSnapshot("Accrual", new SnapshotSide(Array.Empty<SnapshotRow>(), 0m, 0m), new SnapshotSide(Array.Empty<SnapshotRow>(), 0m, 0m), 0m);

            return JsonSerializer.Deserialize<FilingSnapshot>(json, SnapshotJsonOptions)
                ?? new FilingSnapshot("Accrual", new SnapshotSide(Array.Empty<SnapshotRow>(), 0m, 0m), new SnapshotSide(Array.Empty<SnapshotRow>(), 0m, 0m), 0m);
        }

        private static TaxFilingDto ToDto(TaxFiling f) => new(
            Id: f.Id,
            PeriodStart: FormatDate(f.PeriodStart),
            PeriodEnd: FormatDate(f.PeriodEnd),
            PeriodLabel: PeriodLabelFor(f),
            Frequency: f.Frequency.ToString(),
            Basis: f.Basis.ToString(),
            Sequence: f.Sequence,
            AmendsFilingId: f.AmendsFilingId,
            AmendmentReason: f.AmendmentReason,
            FiledOn: FormatDate(f.FiledOn),
            Reference: f.Reference,
            Notes: f.Notes,
            OutputTax: f.OutputTax,
            InputTax: f.InputTax,
            NetTax: f.NetTax,
            CreatedBy: f.CreatedBy,
            CreatedAt: f.CreatedAt.ToString("o", CultureInfo.InvariantCulture));

        private static string PeriodLabelFor(TaxFiling f)
        {
            var cycleStart = f.Frequency == TaxPeriodFrequency.Quarterly
                ? ((f.PeriodStart.Month - 1) % 3) + 1
                : 1;
            return TaxPeriodCalculator.Containing(f.PeriodStart, f.Frequency, cycleStart).Label;
        }

        private bool HasFinancialsModule() =>
            _entitlements.Evaluate(
                _tenant.RequireCompanyId(),
                new EntitlementAction.Module(ModuleAccess.Read, SystemModules.Financials)).Allowed;

        public static bool IsConcurrencyOrUniqueConflict(Exception ex)
        {
            var current = ex;
            while (current != null)
            {
                var sqlState = current.GetType().GetProperty("SqlState")?.GetValue(current)?.ToString();
                if (string.Equals(sqlState, "23505", StringComparison.Ordinal)
                    || string.Equals(sqlState, "40001", StringComparison.Ordinal))
                {
                    return true;
                }

                var msg = current.Message ?? string.Empty;
                if (msg.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase)
                    || msg.Contains("23505", StringComparison.Ordinal)
                    || msg.Contains("40001", StringComparison.Ordinal)
                    || msg.Contains("could not serialize access", StringComparison.OrdinalIgnoreCase)
                    || msg.Contains("IX_TaxFiling_CompanyId_PeriodStart_PeriodEnd_Sequence", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                current = current.InnerException;
            }

            return false;
        }

        private static DateTime WorkspaceToday(string? timezoneId)
        {
            var utcNow = DateTime.UtcNow;
            if (string.IsNullOrWhiteSpace(timezoneId) || string.Equals(timezoneId, "UTC", StringComparison.OrdinalIgnoreCase))
                return utcNow.Date;

            try
            {
                var tz = TimeZoneInfo.FindSystemTimeZoneById(timezoneId);
                return TimeZoneInfo.ConvertTimeFromUtc(utcNow, tz).Date;
            }
            catch (TimeZoneNotFoundException)
            {
                return utcNow.Date;
            }
            catch (InvalidTimeZoneException)
            {
                return utcNow.Date;
            }
        }

        private static decimal Round2(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

        private static string FormatDate(DateTime date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        private sealed record FilingSnapshot(
            string Basis,
            SnapshotSide Output,
            SnapshotSide Input,
            decimal NetTax);

        private sealed record SnapshotSide(
            IReadOnlyList<SnapshotRow> Rows,
            decimal Taxable,
            decimal Tax);

        private sealed record SnapshotRow(
            int? TaxId,
            string TaxCode,
            string TaxName,
            decimal? Rate,
            decimal Taxable,
            decimal Tax);
    }
}
