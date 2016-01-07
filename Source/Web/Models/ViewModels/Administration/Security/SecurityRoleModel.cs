namespace Web.Models.ViewModels.Administration.Security
{
    public class SecurityRoleModel
    {
        public int SecurityRoleId { get; set; }
        public string RoleName { get; set; }
        public bool SysAdmin { get; set; }
        public bool HasUsers { get; set; }
    }
}