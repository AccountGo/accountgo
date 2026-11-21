// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dto.Common;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Metadata;
using Microsoft.Extensions.DependencyInjection;

namespace Api.PublicIds
{
    public sealed class PublicIdModelBinderProvider : IModelBinderProvider
    {
        public IModelBinder? GetBinder(ModelBinderProviderContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            if (context.Metadata is not DefaultModelMetadata meta)
                return null;

            var attr = meta.Attributes.ParameterAttributes?.OfType<PublicIdAttribute>().FirstOrDefault()
                ?? meta.Attributes.PropertyAttributes?.OfType<PublicIdAttribute>().FirstOrDefault();

            if (attr == null)
                return null;

            var modelType = context.Metadata.ModelType;
            if (!PublicIdContractResolver.IsSupportedIntOrIntCollection(modelType))
                return null;

            return new PublicIdModelBinder(attr.Prefix, modelType);
        }
    }

    public sealed class PublicIdModelBinder : IModelBinder
    {
        private readonly string _prefix;
        private readonly Type _modelType;
        private readonly bool _isNullableInt;
        private readonly bool _isCollection;

        public PublicIdModelBinder(string prefix, Type modelType)
        {
            _prefix = prefix;
            _modelType = modelType;
            _isNullableInt = modelType == typeof(int?);
            _isCollection = modelType != typeof(int) && modelType != typeof(int?);
        }

        public async Task BindModelAsync(ModelBindingContext bindingContext)
        {
            var valueProviderResult = bindingContext.ValueProvider.GetValue(bindingContext.ModelName);
            if (valueProviderResult == ValueProviderResult.None)
            {
                if (_isNullableInt)
                {
                    bindingContext.Result = ModelBindingResult.Success(null);
                }
                else if (_isCollection)
                {
                    bindingContext.Result = ModelBindingResult.Success(_modelType == typeof(int[]) ? Array.Empty<int>() : new List<int>());
                }
                return;
            }

            bindingContext.ModelState.SetModelValue(bindingContext.ModelName, valueProviderResult);
            var httpContext = bindingContext.HttpContext;
            var resolver = httpContext.RequestServices.GetRequiredService<IPublicIdResolver>();

            if (!_isCollection)
            {
                var raw = valueProviderResult.FirstValue;
                if (string.IsNullOrWhiteSpace(raw))
                {
                    if (_isNullableInt)
                    {
                        bindingContext.Result = ModelBindingResult.Success(null);
                    }
                    else
                    {
                        var msg = $"IDs are strings like '{_prefix}_…'.";
                        PublicIdErrorRecorder.Record(httpContext, PublicIdErrorCodes.InvalidId, msg, bindingContext.ModelName);
                        bindingContext.ModelState.AddModelError(bindingContext.ModelName, msg);
                    }
                    return;
                }

                var lookup = await resolver.ResolveAsync(raw.Trim(), _prefix, httpContext.RequestAborted);
                if (lookup.Ok)
                {
                    bindingContext.Result = ModelBindingResult.Success(_isNullableInt ? (int?)lookup.Id : lookup.Id);
                    return;
                }

                RecordFailure(bindingContext, lookup);
                return;
            }

            var resolved = new List<int>();
            foreach (var rawEntry in valueProviderResult.Values)
            {
                if (string.IsNullOrWhiteSpace(rawEntry))
                    continue;

                foreach (var piece in rawEntry.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    var lookup = await resolver.ResolveAsync(piece, _prefix, httpContext.RequestAborted);
                    if (lookup.Ok)
                    {
                        resolved.Add(lookup.Id);
                    }
                    else
                    {
                        RecordFailure(bindingContext, lookup);
                        return;
                    }
                }
            }

            bindingContext.Result = ModelBindingResult.Success(_modelType == typeof(int[]) ? resolved.ToArray() : resolved);
        }

        private void RecordFailure(ModelBindingContext bindingContext, PublicIdLookupResult lookup)
        {
            var httpContext = bindingContext.HttpContext;
            if (lookup.Error == PublicIdLookupError.NotFound)
            {
                PublicIdErrorRecorder.Record(httpContext, PublicIdErrorCodes.UnknownId, PublicIdErrorCodes.NotFoundMessage, bindingContext.ModelName);
                bindingContext.ModelState.AddModelError(bindingContext.ModelName, PublicIdErrorCodes.NotFoundMessage);
                return;
            }

            var msg = lookup.Error == PublicIdLookupError.WrongKind
                ? $"Expected a '{_prefix}_…' ID."
                : $"IDs are strings like '{_prefix}_…'.";
            PublicIdErrorRecorder.Record(httpContext, PublicIdErrorCodes.InvalidId, msg, bindingContext.ModelName);
            bindingContext.ModelState.AddModelError(bindingContext.ModelName, msg);
        }
    }
}
