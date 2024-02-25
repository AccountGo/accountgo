﻿using Dto.Common.Response;

namespace Dto.Purchasing.Response
{
    public class GetVendor : BaseDto
    {
        public string No { get; set; }
        public string Name { get; set; }
        public string Email { get; set; }
        public string Website { get; set; }
        public string Phone { get; set; }
        public string Fax { get; set; }
        public decimal Balance { get; set; }
        public string Contact { get; set; }
        public string TaxGroup { get; set; }
    }
}
