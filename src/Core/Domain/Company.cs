//-----------------------------------------------------------------------
// <copyright file="Company.cs" company="AccountGo">
// Copyright (c) AccountGo. All rights reserved.
// <author>Marvin Perez</author>
// <date>1/11/2015 9:48:38 AM</date>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace Core.Domain
{
    [Table("Company")]
    public partial class Company : BaseEntity
    {
        public string Name { get; set; }
        public string ShortName { get; set; }
        public string CompanyCode { get; set; }
    }
}
