using Core.Domain.Security;

namespace Core.Data
{
    public interface ISecurityRepository : IUserRepository<User>, IRoleRepository<SecurityRole>
    { 

    }
    public interface IUserRepository<User>
    {
        void AddUser(User user);
        User GetUser(string username);
    }

    public interface IRoleRepository<SecurityRole>
    {
        void AddRole(SecurityRole role);
        SecurityRole GetRole(string roleName);
    }
}
