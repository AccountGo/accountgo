using System.Collections.Generic;

namespace Dto.Security
{
    public class Group : BaseDto
    {
        public string Name { get; set; }
        public string DisplayName { get; set; }
        public IList<Permission> Permissions { get; set; }

        public Group()
        {
            Permissions = new List<Permission>();
        }
    }
}
