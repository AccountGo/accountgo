using System.Collections.Generic;

namespace Dto.Security
{
    public class User : BaseDto
    {
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string UserName { get; set; }
        public string Email { get; set; }
        public IList<Role> Roles { get; set; }
        public bool SysAdmin { get { return IsSystemAdministrators(); } }

        public User()
        {
            Roles = new List<Role>();
        }

        private bool IsSystemAdministrators()
        {
            foreach (var role in Roles)
                if (role.SysAdmin)
                    return true;
            return false;
        }
    }
}
