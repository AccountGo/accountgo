// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using Core.Domain;
using Core.Domain.Security;
using Core.Entitlements;
using Core.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Api.Filters
{
    /// <summary>
    /// Single builder for entitlement ProblemDetails so RequireModule, RequireFeature, and
    /// <see cref="MultiTenancyExceptionHandler"/> cannot drift. Existing extension keys and type URIs are preserved;
    /// new keys are additive only.
    /// </summary>
    public static class EntitlementProblemFactory
    {
        public static bool CanManageBilling(ITenantContext? tenant) =>
            tenant is { IsResolved: true }
            && tenant.Permissions is { } permissions
            && permissions.Contains(SystemPermissions.AdminSubscriptionManage);

        public static ProblemDetails FromDecision(
            EntitlementDecision decision,
            ITenantContext? tenant,
            string? supportEmail = null,
            IReadOnlyList<string>? requiredModules = null)
        {
            ArgumentNullException.ThrowIfNull(decision);
            var code = decision.Code ?? "MODULE_NOT_SUBSCRIBED";
            var problem = Base(code, decision.Detail, decision.UpgradeAvailable, tenant, supportEmail);
            problem.Extensions["requiredModule"] = decision.RequiredModule;
            if (requiredModules is { Count: > 0 })
                problem.Extensions["requiredModules"] = requiredModules;
            problem.Extensions["requiredFeature"] = decision.RequiredFeature;
            problem.Extensions["effectivePlan"] = decision.EffectivePlan;
            // Additive: never collide with ProblemDetails.Status. Legacy "status" only for inactive.
            problem.Extensions["subscriptionStatus"] = decision.SubscriptionStatus;
            if (string.Equals(code, "SUBSCRIPTION_INACTIVE", StringComparison.Ordinal))
                problem.Extensions["status"] = decision.SubscriptionStatus;
            if (decision.Resource is { } resource)
            {
                problem.Extensions["resource"] = MeteredResources.Key(resource);
                problem.Extensions["limit"] = decision.Limit;
                problem.Extensions["used"] = decision.Used;
                problem.Extensions["period"] = decision.Period ?? MeteredResources.PeriodOf(resource);
                problem.Extensions["monthly"] = MeteredResources.IsMonthly(resource);
            }
            else if (decision.Period != null)
            {
                problem.Extensions["period"] = decision.Period;
            }

            return problem;
        }

        public static ProblemDetails FromException(
            EntitlementException exception,
            ITenantContext? tenant,
            string? supportEmail = null)
        {
            ArgumentNullException.ThrowIfNull(exception);
            var problem = Base(exception.Code, exception.Message, exception.UpgradeAvailable, tenant, supportEmail);
            problem.Extensions["requiredModule"] = exception.RequiredModule;
            problem.Extensions["requiredFeature"] = exception.RequiredFeature;
            problem.Extensions["effectivePlan"] = exception.EffectivePlan;
            problem.Extensions["subscriptionStatus"] = exception.SubscriptionStatus;
            if (string.Equals(exception.Code, "SUBSCRIPTION_INACTIVE", StringComparison.Ordinal))
                problem.Extensions["status"] = exception.SubscriptionStatus;

            if (exception is QuotaExceededException quota)
            {
                problem.Extensions["resource"] = MeteredResources.Key(quota.Resource);
                problem.Extensions["limit"] = quota.Limit;
                problem.Extensions["used"] = quota.Used;
                problem.Extensions["period"] = MeteredResources.PeriodOf(quota.Resource);
                problem.Extensions["monthly"] = quota.Monthly;
            }
            else if (exception is PlanNotAvailableException planEx)
            {
                problem.Extensions["plan"] = planEx.Plan;
            }

            return problem;
        }

        private static ProblemDetails Base(
            string code,
            string? detail,
            bool upgradeAvailable,
            ITenantContext? tenant,
            string? supportEmail)
        {
            var (type, title) = Describe(code);
            var problem = new ProblemDetails
            {
                Type = type,
                Title = title,
                Detail = detail,
                Status = StatusCodes.Status403Forbidden
            };
            problem.Extensions["code"] = code;
            problem.Extensions["upgradeAvailable"] = upgradeAvailable;
            problem.Extensions["canManageBilling"] = CanManageBilling(tenant);
            problem.Extensions["contact"] = supportEmail;
            return problem;
        }

        private static (string Type, string Title) Describe(string code) => code switch
        {
            "QUOTA_EXCEEDED" => ("https://accountgo.dev/errors/quota-exceeded", "Plan limit reached"),
            "PLAN_NOT_AVAILABLE" => ("https://accountgo.dev/errors/plan-not-available", "Plan not available"),
            "MODULE_NOT_SUBSCRIBED" => ("https://accountgo.ph/errors/module-not-subscribed", "Module Not Subscribed"),
            "SUBSCRIPTION_INACTIVE" => ("https://accountgo.ph/errors/subscription-inactive", "Subscription Inactive"),
            "FEATURE_NOT_ENTITLED" => ("https://accountgo.dev/errors/feature-not-entitled", "Feature not entitled"),
            _ => ("https://accountgo.dev/errors/entitlement-denied", "Not entitled")
        };
    }
}
