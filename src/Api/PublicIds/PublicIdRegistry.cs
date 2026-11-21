// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Api.Data;
using Core.Domain;
using Core.Domain.Financials;
using Core.Domain.Security;
using Core.Domain.TaxSystem;

namespace Api.PublicIds
{
    public enum PublicIdScopeRule
    {
        TenantOwned,
        CompanyMembership,
        CompanyOrSystemRole,
        CompanyScopedDirect,
        IdentityUserOwned
    }

    public sealed record PublicIdEntityRegistration(
        string Prefix,
        Type EntityType,
        PublicIdScopeRule ScopeRule,
        bool IsIdentityEntity);

    /// <summary>
    /// Authoritative singleton registry of all <see cref="IHasPublicId"/> entity prefixes and polymorphic type mappings.
    /// Built by reflection at startup; fails fast on duplicate, missing, or malformed prefixes.
    /// </summary>
    public sealed class PublicIdRegistry
    {
        private readonly Dictionary<string, PublicIdEntityRegistration> _byPrefix = new(StringComparer.Ordinal);
        private readonly Dictionary<Type, PublicIdEntityRegistration> _byType = new();
        private readonly Dictionary<string, string> _polymorphicTypeToPrefix = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _exemptTypeNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "CompanySetting",
            "GeneralLedgerSetting",
            // Legacy document types with no record table; nothing to point a public id at.
            "SalesDebitMemo",
            "SalesCreditMemo",
            "PurchaseDebitMemo",
            "PurchaseCreditMemo",
            "WorkspaceOperatingSettings",
            "WorkspacePolicyCoordination",
            "WorkspaceApprovalRule",
            "WorkspaceAccessRequest",
            "BooksLockChange",
            "PartnerShare",
            "TaxGroupTax",
            "ItemTaxGroupTax",
            "CompanyMemberSecurityRole",
            "SecurityRolePermission",
            "SecurityUserRole",
            "MainContraAccount",
            "YearEndCloseEvidence",
            "YearEndIssuanceDeclaration",
            "YearEndLifecycleEvent",
            "YearEndCommandReceipt",
            "SettlementCommandReceipt",
            "CompanyCloseState",
            "PersonalWorkspace",
            "WorkspaceLink",
            "SequenceNumber",
            "DocumentNumberSeries",
            "BankStatementColumnMapping",
            "EntitlementUsage",
            "RecordChange",
            "SecurityAuditEvent",
            "Log",
            "AccountClass",
            "SecurityPermission",
            "SecurityGroup",
            "User",
            "Address",
            "SalesDeliveryLine",
            "SettlementApplicationReversal",
            "SalesInvoiceNumbering",
        };

        public IReadOnlyDictionary<string, PublicIdEntityRegistration> ByPrefix => _byPrefix;
        public IReadOnlyDictionary<Type, PublicIdEntityRegistration> ByType => _byType;

        public PublicIdRegistry()
            : this(new[] { typeof(IHasPublicId).Assembly, typeof(ApiDbContext).Assembly })
        {
        }

        internal PublicIdRegistry(IEnumerable<Assembly> assemblies)
        {
            var types = assemblies
                .Distinct()
                .SelectMany(a => a.GetTypes())
                .Where(t => t.IsClass && !t.IsAbstract)
                .ToList();

            foreach (var type in types)
            {
                var hasInterface = typeof(IHasPublicId).IsAssignableFrom(type);
                var attr = type.GetCustomAttribute<PublicIdPrefixAttribute>(inherit: false);

                if (hasInterface && attr == null)
                    throw new InvalidOperationException($"Entity '{type.FullName}' implements IHasPublicId but is missing [PublicIdPrefix].");
                if (!hasInterface && attr != null)
                    throw new InvalidOperationException($"Entity '{type.FullName}' has [PublicIdPrefix(\"{attr.Prefix}\")] but does not implement IHasPublicId.");
                if (attr == null)
                    continue;

                if (!PublicIdGenerator.IsValidPrefix(attr.Prefix))
                    throw new InvalidOperationException($"Entity '{type.FullName}' has malformed public ID prefix '{attr.Prefix}'.");

                if (_byPrefix.TryGetValue(attr.Prefix, out var existing))
                    throw new InvalidOperationException($"Duplicate public ID prefix '{attr.Prefix}' on '{type.FullName}' and '{existing.EntityType.FullName}'.");

                var (scopeRule, isIdentity) = ClassifyScope(type);
                var reg = new PublicIdEntityRegistration(attr.Prefix, type, scopeRule, isIdentity);
                _byPrefix[attr.Prefix] = reg;
                _byType[type] = reg;
                _polymorphicTypeToPrefix[type.Name] = attr.Prefix;
            }

            RegisterPolymorphicAliases();
        }

        private static (PublicIdScopeRule Scope, bool IsIdentity) ClassifyScope(Type type)
        {
            if (type == typeof(RefreshTokenSession)
                || type == typeof(TrustedDevice)
                || type == typeof(UserWebAuthnCredential))
                return (PublicIdScopeRule.IdentityUserOwned, true);

            if (type == typeof(Company))
                return (PublicIdScopeRule.CompanyMembership, false);

            if (type == typeof(SecurityRole))
                return (PublicIdScopeRule.CompanyOrSystemRole, false);

            if (type == typeof(CompanyMember) || type == typeof(CompanyInvitation))
                return (PublicIdScopeRule.CompanyScopedDirect, false);

            if (typeof(ICompanyOwned).IsAssignableFrom(type))
                return (PublicIdScopeRule.TenantOwned, false);

            if (type == typeof(LinkedEntryPair))
                return (PublicIdScopeRule.CompanyScopedDirect, false);

            throw new InvalidOperationException(
                $"Entity '{type.FullName}' implements IHasPublicId but has no defined PublicIdScopeRule in PublicIdRegistry.");
        }

