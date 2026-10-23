//-----------------------------------------------------------------------
// <copyright file="PostalAddress.cs" company="AccountGo">
// Copyright (c) AccountGo. All rights reserved.
// <author>Marvin Perez</author>
// <date>Created</date>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace Core.Domain
{
    public partial class PostalAddress
    {
        public string? Line1 { get; set; }
        public string? Line2 { get; set; }
        public string? City { get; set; }
        public string? Region { get; set; }
        public string? PostalCode { get; set; }
        public string? CountryCode { get; set; }
    }
}