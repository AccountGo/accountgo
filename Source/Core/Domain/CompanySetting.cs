//-----------------------------------------------------------------------
// <copyright file="CompanySetting.cs" company="AccountGo">
// Copyright (c) AccountGo. All rights reserved.
// <author>Marvin Perez</author>
// <date>1/11/2015 9:48:38 AM</date>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace Core.Domain
{
    [Table("CompanySetting")]
    public partial class CompanySetting : BaseEntity
    {
        public int CompanyId { get; set; }
        public Company Company { get; set; }
    }
}
