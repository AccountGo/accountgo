//-----------------------------------------------------------------------
// <copyright file="TaxGroup.cs" company="AccountGo">
// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only
// <author>Marvin Perez</author>
// <date>1/11/2015 9:48:38 AM</date>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Core.Domain.TaxSystem
{
    [Table("TaxGroup")]
    public partial class TaxGroup : BaseEntity, ICompanyOwned
    {
        public TaxGroup()
        {
            TaxGroupTax = new HashSet<TaxGroupTax>();
        }
        public int CompanyId { get; set; }
        public string Description { get; set; }

        [Display(Name = "Fully Exempt")]
        public bool TaxAppliedToShipping { get; set; }
        public bool IsActive { get; set; }
        public virtual ICollection<TaxGroupTax> TaxGroupTax { get; set; }
    }

    [Table("TaxGroupTax")]
    public partial class TaxGroupTax :  BaseEntity, ICompanyOwned
    {
        public int CompanyId { get; set; }
        public int TaxId { get; set; }
        public int TaxGroupId { get; set; }
        public virtual Tax Tax { get; set; }
        public virtual TaxGroup TaxGroup { get; set; }
    }
}
