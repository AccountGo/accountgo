using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

namespace Core.Domain.Security
{
    [Table("SecurityPermission", Schema = "dbo")]
    public class SecurityPermission : BaseEntity
    {
        //[Key]
        //public int SecurityPermissionId { get; set; }
        public string Name { get; set; }
        public string DisplayName { get; set; }
        public int SecurityGroupId { get; set; }

        public virtual SecurityGroup Group { get; set; }
        public virtual ICollection<SecurityRolePermission> RolePermissions { get; set; }

        public SecurityPermission()
        {
            RolePermissions = new HashSet<SecurityRolePermission>();
        }
    }
}
