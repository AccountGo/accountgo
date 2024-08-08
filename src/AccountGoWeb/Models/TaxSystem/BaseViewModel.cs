namespace AccountGoWeb.Models.TaxSystem
{
    public abstract class BaseViewModel
    {
        public virtual int Id { get; set; }
        // TODO: Get the user from the logged in user
        public string? ModifiedBy { get; set; }
    }
}
