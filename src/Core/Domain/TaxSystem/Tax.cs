//-----------------------------------------------------------------------
// <copyright file="Tax.cs" company="AccountGo">
// Copyright (c) AccountGo. All rights reserved.
// <author>Marvin Perez</author>
// <date>1/11/2015 9:48:38 AM</date>
// </copyright>
//-----------------------------------------------------------------------

using Core.Domain.Financials;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Core.Domain.TaxSystem
{
    [Table("Tax")]
    public partial class Tax : BaseEntity
    {
        public Tax()
        {
            TaxGroupTaxes = new HashSet<TaxGroupTax>();
            ItemTaxGroupTaxes = new HashSet<ItemTaxGroupTax>();
        }

        public int? SalesAccountId { get; set; }
        public int? PurchasingAccountId { get; set; }
        [Required]
        [StringLength(50)]
        public string TaxName { get; set; }
        [Required]
        [StringLength(16)]
        public string TaxCode { get; set; }
        public decimal Rate { get; set; }
        public bool IsActive { get; set; }
        public virtual Account SalesAccount { get; set; }
        public virtual Account PurchasingAccount { get; set; }

        public virtual ICollection<TaxGroupTax> TaxGroupTaxes { get; set; }
        public virtual ICollection<ItemTaxGroupTax> ItemTaxGroupTaxes { get; set; }
    }
}
