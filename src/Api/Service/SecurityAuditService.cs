// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using System;
using System.Text.Json;
using System.Threading.Tasks;
using Api.Data;
using Microsoft.Extensions.Logging;

namespace Api.Service
{
    public class SecurityAuditService : ISecurityAuditService
    {
        private readonly ApplicationIdentityDbContext _identityContext;
        private readonly ILogger<SecurityAuditService> _logger;

        public SecurityAuditService(
            ApplicationIdentityDbContext identityContext,
            ILogger<SecurityAuditService> logger)
        {
            _identityContext = identityContext;
            _logger = logger;
        }

        public async Task LogEventAsync(
            string eventType,
            string? userId,
            int? companyId,
            string? ipAddress,
            string? userAgent,
            object? metadata = null)
        {
            string? metadataJson = null;
            if (metadata != null)
            {
                try
                {
                    metadataJson = JsonSerializer.Serialize(metadata);
                }
                catch
                {
                    metadataJson = metadata.ToString();
                }
            }

            _logger.LogInformation(
                "[SecurityAudit] eventType={EventType} userId={UserId} companyId={CompanyId} ip={IpAddress} ua={UserAgent} metadata={Metadata}",
                eventType, userId, companyId, ipAddress, userAgent, metadataJson);

            try
            {
                var auditEvent = new SecurityAuditEvent
                {
                    EventType = eventType,
                    UserId = userId,
                    CompanyId = companyId,
                    IpAddress = ipAddress,
                    UserAgent = userAgent,
                    MetadataJson = metadataJson,
                    CreatedAt = DateTime.UtcNow
                };

                _identityContext.SecurityAuditEvents.Add(auditEvent);
                await _identityContext.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to persist security audit event {EventType} for user {UserId}", eventType, userId);
            }
        }
    }
}
