// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using Microsoft.AspNetCore.Builder;

namespace Api.Modules;

/// <summary>
/// Contract for pluggable commercial modules (e.g. AccountGo.Security, AccountGo.Billing)
/// composed ahead of time into AccountGo.Cloud.Host.
/// </summary>
public interface IAccountGoModule
{
    /// <summary>
    /// Module name logged at startup and exposed in /health (e.g. "security", "billing").
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Configure module-specific services and replace community defaults.
    /// </summary>
    void ConfigureServices(WebApplicationBuilder builder);

    /// <summary>
    /// Map module-specific endpoints.
    /// </summary>
    void MapEndpoints(WebApplication app);
}
