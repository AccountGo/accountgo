//-----------------------------------------------------------------------
// <copyright file="PaymentTerm.cs" company="AccountGo">
// Copyright (c) AccountGo. All rights reserved.
// <author>Marvin Perez</author>
// <date>1/11/2015 9:48:38 AM</date>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace Core.Domain
{
    [Table("PaymentTerm")]
    public partial class PaymentTerm : BaseEntity
    {
        public string Description { get; set; }
        public PaymentTypes PaymentType { get; set; }
        public int? DueAfterDays { get; set; }
        public bool IsActive { get; set; }
    }
}
