namespace Model.Sales
{
    public class Customer : BaseModel
    {
        public string No { get; set; }
        public string Name { get; set; }
        public string Email { get; set; }
        public string Website { get; set; }
        public string Phone { get; set; }
        public string Fax { get; set; }
        public decimal Balance { get; set; }


    }
}
