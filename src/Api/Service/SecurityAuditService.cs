// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Api.Auditing;
using Api.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace Api.Service
{
    public class SecurityAuditService : ISecurityAuditService, ITransactionalSecurityAudit
    {
        private readonly ApplicationIdentityDbContext _identityContext;
        private readonly ILogger<SecurityAuditService> _logger;
        private readonly IRequestAuditContext? _auditContext;
        private readonly IHttpContextAccessor? _httpContextAccessor;

        public SecurityAuditService(
            ApplicationIdentityDbContext identityContext,
            ILogger<SecurityAuditService> logger,
            IRequestAuditContext? auditContext = null,
            IHttpContextAccessor? httpContextAccessor = null)
        {
            _identityContext = identityContext;
            _logger = logger;
            _auditContext = auditContext;
            _httpContextAccessor = httpContextAccessor;
        }

        public Task LogEventAsync(
            string eventType,
            string? userId,
            int? companyId,
            string? ipAddress,
            string? userAgent,
            object? metadata = null) =>
            PersistAsync(
                eventType,
                outcome: null,
                userId,
                companyId,
                ipAddress,
                userAgent,
                _auditContext?.CorrelationId,
                metadata);

        public Task LogEventAsync(
            string eventType,
            string? outcome,
            string? userId,
            int? companyId,
            object? metadata = null)
        {
            var (ip, userAgent) = RequestOrigin();
            return PersistAsync(
                eventType,
                outcome,
                userId,
                companyId,
                ip,
                userAgent,
                _auditContext?.CorrelationId,
                metadata);
        }

        /// <summary>
        /// Joins the identity context to <paramref name="owner"/>'s connection and open transaction (both contexts use the
        /// same database) and saves the event there. Throws when it can't be written, so the caller's change rolls back.
        /// </summary>
        public async Task LogEventInTransactionAsync(
            DbContext owner,
            string eventType,
            string? outcome,
            string? userId,
            int? companyId,
            object? metadata,
            CancellationToken cancellationToken)
        {
            var transaction = owner.Database.CurrentTransaction
                ?? throw new InvalidOperationException("A transactional audit event needs the caller's open transaction.");
            var connection = owner.Database.GetDbConnection();
            var identityConnection = _identityContext.Database.GetDbConnection();
            if (!ReferenceEquals(identityConnection, connection))
            {
                if (identityConnection.State != ConnectionState.Closed)
                    await identityConnection.CloseAsync().ConfigureAwait(false);
                _identityContext.Database.SetDbConnection(connection);
            }

            var (ip, userAgent) = RequestOrigin();
            var auditEvent = new SecurityAuditEvent
            {
                EventType = eventType,
                Outcome = outcome,
                UserId = userId,
                CompanyId = companyId,
                IpAddress = ip,
                UserAgent = userAgent,
                CorrelationId = _auditContext?.CorrelationId,
                MetadataJson = SecurityAuditMetadata.Serialize(metadata),
                CreatedAt = DateTime.UtcNow
            };
            _logger.LogInformation(
                "[SecurityAudit] eventType={EventType} outcome={Outcome} correlationId={CorrelationId}",
                eventType, outcome, auditEvent.CorrelationId);

            await _identityContext.Database.UseTransactionAsync(transaction.GetDbTransaction(), cancellationToken).ConfigureAwait(false);
            var entry = _identityContext.SecurityAuditEvents.Add(auditEvent);
            try
            {
                await _identityContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                // A failed event must not linger in the tracker: a later best-effort save in this request would record a
                // change that rolled back. A saved one is detached too; the caller's transaction decides if it survives.
                entry.State = EntityState.Detached;
                await _identityContext.Database.UseTransactionAsync(null, CancellationToken.None).ConfigureAwait(false);
            }
        }

        private (string? Ip, string? UserAgent) RequestOrigin()
        {
            var http = _httpContextAccessor?.HttpContext;
            var ip = _auditContext?.IpAddress ?? http?.Connection.RemoteIpAddress?.ToString();
            var userAgent = http?.Request.Headers.UserAgent.ToString();
            return (ip, string.IsNullOrWhiteSpace(userAgent) ? null : userAgent);
        }

        private async Task PersistAsync(
            string eventType,
            string? outcome,
            string? userId,
            int? companyId,
            string? ipAddress,
            string? userAgent,
            string? correlationId,
            object? metadata)
        {
            string? metadataJson = null;
            try
            {
                metadataJson = SecurityAuditMetadata.Serialize(metadata);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to serialize security audit metadata for {EventType}", eventType);
            }

            // Text logs keep only type/outcome/request id — IP, user agent, user id and metadata stay in the DB row.
            _logger.LogInformation(
                "[SecurityAudit] eventType={EventType} outcome={Outcome} correlationId={CorrelationId}",
                eventType, outcome, correlationId);

            try
            {
                _identityContext.SecurityAuditEvents.Add(new SecurityAuditEvent
                {
                    EventType = eventType,
                    Outcome = outcome,
                    UserId = userId,
                    CompanyId = companyId,
                    IpAddress = ipAddress,
                    UserAgent = userAgent,
                    CorrelationId = correlationId,
                    MetadataJson = metadataJson,
                    CreatedAt = DateTime.UtcNow
                });
                await _identityContext.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to persist security audit event {EventType} for user {UserId}", eventType, userId);
            }
        }
    }
}
