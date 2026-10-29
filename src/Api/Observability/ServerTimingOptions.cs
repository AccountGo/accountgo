// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using Microsoft.Extensions.Configuration;

namespace Api.Observability;

public static class ServerTimingOptions
{
    public const string EnvKey = "SERVER_TIMING_ENABLED";

    /// <summary>Default on when unset. Explicit false/0/off disables all hops.</summary>
    public static bool IsEnabled(IConfiguration? configuration = null)
    {
        var raw = Environment.GetEnvironmentVariable(EnvKey)
            ?? configuration?[EnvKey];
        if (string.IsNullOrWhiteSpace(raw))
            return true;
        return raw.Trim() switch
        {
            "0" or "false" or "FALSE" or "no" or "NO" or "off" or "OFF" => false,
            _ => true
        };
    }
}
