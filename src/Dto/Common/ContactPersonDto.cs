namespace Dto.Common
{
    /// <summary>
    /// A contact person linked to a client or vendor: personal details, address, and role flags.
    /// </summary>
    public class ContactPersonDto
    {
        public int Id { get; set; }
        public string FirstName { get; set; } = "";
        public string LastName { get; set; } = "";
        public string? MiddleName { get; set; }
        public string FullName => $"{FirstName} {LastName}".Trim();
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public string? Street { get; set; }
        public string? City { get; set; }
        public string? PostalCode { get; set; }
        public string? Country { get; set; }
        public bool IsPrimary { get; set; }
        public bool IsBillTo { get; set; }
    }
}
