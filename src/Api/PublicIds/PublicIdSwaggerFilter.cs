// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Linq;
using System.Reflection;
using Core.Domain;
using Dto.Common;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Api.PublicIds
{
    /// <summary>
    /// Renders <see cref="PublicIdAttribute"/> and <see cref="PublicIdByTypeAttribute"/> DTO properties
    /// as OpenAPI <c>string</c> fields with their prefix regex pattern.
    /// </summary>
    public sealed class PublicIdSwaggerSchemaFilter : ISchemaFilter
    {
        public void Apply(IOpenApiSchema schema, SchemaFilterContext context)
        {
            if (schema is not OpenApiSchema mutable)
                return;

            var member = context.MemberInfo;
            if (member == null)
                return;

            var fixedAttr = member.GetCustomAttribute<PublicIdAttribute>(inherit: true);
            if (fixedAttr != null)
            {
                ApplyStringSchema(mutable, $"^{fixedAttr.Prefix}_[0-9a-z]{{26}}$");
                return;
            }

            var polyAttr = member.GetCustomAttribute<PublicIdByTypeAttribute>(inherit: true);
            if (polyAttr != null)
            {
                ApplyStringSchema(mutable, PublicIdGenerator.FormatPattern);
            }
        }

        private static void ApplyStringSchema(OpenApiSchema schema, string pattern)
        {
            if (schema.Type == JsonSchemaType.Array && schema.Items is OpenApiSchema itemSchema)
            {
                itemSchema.Type = JsonSchemaType.String;
                itemSchema.Format = null;
                itemSchema.Pattern = pattern;
                return;
            }

            var isNullable = schema.Type.HasValue && schema.Type.Value.HasFlag(JsonSchemaType.Null);
            schema.Type = isNullable ? (JsonSchemaType.String | JsonSchemaType.Null) : JsonSchemaType.String;
            schema.Format = null;
            schema.Pattern = pattern;
        }
    }

    /// <summary>
    /// Renders <see cref="PublicIdAttribute"/> route and query parameters as OpenAPI <c>string</c> parameters.
    /// </summary>
    public sealed class PublicIdSwaggerOperationFilter : IOperationFilter
    {
        public void Apply(OpenApiOperation operation, OperationFilterContext context)
        {
            if (operation.Parameters == null || operation.Parameters.Count == 0)
                return;

            var methodParams = context.MethodInfo.GetParameters();
            foreach (var opParam in operation.Parameters)
            {
                if (opParam is not OpenApiParameter mutableParam)
                    continue;
                var match = methodParams.FirstOrDefault(p =>
                    string.Equals(p.Name, mutableParam.Name, StringComparison.OrdinalIgnoreCase));
                var attr = match?.GetCustomAttribute<PublicIdAttribute>(inherit: true);
                if (attr == null)
                    continue;

                if (mutableParam.Schema is OpenApiSchema paramSchema)
                {
                    paramSchema.Type = JsonSchemaType.String;
                    paramSchema.Format = null;
                    paramSchema.Pattern = $"^{attr.Prefix}_[0-9a-z]{{26}}$";
                }
            }
        }
    }
}
