//-----------------------------------------------------------------------
// <copyright file="Address.cs" company="AccountGo">
// Copyright (c) AccountGo. All rights reserved.
// <author>Marvin Perez</author>
// <date>1/11/2015 9:48:38 AM</date>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace Core.Domain
{
    [Table("Address")]
    public partial class Address : BaseEntity
    {
        public string No { get; set; }
        public string Street { get; set; }
        public string City { get; set; }
    }
}
