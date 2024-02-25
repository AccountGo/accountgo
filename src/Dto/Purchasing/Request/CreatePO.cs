using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Dto.Purchasing.Request
{
    public class CreatePO : BaseDto
    {
        [Required(ErrorMessage = "Required")]
        public int? VendorId { get; set; }
        [Required(ErrorMessage = "Required")]
        public DateTime OrderDate { get; set; }
        public int? PaymentTermId { get; set; }
        public string ReferenceNo { get; set; }
        public List<CreatePOLine> PurchaseOrderLines { get; set; } = new();
    }
}
