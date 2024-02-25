using Dto.Enum;
using Dto.Financial;

namespace Dto.Common.Response
{
    public class GetPaymentTerm : BaseDto
    {
        public string Description { get; set; }
        public int PaymentType { get; set; }
        public int? DueAfterDays { get; set; }
        public bool IsActive { get; set; }
    }
}
