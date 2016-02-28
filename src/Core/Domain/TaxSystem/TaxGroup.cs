//-----------------------------------------------------------------------
// <copyright file="TaxGroup.cs" company="AccountGo">
// Copyright (c) AccountGo. All rights reserved.
// <author>Marvin Perez</author>
// <date>1/11/2015 9:48:38 AM</date>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

namespace Core.Domain.TaxSystem
{
    [Table("TaxGroup")]
    public partial class TaxGroup : BaseEntity
    {
        public TaxGroup()
        {
            TaxGroupTax = new HashSet<TaxGroupTax>();
        }
        public string Description { get; set; }
        public bool TaxAppliedToShipping { get; set; }
        public bool IsActive { get; set; }
        public virtual ICollection<TaxGroupTax> TaxGroupTax { get; set; }
    }

    [Table("TaxGroupTax")]
    public partial class TaxGroupTax :  BaseEntity
    {
        public int TaxId { get; set; }
        public int TaxGroupId { get; set; }
        public virtual Tax Tax { get; set; }
        public virtual TaxGroup TaxGroup { get; set; }
    }
}
