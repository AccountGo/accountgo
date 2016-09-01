using System.ComponentModel.DataAnnotations;

namespace Dto.Common
{
    public class Contact : BaseDto
    {
         public int CompanyId { get; set; }
        [Display(Name = "First Name")]
        [Required]
        [StringLength(100)]
        public string FirstName { get; set; }
        [Display(Name = "Last Name")]
        [Required]
        [StringLength(100)]
        public string LastName { get; set; }
        public Party Party { get; set; }

        public string MiddleName { get; set; }

        public int CustomerId { get; set; }
        public int VendorId { get; set; }

        public Contact() {
            Party = new Party();
        }
    }
}