        private void RegisterPolymorphicAliases()
        {
            // DocumentTypes enum names and short web/controller link aliases
            AddAlias(nameof(DocumentTypes.SalesInvoice), "inv");
            AddAlias("invoice", "inv");
            // The tax report names a void by its document kind plus "Void"; the id is still the document's.
            AddAlias("invoiceVoid", "inv");
            AddAlias("invoicePayment", "inv");
            // Credit-note documents and the refunds paid against them (the report keys a refund row by its credit note).
            AddAlias("customerCredit", "cn");
            AddAlias("customerCreditVoid", "cn");
            AddAlias("customerRefund", "cn");
            AddAlias("supplierCredit", "scn");
            AddAlias("supplierCreditVoid", "scn");
            AddAlias("supplierRefund", "scn");
            AddAlias(nameof(DocumentTypes.SalesReceipt), "rcpt");
            AddAlias(nameof(DocumentTypes.SalesOrder), "so");
            AddAlias(nameof(DocumentTypes.SalesQuote), "quo");
            AddAlias(nameof(DocumentTypes.SalesDelivery), "del");
            AddAlias(nameof(DocumentTypes.PurchaseInvoice), "bill");
            AddAlias("bill", "bill");
            AddAlias("billVoid", "bill");
            AddAlias("billPayment", "bill");
            AddAlias(nameof(DocumentTypes.PurchaseOrder), "po");
            AddAlias(nameof(DocumentTypes.PurchaseReceipt), "prc");
            // PayBill records the bill id (not the vendor payment id) as the ledger source.
            AddAlias(nameof(DocumentTypes.PurchaseInvoicePayment), "bill");
            AddAlias(nameof(DocumentTypes.JournalEntry), "je");
            AddAlias(nameof(DocumentTypes.BankTransaction), "btx");
            AddAlias(nameof(DocumentTypes.CustomerAllocation), "alloc");
            AddAlias("DonationInvoice", "dinv");
            AddAlias(nameof(DocumentTypes.InventoryAdjustment), "iadj");
            AddAlias(nameof(DocumentTypes.CustomerCreditNote), "cn");
            AddAlias(nameof(DocumentTypes.SupplierCreditNote), "scn");
            AddAlias(nameof(DocumentTypes.SupplierAdvance), "adv");
            AddAlias(nameof(DocumentTypes.CustomerReturnReceipt), "crr");
            AddAlias(nameof(DocumentTypes.SupplierReturnDispatch), "srd");
            AddAlias(nameof(DocumentTypes.ReceivableWriteOff), "wo");
            AddAlias(nameof(DocumentTypes.WriteOffRecovery), "wor");
            AddAlias(nameof(DocumentTypes.PartyRefund), "rf");
            AddAlias(nameof(DocumentTypes.SettlementApplication), "sapp");

            // Numeric SettlementSourceType / SettlementDestinationType aliases are handled via named strings or enum conversion
            AddAlias("SettlementSourceType.CustomerCreditNote", "cn");
            AddAlias("SettlementSourceType.SupplierCreditNote", "scn");
            AddAlias("SettlementSourceType.SupplierAdvance", "adv");
            AddAlias("SettlementDestinationType.SalesInvoice", "inv");
            AddAlias("SettlementDestinationType.PurchaseInvoice", "bill");
            AddAlias("1", "inv"); // Not used directly; typed enum helper preferred below
            _polymorphicTypeToPrefix.Remove("1");
        }

        private void AddAlias(string alias, string prefix)
        {
            if (!_byPrefix.ContainsKey(prefix))
                throw new InvalidOperationException($"Polymorphic alias '{alias}' references unknown prefix '{prefix}'.");
            _polymorphicTypeToPrefix[alias] = prefix;
        }

        public bool TryGetByPrefix(string prefix, out PublicIdEntityRegistration registration) =>
            _byPrefix.TryGetValue(prefix, out registration!);

        public bool TryGetByType(Type entityType, out PublicIdEntityRegistration registration) =>
            _byType.TryGetValue(entityType, out registration!);

        public string GetPrefix(Type entityType) =>
            _byType.TryGetValue(entityType, out var reg)
                ? reg.Prefix
                : throw new KeyNotFoundException($"Entity type '{entityType.FullName}' is not registered in PublicIdRegistry.");

        public bool IsExemptTypeName(string? typeName) =>
            !string.IsNullOrWhiteSpace(typeName) && _exemptTypeNames.Contains(typeName.Trim());

        public bool TryGetPrefixForTypeName(string? typeName, out string prefix)
        {
            prefix = string.Empty;
            if (string.IsNullOrWhiteSpace(typeName))
                return false;
            return _polymorphicTypeToPrefix.TryGetValue(typeName.Trim(), out prefix!);
        }

        public static string? PrefixForSettlementSourceType(int sourceType) => (SettlementSourceType)sourceType switch
        {
            SettlementSourceType.CustomerCreditNote => "cn",
            SettlementSourceType.SupplierCreditNote => "scn",
            SettlementSourceType.SupplierAdvance => "adv",
            _ => null
        };

        public static string? PrefixForSettlementDestinationType(int destinationType) => (SettlementDestinationType)destinationType switch
        {
            SettlementDestinationType.SalesInvoice => "inv",
            SettlementDestinationType.PurchaseInvoice => "bill",
            _ => null
        };
    }
}
