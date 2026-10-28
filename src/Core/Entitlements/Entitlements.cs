// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Core.Tenancy;

namespace Core.Entitlements
{
    /// <summary>Things a plan tier can limit. Monthly resources are metered as usage events; the rest are counted live.</summary>
    public enum MeteredResource
    {
        SalesDocuments,   // sales invoices + quotes created per calendar month (UTC)
        Bills,            // bills (purchase invoices) created per calendar month (UTC)
        Seats,            // active members + pending invitations in a workspace
        BankAccounts,     // bank / cash accounts in a workspace
        OwnedWorkspaces   // active workspaces a user owns
    }

    public static class MeteredResources
    {
        public static bool IsMonthly(MeteredResource resource) =>
            resource is MeteredResource.SalesDocuments or MeteredResource.Bills;

        /// <summary>Stable wire / storage name, e.g. "salesDocuments".</summary>
        public static string Key(MeteredResource resource)
        {
            var name = resource.ToString();
            return char.ToLowerInvariant(name[0]) + name.Substring(1);
        }
    }

    public static class CompanyTiers
    {
        public const string Free = "free";   // also what a null Tier means
        public const string Pro = "pro";     // paid (future): no limits
        public const string Comp = "comp";   // managed manually by ops: no limits, any plan

        public static string Normalize(string? tier) =>
            string.IsNullOrWhiteSpace(tier) ? Free : tier.Trim().ToLowerInvariant();
    }

    /// <summary>
    /// Free-tier configuration. Absent configuration = disabled: every plan is self-service and nothing is limited
    /// (self-hosted builds). Hosted values come from the deployment environment, not from source control.
    /// </summary>
    public class EntitlementsOptions
    {
        public bool Enabled { get; set; }
        /// <summary>Plans users may pick themselves. Empty = all known plans.</summary>
        public List<string> SelfServicePlans { get; set; } = new();
        /// <summary>Free-tier limits; a missing resource is unlimited.</summary>
        public Dictionary<MeteredResource, int> FreeLimits { get; set; } = new();
        /// <summary>Shown to users when a limit is reached.</summary>
        public string? SupportEmail { get; set; }
        /// <summary>
        /// When true (default), module gates also call <see cref="IEntitlementService.Evaluate"/> and log
        /// divergences; the old path still decides the HTTP outcome (E1a shadow mode).
        /// </summary>
        public bool EngineShadow { get; set; } = true;

        public bool IsSelfServicePlan(string? plan) =>
            !Enabled || SelfServicePlans.Count == 0
            || (plan != null && SelfServicePlans.Contains(plan.Trim(), StringComparer.OrdinalIgnoreCase));
    }

    public sealed record ResourceUsage(MeteredResource Resource, int? Limit, int Used, bool Monthly);

    public sealed record EntitlementSnapshot(
        bool Enabled,
        string Plan,
        string Tier,
        bool Limited,
        IReadOnlyList<string> Modules,
        IReadOnlyList<string> SelfServicePlans,
        IReadOnlyList<ResourceUsage> Usage,
        string? SupportEmail,
        IReadOnlySet<string>? Features = null)
    {
        public IReadOnlySet<string> Features { get; init; } = Features ?? System.Collections.Immutable.ImmutableHashSet<string>.Empty;
        public bool HasFeature(string feature) => Features.Contains(feature);
    }

    /// <summary>Gate request for <see cref="IEntitlementService.Evaluate"/>. Feature/Create arrive in E2.</summary>
    public abstract record EntitlementAction
    {
        private EntitlementAction() { }

        /// <summary>Any-of module keys (same semantics as <c>[RequireModule]</c>).</summary>
        public sealed record Module(params string[] AnyOf) : EntitlementAction
        {
            public Module(IReadOnlyList<string> anyOf) : this(anyOf?.ToArray() ?? Array.Empty<string>()) { }
        }
    }

    /// <summary>Allow/deny result from <see cref="IEntitlementService.Evaluate"/> (no throw).</summary>
    public sealed record EntitlementDecision(
        bool Allowed,
        string? Code,
        string? Detail,
        string? RequiredModule = null,
        string? RequiredFeature = null,
        MeteredResource? Resource = null,
        int? Limit = null,
        int? Used = null,
        string? Period = null,
        bool UpgradeAvailable = false);

