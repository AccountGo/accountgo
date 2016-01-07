using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Core.Domain.Security
{
    [Flags]
    public enum Permissions
    {
        CreateCase = (1 << 0),
        ViewCase = (1 << 1),
        Admin = (CreateCase | ViewCase)
    }

    [Table("SecurityPermission", Schema = "dbo")]
    public class SecurityPermission : BaseEntity
    {
        //[Key]
        //public int SecurityPermissionId { get; set; }
        public string PermissionName { get; set; }
        public string DisplayName { get; set; }
        public int SecurityGroupId { get; set; }

        public virtual SecurityGroup Groups { get; set; }
        public virtual ICollection<SecurityRolePermission> RolePermission { get; set; }

        public SecurityPermission()
        {
            RolePermission = new HashSet<SecurityRolePermission>();
        }
    }
}
