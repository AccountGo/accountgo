namespace Web.Models.ViewModels.Administration.Security
{
    public class SecurityUserModel
    {
        public int SecurityUserRoleId { get; set; }
        public int UserId { get; set; }
        public string FullName { get; set; }
        public string UserName { get; set; }
        public string Email { get; set; }
    }
}