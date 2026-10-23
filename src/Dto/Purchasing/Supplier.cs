namespace Dto.Purchasing
{
    /// <summary>
    /// A vendor as the Vendors pages show and edit it, the counterpart of a client: its details and default terms,
    /// with what the workspace owes it. Accounts, tax group and contacts keep their values when a vendor is saved.
    /// </summary>
    public class Supplier
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public string? Street { get; set; }
        /// <summary>City and state or province, in one field.</summary>
        public string? City { get; set; }
        public string? PostalCode { get; set; }
        public string? Country { get; set; }
        public int? PaymentTermId { get; set; }
        public bool Archived { get; set; }
        /// <summary>Unpaid on its approved bills.</summary>
        public decimal Outstanding { get; set; }
        /// <summary>The part of it past due.</summary>
        public decimal Overdue { get; set; }
        /// <summary>Payments made on its bills.</summary>
        public decimal PaidToDate { get; set; }
        /// <summary>Date of its latest approved bill.</summary>
        public System.DateTime? LastBillDate { get; set; }
        /// <summary>Whether it can still be deleted: nothing has been recorded for it yet. Only on a single vendor.</summary>
        public bool CanDelete { get; set; }
    }
}
