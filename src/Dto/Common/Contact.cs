using System.ComponentModel.DataAnnotations;

namespace Dto.Common
{
    public class Contact : BaseDto
    {
        [PublicId("cont")]
        public override int Id { get; set; }
         [PublicId("ws")]
         public int CompanyId { get; set; }
        [Display(Name = "First Name")]
        [Required]
        [StringLength(100)]
        public string? FirstName { get; set; }
        [Display(Name = "Last Name")]
        [Required]
        [StringLength(100)]
        public string? LastName { get; set; }
        public Party Party { get; set; }

        public string? MiddleName { get; set; }

        [PublicId("cus")]
        public int CustomerId { get; set; }
        [PublicId("ven")]
        public int VendorId { get; set; }
        public int HoldingPartyType { get; set; } //this is 1 or 2     Customer = 1,Vendor = 2,
        [PublicId("pty")]
        public int HoldingPartyId { get; set; } // id for customerId or VendorId    

        public Contact() {
            Party = new Party();
        }
    }
}
