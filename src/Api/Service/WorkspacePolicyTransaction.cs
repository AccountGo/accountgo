// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Data;
using System.Data.Common;
using Api.Data;
using Core.Authorization;
using Core.Domain;
using Core.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Api.Service
{
    /// <summary>
    /// Acquires the durable per-company coordination row before policy-sensitive work.
    /// A process-local revision or cache invalidation alone is insufficient.
    /// </summary>
    public sealed class WorkspacePolicyTransaction : IWorkspacePolicyTransaction
    {
        private readonly ApiDbContext _db;
        private readonly ITenantContext _tenant;

        public WorkspacePolicyTransaction(ApiDbContext db, ITenantContext tenant)
        {
            _db = db;
            _tenant = tenant ?? throw new ArgumentNullException(nameof(tenant));
        }

        public void Execute(int companyId, Action<WorkspacePolicyGuard> action) =>
            Execute(companyId, guard =>
            {
                action(guard);
                return true;
            });

        public T Execute<T>(int companyId, Func<WorkspacePolicyGuard, T> action)
        {
            if (companyId <= 0)
                throw new ArgumentOutOfRangeException(nameof(companyId));

            var ownsTransaction = _db.Database.CurrentTransaction == null;
            IDbContextTransaction? tx = null;
            try
            {
                if (ownsTransaction)
                    tx = _db.Database.BeginTransaction(IsolationLevel.Serializable);

                // Policy writes are ICompanyOwned; only push impersonation when tenant is not resolved for this company.
                IDisposable? impersonation = null;
                if (!_tenant.IsResolved || _tenant.CompanyId != companyId)
                {
                    impersonation = _tenant.Impersonate(companyId, role: CompanyMemberRole.Owner);
                }

                try
                {
                    var settings = AcquireCoordination(companyId);
                    var guard = new WorkspacePolicyGuard(companyId, settings);
                    var result = action(guard);

                    // Always flush guarded mutations; only commit when this call owns the transaction.
                    _db.SaveChanges();
                    if (ownsTransaction)
                        tx!.Commit();

                    return result;
                }
                finally
                {
                    impersonation?.Dispose();
                }
            }
            catch
            {
                if (ownsTransaction)
                    tx?.Rollback();
                throw;
            }
            finally
            {
                tx?.Dispose();
            }
        }

        private WorkspaceOperatingSettings AcquireCoordination(int companyId)
        {
            EnsureCoordinationRow(companyId);
            LockCoordinationRow(companyId);

            var settings = _db.WorkspaceOperatingSettings
                .IgnoreQueryFilters()
                .SingleOrDefault(s => s.CompanyId == companyId);

            if (settings == null)
            {
                settings = new WorkspaceOperatingSettings
                {
                    CompanyId = companyId,
                    Purpose = WorkspacePurpose.Unspecified,
                    CollaborationIntent = WorkspaceCollaborationIntent.Unspecified,
                    CollaborationState = WorkspaceCollaborationState.LegacyUnclassified,
                    InitializationSource = WorkspaceInitializationSource.Unspecified,
                    PolicyRevision = 0,
                    PolicySchemaVersion = WorkspaceOperatingPolicySchema.CurrentVersion
                };
                _db.WorkspaceOperatingSettings.Add(settings);
                _db.SaveChanges();
            }

            var coordination = _db.WorkspacePolicyCoordinations
                .IgnoreQueryFilters()
                .Single(c => c.CompanyId == companyId);
            coordination.LastAcquiredAtUtc = DateTime.UtcNow;

            return settings;
        }

        private void EnsureCoordinationRow(int companyId)
        {
            if (_db.WorkspacePolicyCoordinations.IgnoreQueryFilters().Any(c => c.CompanyId == companyId))
                return;

            _db.WorkspacePolicyCoordinations.Add(new WorkspacePolicyCoordination
            {
                CompanyId = companyId,
                CreatedAtUtc = DateTime.UtcNow
            });
            _db.SaveChanges();
        }

        private void LockCoordinationRow(int companyId)
        {
            var provider = _db.Database.ProviderName ?? string.Empty;
            var connection = _db.Database.GetDbConnection();
            if (connection.State != ConnectionState.Open)
                connection.Open();

            using var command = connection.CreateCommand();
            command.Transaction = _db.Database.CurrentTransaction?.GetDbTransaction();

            if (provider.Contains("Npgsql", StringComparison.OrdinalIgnoreCase))
            {
                command.CommandText =
                    @"SELECT ""Id"" FROM ""WorkspacePolicyCoordination"" WHERE ""CompanyId"" = @companyId FOR UPDATE";
            }
            else if (provider.Contains("SqlServer", StringComparison.OrdinalIgnoreCase))
            {
                command.CommandText =
                    @"SELECT Id FROM WorkspacePolicyCoordination WITH (UPDLOCK, ROWLOCK) WHERE CompanyId = @companyId";
            }
            else
            {
                // SQLite: Serializable/IMMEDIATE already holds the write lock; a plain read is enough.
                command.CommandText =
                    @"SELECT ""Id"" FROM ""WorkspacePolicyCoordination"" WHERE ""CompanyId"" = @companyId";
            }

            var parameter = command.CreateParameter();
            parameter.ParameterName = "@companyId";
            parameter.Value = companyId;
            command.Parameters.Add(parameter);
            _ = command.ExecuteScalar();
        }
    }
}
