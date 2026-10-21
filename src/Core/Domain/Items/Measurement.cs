//-----------------------------------------------------------------------
// <copyright file="Measurement.cs" company="AccountGo">
// Copyright (c) AccountGo. All rights reserved.
// <author>Marvin Perez</author>
// <date>1/11/2015 9:48:38 AM</date>
// </copyright>
//-----------------------------------------------------------------------

using System.ComponentModel.DataAnnotations.Schema;

namespace Core.Domain.Items
{
    [Table("Measurement")]
    public partial class Measurement : BaseEntity, ICompanyOwned
    {
        public int CompanyId { get; set; }
        public string Code { get; set; }
        public string Description { get; set; }
    }
}
