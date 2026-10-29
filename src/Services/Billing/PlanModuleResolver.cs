// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Core.Domain;
using Dto.Subscription;

namespace Services.Billing;

/// <summary>
/// Active modules for a workspace: catalog <see cref="PlanDefinitionDto.IncludedModules"/>
/// minus owner opt-outs (DisabledModulesJson, with ActiveModulesJson fallback).
/// </summary>
public static class PlanModuleResolver
{
    public static List<string> ResolveActiveModules(IPlanCatalog catalog, Company company)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(company);
        return ResolveActiveModules(catalog.ResolveStoredPlan(company.Plan), company);
    }

    public static List<string> ResolveActiveModules(PlanDefinitionDto plan, Company company)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(company);

        var included = plan.IncludedModules ?? new List<string>();
        var required = plan.RequiredModules ?? new List<string>();

        if (company.DisabledModulesJson != null)
        {
            var disabled = ParseStringList(company.DisabledModulesJson) ?? new List<string>();
            return included
                .Where(m => IsRequired(required, m)
                    || !disabled.Contains(m, StringComparer.OrdinalIgnoreCase))
                .ToList();
        }

        // TODO(E5): remove ActiveModulesJson fallback once every row has DisabledModulesJson.
        if (!string.IsNullOrWhiteSpace(company.ActiveModulesJson))
        {
            var stored = ParseStringList(company.ActiveModulesJson);
            if (stored != null && stored.Count > 0)
            {
                return included
                    .Where(m => IsRequired(required, m)
                        || stored.Contains(m, StringComparer.OrdinalIgnoreCase))
                    .ToList();
            }
        }

        return included.ToList();
    }

    public static List<string> ResolveDisabledModules(IPlanCatalog catalog, Company company)
    {
        var plan = catalog.ResolveStoredPlan(company.Plan);
        var active = ResolveActiveModules(plan, company);
        return (plan.IncludedModules ?? new List<string>())
            .Where(m => !active.Contains(m, StringComparer.OrdinalIgnoreCase))
            .ToList();
    }

    private static bool IsRequired(IReadOnlyList<string> required, string moduleKey) =>
        required.Contains(moduleKey, StringComparer.OrdinalIgnoreCase);

    private static List<string>? ParseStringList(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json);
        }
        catch
        {
            return null;
        }
    }
}
