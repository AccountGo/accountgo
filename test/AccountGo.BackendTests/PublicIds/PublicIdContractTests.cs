// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Api.Data;
using Api.PublicIds;
using Dto;
using Dto.Common;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Xunit;

namespace AccountGo.BackendTests.PublicIds;

public sealed class PublicIdContractTests
{
    private static bool IsIdLikePropertyName(string name) =>
        string.Equals(name, "Id", StringComparison.Ordinal)
        || name.EndsWith("Id", StringComparison.Ordinal)
        || name.EndsWith("Ids", StringComparison.Ordinal);

    private static bool IsIdLikeParamName(string name) =>
        string.Equals(name, "id", StringComparison.OrdinalIgnoreCase)
        || name.EndsWith("Id", StringComparison.Ordinal)
        || name.EndsWith("Ids", StringComparison.Ordinal);

    private static int InheritanceDepth(Type? t)
    {
        var d = 0;
        while (t != null) { d++; t = t.BaseType; }
        return d;
    }

    private static bool IsIntOrIntCollection(Type type) =>
        PublicIdContractResolver.IsSupportedIntOrIntCollection(type);

    [Fact]
    public void DtoContract_AllIntIdPropertiesAreAnnotatedOrExemptedWithReason()
    {
        var violations = new List<string>();
        var types = new HashSet<Type>();

        // 1. All public non-abstract types in Dto assembly
        foreach (var t in typeof(BaseDto).Assembly.GetTypes().Where(t => t.IsClass && !t.IsAbstract && t.IsPublic))
        {
            if (typeof(Attribute).IsAssignableFrom(t)) continue;
            types.Add(t);
        }

        // 2. RecurringSchedule DTOs in Services.Recurring
        foreach (var t in typeof(Services.Recurring.RecurringScheduleInput).Assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && t.IsPublic && t.Namespace == "Services.Recurring"
                && !t.Name.EndsWith("Service", StringComparison.Ordinal)
                && !typeof(Exception).IsAssignableFrom(t)))
        {
            types.Add(t);
        }

        // 3. Nested request/response DTO classes/records inside Api.Controllers
        var apiAssembly = typeof(ApiDbContext).Assembly;
        foreach (var ctrl in apiAssembly.GetTypes().Where(t => t.IsClass && !t.IsAbstract && typeof(ControllerBase).IsAssignableFrom(t)))
        {
            foreach (var nested in ctrl.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (nested.IsClass && !nested.IsAbstract && !nested.Name.StartsWith("<", StringComparison.Ordinal))
                    types.Add(nested);
            }
            foreach (var m in ctrl.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                foreach (var p in m.GetParameters())
                {
                    var pt = p.ParameterType;
                    if (pt.IsClass && !pt.IsAbstract && pt.Assembly == apiAssembly
                        && !pt.Name.EndsWith("Service", StringComparison.Ordinal)
                        && !typeof(ControllerBase).IsAssignableFrom(pt)
                        && !typeof(Microsoft.EntityFrameworkCore.DbContext).IsAssignableFrom(pt))
                        types.Add(pt);
                }
            }
        }

        foreach (var type in types.OrderBy(t => t.FullName, StringComparer.Ordinal))
        {
            // When a derived type hides a base property with `new`, only inspect the most-derived declaration per property name.
            var props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .GroupBy(p => p.Name, StringComparer.Ordinal)
                .Select(g => g.OrderByDescending(p => InheritanceDepth(p.DeclaringType)).First());

            foreach (var prop in props)
            {
                if (!IsIdLikePropertyName(prop.Name))
                    continue;
                if (!IsIntOrIntCollection(prop.PropertyType))
                    continue;

                var hasFixed = prop.GetCustomAttribute<PublicIdAttribute>(inherit: true) != null;
                var hasPoly = prop.GetCustomAttribute<PublicIdByTypeAttribute>(inherit: true) != null;
                if (hasFixed || hasPoly)
                    continue;

                var key = $"{type.FullName}.{prop.Name}";
                if (PublicIdExemptions.DtoProperties.TryGetValue(key, out var reason) && !string.IsNullOrWhiteSpace(reason))
                    continue;

                violations.Add($"{key} ({prop.PropertyType.Name})");
            }
        }

        Assert.True(
            violations.Count == 0,
            $"Unannotated int *Id DTO properties ({violations.Count}):\n" + string.Join("\n", violations));
    }

    [Fact]
    public void RouteContract_NoIntRouteConstraintsAndAllIdParamsAnnotatedOrExempted()
    {
        var routeViolations = new List<string>();
        var paramViolations = new List<string>();
        var intConstraintRegex = new Regex(@"\{[^}]*:int\b[^}]*\}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        var controllers = typeof(ApiDbContext).Assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && typeof(ControllerBase).IsAssignableFrom(t))
            .OrderBy(t => t.Name, StringComparer.Ordinal);

        foreach (var ctrl in controllers)
        {
            foreach (var attr in ctrl.GetCustomAttributes<RouteAttribute>(inherit: true))
            {
                if (attr.Template != null && intConstraintRegex.IsMatch(attr.Template))
                    routeViolations.Add($"{ctrl.Name} [Route(\"{attr.Template}\")]");
            }

            foreach (var method in ctrl.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                var httpAttrs = method.GetCustomAttributes<HttpMethodAttribute>(inherit: true).ToList();
                var routeAttrs = method.GetCustomAttributes<RouteAttribute>(inherit: true).ToList();
                if (httpAttrs.Count == 0 && routeAttrs.Count == 0)
                    continue;

                foreach (var ha in httpAttrs)
                {
                    if (ha.Template != null && intConstraintRegex.IsMatch(ha.Template))
                        routeViolations.Add($"{ctrl.Name}.{method.Name} [{ha.GetType().Name}(\"{ha.Template}\")]");
                }
                foreach (var ra in routeAttrs)
                {
                    if (ra.Template != null && intConstraintRegex.IsMatch(ra.Template))
                        routeViolations.Add($"{ctrl.Name}.{method.Name} [Route(\"{ra.Template}\")]");
                }

                foreach (var param in method.GetParameters())
                {
                    if (param.GetCustomAttribute<FromServicesAttribute>() != null)
                        continue;
                    if (param.GetCustomAttribute<FromBodyAttribute>() != null)
                        continue;
                    if (!IsIntOrIntCollection(param.ParameterType))
                        continue;
                    if (!IsIdLikeParamName(param.Name ?? string.Empty))
                        continue;

                    var hasAttr = param.GetCustomAttribute<PublicIdAttribute>(inherit: true) != null
                        || param.GetCustomAttribute<PublicIdByTypeAttribute>(inherit: true) != null;
                    if (hasAttr)
                        continue;

                    var key = $"{ctrl.Name}.{method.Name}.{param.Name}";
                    if (PublicIdExemptions.ActionParameters.TryGetValue(key, out var reason) && !string.IsNullOrWhiteSpace(reason))
                        continue;

                    paramViolations.Add($"{key} ({param.ParameterType.Name})");
                }
            }
        }

        Assert.True(
            routeViolations.Count == 0,
            $"Found :int route constraints ({routeViolations.Count}):\n" + string.Join("\n", routeViolations));

        Assert.True(
            paramViolations.Count == 0,
            $"Unannotated int *Id action parameters ({paramViolations.Count}):\n" + string.Join("\n", paramViolations));
    }
}
