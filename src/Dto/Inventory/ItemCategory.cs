using Dto.Common;
﻿namespace Dto.Inventory
{
    public class ItemCategory : BaseDto
    {
        [PublicId("icat")]
        public override int Id { get; set; }
        public string? Name { get; set; }
    }
}
