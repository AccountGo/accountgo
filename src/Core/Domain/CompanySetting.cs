//-----------------------------------------------------------------------
// <copyright file="CompanySetting.cs" company="AccountGo">
// Copyright (c) AccountGo. All rights reserved.
// <author>Marvin Perez</author>
// <date>1/11/2015 9:48:38 AM</date>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System.ComponentModel.DataAnnotations.Schema;

namespace Core.Domain
{
    [Table("CompanySetting")]
    public partial class CompanySetting : BaseEntity, ICompanyOwned
    {
        public int CompanyId { get; set; }
        public Company Company { get; set; } = null!;
        public string Currency { get; set; } = "USD";

        /// <summary>Registered or personal name; Company.Name is the trading name.</summary>
        public string? LegalName { get; set; }
        public string? BusinessEmail { get; set; }
        public string? BusinessPhone { get; set; }
        public PostalAddress? BusinessAddress { get; set; }

        public string? SalesInvoiceSettingsJson { get; set; }

        public SalesInvoiceSettings GetSalesInvoiceSettings() => SalesInvoiceSettings.FromJson(SalesInvoiceSettingsJson);

        public void SetSalesInvoiceSettings(SalesInvoiceSettings settings) => SalesInvoiceSettingsJson = settings.ToJson();
    }
}
