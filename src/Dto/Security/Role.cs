using Dto.Common;
﻿using System.Collections.Generic;

namespace Dto.Security
{
    public class Role : BaseDto
    {
        [PublicId("role")]
        public override int Id { get; set; }
        public string? Name { get; set; }
        public string? DisplayName { get; set; }
        public bool SysAdmin { get; set; }
        public IList<User> Users { get; set; }
        public IList<Permission> Permissions { get; set; }

        public Role()
        {
            Permissions = new List<Permission>();
            Users = new List<User>();
        }
    }
}
