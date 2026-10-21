#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Core.Tenancy;
using Microsoft.AspNetCore.Mvc;

namespace Api.Filters
{
    public static class TenantEndpointValidator
    {
        public static void ValidateAllEndpoints(Assembly assembly)
        {
            var controllerTypes = assembly.GetTypes()
                .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
                .ToList();

            var errors = new List<string>();

            foreach (var controller in controllerTypes)
            {
                var actionMethods = controller.GetMethods(BindingFlags.Instance | BindingFlags.Public)
                    .Where(m => !m.IsSpecialName && m.DeclaringType == controller)
                    .ToList();

                var hasControllerScoped = controller.GetCustomAttributes(typeof(TenantScopedAttribute), true).Any();
                var hasControllerFree = controller.GetCustomAttributes(typeof(TenantFreeAttribute), true).Any();

                if (hasControllerScoped && hasControllerFree)
                {
                    errors.Add($"Controller '{controller.Name}' has both [TenantScoped] and [TenantFree].");
                }

                foreach (var method in actionMethods)
                {
                    var hasActionScoped = method.GetCustomAttributes(typeof(TenantScopedAttribute), true).Any();
                    var hasActionFree = method.GetCustomAttributes(typeof(TenantFreeAttribute), true).Any();

                    if (hasActionScoped && hasActionFree)
                    {
                        errors.Add($"Action '{controller.Name}.{method.Name}' has both [TenantScoped] and [TenantFree].");
                        continue;
                    }

                    bool resolved = (hasActionScoped || hasActionFree) || (hasControllerScoped || hasControllerFree);
                    if (!resolved)
                    {
                        errors.Add($"Action '{controller.Name}.{method.Name}' has neither [TenantScoped] nor [TenantFree].");
                    }
                }
            }

            if (errors.Count > 0)
            {
                throw new InvalidOperationException(
                    "Endpoint tenancy classification failed:\n" + string.Join("\n", errors));
            }
        }
    }
}
