namespace Dto.Sales
{
    /// <summary>
    /// A customer as the Clients pages show and edit it: the details that print on invoices and the default terms.
    /// Accounts, tax group and contacts keep their values when a client is saved.
    /// </summary>
    public class Client
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public string? Street { get; set; }
        /// <summary>City and state or province, in one field as the invoice prints it.</summary>
        public string? City { get; set; }
        public string? PostalCode { get; set; }
        public string? Country { get; set; }
        public int? PaymentTermId { get; set; }
        public bool Archived { get; set; }
        /// <summary>Whether it can still be deleted: nothing has been recorded for it yet. Only on a single client.</summary>
        public bool CanDelete { get; set; }
    }
}
