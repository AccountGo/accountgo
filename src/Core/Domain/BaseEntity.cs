//-----------------------------------------------------------------------
// <copyright file="BaseEntity.cs" company="AccountGo">
// Copyright (c) AccountGo. All rights reserved.
// <author>Marvin Perez</author>
// <date>1/11/2015 9:48:38 AM</date>
// </copyright>
//-----------------------------------------------------------------------

namespace Core.Domain
{
    public abstract partial class BaseEntity
    {
        //[System.ComponentModel.DataAnnotations.Schema.NotMapped]
        [System.ComponentModel.DataAnnotations.Key]
        /// <summary>
        /// Gets or sets the entity identifier
        /// </summary>
        public virtual int Id { get; set; }

        //public virtual System.DateTime? CreatedOn { get; set; }
        //public virtual string CreatedBy { get; set; }
        //public virtual System.DateTime? ModifiedOn { get; set; }
        [System.ComponentModel.DataAnnotations.Schema.NotMapped]
        public virtual string ModifiedBy { get; set; }
    }
}
