using Dto.Common;
﻿namespace Dto.TaxSystem
{
    public class TaxGroupTax
    {
        [PublicId("tax")]
        public int TaxId { get; set; }
        [PublicId("txg")]
        public int TaxGroupId { get; set; }
    }
}
