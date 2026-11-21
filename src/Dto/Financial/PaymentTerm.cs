using Dto.Common;
﻿namespace Dto.Financial
{
    public class PaymentTerm : BaseDto
    {
        [PublicId("term")]
        public override int Id { get; set; }
    }
}
