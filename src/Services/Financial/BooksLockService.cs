// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Globalization;
using System.Linq;
using Core.Data;
using Core.Domain;
using Core.Domain.Financials;
using Core.Tenancy;

namespace Services.Financial
{
    public sealed class BooksLockService : IBooksLockService
    {
        private readonly IRepository<CompanySetting> _settings;
        private readonly IRepository<BooksLockChange> _lockChanges;
        private readonly ITenantContext _tenant;

        public BooksLockService(
            IRepository<CompanySetting> settings,
            IRepository<BooksLockChange> lockChanges,
            ITenantContext tenant)
        {
            _settings = settings;
            _lockChanges = lockChanges;
            _tenant = tenant;
        }

        public DateTime? Advance(DateTime through, string? reason, string actor)
        {
            var target = through.Date;
            var setting = _settings.Table.FirstOrDefault();
            if (setting == null)
            {
                setting = new CompanySetting { CompanyId = _tenant.RequireCompanyId() };
                _settings.Insert(setting);
            }

            var today = DateTime.UtcNow.Date;
            if (target > today)
                throw new BooksLockValidationException("Books lock date cannot be after today.");

            var earliest = setting.BooksStartDate.Date.AddDays(-1);
            if (target < earliest)
            {
                throw new BooksLockValidationException(
                    $"Books lock date cannot be before {earliest.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)} (books start minus one day).");
            }

            var previous = setting.BooksLockedThrough?.Date;
            if (previous.HasValue && previous.Value >= target)
                return previous;

            setting.BooksLockedThrough = target;
            _settings.Update(setting);

            _lockChanges.Insert(new BooksLockChange
            {
                CompanyId = setting.CompanyId,
                PreviousLockedThrough = previous,
                NewLockedThrough = target,
                Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(),
                ChangedBy = string.IsNullOrWhiteSpace(actor) ? "unknown" : actor,
                ChangedAt = DateTime.UtcNow
            });

            return target;
        }
    }
}
