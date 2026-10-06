using System.ComponentModel.DataAnnotations;

namespace Core.Domain.Donations
{
    public class Donor : BaseEntity
    {
        [Required, MaxLength(10)]
        public string DonorType { get; set; } = "Individual";

        [MaxLength(100)]
        public string FirstName { get; set; } = string.Empty;

        [MaxLength(100)]
        public string LastName { get; set; } = string.Empty;

        [MaxLength(200)]
        public string CompanyName { get; set; } = string.Empty;

        [Required, MaxLength(200)]
        public string Street { get; set; } = string.Empty;

        [Required, MaxLength(2)]
        public string Province { get; set; } = string.Empty;

        [Required, MaxLength(7)]
        public string PostalCode { get; set; } = string.Empty;

        [Required, MaxLength(30)]
        public string Telephone { get; set; } = string.Empty;

        [Required, MaxLength(254)]
        public string Email { get; set; } = string.Empty;
    }
}