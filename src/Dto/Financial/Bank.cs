using Dto.Common;
﻿namespace Dto.Financial
{
    public class Bank : BaseDto
    {
        [PublicId("ba")]
        public override int Id { get; set; }
        public string? Name { get; set; }
        public string? AccountNo { get; set; }
        public string? BankName { get; set; }
        [PublicId("acct")]
        public int? AccountId { get; set; }
    }
}
