using Dto.Common;
﻿namespace Dto.TaxSystem
{
    public class ItemTaxGroupTax
    {
        [PublicId("tax")]
        public int TaxId { get; set; }
        [PublicId("itg")]
        public int ItemTaxGroupId { get; set; }
        public bool IsExempt { get; set; }
    }
}
