using System.Collections.Generic;

namespace Dto.Financial
{
    public class TreeViewAccounts
    {
        public IList<Group> Groups { get; set; }
    }

    public class Group {
        public string Name { get; set; }
        public IList<Group> Groups { get; set; }
    }
}
