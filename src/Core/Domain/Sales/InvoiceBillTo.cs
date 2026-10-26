//-----------------------------------------------------------------------
// <copyright file="InvoiceBillTo.cs" company="AccountGo">
// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only
// <author>Marvin Perez</author>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Core.Domain.Sales
{
    /// <summary>
    /// The customer's details as they print under "Bill to". Read from the customer while an invoice is a draft, then
    /// stored on the invoice (SalesInvoiceHeader.BillToJson) when it is issued, so later edits to the customer don't
    /// rewrite invoices the customer already has.
    /// </summary>
    public class InvoiceBillTo
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
        };

        public string Name { get; set; } = "";
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public List<string> AddressLines { get; set; } = new();

        /// <summary>The customer as its party describes it now.</summary>
        public static InvoiceBillTo FromParty(Party? party) => new()
        {
            Name = party?.Name ?? "Unknown",
            Email = party?.Email,
            Phone = party?.Phone,
            AddressLines = NonBlank(party?.CompanyStreet, party?.CompanyProvince, party?.CompanyPostalCode, party?.CompanyCountry),
        };

        /// <summary>Reads a stored bill-to; null when there is none or it can't be read.</summary>
        public static InvoiceBillTo? FromJson(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return null;

            try
            {
                var billTo = JsonSerializer.Deserialize<InvoiceBillTo>(json, JsonOptions);
                // The D1 backfill writes blank address parts as nulls or empty strings.
                if (billTo != null)
                    billTo.AddressLines = NonBlank(billTo.AddressLines?.ToArray() ?? System.Array.Empty<string?>());
                return billTo;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

        /// <summary>The address on one line, as the invoice prints it; null without one.</summary>
        [JsonIgnore]
        public string? AddressText => AddressLines.Count > 0 ? string.Join(", ", AddressLines) : null;

        private static List<string> NonBlank(params string?[] parts) =>
            parts.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!.Trim()).ToList();
    }
}
