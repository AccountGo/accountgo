using System.ComponentModel.DataAnnotations.Schema;

namespace Core.Domain
{
    [Table("Contact")]
    public partial class Contact : Party
    {
        public ContactTypes ContactType { get; set; }
        /// <summary>
        /// Check ContactyType to determine whether CompanyNo is Customer No or Vendor No
        /// </summary>
        public string CompanyNo { get; set; }
    }
}
