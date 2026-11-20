// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Dto.Common;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Api.PublicIds
{
    /// <summary>
    /// Global result filter that walks outgoing <see cref="ObjectResult"/> graphs, collects all
    /// <see cref="PublicIdAttribute"/> and <see cref="PublicIdByTypeAttribute"/> integer values,
    /// and prefetches their public IDs in one query per prefix before JSON serialization runs.
    /// </summary>
    public sealed class PublicIdPrefetchFilter : IAsyncResultFilter
    {
        private const int MaxDepth = 16;

        private sealed record FixedPropPlan(PropertyInfo Property, string Prefix, bool IsCollection);
        private sealed record PolyPropPlan(PropertyInfo Property, PropertyInfo? DiscriminatorProperty, PublicIdTypeDiscriminatorKind Kind);
        private sealed record TypeWalkPlan(
            FixedPropPlan[] FixedProps,
            PolyPropPlan[] PolyProps,
            PropertyInfo[] ChildProps);

        private static readonly ConcurrentDictionary<Type, TypeWalkPlan?> PlanCache = new();

        private readonly IPublicIdResolver _resolver;
        private readonly PublicIdRegistry _registry;

        public PublicIdPrefetchFilter(IPublicIdResolver resolver, PublicIdRegistry registry)
        {
            _resolver = resolver;
            _registry = registry;
        }

        public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
        {
            if (context.Result is ObjectResult { Value: not null } objectResult)
            {
                await PrefetchObjectGraphAsync(objectResult.Value, _resolver, _registry, context.HttpContext.RequestAborted);
            }

            await next();
        }

        public static async Task PrefetchObjectGraphAsync(
            object? root,
            IPublicIdResolver resolver,
            PublicIdRegistry registry,
            CancellationToken cancellationToken = default)
        {
            if (root == null)
                return;

            var collected = new List<(string Prefix, int Id)>();
            var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
            Collect(root, registry, collected, visited, depth: 0);

            if (collected.Count > 0)
            {
                await resolver.PrefetchAsync(collected, cancellationToken);
            }
        }

        private static void Collect(
            object? node,
            PublicIdRegistry registry,
            List<(string Prefix, int Id)> collected,
            HashSet<object> visited,
            int depth)
        {
            if (node == null || depth > MaxDepth)
                return;

            var type = node.GetType();
            if (IsScalarType(type))
                return;

            if (!type.IsValueType && !visited.Add(node))
                return;

            if (node is IDictionary dict)
            {
                foreach (DictionaryEntry entry in dict)
                    Collect(entry.Value, registry, collected, visited, depth + 1);
                return;
            }

            if (node is IEnumerable enumerable)
            {
                foreach (var item in enumerable)
                    Collect(item, registry, collected, visited, depth + 1);
                return;
            }

            var plan = PlanCache.GetOrAdd(type, BuildPlan);
            if (plan == null)
                return;

            foreach (var fp in plan.FixedProps)
            {
                var raw = fp.Property.GetValue(node);
                if (raw == null) continue;
                if (!fp.IsCollection)
                {
                    var id = Convert.ToInt32(raw);
                    if (id > 0) collected.Add((fp.Prefix, id));
                }
                else if (raw is IEnumerable list)
                {
                    foreach (var item in list)
                    {
                        if (item == null) continue;
                        var id = Convert.ToInt32(item);
                        if (id > 0) collected.Add((fp.Prefix, id));
                    }
                }
            }

            foreach (var pp in plan.PolyProps)
            {
                var raw = pp.Property.GetValue(node);
                if (raw == null || pp.DiscriminatorProperty == null) continue;
                var id = Convert.ToInt32(raw);
                if (id <= 0) continue;
                var discVal = pp.DiscriminatorProperty.GetValue(node);
                if (discVal == null) continue;

                string? prefix = pp.Kind switch
                {
                    PublicIdTypeDiscriminatorKind.SettlementSourceType =>
                        PublicIdRegistry.PrefixForSettlementSourceType(Convert.ToInt32(discVal)),
                    PublicIdTypeDiscriminatorKind.SettlementDestinationType =>
                        PublicIdRegistry.PrefixForSettlementDestinationType(Convert.ToInt32(discVal)),
                    PublicIdTypeDiscriminatorKind.SettlementDestinationFromSourceType =>
                        (discVal.ToString() ?? string.Empty).Equals("CustomerCreditNote", StringComparison.OrdinalIgnoreCase) || discVal.ToString() == "1" ? "inv" : "bill",
                    _ => registry.TryGetPrefixForTypeName(discVal.ToString(), out var mapped) ? mapped : null
                };

                if (!string.IsNullOrEmpty(prefix))
                    collected.Add((prefix, id));
            }

            foreach (var child in plan.ChildProps)
            {
                var childVal = child.GetValue(node);
                if (childVal != null)
                    Collect(childVal, registry, collected, visited, depth + 1);
            }
        }

        private static bool IsScalarType(Type type)
        {
            var underlying = Nullable.GetUnderlyingType(type) ?? type;
            return underlying.IsPrimitive
                || underlying.IsEnum
                || underlying == typeof(string)
                || underlying == typeof(decimal)
                || underlying == typeof(DateTime)
                || underlying == typeof(DateTimeOffset)
                || underlying == typeof(DateOnly)
                || underlying == typeof(TimeOnly)
                || underlying == typeof(TimeSpan)
                || underlying == typeof(Guid)
                || underlying == typeof(Uri)
                || underlying == typeof(byte[]);
        }

        private static TypeWalkPlan? BuildPlan(Type type)
        {
            if (IsScalarType(type))
                return null;

            // Only walk application/DTO/anonymous types, not framework internals (Stream, JsonElement, etc.)
            var ns = type.Namespace ?? string.Empty;
            var isAnonymous = type.IsDefined(typeof(CompilerGeneratedAttribute), false) && type.Name.Contains("AnonymousType", StringComparison.Ordinal);
            var isAppType = isAnonymous
                || ns.StartsWith("Dto", StringComparison.Ordinal)
                || ns.StartsWith("Api", StringComparison.Ordinal)
                || ns.StartsWith("Services", StringComparison.Ordinal)
                || ns.StartsWith("Core", StringComparison.Ordinal)
                || ns.StartsWith("AccountGo", StringComparison.Ordinal);

            if (!isAppType)
                return null;

            var fixedProps = new List<FixedPropPlan>();
            var polyProps = new List<PolyPropPlan>();
            var childProps = new List<PropertyInfo>();

            var props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanRead && p.GetIndexParameters().Length == 0)
                .ToArray();

            foreach (var prop in props)
            {
                var fixedAttr = prop.GetCustomAttribute<PublicIdAttribute>(inherit: true);
                if (fixedAttr != null && PublicIdContractResolver.IsSupportedIntOrIntCollection(prop.PropertyType))
                {
                    var isCol = prop.PropertyType != typeof(int) && prop.PropertyType != typeof(int?);
                    fixedProps.Add(new FixedPropPlan(prop, fixedAttr.Prefix, isCol));
                    continue;
                }

                var polyAttr = prop.GetCustomAttribute<PublicIdByTypeAttribute>(inherit: true);
                if (polyAttr != null)
                {
                    var disc = props.FirstOrDefault(p => string.Equals(p.Name, polyAttr.TypePropertyName, StringComparison.OrdinalIgnoreCase));
                    polyProps.Add(new PolyPropPlan(prop, disc, polyAttr.Kind));
                    continue;
                }

                if (!IsScalarType(prop.PropertyType))
                {
                    childProps.Add(prop);
                }
            }

            if (fixedProps.Count == 0 && polyProps.Count == 0 && childProps.Count == 0)
                return null;

            return new TypeWalkPlan(fixedProps.ToArray(), polyProps.ToArray(), childProps.ToArray());
        }
    }
}
