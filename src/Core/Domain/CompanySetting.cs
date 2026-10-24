//-----------------------------------------------------------------------
// <copyright file="CompanySetting.cs" company="AccountGo">
// Copyright (c) AccountGo. All rights reserved.
// <author>Marvin Perez</author>
// <date>1/11/2015 9:48:38 AM</date>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace Core.Domain
{
    [Table("CompanySetting")]
    public partial class CompanySetting : BaseEntity, ICompanyOwned
    {
        public int CompanyId { get; set; }
        public Company Company { get; set; } = null!;
        public string Currency { get; set; } = "USD";
        public AccountingBasis AccountingBasis { get; set; } = AccountingBasis.Accrual;

        public ReportingFramework ReportingFramework { get; set; } = ReportingFramework.IfrsForSmes;
        public TaxRegime TaxRegime { get; set; } = TaxRegime.None;
        public FiscalYearPattern FiscalYearPattern { get; set; } = FiscalYearPattern.Calendar;
        public int? FiscalYearStartMonth { get; set; }
        public DateTime BooksStartDate { get; set; } = DateTime.UtcNow.Date;
        public IndustryTemplate IndustryTemplate { get; set; } = IndustryTemplate.Services;
        public InventoryCostingMethod InventoryCostingMethod { get; set; } = InventoryCostingMethod.None;
        public string CoaTemplateId { get; set; } = "legacy-default";
        public DateTime? OnboardingChecklistDismissedAt { get; set; }
        public string? OnboardingChecklistJson { get; set; }

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
