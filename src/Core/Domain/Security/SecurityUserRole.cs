using System.ComponentModel.DataAnnotations.Schema;

namespace Core.Domain.Security
{
    [Table("SecurityUserRole", Schema = "dbo")]
    public class SecurityUserRole : BaseEntity
    {
        //[Key]
        //public int SecurityUserRoleId { get; set; }
        public int UserId { get; set; }
        public int SecurityRoleId { get; set; }

        public virtual User User { get; set; }
        public virtual SecurityRole SecurityRole { get; set; }
    }
}
