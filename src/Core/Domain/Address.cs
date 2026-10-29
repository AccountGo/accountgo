//-----------------------------------------------------------------------
// <copyright file="Address.cs" company="AccountGo">
// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only
// <author>Marvin Perez</author>
// <date>1/11/2015 9:48:38 AM</date>
// </copyright>
//-----------------------------------------------------------------------

using System.ComponentModel.DataAnnotations.Schema;

namespace Core.Domain
{
    [Table("Address")]
    public partial class Address : BaseEntity, ICompanyOwned
    {
        public int CompanyId { get; set; }
        public string No { get; set; }
        public string Street { get; set; }
        public string City { get; set; }
    }
}