    /// <summary>Central entitlement gate: what a workspace may use and how much of it.</summary>
    public interface IEntitlementService
    {
        EntitlementsOptions Options { get; }

        /// <summary>Plan, tier, modules and usage for the workspace.</summary>
        EntitlementSnapshot Get(int companyId);

        /// <summary>
        /// Policy decision without throwing (E1a: Module actions; Feature/Create in E2).
        /// Default throws so out-of-repo implementers (accountgo-commercial) keep compiling until they opt in.
        /// </summary>
        EntitlementDecision Evaluate(int companyId, EntitlementAction action) =>
            throw new NotSupportedException(
                "IEntitlementService.Evaluate is not implemented by this entitlement service.");

        /// <summary>
        /// Drops the per-request policy memo for <paramref name="companyId"/> after Plan / modules / status writes.
        /// Default is a no-op for implementers that do not memoize.
        /// </summary>
        void ClearPolicyMemo(int companyId) { }

        /// <summary>Checks whether the workspace is entitled to the given feature key (e.g. "mfa").</summary>
        bool HasFeature(int companyId, string feature) => Get(companyId).HasFeature(feature);

        /// <summary>Throws <see cref="QuotaExceededException"/> when creating <paramref name="count"/> more would pass the limit.
        /// <paramref name="count"/> 0 checks that the workspace is not already over it.</summary>
        void EnsureCanCreate(int companyId, MeteredResource resource, int count = 1);

        /// <summary>Same as <see cref="EnsureCanCreate(int, MeteredResource, int)"/> for the active workspace.</summary>
        void EnsureCanCreate(MeteredResource resource, int count = 1);

        /// <summary>Records one creation of a monthly resource in the active workspace.</summary>
        void Record(MeteredResource resource, int? entityId);

        /// <summary>Throws <see cref="QuotaExceededException"/> when the user may not own another workspace.</summary>
        void EnsureCanOwnAnotherWorkspace(string userId);

        /// <summary>Throws <see cref="PlanNotAvailableException"/> when the plan cannot be self-selected.</summary>
        void EnsureSelfServicePlan(string plan);

        /// <summary>Suspends limits for the current scope (demo seeding, ops tooling). Dispose to restore.</summary>
        IDisposable Bypass();
    }

    /// <summary>Base for entitlement denials. Derives from <see cref="TenantException"/> so the existing
    /// "rethrow tenant errors" catch blocks in controllers let it reach the exception handler.</summary>
    public abstract class EntitlementException : TenantException
    {
        protected EntitlementException(string message) : base(message) { }
    }

    public class QuotaExceededException : EntitlementException
    {
        public MeteredResource Resource { get; }
        public int Limit { get; }
        public int Used { get; }
        public bool Monthly { get; }

        public QuotaExceededException(MeteredResource resource, int limit, int used)
            : base(Describe(resource, limit))
        {
            Resource = resource;
            Limit = limit;
            Used = used;
            Monthly = MeteredResources.IsMonthly(resource);
        }

        private static string Describe(MeteredResource resource, int limit) => resource switch
        {
            MeteredResource.SalesDocuments => $"The Free plan includes {limit} invoices and quotes per month.",
            MeteredResource.Bills => $"The Free plan includes {limit} bills per month.",
            MeteredResource.Seats => limit == 1
                ? "The Free plan includes 1 user per workspace."
                : $"The Free plan includes {limit} users per workspace.",
            MeteredResource.BankAccounts => $"The Free plan includes {limit} bank and cash accounts.",
            MeteredResource.OwnedWorkspaces => limit == 1
                ? "The Free plan includes 1 workspace per owner."
                : $"The Free plan includes {limit} workspaces per owner.",
            _ => "This workspace has reached a Free plan limit."
        };
    }

    public class PlanNotAvailableException : EntitlementException
    {
        public string Plan { get; }

        public PlanNotAvailableException(string plan)
            : base($"The {plan} plan is not available to choose here.")
        {
            Plan = plan;
        }
    }
}
