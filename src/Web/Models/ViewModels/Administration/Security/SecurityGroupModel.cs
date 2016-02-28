using System.Collections.Generic;

namespace Web.Models.ViewModels.Administration.Security
{
    public class SecurityGroupModel
    {
        public int SecurityGroupId { get; set; }
        public int SecurityRoleId { get; set; }
        public string Name { get; set; }

        public List<SecurityPermissionModel> SecurityPermission { get; set; }
    }
}