// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using System.Threading.Tasks;

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
    }
}
