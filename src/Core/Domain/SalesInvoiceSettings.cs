//-----------------------------------------------------------------------
// <copyright file="SalesInvoiceSettings.cs" company="AccountGo">
// Copyright (c) AccountGo. All rights reserved.
// <author>Marvin Perez</author>
// <date>Created</date>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Core.Domain
{
    public partial class SalesInvoiceSettings
    {
        public bool ShowSellerAddress { get; set; } = true;
        public bool ShowCustomerEmail { get; set; } = true;
        public bool ShowNotes { get; set; } = true;
        public bool ShowPaymentInstructions { get; set; } = true;
        public string? DefaultNotes { get; set; }
        public string? DefaultPaymentInstructions { get; set; }
        public string SellerName { get; set; } = "TradingName";
        public string? BusinessEmail { get; set; }
        public string? BusinessPhone { get; set; }

        // Defaults that reproduce today's invoice output exactly
        // SellerName: "TradingName" | "LegalName", default "TradingName"
        // ShowSellerAddress: bool = true
        // ShowCustomerEmail: bool = true
        // ShowNotes: bool = true
        // ShowPaymentInstructions: bool = true
        // DefaultNotes: string? = null
        // DefaultPaymentInstructions: string? = null

        // JSON deserialization fallback: when JSON is null, empty, malformed, or partial,
        // fall back to defaults per missing key and never throw.
        internal static SalesInvoiceSettings FromJson(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return new SalesInvoiceSettings();
            }

            try
            {
                var options = new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
                };

                var settings = JsonSerializer.Deserialize<SalesInvoiceSettings>(json, options);
                if (settings != null)
                {
                    // Ensure defaults for any missing keys
                    var result = new SalesInvoiceSettings
                    {
                        ShowSellerAddress = settings.ShowSellerAddress != false,
                        ShowCustomerEmail = settings.ShowCustomerEmail != false,
                        ShowNotes = settings.ShowNotes != false,
                        ShowPaymentInstructions = settings.ShowPaymentInstructions != false,
                        SellerName = !string.IsNullOrWhiteSpace(settings.SellerName) ? settings.SellerName : "TradingName",
                        DefaultNotes = string.IsNullOrWhiteSpace(settings.DefaultNotes) ? null : settings.DefaultNotes.Trim(),
                        DefaultPaymentInstructions = string.IsNullOrWhiteSpace(settings.DefaultPaymentInstructions) ? null : settings.DefaultPaymentInstructions.Trim()
                    };
                    return result;
                }
            }
            catch
            {
                // Malformed JSON - fall through to defaults
            }

            // Return defaults for any malformed/partial/empty JSON
            return new SalesInvoiceSettings();
        }

        // Serialization with camelCase for storage in CompanySetting.SalesInvoiceSettingsJson
        internal string ToJson()
        {
            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            };
            return JsonSerializer.Serialize(this, options);
        }

        // Parse a client request body (with legalName, businessEmail, businessPhone, address{...})
        internal static SalesInvoiceSettings FromClientRequest(string legalName, string businessEmail, string businessPhone, PostalAddress? address)
        {
            var settings = new SalesInvoiceSettings
            {
                SellerName = !string.IsNullOrWhiteSpace(legalName) ? legalName.Trim() : "TradingName",
                BusinessEmail = string.IsNullOrWhiteSpace(businessEmail) ? null : businessEmail.Trim(),
                BusinessPhone = string.IsNullOrWhiteSpace(businessPhone) ? null : businessPhone.Trim(),
                ShowSellerAddress = address != null,
                ShowCustomerEmail = true, // default, can be overridden from JSON if needed
                ShowNotes = true, // default
                ShowPaymentInstructions = true, // default
                DefaultNotes = null,
                DefaultPaymentInstructions = null
            };

            // If address is provided, build address lines
            if (address != null)
            {
                var parts = new List<string>();
                if (!string.IsNullOrWhiteSpace(address.Line1)) parts.Add(address.Line1);
                if (!string.IsNullOrWhiteSpace(address.Line2)) parts.Add(address.Line2);
                if (!string.IsNullOrWhiteSpace(address.City)) parts.Add($"{address.City}, {address.Region ?? ""} {address.PostalCode ?? ""}".Trim(',', ' '));
                if (!string.IsNullOrWhiteSpace(address.CountryCode)) parts.Add(address.CountryCode);
                // Store address parts as semicolon-separated for the AddressLines list in the DTO
                // We'll just set the flags here; the actual AddressLines computation happens in BuildSalesInvoiceDto
            }

            return settings;
        }
    }
}