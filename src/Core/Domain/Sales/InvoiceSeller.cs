//-----------------------------------------------------------------------
// <copyright file="InvoiceSeller.cs" company="AccountGo">
// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only
// <author>Marvin Perez</author>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System.Collections.Generic;
using System.Text.Json;

namespace Core.Domain.Sales
{
    /// <summary>
    /// The invoicing party's details as they print on an invoice. Resolved from the workspace while an invoice is a draft,
    /// then stored on the invoice (SalesInvoiceHeader.SellerJson) when it is issued, so later changes to the business
    /// details don't rewrite invoices the customer already has.
    /// </summary>
    public class InvoiceSeller
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
        };

        public string Name { get; set; } = "";
        public string? TaxId { get; set; }
        /// <summary>The full address; whether it prints is still the workspace's ShowSellerAddress setting.</summary>
        public List<string> AddressLines { get; set; } = new();
        public string? Email { get; set; }
        public string? Phone { get; set; }

        /// <summary>The seller as the workspace's current settings describe it.</summary>
        public static InvoiceSeller FromWorkspace(Company? company, CompanySetting? setting)
        {
            var settings = setting?.GetSalesInvoiceSettings() ?? new SalesInvoiceSettings();
            var useLegalName = settings.SellerName == SalesInvoiceSettings.LegalName && !string.IsNullOrWhiteSpace(setting?.LegalName);

            return new InvoiceSeller
            {
                Name = useLegalName ? setting!.LegalName!.Trim() : company?.Name ?? "AccountGo",
                TaxId = company?.CRA,
                AddressLines = setting?.BusinessAddress?.ToLines() ?? new List<string>(),
                Email = setting?.BusinessEmail,
                Phone = setting?.BusinessPhone,
            };
        }

        /// <summary>Reads a stored seller; null when there is none or it can't be read.</summary>
        public static InvoiceSeller? FromJson(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return null;

            try
            {
                return JsonSerializer.Deserialize<InvoiceSeller>(json, JsonOptions);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);
    }
}
