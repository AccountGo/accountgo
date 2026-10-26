using Core.Domain.Security;
using System.Collections.Generic;

namespace Core.Data
{
    public interface ISecurityRepository : IUserRepository<User>, IRoleRepository<SecurityRole>, IGroupRepository<SecurityGroup>
    { 

    }
    public interface IUserRepository<User>
    {
        void AddUser(User user);
        User GetUser(string username);
        User GetUserById(int id);
        IEnumerable<User> GetAllUsers();
        void DeleteUser(int id);
    }

    public interface IRoleRepository<SecurityRole>
    {
        void AddRole(SecurityRole role);
        SecurityRole GetRole(string roleName);
        IEnumerable<SecurityRole> GetAllRoles();
    }

    public interface IGroupRepository<SecurityGroup>
    {
        IEnumerable<SecurityGroup> GetAllGroups();
    }
}
