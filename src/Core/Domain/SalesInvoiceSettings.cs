//-----------------------------------------------------------------------
// <copyright file="SalesInvoiceSettings.cs" company="AccountGo">
// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only
// <author>Marvin Perez</author>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Text.Json;

namespace Core.Domain
{
    /// <summary>
    /// How a workspace's sales invoices are presented. Stored as JSON in CompanySetting.SalesInvoiceSettingsJson
    /// so new options need no schema change. The defaults reproduce the invoice layout from before these settings existed.
    /// </summary>
    public class SalesInvoiceSettings
    {
        public const string TradingName = "TradingName";
        public const string LegalName = "LegalName";
        public const int MaxDefaultTextLength = 2000;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
        };

        /// <summary>Which name heads the invoice: <see cref="TradingName"/> (Company.Name) or <see cref="LegalName"/>.</summary>
        public string SellerName { get; set; } = TradingName;
        public bool ShowSellerAddress { get; set; } = true;
        public bool ShowCustomerEmail { get; set; } = true;
        public bool ShowNotes { get; set; } = true;
        public bool ShowPaymentInstructions { get; set; } = true;
        public string? DefaultNotes { get; set; }
        public string? DefaultPaymentInstructions { get; set; }

        public static bool IsValidSellerName(string? value) => value is TradingName or LegalName;

        /// <summary>Reads stored settings; missing keys keep their defaults, and unreadable JSON yields all defaults.</summary>
        public static SalesInvoiceSettings FromJson(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return new SalesInvoiceSettings();

            try
            {
                var settings = JsonSerializer.Deserialize<SalesInvoiceSettings>(json, JsonOptions) ?? new SalesInvoiceSettings();
                if (!IsValidSellerName(settings.SellerName))
                    settings.SellerName = TradingName;
                return settings;
            }
            catch (JsonException)
            {
                return new SalesInvoiceSettings();
            }
        }

        public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);
    }
}
