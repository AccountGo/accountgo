using Core.Domain.Purchases;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Core.Domain
{
    [Table("VendorContact")]
    public class VendorContact : BaseEntity
    {
        //public int Id { get; set; }
        public int ContactId { get; set; }

        public int VendorId { get; set; }

        public virtual Vendor Vendor { get; set; }

        public virtual Contact Contact { get; set; }
    }
}
