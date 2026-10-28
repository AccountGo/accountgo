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
        public const string Free = "free";   // derived from a free plan key
        public const string Pro = "pro";     // derived from a paid plan key
        public const string Comp = "comp";   // ops override on Company.Tier

        public static string Normalize(string? tier) =>
            string.IsNullOrWhiteSpace(tier) ? Free : tier.Trim().ToLowerInvariant();

        /// <summary>Derive the API tier from effective plan, honouring an explicit <c>comp</c> override.</summary>
        public static string Derive(string? companyTier, string effectivePlan)
        {
            if (string.Equals(Normalize(companyTier), Comp, StringComparison.Ordinal))
                return Comp;
            var key = effectivePlan.Trim().ToLowerInvariant();
            if (key is "enterprise" || key.EndsWith("_pro", StringComparison.Ordinal))
                return Pro;
            return Free;
        }
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
        /// When true with <see cref="LegacyModuleGate"/>, module gates also call Evaluate and log divergences.
        /// Ignored when the engine owns the gate (LegacyModuleGate false).
        /// </summary>
        public bool EngineShadow { get; set; } = true;
        /// <summary>
        /// When true, RequireModule / IsModuleEnabled use SubscriptionAccess and optionally shadow-log (E1a).
        /// Default false: the engine decides and there is no shadow call.
        /// </summary>
        public bool LegacyModuleGate { get; set; }

        public bool IsSelfServicePlan(string? plan)
        {
            if (!Enabled || SelfServicePlans.Count == 0)
                return true;
            if (plan == null)
                return false;
            var normalized = Core.Domain.Company.NormalizePlanKey(plan);
            return SelfServicePlans.Any(p =>
                string.Equals(p.Trim(), plan.Trim(), StringComparison.OrdinalIgnoreCase)
                || string.Equals(Core.Domain.Company.NormalizePlanKey(p), normalized, StringComparison.OrdinalIgnoreCase));
        }
    }

    public sealed record ResourceUsage(
        MeteredResource Resource,
        int? Limit,
        int Used,
        bool Monthly,
        bool OverLimit = false);

    public sealed record EntitlementLifecycle(
        string? Status,
        DateTime? GraceEndsAt,
        bool CancelAtPeriodEnd,
        DateTime? PeriodEnd,
        string? ReducedBy);

    public sealed record EntitlementSnapshot(
        bool Enabled,
        string Plan,
        string Tier,
        bool Limited,
        IReadOnlyList<string> Modules,
        IReadOnlyList<string> SelfServicePlans,
        IReadOnlyList<ResourceUsage> Usage,
        string? SupportEmail,
        IReadOnlySet<string>? Features = null,
        string? EffectivePlan = null,
        IReadOnlyList<string>? ReadOnlyModules = null,
        EntitlementLifecycle? Lifecycle = null,
        IReadOnlyList<string>? AllowedTransitions = null,
        string? Version = null)
    {
        public IReadOnlySet<string> Features { get; init; } = Features ?? System.Collections.Immutable.ImmutableHashSet<string>.Empty;
        public string EffectivePlan { get; init; } = EffectivePlan ?? Plan;
        public IReadOnlyList<string> ReadOnlyModules { get; init; } = ReadOnlyModules ?? Array.Empty<string>();
        public IReadOnlyList<string> AllowedTransitions { get; init; } = AllowedTransitions ?? Array.Empty<string>();
        public bool HasFeature(string feature) => Features.Contains(feature);
    }

    /// <summary>Read vs write intent for module gates (E1b).</summary>
    public enum ModuleAccess
    {
        Read = 0,
        Write = 1
    }

    /// <summary>Gate request for <see cref="IEntitlementService.Evaluate"/>. Feature/Create arrive in E2.</summary>
    public abstract record EntitlementAction
    {
        private EntitlementAction() { }

        /// <summary>Any-of module keys (same semantics as <c>[RequireModule]</c>).</summary>
        public sealed record Module : EntitlementAction
        {
            public string[] AnyOf { get; }
            public ModuleAccess Access { get; }

            public Module(params string[] anyOf) : this(ModuleAccess.Write, anyOf) { }

            public Module(ModuleAccess access, params string[] anyOf)
            {
                Access = access;
                AnyOf = anyOf ?? Array.Empty<string>();
            }

            public Module(IReadOnlyList<string> anyOf, ModuleAccess access = ModuleAccess.Write)
                : this(access, anyOf?.ToArray() ?? Array.Empty<string>()) { }
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
        bool UpgradeAvailable = false,
        string? SubscriptionStatus = null);

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

        /// <summary>
        /// When policy was already resolved for this company in the current request, returns the policy Version
        /// (no extra Company load). Otherwise false.
        /// </summary>
        bool TryGetResolvedPolicyVersion(int companyId, out string version)
        {
            version = "";
            return false;
        }

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
