using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Core.Domain.Security
{
    [Table("SecurityRole", Schema = "dbo")]
    [PublicIdPrefix("role")]
    public class SecurityRole : BaseEntity, IHasPublicId
    {
        public string PublicId { get; set; } = string.Empty;
        //[Key]
        //public int SecurityRoleId { get; set; }
        public string? Name { get; set; }
        public string? DisplayName { get; set; }
        public bool SysAdmin { get; set; }
        public bool System { get; set; }
        public virtual ICollection<SecurityUserRole> Users { get; set; } = null!;
        public virtual ICollection<SecurityRolePermission> Permissions { get; set; } = null!;
        public SecurityRole()
        {
            Users = new HashSet<SecurityUserRole>();
            Permissions = new HashSet<SecurityRolePermission>();
        }
    }
}
