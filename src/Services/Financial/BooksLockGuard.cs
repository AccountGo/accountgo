// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Linq;
using Core.Data;
using Core.Domain;
using Core.Domain.Security;
using Core.Entitlements;
using Core.Tenancy;

namespace Services.Financial
{
    public sealed class BooksLockGuard : IBooksLockGuard
    {
        private readonly IRepository<CompanySetting> _settings;
        private readonly IEntitlementService _entitlements;
        private readonly ITenantContext _tenant;
        private int _bypassDepth;

        public BooksLockGuard(
            IRepository<CompanySetting> settings,
            IEntitlementService entitlements,
            ITenantContext tenant)
        {
            _settings = settings;
            _entitlements = entitlements;
            _tenant = tenant;
        }

        public IDisposable Bypass()
        {
            _bypassDepth++;
            return new Releaser(() => _bypassDepth--);
        }

        public void EnsureDateAllowed(DateTime date)
        {
            if (_bypassDepth > 0)
                return;

            if (!_tenant.IsResolved || !_tenant.CompanyId.HasValue)
                return;

            var lockedThrough = _settings.Table
                .Select(s => s.BooksLockedThrough)
                .FirstOrDefault();
            if (!lockedThrough.HasValue)
                return;

            if (!HasFinancialsModule())
                return;

            if (date.Date <= lockedThrough.Value.Date)
                throw new BooksLockedException(lockedThrough.Value);
        }

        private bool HasFinancialsModule()
        {
            var decision = _entitlements.Evaluate(
                _tenant.RequireCompanyId(),
                new EntitlementAction.Module(ModuleAccess.Read, SystemModules.Financials));
            return decision.Allowed;
        }

        private sealed class Releaser : IDisposable
        {
            private Action? _release;

            public Releaser(Action release) => _release = release;

            public void Dispose()
            {
                var action = System.Threading.Interlocked.Exchange(ref _release, null);
                action?.Invoke();
            }
        }
    }
}
