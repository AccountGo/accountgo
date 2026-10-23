//-----------------------------------------------------------------------
// <copyright file="CompanySetting.cs" company="AccountGo">
// Copyright (c) AccountGo. All rights reserved.
// <author>Marvin Perez</author>
// <date>1/11/2015 9:48:38 AM</date>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;

namespace Core.Domain
{
    [Table("CompanySetting")]
    public partial class CompanySetting : BaseEntity, ICompanyOwned
    {
        public int CompanyId { get; set; }
        public Company Company { get; set; }
        public string Currency { get; set; } = "USD";
        public string? LegalName { get; set; }
        public string? BusinessEmail { get; set; }
        public string? BusinessPhone { get; set; }
        public string? SalesInvoiceSettingsJson { get; set; }

        // Owned type: PostalAddress (BusinessAddress)
        public PostalAddress? BusinessAddress { get; set; }

        // Computed property: current sales invoice settings (from JSON or defaults)
        public SalesInvoiceSettings GetSalesInvoiceSettings()
        {
            if (!string.IsNullOrWhiteSpace(SalesInvoiceSettingsJson))
            {
                try
                {
                    return SalesInvoiceSettings.FromJson(SalesInvoiceSettingsJson);
                }
                catch
                {
                    // Malformed JSON - fall through to defaults
                }
            }
            return SalesInvoiceSettings.FromJson(null);
        }

        // Store settings back to JSON (trimming and null-whitespace handling)
        public void SetSalesInvoiceSettings(SalesInvoiceSettings settings)
        {
            SalesInvoiceSettingsJson = settings.ToJson();
        }
    }
}
