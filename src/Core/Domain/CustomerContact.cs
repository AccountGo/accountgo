using Core.Domain.Sales;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Core.Domain
{
    [Table("CustomerContact")]
    public class CustomerContact : BaseEntity
    {
        
        public int ContactId { get; set; }
        public int CustomerId { get; set; }

        public virtual Customer Customer {get; set;}

        public virtual Contact Contact { get; set; }
    }
}
