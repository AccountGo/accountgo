// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Threading.Tasks;
using Api.Auditing;
using Api.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Api.Service
{
    public class SecurityAuditService : ISecurityAuditService
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
            var http = _httpContextAccessor?.HttpContext;
            var ip = _auditContext?.IpAddress ?? http?.Connection.RemoteIpAddress?.ToString();
            var userAgent = http?.Request.Headers.UserAgent.ToString();
            if (string.IsNullOrWhiteSpace(userAgent))
                userAgent = null;
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

            _logger.LogInformation(
                "[SecurityAudit] eventType={EventType} outcome={Outcome} userId={UserId} companyId={CompanyId} ip={IpAddress} ua={UserAgent} correlationId={CorrelationId} metadata={Metadata}",
                eventType, outcome, userId, companyId, ipAddress, userAgent, correlationId, metadataJson);

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
