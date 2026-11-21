using Dto.Common;
﻿namespace Dto.Inventory
{
    public class Measurement:BaseDto
    {
        [PublicId("uom")]
        public override int Id { get; set; }
        public string? Code { get; set; }
        public string? Description { get; set; }
    }
}
