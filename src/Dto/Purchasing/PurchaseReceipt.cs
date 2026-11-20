using Dto.Common;
﻿namespace Dto.Purchasing
{
    public class PurchaseReceipt : BaseDto
    {
        [PublicId("prc")]
        public override int Id { get; set; }
    }
}
