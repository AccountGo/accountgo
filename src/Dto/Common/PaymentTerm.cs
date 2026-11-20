namespace Dto.Common
{
    public class PaymentTerm : BaseDto
    {
        [PublicId("term")]
        public override int Id { get; set; }
    }
}
