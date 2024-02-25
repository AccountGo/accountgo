using System.ComponentModel.DataAnnotations;

namespace Dto.Purchasing.Request
{
    public class CreatePOLine : BaseDto
    {
        [Required(ErrorMessage = "Required")]
        public int? ItemId { get; set; }

        public string ItemName { get; set; }
        public decimal Cost { get; set; }

        [Required(ErrorMessage = "Required")]
        public int? MeasurementId { get; set; }

        public string MeasurementName { get; set; }

        [Range(1, double.MaxValue, ErrorMessage = "The value must be >= 0")]
        [Required(ErrorMessage = "Required")]
        public decimal? Quantity { get; set; }

        [Required(ErrorMessage = "Required")]
        public decimal? Amount { get; set; }

        public decimal? Discount { get; set; }
    }
}
