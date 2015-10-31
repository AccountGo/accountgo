using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace Core.Domain
{
    [Table("Contact")]
    public partial class Contact : Party
    {
        /// <summary>
        /// Check ContactyType to determine whether CompanyNo is Customer No or Vendor No
        /// </summary>
        public ContactTypes ContactType { get; set; }
        public int? PartyId { get; set; }
        public virtual string FirstName { get; set; }
        public virtual string LastName { get; set; }
        public virtual string MiddleName { get; set; }

        public virtual Party Party { get; set; }
    }
}
