// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Dto.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace Api.PublicIds
{
    /// <summary>
    /// Stateless Newtonsoft contract resolver that attaches <see cref="PublicIdJsonConverter"/> and
    /// <see cref="PublicIdByTypeObjectConverter"/> to DTO properties carrying <see cref="PublicIdAttribute"/>
    /// or <see cref="PublicIdByTypeAttribute"/>. Resolves <see cref="IPublicIdResolver"/> per request via <see cref="IHttpContextAccessor"/>.
    /// </summary>
    public sealed class PublicIdContractResolver : CamelCasePropertyNamesContractResolver
    {
        private readonly IHttpContextAccessor _httpContextAccessor;

        public PublicIdContractResolver(IHttpContextAccessor httpContextAccessor)
        {
            _httpContextAccessor = httpContextAccessor;
        }

        protected override JsonProperty CreateProperty(MemberInfo member, MemberSerialization memberSerialization)
        {
            var property = base.CreateProperty(member, memberSerialization);

            var attr = member.GetCustomAttribute<PublicIdAttribute>(inherit: true);
            if (attr != null && property.PropertyType != null && IsSupportedIntOrIntCollection(property.PropertyType))
            {
                var converter = new PublicIdJsonConverter(attr.Prefix, property.PropertyType, _httpContextAccessor);
                property.Converter = converter;
            }

            return property;
        }

        protected override JsonObjectContract CreateObjectContract(Type objectType)
        {
            var contract = base.CreateObjectContract(objectType);
            if (HasPolymorphicPublicIdProperty(objectType))
            {
                contract.Converter = new PublicIdByTypeObjectConverter(objectType, _httpContextAccessor, this);
            }
            return contract;
        }

        internal static bool HasPolymorphicPublicIdProperty(Type objectType) =>
            objectType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Any(p => p.GetCustomAttribute<PublicIdByTypeAttribute>(inherit: true) != null);

        internal static bool IsSupportedIntOrIntCollection(Type type)
        {
            if (type == typeof(int) || type == typeof(int?))
                return true;
            if (type == typeof(int[]))
                return true;
            if (type.IsGenericType)
            {
                var def = type.GetGenericTypeDefinition();
                var arg = type.GetGenericArguments()[0];
                if (arg == typeof(int) && (
                    def == typeof(List<>)
                    || def == typeof(IList<>)
                    || def == typeof(ICollection<>)
                    || def == typeof(IReadOnlyList<>)
                    || def == typeof(IReadOnlyCollection<>)
                    || def == typeof(IEnumerable<>)))
                {
                    return true;
                }
            }
            return false;
        }
    }

    internal sealed class PublicIdJsonConverter : JsonConverter
    {
        private readonly string _prefix;
        private readonly Type _targetType;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly bool _isCollection;
        private readonly bool _isNullableInt;

        public PublicIdJsonConverter(string prefix, Type targetType, IHttpContextAccessor httpContextAccessor)
        {
            _prefix = prefix;
            _targetType = targetType;
            _httpContextAccessor = httpContextAccessor;
            _isNullableInt = targetType == typeof(int?);
            _isCollection = targetType != typeof(int) && targetType != typeof(int?);
        }

        public override bool CanConvert(Type objectType) => true;

        private (IPublicIdResolver Resolver, HttpContext HttpContext) RequireResolver()
        {
            var httpContext = _httpContextAccessor.HttpContext
                ?? throw new InvalidOperationException(
                    "PublicIdJsonConverter requires an active HttpContext. Persistence code must not use the MVC Newtonsoft serializer settings.");
            var resolver = httpContext.RequestServices.GetRequiredService<IPublicIdResolver>();
            return (resolver, httpContext);
        }

        public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
        {
            if (value == null)
            {
                writer.WriteNull();
                return;
            }

            var (resolver, _) = RequireResolver();

            if (!_isCollection)
            {
                var intVal = Convert.ToInt32(value);
                if (intVal <= 0)
                {
                    writer.WriteNull();
                    return;
                }
                var publicId = resolver.ToPublicOrThrow(_prefix, intVal);
                writer.WriteValue(publicId);
                return;
            }

            writer.WriteStartArray();
            foreach (var item in (IEnumerable)value)
            {
                if (item == null)
                {
                    writer.WriteNull();
                    continue;
                }
                var intVal = Convert.ToInt32(item);
                if (intVal <= 0)
                {
                    writer.WriteNull();
                    continue;
                }
                var publicId = resolver.ToPublicOrThrow(_prefix, intVal);
                writer.WriteValue(publicId);
            }
            writer.WriteEndArray();
        }

        public override object? ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
        {
            var (resolver, httpContext) = RequireResolver();

            if (!_isCollection)
            {
                var resolved = ReadSingleId(reader, resolver, httpContext);
                if (_isNullableInt)
                    return resolved;
                return resolved ?? 0;
            }

            if (reader.TokenType == JsonToken.Null)
                return null;

            if (reader.TokenType != JsonToken.StartArray)
            {
                var msg = $"IDs are strings like '{_prefix}_…' (expected array of strings).";
                PublicIdErrorRecorder.Record(httpContext, PublicIdErrorCodes.InvalidId, msg, reader.Path);
                throw new PublicIdSerializationException(PublicIdErrorCodes.InvalidId, msg);
            }

            var list = new List<int>();
            while (reader.Read() && reader.TokenType != JsonToken.EndArray)
            {
                var resolved = ReadSingleId(reader, resolver, httpContext);
                if (resolved.HasValue)
                    list.Add(resolved.Value);
            }

            if (_targetType == typeof(int[]))
                return list.ToArray();
            return list;
        }

        private int? ReadSingleId(JsonReader reader, IPublicIdResolver resolver, HttpContext httpContext)
        {
            if (reader.TokenType == JsonToken.Null)
                return null;

            if (reader.TokenType is JsonToken.Integer or JsonToken.Float)
            {
                var msg = $"IDs are strings like '{_prefix}_…'.";
                PublicIdErrorRecorder.Record(httpContext, PublicIdErrorCodes.InvalidId, msg, reader.Path);
                throw new PublicIdSerializationException(PublicIdErrorCodes.InvalidId, msg);
            }

            if (reader.TokenType != JsonToken.String)
            {
                var msg = $"IDs are strings like '{_prefix}_…'.";
                PublicIdErrorRecorder.Record(httpContext, PublicIdErrorCodes.InvalidId, msg, reader.Path);
                throw new PublicIdSerializationException(PublicIdErrorCodes.InvalidId, msg);
            }

            var raw = reader.Value as string;
            if (string.IsNullOrEmpty(raw))
                return null;

            var lookup = resolver.Resolve(raw, _prefix);
            if (lookup.Ok)
                return lookup.Id;

            if (lookup.Error == PublicIdLookupError.NotFound)
            {
                PublicIdErrorRecorder.Record(httpContext, PublicIdErrorCodes.UnknownId, PublicIdErrorCodes.NotFoundMessage, reader.Path);
                throw new PublicIdSerializationException(PublicIdErrorCodes.UnknownId, PublicIdErrorCodes.NotFoundMessage);
            }

            var invalidMsg = lookup.Error == PublicIdLookupError.WrongKind
                ? $"Expected a '{_prefix}_…' ID."
                : $"IDs are strings like '{_prefix}_…'.";
            PublicIdErrorRecorder.Record(httpContext, PublicIdErrorCodes.InvalidId, invalidMsg, reader.Path);
            throw new PublicIdSerializationException(PublicIdErrorCodes.InvalidId, invalidMsg);
        }
    }

    /// <summary>
    /// Object-level converter attached only to DTO classes/records containing at least one <see cref="PublicIdByTypeAttribute"/> property.
    /// Allows reading sibling discriminator properties regardless of JSON property order.
    /// </summary>
    internal sealed class PublicIdByTypeObjectConverter : JsonConverter
    {
        private readonly Type _objectType;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly PublicIdContractResolver _resolver;

        public PublicIdByTypeObjectConverter(Type objectType, IHttpContextAccessor httpContextAccessor, PublicIdContractResolver resolver)
        {
            _objectType = objectType;
            _httpContextAccessor = httpContextAccessor;
            _resolver = resolver;
        }

        public override bool CanConvert(Type objectType) => objectType == _objectType;

        private (IPublicIdResolver Resolver, HttpContext HttpContext) RequireResolver()
        {
            var httpContext = _httpContextAccessor.HttpContext
                ?? throw new InvalidOperationException(
                    "PublicIdByTypeObjectConverter requires an active HttpContext. Persistence code must not use the MVC Newtonsoft serializer settings.");
            var resolver = httpContext.RequestServices.GetRequiredService<IPublicIdResolver>();
            return (resolver, httpContext);
        }

        public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
        {
            if (value == null)
            {
                writer.WriteNull();
                return;
            }

            var (pubResolver, _) = RequireResolver();
            var props = _objectType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanRead && p.GetCustomAttribute<JsonIgnoreAttribute>() == null)
                .ToList();

            writer.WriteStartObject();
            foreach (var prop in props)
            {
                var jsonPropName = prop.GetCustomAttribute<JsonPropertyAttribute>()?.PropertyName
                    ?? _resolver.GetResolvedPropertyName(prop.Name);
                writer.WritePropertyName(jsonPropName);

                var byType = prop.GetCustomAttribute<PublicIdByTypeAttribute>(inherit: true);
                if (byType != null)
                {
                    var rawVal = prop.GetValue(value);
                    if (rawVal == null)
                    {
                        writer.WriteNull();
                        continue;
                    }
                    var intVal = Convert.ToInt32(rawVal);
                    if (intVal <= 0)
                    {
                        writer.WriteNull();
                        continue;
                    }
                    var discProp = _objectType.GetProperty(byType.TypePropertyName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                    var discVal = discProp?.GetValue(value);
                    var publicId = ResolvePolymorphicOutbound(pubResolver, byType.Kind, discVal, intVal);
                    writer.WriteValue(publicId);
                    continue;
                }

                var fixedAttr = prop.GetCustomAttribute<PublicIdAttribute>(inherit: true);
                if (fixedAttr != null && PublicIdContractResolver.IsSupportedIntOrIntCollection(prop.PropertyType))
                {
                    var conv = new PublicIdJsonConverter(fixedAttr.Prefix, prop.PropertyType, _httpContextAccessor);
                    conv.WriteJson(writer, prop.GetValue(value), serializer);
                    continue;
                }

                serializer.Serialize(writer, prop.GetValue(value));
            }
            writer.WriteEndObject();
        }

        public override object? ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null)
                return null;

            var (pubResolver, httpContext) = RequireResolver();
            var jObj = JObject.Load(reader);
            var props = _objectType.GetProperties(BindingFlags.Public | BindingFlags.Instance);

            // Resolve every property's value from jObj
            var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

            // First pass: non-[PublicIdByType] properties so discriminators are available
            foreach (var prop in props)
            {
                if (prop.GetCustomAttribute<PublicIdByTypeAttribute>(inherit: true) != null)
                    continue;

                var jsonPropName = prop.GetCustomAttribute<JsonPropertyAttribute>()?.PropertyName
                    ?? _resolver.GetResolvedPropertyName(prop.Name);
                if (!jObj.TryGetValue(jsonPropName, StringComparison.OrdinalIgnoreCase, out var token))
                    continue;

                var fixedAttr = prop.GetCustomAttribute<PublicIdAttribute>(inherit: true);
                if (fixedAttr != null && PublicIdContractResolver.IsSupportedIntOrIntCollection(prop.PropertyType))
                {
                    var conv = new PublicIdJsonConverter(fixedAttr.Prefix, prop.PropertyType, _httpContextAccessor);
                    using var subReader = token.CreateReader();
                    subReader.Read();
                    values[prop.Name] = conv.ReadJson(subReader, prop.PropertyType, null, serializer);
                }
                else
                {
                    values[prop.Name] = token.ToObject(prop.PropertyType, serializer);
                }
            }

            // Second pass: [PublicIdByType] properties using resolved discriminator values
            foreach (var prop in props)
            {
                var byType = prop.GetCustomAttribute<PublicIdByTypeAttribute>(inherit: true);
                if (byType == null)
                    continue;

                var jsonPropName = prop.GetCustomAttribute<JsonPropertyAttribute>()?.PropertyName
                    ?? _resolver.GetResolvedPropertyName(prop.Name);
                if (!jObj.TryGetValue(jsonPropName, StringComparison.OrdinalIgnoreCase, out var token))
                    continue;

                if (token.Type == JTokenType.Null)
                {
                    values[prop.Name] = prop.PropertyType == typeof(int?) ? null : 0;
                    continue;
                }

                if (token.Type is JTokenType.Integer or JTokenType.Float || token.Type != JTokenType.String)
                {
                    var msg = "IDs are strings like 'inv_…'.";
                    PublicIdErrorRecorder.Record(httpContext, PublicIdErrorCodes.InvalidId, msg, jsonPropName);
                    throw new PublicIdSerializationException(PublicIdErrorCodes.InvalidId, msg);
                }

                var raw = token.Value<string>();
                if (string.IsNullOrEmpty(raw))
                {
                    values[prop.Name] = prop.PropertyType == typeof(int?) ? null : 0;
                    continue;
                }

                values.TryGetValue(byType.TypePropertyName, out var discVal);
                var lookup = ResolvePolymorphicInbound(pubResolver, byType.Kind, discVal, raw);
                if (lookup.Ok)
                {
                    values[prop.Name] = prop.PropertyType == typeof(int?) ? (int?)lookup.Id : lookup.Id;
                }
                else if (lookup.Error == PublicIdLookupError.NotFound)
                {
                    PublicIdErrorRecorder.Record(httpContext, PublicIdErrorCodes.UnknownId, PublicIdErrorCodes.NotFoundMessage, jsonPropName);
                    throw new PublicIdSerializationException(PublicIdErrorCodes.UnknownId, PublicIdErrorCodes.NotFoundMessage);
                }
                else
                {
                    var msg = lookup.ExpectedPrefix != null
                        ? $"Expected a '{lookup.ExpectedPrefix}_…' ID."
                        : "IDs are strings like 'inv_…'.";
                    PublicIdErrorRecorder.Record(httpContext, PublicIdErrorCodes.InvalidId, msg, jsonPropName);
                    throw new PublicIdSerializationException(PublicIdErrorCodes.InvalidId, msg);
                }
            }

            // Instantiate via primary constructor (for positional records) or parameterless constructor
            var ctors = _objectType.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
            var defaultCtor = ctors.FirstOrDefault(c => c.GetParameters().Length == 0);
            if (defaultCtor != null)
            {
                var instance = defaultCtor.Invoke(null);
                foreach (var prop in props)
                {
                    if (prop.CanWrite && values.TryGetValue(prop.Name, out var val))
                        prop.SetValue(instance, val);
                }
                return instance;
            }

            var bestCtor = ctors.OrderByDescending(c => c.GetParameters().Length).First();
            var ctorParams = bestCtor.GetParameters();
            var args = new object?[ctorParams.Length];
            for (var i = 0; i < ctorParams.Length; i++)
            {
                var p = ctorParams[i];
                if (p.Name != null && values.TryGetValue(p.Name, out var val))
                    args[i] = val;
                else if (p.HasDefaultValue)
                    args[i] = p.DefaultValue;
                else if (p.ParameterType.IsValueType)
                    args[i] = Activator.CreateInstance(p.ParameterType);
            }
            return bestCtor.Invoke(args);
        }

        internal static string? ResolvePolymorphicOutbound(
            IPublicIdResolver resolver,
            PublicIdTypeDiscriminatorKind kind,
            object? discVal,
            int id)
        {
            if (id <= 0 || discVal == null)
                return null;

            switch (kind)
            {
                case PublicIdTypeDiscriminatorKind.SettlementSourceType:
                {
                    var prefix = PublicIdRegistry.PrefixForSettlementSourceType(Convert.ToInt32(discVal));
                    return prefix == null ? null : resolver.ToPublicOrThrow(prefix, id);
                }
                case PublicIdTypeDiscriminatorKind.SettlementDestinationType:
                {
                    var prefix = PublicIdRegistry.PrefixForSettlementDestinationType(Convert.ToInt32(discVal));
                    return prefix == null ? null : resolver.ToPublicOrThrow(prefix, id);
                }
                default:
                    return resolver.ToPublicPolymorphic(discVal.ToString(), id);
            }
        }

        private static PublicIdLookupResult ResolvePolymorphicInbound(
            IPublicIdResolver resolver,
            PublicIdTypeDiscriminatorKind kind,
            object? discVal,
            string publicId)
        {
            if (discVal == null)
                return PublicIdLookupResult.Fail(PublicIdLookupError.Malformed);

            switch (kind)
            {
                case PublicIdTypeDiscriminatorKind.SettlementSourceType:
                {
                    var prefix = PublicIdRegistry.PrefixForSettlementSourceType(Convert.ToInt32(discVal));
                    return prefix == null
                        ? PublicIdLookupResult.Fail(PublicIdLookupError.Malformed)
                        : resolver.Resolve(publicId, prefix);
                }
                case PublicIdTypeDiscriminatorKind.SettlementDestinationType:
                {
                    var prefix = PublicIdRegistry.PrefixForSettlementDestinationType(Convert.ToInt32(discVal));
                    return prefix == null
                        ? PublicIdLookupResult.Fail(PublicIdLookupError.Malformed)
                        : resolver.Resolve(publicId, prefix);
                }
                default:
                    return resolver.ResolvePolymorphic(publicId, discVal.ToString());
            }
        }
    }
}
