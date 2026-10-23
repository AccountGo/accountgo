//-----------------------------------------------------------------------
// <copyright file="PostalAddress.cs" company="AccountGo">
// Copyright (c) AccountGo. All rights reserved.
// <author>Marvin Perez</author>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System.Collections.Generic;
using System.Linq;

namespace Core.Domain
{
    /// <summary>A postal address value object, stored as columns on its owner (EF owned type).</summary>
    public class PostalAddress
    {
        public string? Line1 { get; set; }
        public string? Line2 { get; set; }
        public string? City { get; set; }
        public string? Region { get; set; }
        public string? PostalCode { get; set; }
        public string? CountryCode { get; set; }

        public bool IsEmpty => ToLines().Count == 0;

        /// <summary>Printable lines: street lines, then the locality line, then country; blank parts are skipped.</summary>
        public List<string> ToLines()
        {
            var lines = new List<string>();
            if (!string.IsNullOrWhiteSpace(Line1)) lines.Add(Line1.Trim());
            if (!string.IsNullOrWhiteSpace(Line2)) lines.Add(Line2.Trim());

            // "City, Region PostalCode"; the comma only separates city from region.
            var cityRegion = string.Join(", ", new[] { City, Region }.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!.Trim()));
            var locality = string.Join(" ", new[] { cityRegion, PostalCode?.Trim() }.Where(s => !string.IsNullOrWhiteSpace(s)));
            if (locality.Length > 0) lines.Add(locality);

            if (!string.IsNullOrWhiteSpace(CountryCode)) lines.Add(CountryCode.Trim());
            return lines;
        }
    }
}
