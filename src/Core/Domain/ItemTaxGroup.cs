//-----------------------------------------------------------------------
// <copyright file="ItemTaxGroup.cs" company="AccountGo">
// Copyright (c) AccountGo. All rights reserved.
// <author>Marvin Perez</author>
// <date>1/11/2015 9:48:38 AM</date>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

namespace Core.Domain
{
    [Table("ItemTaxGroup")]
    public partial class ItemTaxGroup : BaseEntity
    {
        public ItemTaxGroup()
        {
            ItemTaxGroupTax = new HashSet<ItemTaxGroupTax>();
        }

        public string Name { get; set; }
        public bool IsFullyExempt { get; set; }

        public string CreatedBy { get; set; }
        public DateTime CreatedOn { get; set; }
        public string ModifiedBy { get; set; }
        public DateTime ModifiedOn { get; set; }

        public virtual ICollection<ItemTaxGroupTax> ItemTaxGroupTax { get; set; }
    }

    [Table("ItemTaxGroupTax")]
    public partial class ItemTaxGroupTax : BaseEntity
    {
        public int TaxId { get; set; }
        public int ItemTaxGroupId { get; set; }
        public bool IsExempt { get; set; }

        public string CreatedBy { get; set; }
        public DateTime CreatedOn { get; set; }
        public string ModifiedBy { get; set; }
        public DateTime ModifiedOn { get; set; }

        public virtual Tax Tax { get; set; }
        public virtual ItemTaxGroup ItemTaxGroup { get; set; }
    }
}
