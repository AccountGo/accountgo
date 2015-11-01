//-----------------------------------------------------------------------
// <copyright file="Measurement.cs" company="AccountGo">
// Copyright (c) AccountGo. All rights reserved.
// <author>Marvin Perez</author>
// <date>1/11/2015 9:50:13 AM</date>
// </copyright>
//-----------------------------------------------------------------------

using System.ComponentModel.DataAnnotations.Schema;

namespace Core.Domain.Items
{
    [Table("Measurement")]
    public partial class Measurement : BaseEntity
    {
        public string Code { get; set; }
        public string Description { get; set; }
    }
}
