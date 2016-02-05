//-----------------------------------------------------------------------
// <copyright file="ItemTaxGroup.cs" company="AccountGo">
// Copyright (c) AccountGo. All rights reserved.
// <author>Marvin Perez</author>
// <date>1/11/2015 9:48:38 AM</date>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

namespace Core.Domain.TaxSystem
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

        public virtual ICollection<ItemTaxGroupTax> ItemTaxGroupTax { get; set; }
    }

    [Table("ItemTaxGroupTax")]
    public partial class ItemTaxGroupTax : BaseEntity
    {
        public int TaxId { get; set; }
        public int ItemTaxGroupId { get; set; }
        public bool IsExempt { get; set; }
        
        public virtual Tax Tax { get; set; }
        public virtual ItemTaxGroup ItemTaxGroup { get; set; }
    }
}
