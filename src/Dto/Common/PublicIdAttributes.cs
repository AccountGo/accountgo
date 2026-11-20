// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using System;

namespace Dto.Common
{
    /// <summary>
    /// Marks an integer DTO property or controller parameter as a prefixed public ID at the wire boundary.
    /// C# code keeps <c>int</c>/<c>int?</c>/<c>IEnumerable&lt;int&gt;</c>; JSON and route/query binding translate to/from <c>{prefix}_{body}</c>.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter | AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
    public sealed class PublicIdAttribute : Attribute
    {
        public string Prefix { get; }

        public PublicIdAttribute(string prefix)
        {
            if (string.IsNullOrWhiteSpace(prefix))
                throw new ArgumentException("PublicId prefix is required.", nameof(prefix));

            Prefix = prefix;
        }
    }

    public enum PublicIdTypeDiscriminatorKind
    {
        /// <summary>Sibling property holds an entity/document type name (e.g. "SalesInvoice", "SalesInvoiceHeader", "BankTransaction").</summary>
        TypeName = 0,
        /// <summary>Sibling property holds a SettlementSourceType integer/enum (1=CustomerCreditNote, 2=SupplierCreditNote, 3=SupplierAdvance).</summary>
        SettlementSourceType = 1,
        /// <summary>Sibling property holds a SettlementDestinationType integer/enum (1=SalesInvoice, 2=PurchaseInvoice).</summary>
        SettlementDestinationType = 2,
    }

    /// <summary>
    /// Marks a polymorphic integer reference whose public ID prefix is determined by a sibling type property.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter, AllowMultiple = false, Inherited = true)]
    public sealed class PublicIdByTypeAttribute : Attribute
    {
        public string TypePropertyName { get; }
        public PublicIdTypeDiscriminatorKind Kind { get; }

        public PublicIdByTypeAttribute(string typePropertyName, PublicIdTypeDiscriminatorKind kind = PublicIdTypeDiscriminatorKind.TypeName)
        {
            if (string.IsNullOrWhiteSpace(typePropertyName))
                throw new ArgumentException("TypePropertyName is required.", nameof(typePropertyName));

            TypePropertyName = typePropertyName;
            Kind = kind;
        }
    }
}
