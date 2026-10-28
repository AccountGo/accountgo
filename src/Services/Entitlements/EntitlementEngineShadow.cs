// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Core.Entitlements;
using Microsoft.Extensions.Logging;

namespace Services.Entitlements;

/// <summary>
/// E1a: compare the legacy module gate to <see cref="IEntitlementService.Evaluate"/> and log divergences.
/// Never throws into the request. Dedupes identical log keys for one hour per process.
/// </summary>
public static class EntitlementEngineShadow
{
    private static readonly TimeSpan DedupeWindow = TimeSpan.FromHours(1);
    private static readonly ConcurrentDictionary<string, long> Recent = new(StringComparer.Ordinal);

    public static void CompareModuleAllow(
        IEntitlementService? entitlements,
        ILogger? logger,
        int companyId,
        string moduleKey,
        bool oldAllowed)
    {
        if (entitlements == null || !entitlements.Options.EngineShadow)
            return;

        try
        {
            var decision = entitlements.Evaluate(companyId, new EntitlementAction.Module(moduleKey));
            if (decision.Allowed == oldAllowed)
                return;

            LogDivergence(logger, companyId, new[] { moduleKey }, oldAllowed, oldCode: null, decision);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex,
                "Entitlement engine shadow failed (companyId={CompanyId}, action=Module:{Module})",
                companyId, moduleKey);
        }
    }

    public static void CompareModuleGate(
        IEntitlementService? entitlements,
        ILogger? logger,
        int companyId,
        IReadOnlyList<string> moduleKeys,
        bool oldAllowed,
        string? oldCode)
    {
        if (entitlements == null || !entitlements.Options.EngineShadow)
            return;

        try
        {
            var decision = entitlements.Evaluate(companyId, new EntitlementAction.Module(moduleKeys.ToArray()));
            var codesMatch = string.Equals(decision.Code, oldCode, StringComparison.Ordinal);
            if (decision.Allowed == oldAllowed && codesMatch)
                return;

            LogDivergence(logger, companyId, moduleKeys, oldAllowed, oldCode, decision);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex,
                "Entitlement engine shadow failed (companyId={CompanyId}, action=Module:[{Keys}])",
                companyId, string.Join(',', moduleKeys));
        }
    }

    /// <summary>Error-level log, deduped 1h per process on <paramref name="dedupeKey"/>.</summary>
    public static void LogErrorOnce(ILogger? logger, string dedupeKey, string message, params object?[] args)
    {
        if (logger == null || !TryBeginEmit(dedupeKey))
            return;
        logger.LogError(message, args);
    }

    private static void LogDivergence(
        ILogger? logger,
        int companyId,
        IReadOnlyList<string> keys,
        bool oldAllowed,
        string? oldCode,
        EntitlementDecision decision)
    {
        if (logger == null)
            return;

        var keyList = string.Join(',', keys);
        var dedupeKey = $"{companyId}|{keyList}|{oldAllowed}|{oldCode}|{decision.Allowed}|{decision.Code}";
        if (!TryBeginEmit(dedupeKey))
            return;

        logger.LogWarning(
            "Entitlement engine shadow divergence: companyId={CompanyId}, keys=[{Keys}], oldAllow={OldAllow}, oldCode={OldCode}, newAllow={NewAllow}, newCode={NewCode}",
            companyId, keyList, oldAllowed, oldCode, decision.Allowed, decision.Code);
    }

    private static bool TryBeginEmit(string dedupeKey)
    {
        var nowTicks = DateTime.UtcNow.Ticks;
        Prune(nowTicks);
        if (Recent.TryAdd(dedupeKey, nowTicks))
            return true;

        if (Recent.TryGetValue(dedupeKey, out var previous) && nowTicks - previous < DedupeWindow.Ticks)
            return false;

        Recent[dedupeKey] = nowTicks;
        return true;
    }

    private static void Prune(long nowTicks)
    {
        if (Recent.Count < 256)
            return;
        foreach (var pair in Recent)
        {
            if (nowTicks - pair.Value >= DedupeWindow.Ticks)
                Recent.TryRemove(pair.Key, out _);
        }
    }
}
