using System.ComponentModel.DataAnnotations.Schema;

namespace Core.Domain.Items
{
    [Table("Measurement")]
    public partial class Measurement : BaseEntity
    {
        public string Code { get; set; }
        public string Description { get; set; }
    }
}
