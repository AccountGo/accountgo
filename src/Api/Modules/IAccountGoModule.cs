// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using Microsoft.AspNetCore.Builder;

namespace Api.Modules;

/// <summary>
/// Contract for pluggable modules composed into the API host at startup.
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
