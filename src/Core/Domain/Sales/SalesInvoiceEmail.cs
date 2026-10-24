//-----------------------------------------------------------------------
// <copyright file="SalesInvoiceEmail.cs" company="AccountGo">
// Copyright (c) AccountGo. All rights reserved.
// <author>Marvin Perez</author>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Core.Domain.Sales
{
    /// <summary>
    /// One email sent to a customer about an invoice: the audit trail behind "Emailed to … on …". Written only after the
    /// provider accepted the message, so every row is an email that actually went out.
    /// </summary>
    [Table("SalesInvoiceEmail")]
    public class SalesInvoiceEmail : BaseEntity, ICompanyOwned
    {
        /// <summary>The invoice itself, sent when issued or again later.</summary>
        public const string InvoiceKind = "Invoice";
        /// <summary>A polite reminder that the invoice is still unpaid.</summary>
        public const string FollowUpKind = "FollowUp";

        public int CompanyId { get; set; }
        public int SalesInvoiceHeaderId { get; set; }
        /// <summary>What was sent; <see cref="InvoiceKind"/> or <see cref="FollowUpKind"/>.</summary>
        [MaxLength(20)]
        public string Kind { get; set; } = InvoiceKind;
        [MaxLength(320)]
        public string ToEmail { get; set; } = "";
        [MaxLength(320)]
        public string? BccEmail { get; set; }
        [MaxLength(300)]
        public string Subject { get; set; } = "";
        public DateTime SentAt { get; set; }
        [MaxLength(450)]
        public string? SentByUserId { get; set; }
        [MaxLength(320)]
        public string? SentByEmail { get; set; }
        /// <summary>The email provider's id for the message, for tracing delivery questions.</summary>
        [MaxLength(100)]
        public string? ProviderMessageId { get; set; }
    }
}
