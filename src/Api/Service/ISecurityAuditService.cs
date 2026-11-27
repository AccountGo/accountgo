// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using System.Threading;
using System.Threading.Tasks;
using Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Api.Service
{
    public interface ISecurityAuditService
    {
        Task LogEventAsync(
            string eventType,
            string? userId,
            int? companyId,
            string? ipAddress,
            string? userAgent,
            object? metadata = null);

        /// <summary>
        /// Records a security event using IP, user agent and correlation ID from the current request.
        /// </summary>
        Task LogEventAsync(
            string eventType,
            string? outcome,
            string? userId,
            int? companyId,
            object? metadata = null);

        /// <summary>
        /// Adds a security event to the shared <see cref="ApplicationIdentityDbContext"/> without saving
        /// and returns the staged entity. The event is persisted only by a later <c>SaveChangesAsync</c>
        /// on that context; the caller must save before returning success and must detach the returned
        /// entity on a failure path.
        /// </summary>
        SecurityAuditEvent StageEvent(
            string eventType,
            string? outcome,
            string? userId,
            int? companyId,
            string? ipAddress,
            string? userAgent,
            object? metadata = null);
    }

    /// <summary>
    /// Writes a security event inside the caller's open database transaction, so the event and the change it records
    /// commit or roll back together. Unlike <see cref="ISecurityAuditService"/>, a failure to write throws.
    /// </summary>
    public interface ITransactionalSecurityAudit
    {
        Task LogEventInTransactionAsync(
            DbContext owner,
            string eventType,
            string? outcome,
            string? userId,
            int? companyId,
            object? metadata,
            CancellationToken cancellationToken);
    }
}
