namespace Dto.Dashboard
{
    public class DashboardReceivableItemDto
    {
        public int CustomerId { get; set; }
        public string CustomerName { get; set; } = string.Empty;
        public decimal Balance { get; set; }
    }
}
