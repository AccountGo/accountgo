//-----------------------------------------------------------------------
// <copyright file="SequenceNumber.cs" company="AccountGo">
// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only
// <author>Marvin Perez</author>
// <date>1/11/2015 9:48:38 AM</date>
// </copyright>
//-----------------------------------------------------------------------

using System.ComponentModel.DataAnnotations.Schema;

namespace Core.Domain
{
    [Table("SequenceNumber")]
    public partial class SequenceNumber : BaseEntity, ICompanyOwned
    {
        public int CompanyId { get; set; }
        public SequenceNumberTypes SequenceNumberType { get; set; }
        public string Description { get; set; }
        public string Prefix { get; set; }
        public int NextNumber { get; set; }
        public bool UsePrefix { get; set; }
    }
}
