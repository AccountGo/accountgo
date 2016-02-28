//-----------------------------------------------------------------------
// <copyright file="SalesQuoteHeader.cs" company="AccountGo">
// Copyright (c) AccountGo. All rights reserved.
// <author>Marvin Perez</author>
// <date>1/11/2015 9:48:38 AM</date>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace Core.Domain.Sales
{
    [Table("SalesQuoteHeader")]
    public partial class SalesQuoteHeader : BaseEntity
    {
        public int CustomerId { get; set; }
        public DateTime Date { get; set; }
        public virtual Customer Customer { get; set; }
    }
}
